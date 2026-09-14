using Android.App;
using Android.Content;
using Android.Views;
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
public sealed class AndroidPlatform : IPlatform
{
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

        return new AndroidPlatform(activity);
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
            _activity.SetContentView(_view);

            // размер придёт в OnSizeChanged: до него раскладывать нечего
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
                Math.Max(0, surface.Height - _insetTop - _insetBottom));

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
        _insetLeft = left / Scale;
        _insetTop = top / Scale;
        _insetRight = right / Scale;
        _insetBottom = bottom / Scale;

        // до первого OnSizeChanged раскладывать нечего
        if (_physicalWidth > 0)
            HandleResize(_physicalWidth, _physicalHeight);
    }

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
        // до создания поверхности рисовать некуда, и метку ставить нельзя:
        // взведённая без отправки, она закрыла бы все последующие вызовы
        // навсегда — экран так и остался бы чёрным
        if (_view is null) return;

        if (_paintPending) return;

        _paintPending = true;
        _view.PostInvalidateOnAnimation();
    }

    /// <summary>Рисование. Зовётся из OnPaintSurface и только оттуда.</summary>
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