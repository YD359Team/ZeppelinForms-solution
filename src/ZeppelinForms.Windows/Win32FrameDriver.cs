namespace ZeppelinForms.Windows;

/// <summary>Frames through the window's timer: WM_TIMER comes into the same
/// message loop as input, so no separate synchronization is needed.</summary>
internal sealed class Win32FrameDriver(Func<nint> handle) : IFrameDriver
{
    public bool IsRunning { get; private set; }

    public void Start(int intervalMs)
    {
        nint hwnd = handle();

        if (IsRunning || hwnd == 0) return;

        NativeMethods.SetTimer(hwnd, NativeConstants.AnimationTimerId, (uint)intervalMs, 0);
        IsRunning = true;
    }

    public void Stop()
    {
        nint hwnd = handle();

        if (!IsRunning || hwnd == 0) return;

        NativeMethods.KillTimer(hwnd, NativeConstants.AnimationTimerId);
        IsRunning = false;
    }

    /// <summary>A single frame is simply marking the window dirty:
    /// WM_PAINT comes by itself, no separate mechanism is needed.</summary>
    public void RequestFrame()
    {
        nint hwnd = handle();

        if (hwnd != 0) NativeMethods.InvalidateRect(hwnd, 0, false);
    }
}