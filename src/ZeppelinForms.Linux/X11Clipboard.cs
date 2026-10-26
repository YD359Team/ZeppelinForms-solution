using System.Runtime.InteropServices;
using System.Text;

namespace ZeppelinForms.Linux;

/// <summary>
/// The X11 clipboard. Unlike on Windows, it is not a system store but a protocol:
/// the owner of the selection hands out the data on request. So copied text lives
/// as long as the application does.
/// </summary>
/// <remarks>
/// A paste is received in a window of its own, never mapped, that listens to its
/// properties: a large text comes in parts (INCR), each announced by a PropertyNotify,
/// and a form's window doesn't listen to those. The window is made on the first paste.
/// </remarks>
internal sealed class X11Clipboard : IClipboard, IDisposable
{
    /// <summary>How long the owner may take to answer, and between two parts of a large
    /// text. An owner that doesn't answer must not hang the paste.</summary>
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PartTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How much of a property one request reads, in 32-bit units: 4 MB. A larger
    /// text is read in more requests, by the bytes the server says are left.</summary>
    private const long ReadUnits = 1 << 20;

    private readonly nint _display;
    /// <summary>The window that owns the selection for the application: the first one
    /// opened, and another one when that closes, see <see cref="MoveTo"/>.</summary>
    private nuint _window;

    private readonly nuint _clipboardAtom;
    private readonly nuint _targetsAtom;
    private readonly nuint _utf8Atom;
    private readonly nuint _transferAtom;
    private readonly nuint _incrAtom;

    private string? _ownedText;

    /// <summary>The window pastes are received in; 0 until the first one.</summary>
    private nuint _transferWindow;

    public X11Clipboard(nint display, nuint window)
    {
        _display = display;
        _window = window;

        _clipboardAtom = X11.XInternAtom(display, "CLIPBOARD", false);
        _targetsAtom = X11.XInternAtom(display, "TARGETS", false);
        _utf8Atom = X11.XInternAtom(display, "UTF8_STRING", false);
        _transferAtom = X11.XInternAtom(display, "ZF_CLIPBOARD", false);
        _incrAtom = X11.XInternAtom(display, "INCR", false);
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

        nuint requestor = TransferWindow();

        // what a paste that timed out left behind must not be read as this one's
        X11.XDeleteProperty(_display, requestor, _transferAtom);

        X11.XConvertSelection(_display, _clipboardAtom, _utf8Atom, _transferAtom, requestor, 0);
        X11.XFlush(_display);

        // the answer comes as a SelectionNotify event; we wait for it a limited time,
        // otherwise we would hang if the owner doesn't answer. No property in it — the
        // owner has no text to give
        if (WaitForSelectionNotify(requestor) is not { } property || property == X11.None)
            return null;

        return ReadTransfer(requestor);
    }

    /// <summary>The window pastes are received in: unmapped, listening to its own
    /// properties for the parts of a large text.</summary>
    private nuint TransferWindow()
    {
        if (_transferWindow != 0) return _transferWindow;

        int screen = X11.XDefaultScreen(_display);

        _transferWindow = X11.XCreateSimpleWindow(
            _display, X11.XRootWindow(_display, screen), -10, -10, 1, 1, 0, 0, 0);

        X11.XSelectInput(_display, _transferWindow, X11.PropertyChangeMask);

        return _transferWindow;
    }

    /// <summary>The property of the owner's answer; null — no answer in time.</summary>
    /// <remarks>
    /// Only our SelectionNotify is taken out of the queue; everything else stays
    /// for the main loop. This used to call XNextEvent and drop every event that
    /// wasn't the answer — a key release lost here left the key "held", an Expose
    /// lost here left garbage on screen.
    /// </remarks>
    private nuint? WaitForSelectionNotify(nuint requestor)
    {
        nint buffer = Marshal.AllocHGlobal(192);

        try
        {
            DateTime deadline = DateTime.UtcNow + AnswerTimeout;

            while (DateTime.UtcNow < deadline)
            {
                if (X11.XCheckTypedWindowEvent(_display, requestor, X11.SelectionNotify, buffer))
                    return Marshal.PtrToStructure<X11.XSelectionEvent>(buffer).property;

                Thread.Sleep(5);
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>The text the owner put into the transfer property: whole, or — INCR — in
    /// parts. Null — not text, or the owner stopped answering halfway.</summary>
    /// <remarks>
    /// INCR: the owner gives the size instead of the text and waits for the property to
    /// be deleted; then it writes a part, waits for that one to be deleted, and so on,
    /// until a part of no length. Browsers and IDEs send a large text this way, and a
    /// paste of one used to give nothing at all.
    /// </remarks>
    private string? ReadTransfer(nuint window)
    {
        if (ReadProperty(window) is not { } first)
            return null;

        if (first.Type != _incrAtom)
        {
            X11.XDeleteProperty(_display, window, _transferAtom);
            X11.XFlush(_display);

            return first.Format == 8 ? Encoding.UTF8.GetString(first.Bytes) : null;
        }

        var text = new MemoryStream();

        // the deletion tells the owner to start
        X11.XDeleteProperty(_display, window, _transferAtom);
        X11.XFlush(_display);

        while (true)
        {
            if (!WaitForNewValue(window))
                return null;

            // a notification of a value already gone — the INCR answer itself,
            // still queued — finds no property: the next one is waited for
            if (ReadProperty(window) is not { } part || part.Type == X11.None)
                continue;

            X11.XDeleteProperty(_display, window, _transferAtom);
            X11.XFlush(_display);

            // a part of no length is the end
            if (part.Bytes.Length == 0)
                break;

            if (part.Format != 8)
                return null;

            text.Write(part.Bytes);
        }

        return Encoding.UTF8.GetString(text.GetBuffer(), 0, (int)text.Length);
    }

    /// <summary>The next PropertyNotify of a new value of the transfer property; false —
    /// none came in time.</summary>
    private bool WaitForNewValue(nuint window)
    {
        nint buffer = Marshal.AllocHGlobal(192);

        try
        {
            DateTime deadline = DateTime.UtcNow + PartTimeout;

            while (DateTime.UtcNow < deadline)
            {
                // the window is ours alone: the notifications of deletions — our own —
                // are dropped here with nobody missing them
                while (X11.XCheckTypedWindowEvent(_display, window, X11.PropertyNotify, buffer))
                {
                    var notify = Marshal.PtrToStructure<X11.XPropertyEvent>(buffer);

                    if (notify.atom == _transferAtom && notify.state == X11.PropertyNewValue)
                        return true;
                }

                Thread.Sleep(2);
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private readonly record struct PropertyValue(nuint Type, int Format, byte[] Bytes);

    /// <summary>The whole transfer property, in as many requests as the server needs; its
    /// type None and no bytes when there is no such property. Null — the request failed.</summary>
    /// <remarks>
    /// Every block Xlib returns is freed, whatever is in it: Xlib allocates one even for
    /// a property of no length, and the format and the count used to be checked before
    /// the try — an answer of another format, an INCR one among them, leaked its block.
    /// </remarks>
    private PropertyValue? ReadProperty(nuint window)
    {
        var bytes = new MemoryStream();
        long offset = 0;
        nuint type;
        int format;
        nuint after;

        do
        {
            int status = X11.XGetWindowProperty(
                _display, window, _transferAtom, offset, ReadUnits, false, X11.AnyPropertyType,
                out type, out format, out nuint count, out after, out nint data);

            try
            {
                if (status != 0) return null;

                if (type == X11.None) return new PropertyValue(X11.None, 0, []);

                // 32-bit items come as C longs, which are 8 bytes on 64-bit Linux
                int itemSize = format switch { 8 => 1, 16 => 2, _ => nint.Size };
                int length = checked((int)count * itemSize);

                if (data != 0 && length > 0)
                {
                    byte[] block = new byte[length];
                    Marshal.Copy(data, block, 0, length);
                    bytes.Write(block);
                }

                // the offset counts 32-bit units of the property as the server keeps it
                offset += checked((long)count * (format / 8) / 4);
            }
            finally
            {
                if (data != 0) X11.XFree(data);
            }
        }
        while (after > 0);

        return new PropertyValue(type, format, bytes.ToArray());
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

    /// <summary>The window the clipboard works through.</summary>
    internal nuint Window => _window;

    /// <summary>The owner window closed: the clipboard goes on through another one.</summary>
    /// <remarks>
    /// The server dropped the selection with the window. Text copied here and not yet
    /// replaced by another application's — no SelectionClear came — is owned again by
    /// the new window, so a paste elsewhere still finds it. The dead window used to stay
    /// the owner: the next copy was a BadWindow error, and Xlib's default handler ends
    /// the process on one.
    /// </remarks>
    internal void MoveTo(nuint window)
    {
        _window = window;

        if (_ownedText is null) return;

        X11.XSetSelectionOwner(_display, _clipboardAtom, _window, 0);
        X11.XFlush(_display);
    }

    /// <summary>The clipboard is no longer the application's: its paste window goes.</summary>
    public void Dispose()
    {
        if (_transferWindow == 0) return;

        X11.XDestroyWindow(_display, _transferWindow);
        X11.XFlush(_display);
        _transferWindow = 0;
    }
}