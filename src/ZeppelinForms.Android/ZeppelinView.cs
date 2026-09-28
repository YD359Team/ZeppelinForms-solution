using Android.Content;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using SkiaSharp.Views.Android;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Android;

/// <summary>The application's surface and the only receiver of touches.</summary>
public sealed class ZeppelinView : SKCanvasView
{
    private readonly AndroidPlatform _platform;

    /// <summary>The offset between EventTime (uptimeMillis) and Environment.TickCount64,
    /// which Form lives by. Taken from the first event: the hold duration is computed
    /// by subtraction, and only homogeneous values can be subtracted.</summary>
    private long? _timeOffset;

    /// <summary>The keyboard type requested by the last show.</summary>
    internal InputTypes SoftKeyboardInputType { get; set; } = InputTypes.ClassText;

    /// <summary>Without an InputConnection the software keyboard appears,
    /// but what is typed doesn't reach the application: the IME talks to
    /// the receiver only through it.</summary>
    public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
    {
        if (outAttrs is not null)
        {
            outAttrs.InputType = SoftKeyboardInputType;
            outAttrs.ImeOptions = ImeFlags.NoFullscreen | ImeFlags.NoExtractUi;
        }

        AndroidWindow? target = _platform.InputTarget();

        return target is null ? null : new ZeppelinInputConnection(this, target.Form);
    }

    public override bool OnCheckIsTextEditor() => true;

    internal ZeppelinView(Context context, AndroidPlatform platform) : base(context)
    {
        _platform = platform;

        Focusable = true;
        FocusableInTouchMode = true;
    }

    public override WindowInsets? OnApplyWindowInsets(WindowInsets? insets)
    {
        if (insets is not null)
        {
            // from API 30 there is a typed query; below it — deprecated properties,
            // but on 24..29 there are no others, and cutouts already exist there
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                global::Android.Graphics.Insets bars = insets.GetInsets(
                    WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());

                _platform.HandleInsets(bars.Left, bars.Top, bars.Right, bars.Bottom);

                // the keyboard is a separate inset, not part of the system bars:
                // the form must shrink under it rather than just move away from
                // the edge, and its height changes independently of cutouts
                global::Android.Graphics.Insets ime = insets.GetInsets(WindowInsets.Type.Ime());

                _platform.HandleKeyboardInset(ime.Bottom);
            }
            else
            {
                // before API 30 there is no separate IME query at all: with
                // adjustResize the system subtracts the keyboard from the system
                // insets itself, and they arrive already accounting for it
                _platform.HandleInsets(
                    insets.SystemWindowInsetLeft,
                    insets.SystemWindowInsetTop,
                    insets.SystemWindowInsetRight,
                    insets.SystemWindowInsetBottom);

                _platform.HandleKeyboardInset(0);
            }
        }

        return base.OnApplyWindowInsets(insets);
    }

    public override bool OnGenericMotionEvent(MotionEvent? e)
    {
        if (e is null) return false;

        AndroidWindow? target = _platform.InputTarget();
        if (target is null) return false;

        float scale = _platform.Scale;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Scroll:
                {
                    // the axis comes in wheel "notches", the form expects numbers in
                    // the same units as Win32: one notch there is 120
                    float vertical = e.GetAxisValue(Axis.Vscroll);
                    float horizontal = e.GetAxisValue(Axis.Hscroll);

                    target.HandleWheel(
                        e.GetX() / scale,
                        e.GetY() / scale,
                        (int)(vertical * 120),
                        (int)(horizontal * 120));

                    return true;
                }

            case MotionEventActions.HoverMove:
            case MotionEventActions.HoverEnter:
                target.HandleHoverMove(e.GetX() / scale, e.GetY() / scale, ToTicks(e.EventTime));
                return true;

            case MotionEventActions.HoverExit:
                target.HandlePointerLeave();
                return true;

            default:
                return false;
        }
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);

        _platform.HandleResize(w, h);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        _platform.Render(e.Surface);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;

        AndroidWindow? target = _platform.InputTarget();
        if (target is null) return false;

        float scale = _platform.Scale;
        long timestamp = ToTicks(e.EventTime);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
                Dispatch(target.HandleTouchDown, e, e.ActionIndex, scale, timestamp);
                break;

            case MotionEventActions.Move:
                // ACTION_MOVE carries all live contacts at once, not only the one
                // that moved, and ActionIndex is undefined for it. Each must be
                // walked, otherwise the second finger would freeze in place
                for (int index = 0; index < e.PointerCount; index++)
                    Dispatch(target.HandleTouchMove, e, index, scale, timestamp);
                break;

            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                Dispatch(target.HandleTouchUp, e, e.ActionIndex, scale, timestamp);
                break;

            case MotionEventActions.Cancel:
                // all contacts are cancelled at once: the system took the gesture
                for (int index = 0; index < e.PointerCount; index++)
                    target.HandleTouchCancel(e.GetPointerId(index), e.GetToolType(index));
                break;

            default:
                return false;
        }

        return true;
    }

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (e is null) return false;

        // "back" is not a key but a system gesture: we ask the application and,
        // if it didn't take it, let the system close the activity
        if (keyCode == Keycode.Back)
        {
            return _platform.RaiseBackRequested() || base.OnKeyDown(keyCode, e);
        }

        AndroidWindow? target = _platform.InputTarget();
        if (target is null) return false;

        KeyModifiers modifiers = AndroidKeyMap.ToModifiers(e.MetaState);
        Key key = AndroidKeyMap.ToKey(keyCode);

        if (key != Key.None)
            target.Form.OnKeyDown(key, modifiers, e.RepeatCount > 0);

        // the character goes separately from the key, as WM_CHAR separately from
        // WM_KEYDOWN. Control characters are cut off: Backspace and Enter already
        // went above as keys, and inserting them as text too means getting them twice.
        // A character beyond the BMP is split into its surrogate pair rather than
        // cut down to one char
        int unicode = e.UnicodeChar;

        if (unicode is > 0x1F and not 0x7F and <= 0x10FFFF)
        {
            foreach (char c in char.ConvertFromUtf32(unicode))
                target.Form.OnTextInput(c);
        }

        return key != Key.None || unicode > 0;
    }

    public override bool OnKeyUp(Keycode keyCode, KeyEvent? e)
    {
        if (e is null || keyCode == Keycode.Back) return base.OnKeyUp(keyCode, e);

        AndroidWindow? target = _platform.InputTarget();
        if (target is null) return false;

        Key key = AndroidKeyMap.ToKey(keyCode);
        if (key == Key.None) return false;

        target.Form.OnKeyUp(key, AndroidKeyMap.ToModifiers(e.MetaState));

        return true;
    }

    private delegate void TouchHandler(
        float x, float y, int pointerId, MotionEventToolType toolType, float pressure, long timestamp);

    private static void Dispatch(
        TouchHandler handler, MotionEvent e, int index, float scale, long timestamp)
    {
        // the coordinates come in physical pixels, the form lives in logical ones
        handler(
            e.GetX(index) / scale,
            e.GetY(index) / scale,
            e.GetPointerId(index),
            e.GetToolType(index),
            e.GetPressure(index),
            timestamp);
    }

    private long ToTicks(long eventTimeMs)
    {
        _timeOffset ??= Environment.TickCount64 - eventTimeMs;

        return _timeOffset.Value + eventTimeMs;
    }
}