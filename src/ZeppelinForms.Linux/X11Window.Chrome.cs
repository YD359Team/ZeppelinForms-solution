using System.Runtime.InteropServices;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Linux;

/// <summary>The frame of the window from the form's properties: FormBorderStyle,
/// ControlBox, ShowInTaskbar, CanMinimize, CanMaximize, CanResize, and the owner</summary>
/// <remarks>
/// <para>
/// X has no frame of its own: the window manager draws it, and the application
/// can only ask. The decorations and the allowed actions go as Motif hints,
/// which every common manager reads; a window that can't be resized also gets
/// equal minimum and maximum sizes — some managers ignore the Motif functions
/// but none ignores the size limits. The taskbar button and the tool window
/// are EWMH: <c>_NET_WM_STATE_SKIP_TASKBAR</c> and <c>_NET_WM_WINDOW_TYPE_UTILITY</c>.
/// </para>
/// <para>
/// A manager is free to refuse any of it — a tiling one has no title bar to take
/// the buttons from. The window type is read when the window is mapped; changing
/// it later works with most managers, but not all.
/// </para>
/// </remarks>
internal sealed partial class X11Window
{
    // _MOTIF_WM_HINTS, from MwmUtil.h
    private const long MwmHintsFunctions = 1 << 0;
    private const long MwmHintsDecorations = 1 << 1;

    private const long MwmFuncResize = 1 << 1;
    private const long MwmFuncMove = 1 << 2;
    private const long MwmFuncMinimize = 1 << 3;
    private const long MwmFuncMaximize = 1 << 4;
    private const long MwmFuncClose = 1 << 5;

    private const long MwmDecorBorder = 1 << 1;
    private const long MwmDecorResizeH = 1 << 2;
    private const long MwmDecorTitle = 1 << 3;
    private const long MwmDecorMenu = 1 << 4;
    private const long MwmDecorMinimize = 1 << 5;
    private const long MwmDecorMaximize = 1 << 6;

    /// <summary>Rebuild the frame of an open window.</summary>
    public void UpdateChrome()
    {
        if (_window == 0) return;

        ApplyChrome(mapped: true);
        X11.XFlush(_display);
    }

    /// <param name="mapped">Whether the manager already has the window: then the
    /// state is changed by a message to it, before that — by the property itself.</param>
    private void ApplyChrome(bool mapped)
    {
        SetMotifHints();
        ApplySizeHints(position: !mapped);
        SetWindowType();
        SetTransientFor();
        SetSkipTaskbar(mapped);
    }

    private void SetMotifHints()
    {
        long functions = MwmFuncMove;
        long decorations = 0;

        // Alt+F4 closes a window without the control box too, as on Windows:
        // Closing is the way to refuse it
        functions |= MwmFuncClose;

        if (_form.HasMinimizeBox) functions |= MwmFuncMinimize;
        if (_form.HasMaximizeBox) functions |= MwmFuncMaximize;
        if (_form.IsResizable) functions |= MwmFuncResize;

        if (_form.FormBorderStyle != FormBorderStyle.None)
        {
            decorations = MwmDecorBorder | MwmDecorTitle;

            if (_form.ControlBox) decorations |= MwmDecorMenu;
            if (_form.HasMinimizeBox) decorations |= MwmDecorMinimize;
            if (_form.HasMaximizeBox) decorations |= MwmDecorMaximize;
            if (_form.IsResizable) decorations |= MwmDecorResizeH;
        }

        nuint property = X11.XInternAtom(_display, "_MOTIF_WM_HINTS", false);

        // flags, functions, decorations, input mode, status
        ChangeLongs(property, property, [
            MwmHintsFunctions | MwmHintsDecorations,
            functions,
            decorations,
            0,
            0,
        ]);
    }

    /// <summary>WM_NORMAL_HINTS: the size limits the manager keeps the window within,
    /// and where it is to open.</summary>
    /// <param name="physicalWidth">The size to fix the window at, in pixels — when it
    /// is being resized from code; otherwise the current one.</param>
    /// <param name="physicalHeight">See physicalWidth.</param>
    /// <param name="position">Ask for the window's place too: only before it is mapped —
    /// the manager reads the place when it takes the window, and a stale place in
    /// later hints would have nothing to do.</param>
    /// <remarks>
    /// A window that can't be resized is held at one size, minimum and maximum equal.
    /// A resizable one gets the form's MinimumSize and MaximumSize; an axis with no
    /// limit of its own is 1 for the minimum and the protocol's largest size for the
    /// maximum, since X11 gives both axes of a limit together. Until 0.17 a resizable
    /// window had no limits at all.
    /// </remarks>
    internal void ApplySizeHints(int? physicalWidth = null, int? physicalHeight = null, bool position = false)
    {
        var hints = new X11.XSizeHints();

        if (!_form.IsResizable)
        {
            Drawing.Primitives.Size size = _form.ClientSize is { Width: > 0, Height: > 0 } client
                ? client
                : _form.Size;

            int width = Math.Max(1, physicalWidth ?? (int)(size.Width * _scale));
            int height = Math.Max(1, physicalHeight ?? (int)(size.Height * _scale));

            hints.flags = X11.PMinSize | X11.PMaxSize;
            hints.min_width = hints.max_width = width;
            hints.min_height = hints.max_height = height;
        }
        else
        {
            Drawing.Primitives.Size minimum = _form.MinimumSize;
            Drawing.Primitives.Size maximum = _form.MaximumSize;

            if (!minimum.IsWidthAuto || !minimum.IsHeightAuto)
            {
                hints.flags |= X11.PMinSize;
                hints.min_width = ToPhysical(minimum.Width, unlimited: 1);
                hints.min_height = ToPhysical(minimum.Height, unlimited: 1);
            }

            if (!maximum.IsWidthAuto || !maximum.IsHeightAuto)
            {
                hints.flags |= X11.PMaxSize;
                hints.max_width = ToPhysical(maximum.Width, unlimited: MaxProtocolSize);
                hints.max_height = ToPhysical(maximum.Height, unlimited: MaxProtocolSize);
            }
        }

        if (position && OpeningPosition() is (int x, int y))
        {
            // PPosition alone some managers ignore for a normal window; USPosition is
            // what they all keep — the place is the application's explicit wish
            hints.flags |= X11.USPosition | X11.PPosition;
            hints.x = x;
            hints.y = y;
        }

        // no flags — no limits: a window made resizable again loses them
        X11.XSetWMNormalHints(_display, _window, ref hints);
    }

    /// <summary>The largest width or height a window can have in the X protocol.</summary>
    private const int MaxProtocolSize = short.MaxValue;

    private int ToPhysical(float logical, int unlimited) =>
        float.IsNaN(logical) ? unlimited : Math.Clamp((int)MathF.Round(logical * _scale), 1, MaxProtocolSize);

    private void SetWindowType()
    {
        // a tool window asked for by its frame stays a utility one; a dialog says
        // it is one, so the manager centers it over its owner and keeps it above
        string name = _form.IsToolWindow ? "_NET_WM_WINDOW_TYPE_UTILITY"
            : _form.IsDialog ? "_NET_WM_WINDOW_TYPE_DIALOG"
            : "_NET_WM_WINDOW_TYPE_NORMAL";

        nuint property = X11.XInternAtom(_display, "_NET_WM_WINDOW_TYPE", false);
        nuint type = X11.XInternAtom(_display, name, false);

        ChangeLongs(property, X11.XA_ATOM, [(long)type]);
    }

    /// <summary>An owned window is transient for its owner: the manager keeps it
    /// above the owner and minimizes it with it. Without an owner the hint goes —
    /// a form shown again on its own must not stay tied to the old one.</summary>
    private void SetTransientFor()
    {
        if (_form.Owner?.PlatformWindow is X11Window { Handle: not 0 } owner)
        {
            X11.XSetTransientForHint(_display, _window, owner.Handle);
            return;
        }

        nuint property = X11.XInternAtom(_display, "WM_TRANSIENT_FOR", false);
        X11.XDeleteProperty(_display, _window, property);
    }

    private void SetSkipTaskbar(bool mapped)
    {
        // a tool window stays out of the taskbar as on Windows
        bool skip = !_form.ShowInTaskbar || _form.IsToolWindow;

        nuint netWmState = X11.XInternAtom(_display, "_NET_WM_STATE", false);
        nuint skipTaskbar = X11.XInternAtom(_display, "_NET_WM_STATE_SKIP_TASKBAR", false);

        if (!mapped)
        {
            // before mapping the manager takes the initial state from the property —
            // the whole of it at once: the topmost place is written with the taskbar
            // flag, or one would replace the other
            nuint above = X11.XInternAtom(_display, "_NET_WM_STATE_ABOVE", false);

            List<long> state = [];

            if (skip) state.Add((long)skipTaskbar);
            if (_form.TopMost) state.Add((long)above);

            if (state.Count > 0)
                ChangeLongs(netWmState, X11.XA_ATOM, [.. state]);

            return;
        }

        // after mapping the manager owns the property, and the change is a request
        var message = new X11.XClientMessageEvent
        {
            type = X11.ClientMessage,
            display = _display,
            window = _window,
            message_type = netWmState,
            format = 32,
            data0 = skip ? X11.NetWmStateAdd : X11.NetWmStateRemove,
            data1 = (nint)skipTaskbar,
            data2 = 0,
            // the source is an ordinary application, not a panel or a pager
            data3 = 1,
        };

        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<X11.XClientMessageEvent>());

        try
        {
            Marshal.StructureToPtr(message, buffer, false);

            nuint root = X11.XRootWindow(_display, X11.XDefaultScreen(_display));

            X11.XSendEvent(_display, root, false,
                X11.SubstructureNotifyMask | X11.SubstructureRedirectMask, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>A property of 32-bit items. Xlib takes them as C longs — eight
    /// bytes each on a 64-bit system — and keeps the low 32 bits of each.</summary>
    private void ChangeLongs(nuint property, nuint type, long[] values)
    {
        byte[] data = new byte[values.Length * nint.Size];

        for (int i = 0; i < values.Length; i++)
        {
            if (nint.Size == 8)
                BitConverter.TryWriteBytes(data.AsSpan(i * 8), values[i]);
            else
                BitConverter.TryWriteBytes(data.AsSpan(i * 4), (int)values[i]);
        }

        X11.XChangeProperty(_display, _window, property, type, 32,
            X11.PropModeReplace, data, values.Length);
    }
}