using ZeppelinForms.Forms;

namespace ZeppelinForms.Windows;

public class WindowsPlatform : IPlatform
{
    private int _windowCount = 0;

    public IPlatformWindow CreateWindow(Form form)
    {
        var window = new Win32Window(this, form);
        window.Create();
        form.PlatformWindow = window;
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

    public void Exit()
    {
        throw new NotImplementedException();
    }

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
}
