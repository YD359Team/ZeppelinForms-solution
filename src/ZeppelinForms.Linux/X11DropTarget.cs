using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Input.DragDrop;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Linux;

/// <summary>
/// Приёмник XDND для одного окна. Протокол построен на обмене ClientMessage:
/// источник объявляет типы в Enter, спрашивает разрешение в Position, мы
/// отвечаем Status, и только на Drop данные реально запрашиваются через
/// механизм выделений — тот же, что у буфера обмена.
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

    /// <summary>Объявить окно приёмником. Версия протокола пишется как
    /// свойство окна — источник читает её перед началом обмена.</summary>
    public void Register()
    {
        byte[] version = BitConverter.GetBytes((nint)XdndVersion);

        X11.XChangeProperty(_display, _window, _aware, X11.XA_ATOM, 32,
            X11.PropModeReplace, version, 1);
    }

    public void Unregister() => X11.XDeleteProperty(_display, _window, _aware);

    /// <summary>Обработать сообщение протокола. false — сообщение не наше.</summary>
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

        // до трёх типов приезжают прямо в сообщении; при большем количестве
        // старший бит data1 поднят и полный список лежит в XdndTypeList.
        // Нам достаточно знать, есть ли среди них text/uri-list
        _sourceHasUris =
            (nuint)message.data2 == _uriList ||
            (nuint)message.data3 == _uriList ||
            (nuint)message.data4 == _uriList;

        if (((nint)message.data1 & 1) != 0)
            _sourceHasUris |= TypeListHasUris();

        _data = new DragDropData();
    }

    /// <summary>Полный список типов, когда их больше трёх.</summary>
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
        // координаты в data2 упакованы парой: старшие 16 бит — x, младшие — y
        int packed = (int)(nint)message.data2;
        var screen = new Point((packed >> 16) & 0xFFFF, packed & 0xFFFF);

        // запоминаем: XdndDrop координат не присылает, а бросок происходит
        // там, где был последний Position
        _lastPosition = screen;

        // данных ещё нет — они придут только на Drop. Приёмник решает
        // по признаку «тащат файлы», поэтому отдаём заготовку с пометкой
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
            // младший бит: готовы принять. Второй бит не ставим — тогда
            // источник будет присылать Position на каждое движение,
            // а нам это и нужно для подсветки приёмника
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

        // только теперь запрашиваем данные — до Drop источник их не отдаёт
        X11.XConvertSelection(_display, _selection, _uriList, _transfer, _window, _sourceTime);
    }

    /// <summary>Данные приехали: SelectionNotify на наше свойство переноса.
    /// Возвращает false, если событие не про нас.</summary>
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

                // комментарии по формату text/uri-list начинаются с решётки
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                // приезжают именно URI, а не пути: file:///home/user/%D1%84.txt
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
