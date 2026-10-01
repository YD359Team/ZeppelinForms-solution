using ZeppelinForms.Animation;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Windows;

public partial class WindowsPlatform : IPlatform, INestedLoopSupport, ISystemMotionSettings
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

        // without it Displays returned the built-in 1920×1080 stand-in at 96 DPI
        // on Windows: windows were centered on a monitor that doesn't exist,
        // and gesture thresholds ignored the real pixel density
        Win32DisplayProvider.Register();

        _reducedMotion = QueryReducedMotion();
        Motion.UseSystemSettings(this);

        InitAppearance();
    }

    public bool PrefersReducedMotion => _reducedMotion;

    public event EventHandler? Changed;

    /// <summary>"Show animations in Windows" is off — that means motion should be
    /// reduced. If the query fails, we assume it isn't asked for: this way the
    /// application behaves as before rather than silently losing its animation.</summary>
    private static bool QueryReducedMotion() =>
        NativeMethods.SystemParametersInfo(
            NativeConstants.SPI_GETCLIENTAREAANIMATION, 0, out int enabled, 0)
        && enabled == 0;

    /// <summary>WM_SETTINGCHANGE arrived. It is broadcast to all top-level windows,
    /// so the setting is re-read and only a real change is reported — there may be
    /// several windows.</summary>
    internal void OnSystemSettingsChanged()
    {
        // first, and on its own: the motion check below returns early
        // when nothing changed there
        RefreshAppearance();

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

    /// <remarks>
    /// GetMessage returns 0 on WM_QUIT and takes it off the queue. The nested loop
    /// used to simply exit on it, and the outer loop never saw the quit: Exit() or
    /// the last window closing during a modal dialog didn't end the application.
    /// Now the nested loop posts WM_QUIT again for the outer loop to see.
    /// </remarks>
    public void RunNestedLoop(IPlatformWindow until)
    {
        var window = (Win32Window)until;

        // WM_NCDESTROY resets Handle, and the next check lets us out
        while (window.Handle != 0)
        {
            int result = NativeMethods.GetMessage(out NativeMethods.MSG message, 0, 0, 0);

            if (result == 0)
            {
                // WM_QUIT: hand it on to the outer loop with the same exit code
                NativeMethods.PostQuitMessage((int)message.wParam);
                return;
            }

            // -1 — an error, the handle is invalid; there is nothing to pump anymore
            if (result < 0) return;

            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }
}