using System.Runtime.InteropServices;
using System.Text;

namespace ZeppelinForms.Linux;

/// <summary>
/// The X11 clipboard. Unlike on Windows, it is not a system store but a protocol:
/// the owner of the selection hands out the data on request. So copied text lives
/// as long as the application does.
/// </summary>
internal sealed class X11Clipboard : IClipboard
{
    private readonly nint _display;
    private readonly nuint _window;

    private readonly nuint _clipboardAtom;
    private readonly nuint _targetsAtom;
    private readonly nuint _utf8Atom;
    private readonly nuint _transferAtom;

    private string? _ownedText;

    public X11Clipboard(nint display, nuint window)
    {
        _display = display;
        _window = window;

        _clipboardAtom = X11.XInternAtom(display, "CLIPBOARD", false);
        _targetsAtom = X11.XInternAtom(display, "TARGETS", false);
        _utf8Atom = X11.XInternAtom(display, "UTF8_STRING", false);
        _transferAtom = X11.XInternAtom(display, "ZF_CLIPBOARD", false);
    }

    public void SetText(string text)
    {
        _ownedText = text;
        X11.XSetSelectionOwner(_display, _clipboardAtom, _window, 0);
        X11.XFlush(_display);
    }

    public string? GetText()
    {
        // we own the selection ourselves — no point running the protocol idle
        if (X11.XGetSelectionOwner(_display, _clipboardAtom) == _window)
            return _ownedText;

        X11.XConvertSelection(_display, _clipboardAtom, _utf8Atom, _transferAtom, _window, 0);
        X11.XFlush(_display);

        // the answer comes as a SelectionNotify event; we wait for it a limited time,
        // otherwise we would hang if the owner doesn't answer
        if (!WaitForSelectionNotify())
            return null;

        return ReadTransferProperty();
    }

    /// <remarks>
    /// Only our SelectionNotify is taken out of the queue; everything else stays
    /// for the main loop. This used to call XNextEvent and drop every event that
    /// wasn't the answer — a key release lost here left the key "held", an Expose
    /// lost here left garbage on screen.
    /// </remarks>
    private bool WaitForSelectionNotify()
    {
        nint buffer = Marshal.AllocHGlobal(192);

        try
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(500);

            while (DateTime.UtcNow < deadline)
            {
                if (X11.XCheckTypedWindowEvent(_display, _window, X11.SelectionNotify, buffer))
                    return true;

                Thread.Sleep(5);
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private string? ReadTransferProperty()
    {
        int status = X11.XGetWindowProperty(
            _display, _window, _transferAtom, 0, 1 << 20, true, 0,
            out _, out int format, out nuint count, out _, out nint data);

        if (status != 0 || data == 0 || format != 8 || count == 0)
            return null;

        try
        {
            byte[] bytes = new byte[(int)count];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            X11.XFree(data);
        }
    }

    /// <summary>The answer to another application's request — called from the event loop.</summary>
    internal void HandleSelectionRequest(X11.XSelectionRequestEvent request)
    {
        nuint property = request.property;

        if (_ownedText is null)
        {
            property = 0;   // nothing to give
        }
        else if (request.target == _utf8Atom)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(_ownedText);
            X11.XChangeProperty(_display, request.requestor, request.property,
                _utf8Atom, 8, X11.PropModeReplace, bytes, bytes.Length);
        }
        else if (request.target == _targetsAtom)
        {
            // report which formats we can give the data in
            byte[] targets = new byte[16];
            BitConverter.TryWriteBytes(targets.AsSpan(0), (ulong)_targetsAtom);
            BitConverter.TryWriteBytes(targets.AsSpan(8), (ulong)_utf8Atom);

            X11.XChangeProperty(_display, request.requestor, request.property,
                4 /* XA_ATOM */, 32, X11.PropModeReplace, targets, 2);
        }
        else
        {
            property = 0;   // the format is not supported
        }

        var response = new X11.XSelectionEvent
        {
            type = X11.SelectionNotify,
            display = _display,
            requestor = request.requestor,
            selection = request.selection,
            target = request.target,
            property = property,
            time = request.time,
        };

        nint buffer = Marshal.AllocHGlobal(192);

        try
        {
            Marshal.StructureToPtr(response, buffer, false);
            X11.XSendEvent(_display, request.requestor, false, 0, buffer);
            X11.XFlush(_display);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal void HandleSelectionClear() => _ownedText = null;
}