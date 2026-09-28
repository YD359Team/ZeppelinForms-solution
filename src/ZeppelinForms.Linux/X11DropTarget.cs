using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Input.DragDrop;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Linux;

/// <summary>
/// The XDND drop target for one window. The protocol is built on exchanging
/// ClientMessages: the source announces the types in Enter, asks for permission
/// in Position, we answer with Status, and only on Drop is the data actually
/// requested through the selection mechanism — the same one the clipboard uses.
/// </summary>
internal sealed class X11DropTarget
{
    private const int XdndVersion = 5;

    private Point _lastPosition;
    private readonly nint _display;
    private readonly nuint _window;
    private readonly Form _form;
    private readonly Func<Point, Point> _toClient;

    private readonly nuint _aware;
    private readonly nuint _enter;
    private readonly nuint _position;
    private readonly nuint _status;
    private readonly nuint _leave;
    private readonly nuint _drop;
    private readonly nuint _finished;
    private readonly nuint _selection;
    private readonly nuint _actionCopy;
    private readonly nuint _uriList;
    private readonly nuint _transfer;

    private nuint _source;
    private nuint _sourceTime;
    private bool _sourceHasUris;
    private DragDropData _data = new();

    public X11DropTarget(nint display, nuint window, Form form, Func<Point, Point> toClient)
    {
        _display = display;
        _window = window;
        _form = form;
        _toClient = toClient;

        _aware = Atom("XdndAware");
        _enter = Atom("XdndEnter");
        _position = Atom("XdndPosition");
        _status = Atom("XdndStatus");
        _leave = Atom("XdndLeave");
        _drop = Atom("XdndDrop");
        _finished = Atom("XdndFinished");
        _selection = Atom("XdndSelection");
        _actionCopy = Atom("XdndActionCopy");
        _uriList = Atom("text/uri-list");
        _transfer = Atom("ZF_XDND_TRANSFER");
    }

    private nuint Atom(string name) => X11.XInternAtom(_display, name, false);

    /// <summary>Declare the window a drop target. The protocol version is written as
    /// a window property — the source reads it before the exchange begins.</summary>
    public void Register()
    {
        byte[] version = BitConverter.GetBytes((nint)XdndVersion);

        X11.XChangeProperty(_display, _window, _aware, X11.XA_ATOM, 32,
            X11.PropModeReplace, version, 1);

        // without flushing the buffer the property may not reach the server
        // before the source starts reading our window
        X11.XFlush(_display);
    }

    public void Unregister() => X11.XDeleteProperty(_display, _window, _aware);

    /// <summary>Handle a protocol message. false — the message isn't ours.</summary>
    /// <remarks>
    /// This used to write a debug line for every ClientMessage the window received,
    /// including every ZF_INVOKE — that is, every Form.Invoke.
    /// </remarks>
    public bool Handle(in X11.XClientMessageEvent message)
    {
        if (message.message_type == _enter) { OnEnter(message); return true; }
        if (message.message_type == _position) { OnPosition(message); return true; }
        if (message.message_type == _leave) { OnLeave(); return true; }
        if (message.message_type == _drop) { OnDrop(message); return true; }

        return false;
    }

    private void OnEnter(in X11.XClientMessageEvent message)
    {
        _source = (nuint)message.data0;

        // up to three types come right in the message; with more, the high bit of
        // data1 is raised and the full list lies in XdndTypeList.
        // All we need to know is whether text/uri-list is among them
        _sourceHasUris =
            (nuint)message.data2 == _uriList ||
            (nuint)message.data3 == _uriList ||
            (nuint)message.data4 == _uriList;

        if (((nint)message.data1 & 1) != 0)
            _sourceHasUris |= TypeListHasUris();

        _data = new DragDropData();
    }

    /// <summary>The full list of types, when there are more than three.</summary>
    private bool TypeListHasUris()
    {
        nuint typeList = Atom("XdndTypeList");

        int status = X11.XGetWindowProperty(
            _display, _source, typeList, 0, 1024, false, X11.XA_ATOM,
            out _, out _, out nuint count, out _, out nint data);

        if (status != 0 || data == 0) return false;

        try
        {
            for (int i = 0; i < (int)count; i++)
                if ((nuint)Marshal.ReadIntPtr(data, i * nint.Size) == _uriList)
                    return true;

            return false;
        }
        finally
        {
            X11.XFree(data);
        }
    }

    private void OnPosition(in X11.XClientMessageEvent message)
    {
        // the coordinates in data2 are packed as a pair: the high 16 bits — x, the low — y
        int packed = (int)(nint)message.data2;
        var screen = new Point((packed >> 16) & 0xFFFF, packed & 0xFFFF);

        // remembered: XdndDrop sends no coordinates, and the drop happens
        // where the last Position was
        _lastPosition = screen;

        // there is no data yet — it arrives only on Drop. The target decides by
        // the "files are being dragged" mark, so a stand-in with the mark is passed
        var probe = new DragDropData { Files = _sourceHasUris ? [string.Empty] : [] };

        DragDropEffect effect = _form.OnDragOverWindow(probe, _toClient(screen), Keyboard.Modifiers);

        SendStatus(effect != DragDropEffect.None);
    }

    private void SendStatus(bool accept)
    {
        var reply = new X11.XClientMessageEvent
        {
            type = X11.ClientMessage,
            display = _display,
            window = _source,
            message_type = _status,
            format = 32,
            data0 = (nint)_window,
            // the low bit: ready to accept. The second bit is not set — then the
            // source sends Position on every move, which is exactly what we need
            // to highlight the target
            data1 = accept ? 1 : 0,
            data2 = 0,
            data3 = 0,
            data4 = accept ? (nint)_actionCopy : 0,
        };

        Send(reply);
    }

    private void OnLeave()
    {
        _form.OnDragLeaveWindow();

        _source = 0;
        _sourceHasUris = false;
        _data = new DragDropData();
    }

    private void OnDrop(in X11.XClientMessageEvent message)
    {
        _sourceTime = (nuint)message.data2;

        if (!_sourceHasUris)
        {
            SendFinished(accepted: false);
            _form.OnDragLeaveWindow();

            return;
        }

        // only now is the data requested — before Drop the source doesn't hand it out
        X11.XConvertSelection(_display, _selection, _uriList, _transfer, _window, _sourceTime);
    }

    /// <summary>The data arrived: a SelectionNotify for our transfer property.
    /// Returns false if the event isn't about us.</summary>
    public bool HandleSelection(nuint property)
    {
        if (property != _transfer || _source == 0) return false;

        _data = new DragDropData { Files = ReadUris() };

        DragDropEffect effect = _form.OnDropWindow(
               _data, _toClient(_lastPosition), Keyboard.Modifiers);

        SendFinished(effect != DragDropEffect.None);

        _source = 0;
        _sourceHasUris = false;
        _data = new DragDropData();

        return true;
    }

    private string[] ReadUris()
    {
        int status = X11.XGetWindowProperty(
            _display, _window, _transfer, 0, 0x100000, true, 0,
            out _, out _, out nuint count, out _, out nint data);

        if (status != 0 || data == 0) return [];

        try
        {
            string text = Marshal.PtrToStringUTF8(data, (int)count) ?? string.Empty;
            List<string> files = [];

            foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();

                // comments in the text/uri-list format start with a hash
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                // what comes is URIs, not paths: file:///home/user/%D1%84.txt
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                    files.Add(uri.LocalPath);
            }

            return [.. files];
        }
        finally
        {
            X11.XFree(data);
        }
    }

    private void SendFinished(bool accepted)
    {
        var reply = new X11.XClientMessageEvent
        {
            type = X11.ClientMessage,
            display = _display,
            window = _source,
            message_type = _finished,
            format = 32,
            data0 = (nint)_window,
            data1 = accepted ? 1 : 0,
            data2 = accepted ? (nint)_actionCopy : 0,
        };

        Send(reply);
    }

    private void Send(X11.XClientMessageEvent message)
    {
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<X11.XClientMessageEvent>());

        try
        {
            Marshal.StructureToPtr(message, buffer, false);
            X11.XSendEvent(_display, _source, false, 0, buffer);
            X11.XFlush(_display);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}