using ZeppelinForms.Forms;
using ZeppelinForms.Linux.Accessibility;

namespace ZeppelinForms.Linux;

/// <summary>Screen readers: the forms' accessibility trees on the AT-SPI bus, see
/// <see cref="AtSpiBridge"/>. The bus socket is waited on by the loop's select next to the
/// X connection, so a screen reader's question wakes the loop like any input.</summary>
public sealed partial class X11Platform : IAtSpiWindows
{
    /// <summary>Null without AT-SPI on the desktop.</summary>
    private AtSpiBridge? _accessibility;

    private void InitAccessibility() => _accessibility = AtSpiBridge.TryCreate(this);

    /// <summary>Add the bus socket to the select set; returns the new nfds.</summary>
    private int WatchAccessibility(ref X11.FdSet readSet, int nfds)
    {
        int fd = _accessibility?.FileDescriptor ?? -1;

        if (fd is < 0 or >= 1024) return nfds;

        readSet.Set(fd);

        return Math.Max(nfds, fd + 1);
    }

    private void PollAccessibility() => _accessibility?.Poll();

    private X11Window? WindowOf(Form form)
    {
        foreach (X11Window window in _windows.Values)
            if (ReferenceEquals(window.Form, form))
                return window;

        return null;
    }

    float IAtSpiWindows.Scale(Form form) => WindowOf(form)?.Scale ?? 1f;

    /// <summary>X11 knows where every window is: the client area's corner, translated
    /// to the root window.</summary>
    bool IAtSpiWindows.TryGetScreenOrigin(Form form, out int x, out int y)
    {
        x = y = 0;

        if (WindowOf(form) is not { Handle: not 0 } window) return false;

        nuint root = X11.XRootWindow(Display, X11.XDefaultScreen(Display));
        X11.XTranslateCoordinates(Display, window.Handle, root, 0, 0, out x, out y, out _);

        return true;
    }
}
