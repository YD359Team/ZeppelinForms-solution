using Android.Content;
using Android.Views;
using SkiaSharp.Views.Android;

namespace ZeppelinForms.Android;

/// <summary>Поверхность приложения и единственный приёмник касаний.</summary>
public sealed class ZeppelinView : SKCanvasView
{
    private readonly AndroidPlatform _platform;

    /// <summary>Сдвиг между EventTime (uptimeMillis) и Environment.TickCount64,
    /// которым живёт Form. Берётся по первому событию: длительность удержания
    /// считается вычитанием, а вычитать можно только однородные величины.</summary>
    private long? _timeOffset;

    internal ZeppelinView(Context context, AndroidPlatform platform) : base(context)
    {
        _platform = platform;

        Focusable = true;
        FocusableInTouchMode = true;

        System.Diagnostics.Debug.WriteLine("ZF: ZeppelinView создан");
    }

    public override WindowInsets? OnApplyWindowInsets(WindowInsets? insets)
    {
        if (insets is not null)
        {
            // с API 30 есть типизированный запрос; ниже — устаревшие
            // свойства, но на 24..29 других нет, а вырез там уже бывает
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                global::Android.Graphics.Insets bars = insets.GetInsets(
                    WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());

                _platform.HandleInsets(bars.Left, bars.Top, bars.Right, bars.Bottom);
            }
            else
            {
                _platform.HandleInsets(
                    insets.SystemWindowInsetLeft,
                    insets.SystemWindowInsetTop,
                    insets.SystemWindowInsetRight,
                    insets.SystemWindowInsetBottom);
            }
        }

        return base.OnApplyWindowInsets(insets);
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
                // ACTION_MOVE несёт все живые контакты сразу, а не только
                // сдвинувшийся, и ActionIndex для него не определён.
                // Обойти надо каждый, иначе второй палец замрёт на месте
                for (int index = 0; index < e.PointerCount; index++)
                    Dispatch(target.HandleTouchMove, e, index, scale, timestamp);
                break;

            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                Dispatch(target.HandleTouchUp, e, e.ActionIndex, scale, timestamp);
                break;

            case MotionEventActions.Cancel:
                // отменяются все контакты разом: жест забрала система
                for (int index = 0; index < e.PointerCount; index++)
                    target.HandleTouchCancel(e.GetPointerId(index), e.GetToolType(index));
                break;

            default:
                return false;
        }

        return true;
    }

    private delegate void TouchHandler(
        float x, float y, int pointerId, MotionEventToolType toolType, float pressure, long timestamp);

    private static void Dispatch(
        TouchHandler handler, MotionEvent e, int index, float scale, long timestamp)
    {
        // координаты приходят в физических пикселях, форма живёт в логических
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