using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Browser;

/// <summary>
/// Платформа браузера. INestedLoopSupport не реализует: заблокировать поток
/// и продолжать получать события в браузере невозможно, поэтому Form.ShowDialog
/// здесь честно бросит исключение — работает только ShowDialogAsync.
/// </summary>
public sealed class BrowserPlatform : IPlatform, IAppLifecycle
{
    public event EventHandler? Paused;
    public event EventHandler? Resumed;
    public event EventHandler? Saving;

    internal static BrowserPlatform? Current { get; private set; }

    private readonly string _canvasId;
    private BrowserWindow? _window;

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

    public IPlatformWindow CreateWindow(Form form)
    {
        // canvas один, наложением форм друг на друга никто пока не занимается.
        // Пока это так, диалоги в браузере не работают — ни синхронные,
        // ни асинхронные: ShowDialogAsync тоже идёт через CreateWindow
        if (_window is not null)
            throw new NotSupportedException(
                "В браузере поддержано одно окно. Наложение форм на общий canvas ещё не сделано.");

        var window = new BrowserWindow(form);

        _window = window;
        Interop.Window = window;
        form.PlatformWindow = window;
        form.Platform = this;

        Interop.Init(_canvasId);
        Interop.SetTitle(form.Title ?? string.Empty);

        return window;
    }

    /// <summary>Циклом владеет браузер, поэтому возвращает управление сразу.
    /// Дальше всё происходит в обработчиках событий и rAF.</summary>
    public void Start() { }

    public void Exit() => _window?.Close();

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