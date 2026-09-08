namespace ZeppelinForms.Windows;

/// <summary>Кадры через таймер окна: WM_TIMER приходит в тот же цикл
/// сообщений, что и ввод, поэтому отдельной синхронизации не нужно.</summary>
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

    /// <summary>Одиночный кадр — это просто пометка окна грязным:
    /// WM_PAINT придёт сам, отдельного механизма не требуется.</summary>
    public void RequestFrame()
    {
        nint hwnd = handle();

        if (hwnd != 0) NativeMethods.InvalidateRect(hwnd, 0, false);
    }
}
