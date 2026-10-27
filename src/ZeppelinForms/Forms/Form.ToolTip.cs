using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms;

/// <summary>The tooltip of the hovered element: shown after <see cref="ToolTipDelay"/>,
/// near the pointer and inside the client area. The tooltip of the focused element too,
/// while the form is worked from the keyboard: under the element, centered on it.</summary>
/// <remarks>
/// <para>
/// The tooltip is a <see cref="Controls.ToolTip"/>: the theme gives it its colors, edge,
/// rounding and shadow, and a style sheet reaches it by its type.
/// </para>
/// <para>
/// From the keyboard, as in WinUI: someone who tabs onto an icon button has no pointer
/// to rest on it, and the tooltip is the only place its meaning is written. A key or a
/// pointer press closes a tooltip, whichever brought it.
/// </para>
/// </remarks>
public partial class Form
{
    /// <summary>How long the pointer rests on an element, or the keyboard focus stays
    /// on it, before its tooltip shows, in milliseconds. An element's own
    /// <see cref="UIElement.ToolTipDelay"/> wins.</summary>
    public int ToolTipDelay { get; set; } = 700;

    private IDisposable? _toolTipWake;
    private UIElement? _toolTipOwner;
    private ToolTip? _activeToolTip;
    private Point _lastPointerPosition;

    /// <summary>The tooltip waited for or shown belongs to the focused element, not to the
    /// one under the pointer: it is placed by the element and closed when focus moves.</summary>
    private bool _toolTipFromKeyboard;

    /// <summary>The tooltip on the screen, for tests and the inspector.</summary>
    internal ToolTip? ActiveToolTip => _activeToolTip;

    /// <summary>Gap between a focused element and its tooltip.</summary>
    private const float KeyboardToolTipGap = 4f;

    private static bool HasToolTip(UIElement element, Point at) =>
        element.ToolTipContent is not null || !string.IsNullOrEmpty(element.GetToolTip(at));

    private int DelayOf(UIElement element) => Math.Max(0, element.ToolTipDelay ?? ToolTipDelay);

    /// <summary>Where a keyboard tooltip asks its element: the element's middle, for the
    /// controls that answer by the part under a point.</summary>
    private static Point CenterOf(UIElement element)
    {
        Point at = element.GetAbsolutePosition();
        return new Point(at.X + element.ActualSize.Width / 2f, at.Y + element.ActualSize.Height / 2f);
    }

    private void ScheduleToolTip(UIElement? target)
    {
        HideToolTip();

        _toolTipFromKeyboard = false;
        _toolTipOwner = target is not null && HasToolTip(target, _lastPointerPosition) ? target : null;

        if (_toolTipOwner is not null)
            _toolTipWake = Schedule(DelayOf(_toolTipOwner), ShowToolTipCore);
    }

    /// <summary>Focus moved: a tooltip of the old focus closes, and the new focus gets
    /// its own while the form is worked from the keyboard. A click that focuses an
    /// element shows nothing — the pointer brings its own tooltip by hovering.</summary>
    private void OnFocusChangedForToolTip(object? sender, UIElement? focused)
    {
        if (_toolTipFromKeyboard) HideToolTip();

        if (focused is null || !IsFocusVisible || !HasToolTip(focused, CenterOf(focused))) return;

        HideToolTip();

        _toolTipFromKeyboard = true;
        _toolTipOwner = focused;
        _toolTipWake = Schedule(DelayOf(focused), ShowToolTipCore);
    }

    private void ShowToolTipCore()
    {
        if (_toolTipOwner is not { } owner) return;

        Point asked = _toolTipFromKeyboard ? CenterOf(owner) : _lastPointerPosition;

        ToolTip tip;

        if (owner.ToolTipContent is { } content)
        {
            // lent: a content still inside the last tooltip, or put somewhere else
            // meanwhile, can't have two parents
            if (content.Parent is not null) return;

            tip = new ToolTip(content);
        }
        else if (owner.GetToolTip(asked) is { Length: > 0 } text)
        {
            tip = new ToolTip(text);
        }
        else
        {
            return;
        }

        // attached first: the theme and the styles give it its padding and font,
        // and it fades in from here (see AttachOverlay)
        AttachOverlay(tip);

        tip.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
        Size size = tip.DesiredSize;

        tip.Position = _toolTipFromKeyboard ? PlaceByElement(owner, size) : PlaceByPointer(size);

        _activeToolTip = tip;

        Invalidate();
    }

    /// <summary>Slightly below and to the right of the cursor, as system tooltips do;
    /// above it where there's no room below. Inside the client area.</summary>
    private Point PlaceByPointer(Size size)
    {
        float x = _lastPointerPosition.X + 12;
        float y = _lastPointerPosition.Y + 20;

        if (x + size.Width > ClientSize.Width)
            x = Math.Max(0, ClientSize.Width - size.Width);

        if (y + size.Height > ClientSize.Height)
            y = Math.Max(0, _lastPointerPosition.Y - size.Height - 4);

        return new Point(x, y);
    }

    /// <summary>Under the element and centered on it; above it where there's no room
    /// below. Inside the client area.</summary>
    private Point PlaceByElement(UIElement element, Size size)
    {
        Point at = element.GetAbsolutePosition();
        Size box = element.ActualSize;

        float x = at.X + (box.Width - size.Width) / 2f;
        float y = at.Y + box.Height + KeyboardToolTipGap;

        if (y + size.Height > ClientSize.Height)
            y = at.Y - size.Height - KeyboardToolTipGap;

        x = Math.Clamp(x, 0, Math.Max(0, ClientSize.Width - size.Width));
        y = Math.Clamp(y, 0, Math.Max(0, ClientSize.Height - size.Height));

        return new Point(x, y);
    }

    /// <summary>The hovered element's tooltip changed with the part under the pointer.</summary>
    internal void RefreshToolTip(UIElement element)
    {
        if (!ReferenceEquals(element, _hoveredElement)) return;

        // a keyboard tooltip doesn't follow the pointer's parts
        if (_toolTipFromKeyboard) return;

        bool shown = _activeToolTip is not null;

        HideToolTip();

        _toolTipOwner = HasToolTip(element, _lastPointerPosition) ? element : null;

        if (_toolTipOwner is null) return;

        // moving between the parts of one control, the tip follows without
        // the delay again — it was asked for already
        if (shown) ShowToolTipCore();
        else _toolTipWake = Schedule(DelayOf(element), ShowToolTipCore);
    }

    private void HideToolTip()
    {
        _toolTipWake?.Dispose();
        _toolTipWake = null;

        if (_activeToolTip is { } tip)
        {
            _activeToolTip = null;

            DetachOverlay(tip);

            // a lent ToolTipContent goes back to its element, free for the next showing
            tip.Child = null;

            Invalidate();
        }
    }
}
