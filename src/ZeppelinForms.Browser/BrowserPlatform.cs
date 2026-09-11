using SkiaSharp;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Browser;

/// <summary>
/// Платформа браузера. Canvas один на всё приложение, поэтому окна здесь —
/// слои на общей поверхности, а не отдельные окна системы: поверхность,
/// очередь Invoke и раздача ввода живут в платформе, окна только знают
/// своё место на ней.
///
/// INestedLoopSupport не реализует: заблокировать поток и продолжать
/// получать события в браузере невозможно, поэтому Form.ShowDialog честно
/// бросит исключение — работает только ShowDialogAsync.
/// </summary>
public sealed class BrowserPlatform : IPlatform, IAppLifecycle
{
    /// <summary>Затемнение под модальным диалогом. Своих окон у браузера нет,
    /// и без этого непонятно, что нижняя форма перестала принимать ввод.</summary>
    private static readonly SKColor s_scrim = new(0, 0, 0, 96);

    public event EventHandler? Paused;
    public event EventHandler? Resumed;
    public event EventHandler? Saving;

    internal static BrowserPlatform? Current { get; private set; }

    private readonly string _canvasId;
    private readonly BrowserSkiaSurface _surface = new();
    private readonly List<BrowserWindow> _windows = [];
    private readonly Queue<Action> _invokeQueue = new();

    private int _physicalWidth;
    private int _physicalHeight;
    private bool _initialized;
    private bool _paintPending;

    private BrowserPlatform(string canvasId)
    {
        _canvasId = canvasId;
    }

    /// <summary>
    /// Модуль zf регистрирует загрузчик страницы через setModuleImports —
    /// так путь к нему остаётся в main.js, а не зависит от того, куда
    /// SDK решит положить дополнительные файлы.
    /// </summary>
    public static BrowserPlatform Create(string canvasId = "zf-canvas")
    {
        Skia.SkiaImageDecoder.Register();
        Skia.SkiaTextMeasurer.Register();
        Skia.SkiaOffscreenRenderer.Register();
        BrowserClipboard.Register();
        Displays.Current = new BrowserDisplayProvider();

        var platform = new BrowserPlatform(canvasId);
        Current = platform;

        return platform;
    }

    /// <summary>
    /// Забрать файлы с сервера и положить в виртуальную ФС по тем же путям.
    /// Нужно всему, что читает с диска синхронно — Image.LoadAsset,
    /// Font.WithFile, — потому что в браузере скачать по ходу дела нельзя:
    /// fetch асинхронный, а эти вызовы ждать не умеют.
    ///
    /// Пути абсолютные и совпадают с адресами на сервере: "/Assets/x.png"
    /// скачивается из wwwroot/Assets/x.png и туда же ложится в ФС.
    /// </summary>
    public static async Task PreloadAsync(params string[] paths)
    {
        using HttpClient http = new() { BaseAddress = new Uri(Interop.BaseUri()) };

        foreach (string path in paths)
        {
            // ведущий слэш увёл бы запрос в корень сайта мимо базового адреса
            byte[] bytes = await http.GetByteArrayAsync(path.TrimStart('/'));

            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(path, bytes);
        }
    }

    internal float Scale { get; private set; } = 1f;

    /// <summary>Размер холста в логических единицах.</summary>
    internal Size CanvasSize => new(_physicalWidth / Scale, _physicalHeight / Scale);

    /// <summary>Нижняя форма занимает холст целиком; всё, что поверх неё —
    /// диалоги.</summary>
    private BrowserWindow? Root => _windows.Count > 0 ? _windows[0] : null;

    public IPlatformWindow CreateWindow(Form form)
    {
        var window = new BrowserWindow(this, form);

        _windows.Add(window);
        form.PlatformWindow = window;
        form.Platform = this;

        if (!_initialized)
        {
            _initialized = true;
            Interop.Platform = this;

            // init сам вызовет resize, а тот — HandleResize: размер холста
            // до этого момента неизвестен, раскладывать нечего
            Interop.Init(_canvasId);
            Interop.SetTitle(form.Title ?? string.Empty);
            if (form.Icon is { } icon)
            {
                // ICO браузеры принимают, и data-адрес избавляет от отдельного
                // файла в wwwroot — источник иконки остаётся один
                string base64 = Convert.ToBase64String(icon.GetRawData());
                Interop.SetFavicon($"data:image/x-icon;base64,{base64}");
            }
        }
        else
        {
            LayoutOverlay(window);
            Paint();
        }

        return window;
    }

    internal void BringToFront(BrowserWindow window)
    {
        // нижнюю форму наверх не поднимаем: она растянута на холст,
        // и диалоги под ней стали бы невидимыми
        if (_windows.Count < 2 || _windows[0] == window) return;
        if (_windows[^1] == window) return;

        _windows.Remove(window);
        _windows.Add(window);

        Paint();
    }

    internal void Remove(BrowserWindow window)
    {
        if (!_windows.Remove(window)) return;

        Paint();
    }

    /// <summary>Циклом владеет браузер, поэтому возвращает управление сразу.
    /// Дальше всё происходит в обработчиках событий и rAF.</summary>
    public void Start() { }

    public void Exit()
    {
        // с конца: закрытие снимает окно со списка
        for (int i = _windows.Count - 1; i >= 0; i--)
            _windows[i].Close();
    }

    // ==== поверхность ====

    internal void HandleResize(int physicalWidth, int physicalHeight, float scale)
    {
        Scale = scale <= 0 ? 1f : scale;
        _physicalWidth = physicalWidth;
        _physicalHeight = physicalHeight;

        _surface.Resize(physicalWidth, physicalHeight);

        if (Root is { } root)
        {
            root.Form.ClientSize = CanvasSize;
            root.Form.PerformLayout();
        }

        // диалоги привязаны к центру холста, а он только что переехал
        for (int i = 1; i < _windows.Count; i++)
            LayoutOverlay(_windows[i]);

        Paint();
    }

    /// <summary>Диалог держит собственный размер и встаёт по центру.
    /// Если он не помещается, ужимается до холста: деваться ему некуда,
    /// за края canvas ничего не видно.</summary>
    private void LayoutOverlay(BrowserWindow window)
    {
        Size canvas = CanvasSize;
        Size requested = window.Form.Size;

        float width = requested.IsWidthAuto || requested.Width <= 0
            ? canvas.Width * 0.6f
            : Math.Min(requested.Width, canvas.Width);

        float height = requested.IsHeightAuto || requested.Height <= 0
            ? canvas.Height * 0.4f
            : Math.Min(requested.Height, canvas.Height);

        window.Form.ClientSize = new Size(width, height);
        window.Origin = new Point((canvas.Width - width) / 2f, (canvas.Height - height) / 2f);

        window.Form.PerformLayout();
    }

    /// <summary>Пометить холст устаревшим. Рисовать прямо здесь нельзя:
    /// за одно действие пользователя Invalidate прилетает десятки раз —
    /// от раскладки, от загрузки картинки, от каждого контрола, — а кадр
    /// в браузере полный, с копированием всего буфера в ImageData.
    /// Поэтому копим и рисуем один раз в rAF.</summary>
    internal void Invalidate()
    {
        if (_paintPending) return;

        _paintPending = true;
        Interop.RequestFrame();
    }

    private void Paint()
    {
        if (_surface.BeginFrame() is not SKSurface surface) return;

        SKCanvas canvas = surface.Canvas;

        for (int i = 0; i < _windows.Count; i++)
        {
            BrowserWindow window = _windows[i];
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

        _surface.EndFrame();
    }

    private void DimBelow(SKCanvas canvas)
    {
        canvas.Save();
        canvas.Scale(Scale, Scale);

        using var paint = new SKPaint { Color = s_scrim };
        canvas.DrawRect(new SKRect(0, 0, CanvasSize.Width, CanvasSize.Height), paint);

        canvas.Restore();
    }

    // ==== кадры и очередь ====

    internal void HandleFrame(double timestampMs)
    {
        // копия: тик может открыть или закрыть окно
        foreach (BrowserWindow window in _windows.ToArray())
            window.HandleFrame(timestampMs);

        // тик анимации помечает холст устаревшим — проверяем после него
        if (!_paintPending) return;

        _paintPending = false;
        Paint();
    }

    internal void Enqueue(Action action)
    {
        _invokeQueue.Enqueue(action);
        Interop.ScheduleDrain();
    }

    internal void DrainInvokes()
    {
        // Count фиксируем заранее: действие может поставить в очередь новое,
        // и без этого разбор очереди мог бы не кончиться никогда
        int pending = _invokeQueue.Count;

        for (int i = 0; i < pending; i++)
            _invokeQueue.Dequeue()();
    }

    // ==== ввод ====

    /// <summary>Ввод получает верхнее окно, принимающее его. Владелец диалога
    /// в это время заглушён через SetEnabled, поэтому отдельной проверки
    /// на модальность не нужно.</summary>
    internal BrowserWindow? InputTarget()
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].IsInputEnabled)
                return _windows[i];
        }

        return null;
    }

    internal void HandleVisibilityChange(bool visible)
    {
        if (visible)
            Resumed?.Invoke(this, EventArgs.Empty);
        else
            Paused?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>pagehide, а не beforeunload: на мобильных браузерах второй
    /// часто не приходит вовсе, а первый — последняя гарантированная точка.</summary>
    internal void HandlePageHide() => Saving?.Invoke(this, EventArgs.Empty);
}