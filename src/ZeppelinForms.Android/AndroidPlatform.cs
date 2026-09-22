using Android.Content;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using SkiaSharp;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Android;

/// <summary>
/// Платформа Android. Поверхность одна на всё приложение, поэтому окна
/// здесь — слои на ней, а не окна системы.
///
/// INestedLoopSupport не реализует: заблокировать поток UI и продолжать
/// получать события нельзя, поэтому Form.ShowDialog честно бросит
/// исключение — работает только ShowDialogAsync.
/// </summary>
public sealed class AndroidPlatform : IPlatform, ISystemMotionSettings
{
    /// <summary>Нажата системная кнопка или жест «назад».
    /// Установите Handled, чтобы система не закрывала активность.</summary>
    /// <remarks>
    /// Не маплю её на Escape намеренно: Escape в форме обрабатывают многие,
    /// и результат мы не видим — OnKeyDown ничего не возвращает. Тогда
    /// оказалось бы невозможно отличить «диалог закрылся» от «никто
    /// не взялся», и приложение выходило бы при каждом нажатии.
    /// </remarks>
    public event EventHandler<BackRequestedEventArgs>? BackRequested;

    internal bool RaiseBackRequested()
    {
        if (BackRequested is null) return false;

        var args = new BackRequestedEventArgs();
        BackRequested(this, args);

        return args.Handled;
    }

    /// <summary>Затемнение под модальным диалогом.</summary>
    private static readonly SKColor s_scrim = new(0, 0, 0, 96);

    private readonly Activity _activity;
    private readonly List<AndroidWindow> _windows = [];
    private readonly FrameCallback _frameCallback;

    private ZeppelinView? _view;

    // отступы под системные панели и вырез, в логических единицах
    private float _insetLeft;
    private float _insetTop;
    private float _insetRight;
    private float _insetBottom;
    private int _physicalWidth;
    private int _physicalHeight;
    private bool _paintPending;
    private bool _frameScheduled;

    private float _insetKeyboard;

    private int _probeFrames = 10;

    private AndroidPlatform(Activity activity)
    {
        _activity = activity;
        _frameCallback = new FrameCallback(HandleFrame);
    }

    /// <summary>Масштаб длительности анимаций, выставленный в ноль, — так
    /// на Android выглядит «удалить анимацию» в специальных возможностях.
    /// Читается при запуске: подписка на смену настройки требует
    /// ContentObserver, а менять её посреди работы приложения никто
    /// в здравом уме не станет.</summary>
    public bool PrefersReducedMotion { get; private set; }

    event EventHandler? ISystemMotionSettings.Changed
    {
        add { }
        remove { }
    }

    public static AndroidPlatform Create(Activity activity)
    {
        Skia.SkiaImageDecoder.Register();
        Skia.SkiaTextMeasurer.Register();
        Skia.SkiaOffscreenRenderer.Register();
        Displays.Current = new AndroidDisplayProvider(activity);

        var platform = new AndroidPlatform(activity)
        {
            // global:: обязателен: внутри ZeppelinForms.Android имя Android
            // указывает на наше же пространство имён, а не на привязки SDK
            PrefersReducedMotion = global::Android.Provider.Settings.Global.GetFloat(
                activity.ContentResolver,
                global::Android.Provider.Settings.Global.AnimatorDurationScale,
                1f) == 0f,
        };

        ZeppelinForms.Animation.Motion.UseSystemSettings(platform);

        return platform;
    }

    internal float Scale { get; private set; } = 1f;

    /// <summary>Размер поверхности в логических единицах.</summary>
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

            // явно, а не полагаясь на умолчание SetContentView: SKCanvasView
            // приносит свои LayoutParams, и при WRAP_CONTENT умолчательный
            // View.onMeasure меряет его в ноль на ноль — визуально это
            // неотличимо от неработающего рисования
            _view.LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent);

            _activity.SetContentView(_view);

            // размер придёт в OnSizeChanged: до него раскладывать нечего

            System.Diagnostics.Debug.WriteLine(
                $"ZF: SetContentView выполнен, масштаб {Scale}");
        }
        else
        {
            LayoutOverlay(window);
            Invalidate();
        }

        return window;
    }

    /// <summary>Циклом владеет система, поэтому возвращает управление сразу.</summary>
    public void Start() { }

    public void Exit()
    {
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

    // ==== поверхность ====

    internal void HandleResize(int physicalWidth, int physicalHeight)
    {
        System.Diagnostics.Debug.WriteLine($"ZF: HandleResize {physicalWidth}x{physicalHeight}");

        Scale = _activity.Resources?.DisplayMetrics?.Density ?? 1f;

        _physicalWidth = physicalWidth;
        _physicalHeight = physicalHeight;

        if (Root is { } root)
        {
            Size surface = SurfaceSize;

            // корневая форма живёт внутри безопасной области, а не во всём
            // окне: при targetSdk 35 Android рисует содержимое под строкой
            // состояния всегда, и отказаться от этого нельзя.
            // Сдвиг идёт через Origin, поэтому касания приходят туда же,
            // куда нарисовано, — ToLocal его вычитает
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

    /// <summary>Безопасная область изменилась: появилась клавиатура,
    /// повернули экран, поехала жестовая панель.</summary>
    internal void HandleInsets(int left, int top, int right, int bottom)
    {
        System.Diagnostics.Debug.WriteLine($"ZF: HandleInsets {left},{top},{right},{bottom}");

        _insetLeft = left / Scale;
        _insetTop = top / Scale;
        _insetRight = right / Scale;
        _insetBottom = bottom / Scale;

        // до первого OnSizeChanged раскладывать нечего
        if (_physicalWidth > 0)
            HandleResize(_physicalWidth, _physicalHeight);
    }

    /// <summary>Клавиатура открылась или закрылась.</summary>
    /// <remarks>
    /// Форму под ней просто ужимаем. Правильнее было бы ещё и подтянуть
    /// поле с фокусом в видимую часть, но для этого нужен ScrollIntoView,
    /// которого пока нет ни у TreeView, ни у будущего DataGrid — сделаем
    /// один раз для всех, а не трижды по месту.
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

        // без фокуса на стороне Android система не спросит InputConnection
        // и покажет клавиатуру «в никуда»
        _view.RequestFocus();

        // перезапрос типа: если клавиатура уже открыта, раскладку она
        // сменит только после переподключения ввода
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

        float width = requested.IsWidthAuto || requested.Width <= 0
            ? surface.Width * 0.9f
            : Math.Min(requested.Width, surface.Width);

        float height = requested.IsHeightAuto || requested.Height <= 0
            ? surface.Height * 0.4f
            : Math.Min(requested.Height, surface.Height);

        window.Form.ClientSize = new Size(width, height);
        window.Origin = new Point((surface.Width - width) / 2f, (surface.Height - height) / 2f);

        window.Form.PerformLayout();
    }

    /// <summary>Пометить поверхность устаревшей. Рисовать прямо здесь нельзя
    /// вдвойне: за одно действие Invalidate прилетает десятки раз,
    /// и рисовать вне OnDraw на Android в принципе нечем.</summary>
    internal void Invalidate()
    {
        // до создания поверхности рисовать некуда
        if (_view is null) return;

        // метка нужна только HandleFrame, чтобы знать, что кадр устарел.
        // Заменять ею отправку нельзя: PostInvalidateOnAnimation и так
        // склеивает повторные вызовы внутри кадра, а взведённая метка
        // без отправки — это чёрный экран навсегда, если хоть один кадр
        // почему-то не дошёл до OnPaintSurface
        _paintPending = true;
        _view.PostInvalidateOnAnimation();
    }

    /// <summary>Рисование. Зовётся из OnPaintSurface и только оттуда.</summary>
    internal void Render(SKSurface surface)
    {
        System.Diagnostics.Debug.WriteLine($"ZF: Render, окон {_windows.Count}, поверхность {_physicalWidth}x{_physicalHeight}, масштаб {Scale}");

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

    // ==== кадры и очередь ====

    internal void ScheduleFrame()
    {
        // Choreographer принимает один и тот же обратный вызов повторно,
        // и тогда кадров придёт два вместо одного
        if (_frameScheduled) return;

        _frameScheduled = true;
        Choreographer.Instance?.PostFrameCallback(_frameCallback);
    }

    private void HandleFrame(long frameTimeNanos)
    {
        if (_probeFrames > 0 && _view is not null)
        {
            _probeFrames--;

            System.Diagnostics.Debug.WriteLine(
                $"ZF: вью attached={_view.IsAttachedToWindow} " +
                $"размер={_view.Width}x{_view.Height} " +
                $"родитель={_view.Parent?.GetType().Name ?? "нет"} " +
                $"видимость={_view.Visibility}");
        }

        _frameScheduled = false;

        double timestampMs = frameTimeNanos / 1_000_000.0;

        // копия: тик может открыть или закрыть окно
        foreach (AndroidWindow window in _windows.ToArray())
            window.HandleFrame(timestampMs);

        if (_paintPending)
            _view?.PostInvalidateOnAnimation();
    }

    /// <summary>Выполнить в потоке UI. На Android это очередь самого View,
    /// своей заводить незачем.</summary>
    internal void Post(Action action) => _activity.RunOnUiThread(action);

    internal AndroidWindow? InputTarget()
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].IsInputEnabled)
                return _windows[i];
        }

        return null;
    }

    /// <summary>Choreographer требует наследника Java.Lang.Object,
    /// поэтому обратный вызов вынесен в отдельный тип.</summary>
    private sealed class FrameCallback(Action<long> onFrame)
        : Java.Lang.Object, Choreographer.IFrameCallback
    {
        public void DoFrame(long frameTimeNanos) => onFrame(frameTimeNanos);
    }
}