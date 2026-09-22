using ZeppelinForms.Animation;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Windows;

public class WindowsPlatform : IPlatform, INestedLoopSupport, ISystemMotionSettings
{
    private int _windowCount = 0;
    private bool _reducedMotion;

    public WindowsPlatform()
    {
        NativeMethods.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        ZeppelinForms.Skia.SkiaImageDecoder.Register();
        ZeppelinForms.Skia.SkiaTextMeasurer.Register();
        ZeppelinForms.Skia.SkiaOffscreenRenderer.Register();
        Win32Clipboard.Register();

        _reducedMotion = QueryReducedMotion();
        Motion.UseSystemSettings(this);
    }

    public bool PrefersReducedMotion => _reducedMotion;

    public event EventHandler? Changed;

    /// <summary>«Показывать анимацию в Windows» выключен — значит просят
    /// уменьшить движение. Если запрос не удался, считаем, что не просят:
    /// так приложение ведёт себя как раньше, а не теряет анимацию молча.</summary>
    private static bool QueryReducedMotion() =>
        NativeMethods.SystemParametersInfo(
            NativeConstants.SPI_GETCLIENTAREAANIMATION, 0, out int enabled, 0)
        && enabled == 0;

    /// <summary>Пришёл WM_SETTINGCHANGE. Его рассылают всем окнам верхнего
    /// уровня, поэтому перечитываем настройку и сообщаем только о настоящей
    /// смене — окон может быть несколько.</summary>
    internal void OnSystemSettingsChanged()
    {
        bool reduced = QueryReducedMotion();

        if (reduced == _reducedMotion) return;

        _reducedMotion = reduced;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IPlatformWindow CreateWindow(Form form)
    {
        var window = new Win32Window(this, form);
        window.Create();
        form.PlatformWindow = window;
        form.Platform = this;
        _windowCount++;
        return window;
    }

    internal void WindowDestroyed()
    {
        if (_windowCount <= 0)
            return;

        _windowCount--;

        if (_windowCount == 0)
            NativeMethods.PostQuitMessage(0);
    }

    public void Exit() => NativeMethods.PostQuitMessage(0);

    public void Start()
    {
        while (NativeMethods.GetMessage(
            out NativeMethods.MSG message,
            0,
            0,
            0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }

    public void RunNestedLoop(IPlatformWindow until)
    {
        var window = (Win32Window)until;

        // WM_NCDESTROY обнулит Handle, и следующая проверка выпустит нас наружу
        while (window.Handle != 0 &&
               NativeMethods.GetMessage(out NativeMethods.MSG message, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }
}