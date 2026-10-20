using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>What a point of a decorated window's surface is.</summary>
internal enum DecorationPart
{
    /// <summary>The transparent band around a window that can't be resized: nothing.</summary>
    None,

    /// <summary>The form.</summary>
    Client,

    Caption,
    Minimize,
    Maximize,
    Close,

    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// The frame a Wayland window draws itself where the compositor draws none — GNOME, Weston:
/// a title bar with the title and the buttons, a border, and a band around them that
/// shades the window and that the user drags to resize it.
/// </summary>
/// <remarks>
/// <para>
/// All of it is drawn into the window's own buffer, around the form: the surface is the
/// form, the title bar over it and the band around both; the window geometry tells the
/// compositor where the window proper is, so the band is neither placed nor snapped by.
/// Maximized or tiled, the window has no band: it fills its place to the edge.
/// </para>
/// <para>
/// The look follows the form's title bar — <c>TitleBarTheme</c>, <c>TitleBarColor</c> —
/// as the system's caption does on Windows; the buttons are the ones the form's
/// <c>FormBorderStyle</c>, <c>ControlBox</c>, <c>CanMinimize</c> and <c>CanMaximize</c>
/// give it, as far as the compositor offers them.
/// </para>
/// </remarks>
internal sealed class WaylandDecoration
{
    private const float CaptionHeight = 36f;
    private const float ToolCaptionHeight = 28f;

    /// <summary>The band around a floating window: its shadow, and where it is resized from.</summary>
    private const float BandWidth = 12f;

    /// <summary>How far along an edge from a corner the corner's diagonal resize reaches.</summary>
    private const float CornerGrip = 20f;

    /// <summary>The resize grip inside the window's edge: some compositors — sway — send
    /// the pointer no event over the band outside it.</summary>
    private const float InnerGrip = 4f;

    private DecorationPart _hover = DecorationPart.None;
    private DecorationPart _pressed = DecorationPart.None;

    public string Title { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    /// <summary>Maximized, fullscreen or tiled: no band, the window fills its place.</summary>
    public bool FillsPlace { get; set; }

    public bool IsDark { get; set; }
    public Color? Background { get; set; }
    public Color? Foreground { get; set; }

    public bool HasMinimize { get; set; } = true;
    public bool HasMaximize { get; set; } = true;
    public bool HasClose { get; set; } = true;
    public bool IsResizable { get; set; } = true;
    public bool IsToolWindow { get; set; }

    /// <summary>The window is maximized: the maximize button shows restore.</summary>
    public bool IsMaximized { get; set; }

    public float Band => FillsPlace ? 0f : BandWidth;

    public float Caption => IsToolWindow ? ToolCaptionHeight : CaptionHeight;

    /// <summary>The surface of a form of <paramref name="client"/> size, in logical units.</summary>
    public Size SurfaceSize(Size client) =>
        new(client.Width + 2 * Band, client.Height + Caption + 2 * Band);

    /// <summary>The window proper on the surface: the title bar and the form, without the band.</summary>
    public Rectangle Geometry(Size client) =>
        new(Band, Band, client.Width, client.Height + Caption);

    /// <summary>Where the form's top-left corner is on the surface.</summary>
    public Point ContentOrigin => new(Band, Band + Caption);

    public DecorationPart HitTest(Point p, Size client)
    {
        Rectangle frame = Geometry(client);

        bool inside = frame.Contains(p);

        bool onEdge = inside && IsResizable && !FillsPlace
            && (p.X < frame.X + InnerGrip || p.X >= frame.Right - InnerGrip
                || p.Y < frame.Y + InnerGrip || p.Y >= frame.Bottom - InnerGrip);

        if (!inside || onEdge)
        {
            if (!IsResizable || FillsPlace) return DecorationPart.None;

            bool nearLeft = p.X < frame.X + CornerGrip;
            bool nearRight = p.X >= frame.Right - CornerGrip;
            bool nearTop = p.Y < frame.Y + CornerGrip;
            bool nearBottom = p.Y >= frame.Bottom - CornerGrip;

            if (nearTop && nearLeft) return DecorationPart.TopLeft;
            if (nearTop && nearRight) return DecorationPart.TopRight;
            if (nearBottom && nearLeft) return DecorationPart.BottomLeft;
            if (nearBottom && nearRight) return DecorationPart.BottomRight;

            if (p.Y < frame.Y + InnerGrip) return DecorationPart.Top;
            if (p.Y >= frame.Bottom - InnerGrip) return DecorationPart.Bottom;

            return p.X < frame.X + InnerGrip ? DecorationPart.Left : DecorationPart.Right;
        }

        if (p.Y >= frame.Y + Caption) return DecorationPart.Client;

        foreach ((DecorationPart part, Rectangle rect) in Buttons(frame))
            if (rect.Contains(p))
                return part;

        return DecorationPart.Caption;
    }

    /// <summary>The buttons from the right: close, maximize, minimize — the ones there are.</summary>
    private IEnumerable<(DecorationPart Part, Rectangle Rect)> Buttons(Rectangle frame)
    {
        float width = MathF.Round(Caption * 1.25f);
        float right = frame.Right;

        if (HasClose)
        {
            yield return (DecorationPart.Close, new Rectangle(right - width, frame.Y, width, Caption));
            right -= width;
        }

        // a tool window has the close button alone, as on Windows
        if (IsToolWindow) yield break;

        if (HasMaximize)
        {
            yield return (DecorationPart.Maximize, new Rectangle(right - width, frame.Y, width, Caption));
            right -= width;
        }

        if (HasMinimize)
            yield return (DecorationPart.Minimize, new Rectangle(right - width, frame.Y, width, Caption));
    }

    /// <summary>The part under the pointer; true when that changes how the frame looks.</summary>
    public bool SetHover(DecorationPart part)
    {
        if (part == _hover) return false;

        bool wasButton = IsButton(_hover);
        _hover = part;

        return wasButton || IsButton(part);
    }

    /// <summary>The button held down; true when that changes how the frame looks.</summary>
    public bool SetPressed(DecorationPart part)
    {
        if (part == _pressed) return false;

        _pressed = part;
        return true;
    }

    public DecorationPart Pressed => _pressed;

    public static bool IsButton(DecorationPart part) =>
        part is DecorationPart.Minimize or DecorationPart.Maximize or DecorationPart.Close;

    /// <summary>The cursor over a part of the frame; null over the form — the form's own.</summary>
    public static CursorKind? CursorFor(DecorationPart part) => part switch
    {
        DecorationPart.Client => null,
        DecorationPart.Top or DecorationPart.Bottom => CursorKind.SizeNorthSouth,
        DecorationPart.Left or DecorationPart.Right => CursorKind.SizeWestEast,
        DecorationPart.TopLeft or DecorationPart.BottomRight => CursorKind.SizeNorthWestSouthEast,
        DecorationPart.TopRight or DecorationPart.BottomLeft => CursorKind.SizeNorthEastSouthWest,
        _ => CursorKind.Default,
    };

    /// <summary>xdg_toplevel's edge for a part the window is resized by; null for the others.</summary>
    public static XdgToplevel.ResizeEdge? EdgeOf(DecorationPart part) => part switch
    {
        DecorationPart.Top => XdgToplevel.ResizeEdge.Top,
        DecorationPart.Bottom => XdgToplevel.ResizeEdge.Bottom,
        DecorationPart.Left => XdgToplevel.ResizeEdge.Left,
        DecorationPart.Right => XdgToplevel.ResizeEdge.Right,
        DecorationPart.TopLeft => XdgToplevel.ResizeEdge.TopLeft,
        DecorationPart.TopRight => XdgToplevel.ResizeEdge.TopRight,
        DecorationPart.BottomLeft => XdgToplevel.ResizeEdge.BottomLeft,
        DecorationPart.BottomRight => XdgToplevel.ResizeEdge.BottomRight,
        _ => null,
    };

    /// <summary>The frame, in logical units: the graphics is scaled by the caller, and the
    /// surface is cleared to transparent before.</summary>
    public void Draw(Graphics g, Size client)
    {
        Rectangle frame = Geometry(client);

        Color background = Background ?? (IsDark ? new Color(0x2B, 0x2B, 0x2B) : new Color(0xEB, 0xEB, 0xEB));
        Color foreground = Foreground ?? (IsDark ? new Color(0xF2, 0xF2, 0xF2) : new Color(0x1A, 0x1A, 0x1A));

        // an inactive window's caption is dimmed, as every desktop does
        Color text = IsActive ? foreground : foreground.WithA(0x99);

        if (Band > 0)
            g.DrawShadow(frame, new BoxShadow(0, 2, Band - 2, 0, new Color(IsActive ? (byte)80 : (byte)45, 0, 0, 0)));

        var caption = new Rectangle(frame.X, frame.Y, frame.Width, Caption);
        g.FillRectangle(caption, background);

        // the title in the middle of the window, between the buttons' room on both
        // sides, so it doesn't drift with how many buttons there are
        float buttonsWidth = Buttons(frame).Sum(b => b.Rect.Width);
        var titleRect = new Rectangle(
            frame.X + buttonsWidth + 8, frame.Y, Math.Max(0, frame.Width - 2 * (buttonsWidth + 8)), Caption);

        Font font = Font.Default.WithSize(IsToolWindow ? 12 : 13).SemiBold();
        g.DrawText(Title, titleRect, text, font);

        foreach ((DecorationPart part, Rectangle rect) in Buttons(frame))
            DrawButton(g, part, rect, foreground, text);

        // a thin line round a floating window: it tells the window from one behind it of
        // the same color
        if (Band > 0)
        {
            Color border = IsDark ? new Color(0x60, 0xFF, 0xFF, 0xFF) : new Color(0x40, 0, 0, 0);
            g.DrawRectangle(new Rectangle(frame.X - 0.5f, frame.Y - 0.5f, frame.Width + 1, frame.Height + 1), border, 1f);
        }
    }

    private void DrawButton(Graphics g, DecorationPart part, Rectangle rect, Color foreground, Color glyph)
    {
        bool hovered = _hover == part;
        bool pressed = _pressed == part && hovered;

        if (part == DecorationPart.Close && hovered)
        {
            // the close button turns red, as on Windows: its glyph goes white over it
            g.FillRectangle(rect, pressed ? new Color(0x94, 0x1F, 0x13) : new Color(0xC4, 0x2B, 0x1C));
            glyph = Colors.White;
        }
        else if (hovered)
        {
            g.FillRectangle(rect, foreground.WithA(pressed ? (byte)0x33 : (byte)0x1A));
        }

        // a 1-unit stroke on a pixel's middle is one pixel wide at scale 1, not two half-lit
        Point c = new(MathF.Floor(rect.Center.X) + 0.5f, MathF.Floor(rect.Center.Y) + 0.5f);
        const float h = 5f;

        switch (part)
        {
            case DecorationPart.Minimize:
                g.DrawLine(new Point(c.X - h, c.Y), new Point(c.X + h, c.Y), glyph, 1f);
                break;

            case DecorationPart.Maximize when IsMaximized:
                // restore: two windows, the front one lower and to the left
                g.DrawRectangle(new Rectangle(c.X - h, c.Y - h + 2, 2 * h - 2, 2 * h - 2), glyph, 1f);
                g.DrawPolyline(
                    [new Point(c.X - h + 2, c.Y - h + 2), new Point(c.X - h + 2, c.Y - h), new Point(c.X + h, c.Y - h), new Point(c.X + h, c.Y + h - 2), new Point(c.X + h - 2, c.Y + h - 2)],
                    glyph, 1f);
                break;

            case DecorationPart.Maximize:
                g.DrawRectangle(new Rectangle(c.X - h, c.Y - h, 2 * h, 2 * h), glyph, 1f);
                break;

            case DecorationPart.Close:
                g.DrawLine(new Point(c.X - h, c.Y - h), new Point(c.X + h, c.Y + h), glyph, 1f);
                g.DrawLine(new Point(c.X + h, c.Y - h), new Point(c.X - h, c.Y + h), glyph, 1f);
                break;
        }
    }
}