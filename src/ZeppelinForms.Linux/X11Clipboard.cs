using System.Runtime.InteropServices;
using System.Text;

namespace ZeppelinForms.Linux;

/// <summary>
/// Буфер обмена X11. В отличие от Windows это не системное хранилище,
/// а протокол: владелец выделения отдаёт данные по запросу. Поэтому
/// скопированный текст живёт, пока живо приложение.
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
        // мы сами владеем выделением — не гоняем протокол вхолостую
        if (X11.XGetSelectionOwner(_display, _clipboardAtom) == _window)
            return _ownedText;

        X11.XConvertSelection(_display, _clipboardAtom, _utf8Atom, _transferAtom, _window, 0);
        X11.XFlush(_display);

        // ответ придёт событием SelectionNotify; ждём его ограниченное время,
        // иначе зависнем, если владелец не отвечает
        if (!WaitForSelectionNotify())
            return null;

        return ReadTransferProperty();
    }

    private bool WaitForSelectionNotify()
    {
        nint buffer = Marshal.AllocHGlobal(192);

        try
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(500);

            while (DateTime.UtcNow < deadline)
            {
                if (X11.XPending(_display) == 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                X11.XNextEvent(_display, buffer);

                if (Marshal.ReadInt32(buffer) == X11.SelectionNotify)
                    return true;

                // чужие события в этом вложенном ожидании теряются —
                // компромисс синхронного API поверх асинхронного протокола
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

    /// <summary>Ответ на запрос чужого приложения — вызывается из цикла событий.</summary>
    internal void HandleSelectionRequest(X11.XSelectionRequestEvent request)
    {
        nuint property = request.property;

        if (_ownedText is null)
        {
            property = 0;   // нечего отдавать
        }
        else if (request.target == _utf8Atom)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(_ownedText);
            X11.XChangeProperty(_display, request.requestor, request.property,
                _utf8Atom, 8, X11.PropModeReplace, bytes, bytes.Length);
        }
        else if (request.target == _targetsAtom)
        {
            // сообщаем, в каких форматах умеем отдавать
            byte[] targets = new byte[16];
            BitConverter.TryWriteBytes(targets.AsSpan(0), (ulong)_targetsAtom);
            BitConverter.TryWriteBytes(targets.AsSpan(8), (ulong)_utf8Atom);

            X11.XChangeProperty(_display, request.requestor, request.property,
                4 /* XA_ATOM */, 32, X11.PropModeReplace, targets, 2);
        }
        else
        {
            property = 0;   // формат не поддерживаем
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
