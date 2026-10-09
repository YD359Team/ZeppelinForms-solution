using Android.Content;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using SkiaSharp;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Android;

/// <summary>
/// The Android platform. There is one surface for the whole application, so windows
/// here are layers on it, not system windows.
///
/// It doesn't implement INestedLoopSupport: blocking the UI thread and keeping on
/// receiving events is impossible, so Form.ShowDialog honestly throws — only
/// ShowDialogAsync works.
/// </summary>
public sealed partial class AndroidPlatform : IPlatform, ISystemMotionSettings, IAppLifecycle
{
    /// <summary>The system "back" button or gesture was pressed.
    /// Set Handled so that the system doesn't close the activity.</summary>
    /// <remarks>
    /// Deliberately not mapped to Escape: many handle Escape in a form, and we
    /// don't see the result — OnKeyDown returns nothing. Then it would be impossible
    /// to tell "a dialog closed" from "nobody took it", and the application would
    /// exit on every press.
    /// </remarks>
    public event EventHandler<BackRequestedEventArgs>? BackRequested;

    internal bool RaiseBackRequested()
    {
        if (BackRequested is null) return false;

        var args = new BackRequestedEventArgs();
        BackRequested(this, args);

        return args.Handled;
    }

    /// <summary>The dimming under a modal dialog.</summary>
    private static readonly SKColor s_scrim = new(0, 0, 0, 96);

    private readonly Activity _activity;
    private readonly List<AndroidWindow> _windows = [];
    private readonly FrameCallback _frameCallback;

    /// <summary>The main thread's queue. Invoke must queue the action even when it
    /// is called on the UI thread — Activity.RunOnUiThread runs it right away then.</summary>
    private readonly Handler _uiHandler = new(Looper.MainLooper!);

    private ZeppelinView? _view;

    // insets for the system bars and the cutout, in logical units
    private float _insetLeft;
    private float _insetTop;
    private float _insetRight;
    private float _insetBottom;
    private int _physicalWidth;
    private int _physicalHeight;
    private bool _paintPending;
    private bool _frameScheduled;

    private float _insetKeyboard;

    private AndroidPlatform(Activity activity)
    {
        _activity = activity;
        _frameCallback = new FrameCallback(HandleFrame);
    }

    public static AndroidPlatform Create(Activity activity)
    {
        Skia.SkiaImageDecoder.Register();
        Skia.SkiaTextMeasurer.Register();
        Skia.SkiaOffscreenRenderer.Register();
        Displays.Current = new AndroidDisplayProvider(activity);

        // without it Clipboard.Current stayed the built-in stand-in, which silently
        // does nothing: copying did nothing and pasting gave nothing
        AndroidClipboard.Register(activity);

        var platform = new AndroidPlatform(activity)
        {
            PrefersReducedMotion = QueryReducedMotion(activity),
        };

        ZeppelinForms.Animation.Motion.UseSystemSettings(platform);

        platform.InitAppearance();

        // the bridge existed but was never registered: Paused, Resumed and Saving
        // never came, frames kept running in the background, and the last chance
        // to save state before the process is killed was never offered
        platform._lifecycle = new LifecycleBridge(platform, activity);
        activity.Application?.RegisterActivityLifecycleCallbacks(platform._lifecycle);

        return platform;
    }

    internal float Scale { get; private set; } = 1f;

    /// <summary>The surface size in logical units.</summary>
    internal Size SurfaceSize => new(_physicalWidth / Scale, _physicalHeight / Scale);

    private AndroidWindow? Root => _windows.Count > 0 ? _windows[0] : null;

    public IPlatformWindow CreateWindow(Form form)
    {
        var window = new AndroidWindow(this, form);

        _windows.Add(window);
        form.PlatformWindow = window;
        form.Platform = this;

        if (_view is null)
        {
            Scale = _activity.Resources?.DisplayMetrics?.Density ?? 1f;

            _view = new ZeppelinView(_activity, this);

            // explicitly, rather than relying on the SetContentView default: SKCanvasView
            // brings its own LayoutParams, and with WRAP_CONTENT the default
            // View.onMeasure measures it to zero by zero — visually indistinguishable
            // from drawing that doesn't work
            _view.LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent);

            _activity.SetContentView(_view);

            // the size comes in OnSizeChanged: there is nothing to lay out before it
        }
        else
        {
            LayoutOverlay(window);
            Invalidate();
        }

        return window;
    }

    /// <summary>The system owns the loop, so this returns control immediately.</summary>
    public void Start() { }

    // ===== the lifecycle =====

    /// <summary>The activity goes to the background: frames stop, animations
    /// freeze, the application stops spending the battery on what isn't visible.</summary>
    public event EventHandler? Paused;

    public event EventHandler? Resumed;

    /// <summary>The system is about to save the state. After this the application
    /// may be killed without warning — this is the last chance to write what would
    /// be a pity to lose.</summary>
    public event EventHandler? Saving;

    private LifecycleBridge? _lifecycle;

    /// <summary>
    /// A bridge from the activity's lifecycle to the platform's events.
    /// </summary>
    /// <remarks>
    /// Through IActivityLifecycleCallbacks rather than by overriding OnPause and
    /// OnResume in the activity: otherwise every application would have to remember
    /// to call the platform from four methods, and a forgotten OnPause means frames
    /// that keep running in the background.
    ///
    /// The callbacks come for all activities of the process, so foreign ones are
    /// filtered out: an application may have a second activity that has nothing
    /// to do with the form.
    /// </remarks>
    private sealed class LifecycleBridge(AndroidPlatform platform, Activity activity)
        : Java.Lang.Object, Application.IActivityLifecycleCallbacks
    {
        private bool Ours(Activity other) => ReferenceEquals(other, activity);

        public void OnActivityPaused(Activity other)
        {
            if (Ours(other)) platform.Paused?.Invoke(platform, EventArgs.Empty);
        }

        public void OnActivityResumed(Activity other)
        {
            if (!Ours(other)) return;

            // while the application was in the background, the user may have gone
            // into accessibility and changed the motion setting
            platform.RefreshReducedMotion();

            // the night mode and the wallpaper colors are changed there too
            platform.RefreshAppearance();

            platform.Resumed?.Invoke(platform, EventArgs.Empty);
        }

        public void OnActivitySaveInstanceState(Activity other, Bundle outState)
        {
            if (Ours(other)) platform.Saving?.Invoke(platform, EventArgs.Empty);
        }

        // the framework doesn't need the other lifecycle steps: creation and start
        // happen before the platform appears, and stopping and destruction come
        // right after the pause
        public void OnActivityCreated(Activity other, Bundle? savedInstanceState) { }

        public void OnActivityStarted(Activity other) { }

        public void OnActivityStopped(Activity other) { }

        public void OnActivityDestroyed(Activity other) { }
    }

    /// <summary>The animation duration scale set to zero — this is what "remove
    /// animations" in accessibility looks like on Android.</summary>
    /// <remarks>
    /// Re-read on returning from the background rather than observed continuously:
    /// a ContentObserver for a setting that is changed once in a lifetime is an extra
    /// subscription, and it can be changed only by going into the settings, that is,
    /// by taking the application out of the active state.
    /// </remarks>
    public bool PrefersReducedMotion { get; private set; }

    private EventHandler? _motionChanged;

    event EventHandler? ISystemMotionSettings.Changed
    {
        add => _motionChanged += value;
        remove => _motionChanged -= value;
    }

    /// <summary>global:: is mandatory: inside ZeppelinForms.Android the name Android
    /// points to our own namespace, not to the SDK bindings.</summary>
    private static bool QueryReducedMotion(Activity activity) =>
        global::Android.Provider.Settings.Global.GetFloat(
            activity.ContentResolver,
            global::Android.Provider.Settings.Global.AnimatorDurationScale,
            1f) == 0f;

    private void RefreshReducedMotion()
    {
        bool reduced = QueryReducedMotion(_activity);

        if (reduced == PrefersReducedMotion) return;

        PrefersReducedMotion = reduced;
        _motionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Exit()
    {
        if (_lifecycle is not null)
        {
            _activity.Application?.UnregisterActivityLifecycleCallbacks(_lifecycle);
            _lifecycle.Dispose();
            _lifecycle = null;
        }

        for (int i = _windows.Count - 1; i >= 0; i--)
            _windows[i].Close();

        _activity.Finish();
    }

    internal void BringToFront(AndroidWindow window)
    {
        if (_windows.Count < 2 || _windows[0] == window) return;
        if (_windows[^1] == window) return;

        _windows.Remove(window);
        _windows.Add(window);

        Invalidate();
    }

    internal void Remove(AndroidWindow window)
    {
        if (!_windows.Remove(window)) return;

        Invalidate();
    }

    // ==== the surface ====

    internal void HandleResize(int physicalWidth, int physicalHeight)
    {
        Scale = _activity.Resources?.DisplayMetrics?.Density ?? 1f;

        _physicalWidth = physicalWidth;
        _physicalHeight = physicalHeight;

        if (Root is { } root)
        {
            Size surface = SurfaceSize;

            // the root form lives inside the safe area rather than the whole window:
            // with targetSdk 35 Android always draws the content under the status bar,
            // and this can't be opted out of.
            // The shift goes through Origin, so touches arrive where things are drawn —
            // ToLocal subtracts it
            root.Origin = new Point(_insetLeft, _insetTop);

            root.Form.ClientSize = new Size(
                Math.Max(0, surface.Width - _insetLeft - _insetRight),
                Math.Max(0, surface.Height - _insetTop - _insetBottom - _insetKeyboard));

            root.Form.PerformLayout();
        }

        for (int i = 1; i < _windows.Count; i++)
            LayoutOverlay(_windows[i]);

        Invalidate();
    }

    /// <summary>The safe area changed: the keyboard appeared, the screen was rotated,
    /// the gesture bar moved.</summary>
    internal void HandleInsets(int left, int top, int right, int bottom)
    {
        _insetLeft = left / Scale;
        _insetTop = top / Scale;
        _insetRight = right / Scale;
        _insetBottom = bottom / Scale;

        // there is nothing to lay out before the first OnSizeChanged
        if (_physicalWidth > 0)
            HandleResize(_physicalWidth, _physicalHeight);
    }

    /// <summary>The keyboard opened or closed.</summary>
    /// <remarks>
    /// The form under it is simply shrunk. It would be more correct to also pull the
    /// focused field into the visible part, but that needs ScrollIntoView, which
    /// neither TreeView nor the future DataGrid has yet — we'll do it once for all,
    /// rather than three times in place.
    /// </remarks>
    internal void HandleKeyboardInset(int bottomPixels)
    {
        float inset = bottomPixels / Scale;

        if (Math.Abs(inset - _insetKeyboard) < 0.5f) return;

        _insetKeyboard = inset;

        if (_physicalWidth > 0)
            HandleResize(_physicalWidth, _physicalHeight);
    }

    internal void ShowSoftKeyboard(SoftKeyboardKind kind)
    {
        if (_view is null) return;

        _view.SoftKeyboardInputType = ToInputType(kind);

        // without focus on the Android side the system won't ask for
        // an InputConnection and will show the keyboard "into nowhere"
        _view.RequestFocus();

        // re-requesting the type: if the keyboard is already open, it changes
        // the layout only after the input is reconnected
        InputMethodManager? manager = GetInputMethodManager();

        manager?.RestartInput(_view);
        manager?.ShowSoftInput(_view, ShowFlags.Implicit);
    }

    internal void HideSoftKeyboard()
    {
        if (_view is null) return;

        GetInputMethodManager()?.HideSoftInputFromWindow(_view.WindowToken, HideSoftInputFlags.None);
    }

    private InputMethodManager? GetInputMethodManager() =>
        _activity.GetSystemService(Context.InputMethodService) as InputMethodManager;

    private static InputTypes ToInputType(SoftKeyboardKind kind) => kind switch
    {
        SoftKeyboardKind.Number => InputTypes.ClassNumber,
        SoftKeyboardKind.Decimal => InputTypes.ClassNumber | InputTypes.NumberFlagDecimal,
        SoftKeyboardKind.Email => InputTypes.ClassText | InputTypes.TextVariationEmailAddress,
        SoftKeyboardKind.Phone => InputTypes.ClassPhone,
        SoftKeyboardKind.Url => InputTypes.ClassText | InputTypes.TextVariationUri,
        SoftKeyboardKind.Password => InputTypes.ClassText | InputTypes.TextVariationPassword,
        _ => InputTypes.ClassText,
    };

    private void LayoutOverlay(AndroidWindow window)
    {
        Size surface = SurfaceSize;
        Size requested = window.Form.Size;

        // a form whose Size was never set has the desktop default, 800×600 —
        // here it gets the share of the surface an unsized dialog always had
        bool sized = window.Form.IsSizeSet;

        float width = !sized || requested.IsWidthAuto || requested.Width <= 0
            ? surface.Width * 0.9f
            : Math.Min(requested.Width, surface.Width);

        float height = !sized || requested.IsHeightAuto || requested.Height <= 0
            ? surface.Height * 0.4f
            : Math.Min(requested.Height, surface.Height);

        window.Form.ClientSize = new Size(width, height);
        window.Origin = new Point((surface.Width - width) / 2f, (surface.Height - height) / 2f);

        window.Form.PerformLayout();
    }

    /// <summary>Mark the surface stale. Drawing right here is doubly wrong:
    /// one action brings dozens of Invalidates, and there is nothing to draw with
    /// outside OnDraw on Android at all.</summary>
    internal void Invalidate()
    {
        // there is nowhere to draw before the surface is created
        if (_view is null) return;

        // the mark is needed only by HandleFrame, to know the frame is stale.
        // It must not replace the posting: PostInvalidateOnAnimation already merges
        // repeated calls within a frame, while a raised mark without posting means
        // a black screen forever if even one frame somehow never reached OnPaintSurface
        _paintPending = true;
        _view.PostInvalidateOnAnimation();
    }

    /// <summary>Drawing. Called from OnPaintSurface and only from there.</summary>
    internal void Render(SKSurface surface)
    {
        _paintPending = false;

        SKCanvas canvas = surface.Canvas;

        for (int i = 0; i < _windows.Count; i++)
        {
            AndroidWindow window = _windows[i];
            bool isRoot = i == 0;

            if (!isRoot)
                DimBelow(canvas);

            Skia.SkiaRenderer.Render(
                window.Form,
                canvas,
                Scale,
                clip: null,
                clearBackground: isRoot,
                origin: window.Origin);

            window.Form.TakeDirtyRegion();
        }
    }

    private void DimBelow(SKCanvas canvas)
    {
        canvas.Save();
        canvas.Scale(Scale, Scale);

        using var paint = new SKPaint { Color = s_scrim };
        canvas.DrawRect(new SKRect(0, 0, SurfaceSize.Width, SurfaceSize.Height), paint);

        canvas.Restore();
    }

    // ==== frames and the queue ====

    internal void ScheduleFrame()
    {
        // Choreographer accepts the same callback repeatedly,
        // and then two frames would come instead of one
        if (_frameScheduled) return;

        _frameScheduled = true;
        Choreographer.Instance?.PostFrameCallback(_frameCallback);
    }

    private void HandleFrame(long frameTimeNanos)
    {
        _frameScheduled = false;

        double timestampMs = frameTimeNanos / 1_000_000.0;

        // a copy: a tick may open or close a window
        foreach (AndroidWindow window in _windows.ToArray())
            window.HandleFrame(timestampMs);

        if (_paintPending)
            _view?.PostInvalidateOnAnimation();
    }

    /// <summary>Run on the UI thread — always later, through the queue.</summary>
    /// <remarks>
    /// This used to be Activity.RunOnUiThread, which runs the action right away when
    /// called on the UI thread. ZfSynchronizationContext.Post relies on a queue:
    /// await continuations ran inside the caller's own code — Task.Yield didn't yield,
    /// and the continuation after ShowDialogAsync ran inside OnWindowClosed, before
    /// the form had finished closing.
    /// </remarks>
    internal void Post(Action action) => _uiHandler.Post(action);

    internal AndroidWindow? InputTarget()
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].IsInputEnabled)
                return _windows[i];
        }

        return null;
    }

    /// <summary>Choreographer requires a descendant of Java.Lang.Object,
    /// so the callback is moved into a separate type.</summary>
    private sealed class FrameCallback(Action<long> onFrame)
        : Java.Lang.Object, Choreographer.IFrameCallback
    {
        public void DoFrame(long frameTimeNanos) => onFrame(frameTimeNanos);
    }
}