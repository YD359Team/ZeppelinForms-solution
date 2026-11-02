using static ZeppelinForms.Windows.NativeMethods;

namespace ZeppelinForms.Windows;

/// <summary>Whether the window can be seen: minimized, or cloaked by DWM on another
/// virtual desktop. Out of sight, the form asks for no frames.</summary>
/// <remarks>
/// <para>
/// Until 0.21 the frame timer ticked on in a minimized window: every running animation
/// was advanced sixty times a second, and a spinner of an application left minimized
/// for the day kept a core busy for nothing.
/// </para>
/// <para>
/// A window under other windows is still "in sight": Windows tells nobody about that,
/// and Chromium's own occlusion tracking walks every top-level window of the desktop to
/// find out. Not worth it for a frame timer.
/// </para>
/// </remarks>
internal sealed partial class Win32Window
{
    // one hook per UI thread: the events come through the message loop of the thread
    // that set it, and a window must hear of its cloaking on its own thread
    [ThreadStatic]
    private static nint t_cloakHook;

    [ThreadStatic]
    private static Dictionary<nint, Win32Window>? t_hooked;

    // kept alive for as long as the process: the system holds a pointer to it
    private static readonly WinEventProc s_cloakProc = OnCloakEvent;

    private bool _minimized;
    private bool _cloaked;

    /// <summary>WM_SIZE told the window's new state.</summary>
    private void SetMinimized(bool minimized)
    {
        if (_minimized == minimized) return;

        _minimized = minimized;
        UpdateSight();
    }

    private void UpdateSight() => _form.SetWindowOutOfSight(_minimized || _cloaked);

    /// <summary>Listen for the window's cloaking. Called once the handle exists.</summary>
    private void HookCloaking()
    {
        t_hooked ??= [];
        t_hooked[_handle] = this;

        if (t_cloakHook != 0) return;

        // the events of this process only: another one's windows are not ours to hear of
        t_cloakHook = SetWinEventHook(
            NativeConstants.EVENT_OBJECT_CLOAKED,
            NativeConstants.EVENT_OBJECT_UNCLOAKED,
            0,
            s_cloakProc,
            (uint)Environment.ProcessId,
            0,
            NativeConstants.WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Stop listening. Called as the window is destroyed; the hook goes with
    /// the thread's last window.</summary>
    private void UnhookCloaking(nint hwnd)
    {
        if (t_hooked is null || !t_hooked.Remove(hwnd)) return;

        if (t_hooked.Count > 0 || t_cloakHook == 0) return;

        UnhookWinEvent(t_cloakHook);
        t_cloakHook = 0;
    }

    private static void OnCloakEvent(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (idObject != NativeConstants.OBJID_WINDOW || idChild != NativeConstants.CHILDID_SELF) return;

        // a window of ours, found by its handle rather than by GWLP_USERDATA: the hook
        // hears of every window of the process — a hosted native control, a COM
        // dialog — and their user data is not a GCHandle of ours
        if (t_hooked is null || !t_hooked.TryGetValue(hwnd, out Win32Window? window)) return;

        bool cloaked = eventType == NativeConstants.EVENT_OBJECT_CLOAKED;

        if (window._cloaked == cloaked) return;

        window._cloaked = cloaked;
        window.UpdateSight();
    }
}
