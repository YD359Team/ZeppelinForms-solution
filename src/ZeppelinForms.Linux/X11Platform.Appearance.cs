namespace ZeppelinForms.Linux;

/// <summary>The desktop's light or dark mode and accent, through the XDG Desktop
/// Portal on the session bus, see <see cref="PortalAppearance"/>.</summary>
/// <remarks>
/// The bus socket is watched by the loop's own select next to the X connection,
/// so a change on the desktop wakes the loop like any input, and the portal's
/// signal is handled on the UI thread — no thread of its own, nothing to marshal.
/// </remarks>
public sealed partial class X11Platform
{
    /// <summary>Null without a session bus or without a portal on it:
    /// the application then simply gets no system appearance.</summary>
    private PortalAppearance? _appearance;

    private void InitAppearance()
    {
        _appearance = PortalAppearance.TryCreate();

        if (_appearance is not null)
            App.UseSystemAppearance(_appearance);
    }

    /// <summary>Add the bus socket to the select set; returns the new nfds.
    /// fd_set holds 1024 descriptors, and a socket past them is not watched —
    /// it is still read on every turn of the loop, only without waking it.</summary>
    private int WatchAppearance(ref X11.FdSet readSet, int nfds)
    {
        int fd = _appearance?.FileDescriptor ?? -1;

        if (fd is < 0 or >= 1024) return nfds;

        readSet.Set(fd);

        return Math.Max(nfds, fd + 1);
    }

    /// <summary>Read what the bus has sent. Without data it returns at once.</summary>
    private void PollAppearance() => _appearance?.Poll();
}