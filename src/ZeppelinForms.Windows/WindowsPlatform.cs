using ZeppelinForms.Forms;

namespace ZeppelinForms.Windows;

public class WindowsPlatform : IPlatform
{
    private int _windowCount = 0;

    public WindowsPlatform()
    {
        NativeMethods.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        Skia.SkiaImageDecoder.Register();
        Skia.SkiaTextMeasurer.Register();
        Skia.SkiaOffscreenRenderer.Register();
        Win32Clipboard.Register();

        ZeppelinForms.Skia.SkiaImageDecoder.Register();
        ZeppelinForms.Skia.SkiaTextMeasurer.Register();
        ZeppelinForms.Skia.SkiaOffscreenRenderer.Register();
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

    public void Run()
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
