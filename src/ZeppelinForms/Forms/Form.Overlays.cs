using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Layout;

namespace ZeppelinForms.Forms;

/// <summary>What lies over the content: overlays, flyouts, toasts and context menus.</summary>
public partial class Form
{
    // The list is private and changes only through AttachOverlay/DetachOverlay.
    // It used to be a plain List, and five methods out of six added the element
    // directly — the theme never reached the overlay, and DetachTree was not
    // called on close. There is no direct Add anymore, so attaching can't be
    // forgotten: the only way in goes through it.
    private readonly List<UIElement> _overlays = [];
    public IReadOnlyList<UIElement> Overlays => _overlays;
    private readonly List<UIElement> _flyouts = [];
    private readonly List<UIElement> _toasts = [];

    /// <summary>Show <paramref name="content"/> next to <paramref name="anchor"/>. It closes
    /// on a click outside or Esc.</summary>
    /// <remarks>
    /// <para>The content is shown as it is: a menu or a picker draws its own surface.
    /// Content without one — a plain panel — goes into a <see cref="FlyoutPresenter"/>,
    /// otherwise it has no background and the form shows through it.</para>
    /// <para>The side is a wish: where the flyout doesn't fit, it opens on the opposite
    /// side if it fits there, and stays inside the window along both axes. A drop-down
    /// at the bottom of the window used to be cut off by its edge.</para>
    /// </remarks>
    public void ShowFlyout(UIElement anchor, UIElement content, FlyoutPlacement placement = FlyoutPlacement.Bottom)
    {
        AttachOverlay(content);

        content.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        Point anchorPos = anchor.GetAbsolutePosition();
        Size anchorSize = anchor.ActualSize;
        Size contentSize = content.DesiredSize;
        Size window = ClientSize;

        FlyoutPlacement side = FitPlacement(placement, anchorPos, anchorSize, contentSize, window);

        Point at = side switch
        {
            FlyoutPlacement.Bottom => new Point(anchorPos.X, anchorPos.Y + anchorSize.Height),
            FlyoutPlacement.Top => new Point(anchorPos.X, anchorPos.Y - contentSize.Height),
            FlyoutPlacement.Right => new Point(anchorPos.X + anchorSize.Width, anchorPos.Y),
            FlyoutPlacement.Left => new Point(anchorPos.X - contentSize.Width, anchorPos.Y),
            _ => anchorPos,
        };

        // kept inside the window on both axes, as far as it fits: across its side it
        // slides along the anchor, and on a side too small for it, it covers the anchor
        // rather than leaving the window
        content.Position = new Point(
            Math.Clamp(at.X, 0, Math.Max(0, window.Width - contentSize.Width)),
            Math.Clamp(at.Y, 0, Math.Max(0, window.Height - contentSize.Height)));

        _flyouts.Add(content);

        Invalidate();
    }

    /// <summary>The side a flyout opens on: the one asked for while it fits; the opposite
    /// one when that fits and the asked one doesn't; when neither fits, the roomier one.</summary>
    private static FlyoutPlacement FitPlacement(
        FlyoutPlacement wanted, Point anchor, Size anchorSize, Size content, Size window)
    {
        float below = window.Height - (anchor.Y + anchorSize.Height);
        float above = anchor.Y;
        float right = window.Width - (anchor.X + anchorSize.Width);
        float left = anchor.X;

        return wanted switch
        {
            FlyoutPlacement.Bottom => Choose(FlyoutPlacement.Bottom, below, FlyoutPlacement.Top, above, content.Height),
            FlyoutPlacement.Top => Choose(FlyoutPlacement.Top, above, FlyoutPlacement.Bottom, below, content.Height),
            FlyoutPlacement.Right => Choose(FlyoutPlacement.Right, right, FlyoutPlacement.Left, left, content.Width),
            FlyoutPlacement.Left => Choose(FlyoutPlacement.Left, left, FlyoutPlacement.Right, right, content.Width),
            _ => wanted,
        };

        static FlyoutPlacement Choose(
            FlyoutPlacement side, float room, FlyoutPlacement opposite, float oppositeRoom, float need) =>
            room >= need ? side
            : oppositeRoom >= need || oppositeRoom > room ? opposite
            : side;
    }

    public void CloseFlyout(UIElement content)
    {
        if (!DetachOverlay(content)) return;

        _flyouts.Remove(content);

        FlyoutClosed?.Invoke(this, content);
        Invalidate();
    }

    public void CloseAllFlyouts()
    {
        if (_flyouts.Count == 0) return;

        // a copy: event handlers may open a new flyout,
        // and the collection would change during the walk
        UIElement[] closing = [.. _flyouts];

        foreach (UIElement flyout in closing)
            DetachOverlay(flyout);

        _flyouts.Clear();

        foreach (UIElement flyout in closing)
            FlyoutClosed?.Invoke(this, flyout);

        Invalidate();
    }

    /// <summary>Put an element on top of the content. Unlike ShowFlyout, it is not
    /// closed by a click outside and takes no part in the popup logic.</summary>
    public void AddOverlay(UIElement content)
    {
        AttachOverlay(content);
        Invalidate();
    }

    public void RemoveOverlay(UIElement content)
    {
        if (!DetachOverlay(content)) return;

        Invalidate();
    }

    // ==== Toast =====

    public void ShowToast(string message, int durationMs = 3000, ToastPosition position = ToastPosition.BottomRight)
    {
        var toast = new Border
        {
            BorderColor = new Color(255, 60, 60, 60),
            BorderWidth = 1,
            Background = new Color(235, 45, 45, 45),
            Padding = new Thickness(14, 10),
            IsHitTestVisible = false,
            Child = new Label { Text = message, TextColor = Colors.White },
        };

        // Owner is set by AttachOverlay itself; assigning it here first made
        // its "not owned yet" contract fail on every toast
        AttachOverlay(toast);

        toast.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        _toasts.Add(toast);
        ArrangeToasts(position);
        Invalidate();

        Schedule(durationMs, () =>
        {
            DetachOverlay(toast);
            _toasts.Remove(toast);
            ArrangeToasts(position);   // the remaining ones move up into the freed space
            Invalidate();
        });
    }

    private void ArrangeToasts(ToastPosition position)
    {
        const float margin = 16f;
        const float gap = 8f;

        bool fromTop = position is ToastPosition.TopRight or ToastPosition.TopCenter;
        bool centered = position is ToastPosition.TopCenter or ToastPosition.BottomCenter;

        float offset = margin;

        // at the bottom new ones appear lower and old ones move up, so we go from the end
        IEnumerable<UIElement> order = fromTop ? _toasts : Enumerable.Reverse(_toasts);

        foreach (var toast in order)
        {
            Size size = toast.DesiredSize;

            float x = centered
                ? (ClientSize.Width - size.Width) / 2f
                : ClientSize.Width - size.Width - margin;

            float y = fromTop ? offset : ClientSize.Height - size.Height - offset;

            toast.Position = new Point(x, y);
            offset += size.Height + gap;
        }
    }

    internal void OnContextMenu(Point point)
    {
        UIElement? hit = HitTestAll(point);

        // the menu is searched up the tree: if the button itself has none,
        // ask the panel, then the form. An element's own menu comes first,
        // then the one it offers by default — a text field's editing commands
        for (UIElement? current = hit; current is not null; current = current.Parent)
        {
            List<MenuItem>? items = current.ContextMenu is { Count: > 0 } own
                ? own
                : current.DefaultContextMenu();

            if (items is { Count: > 0 })
            {
                ShowContextMenu(items, point);
                return;
            }
        }
    }

    /// <summary>A submenu beside its item: right of the menu, or left of it where the
    /// window has no room on the right; moved up as far as it must to stay inside.
    /// The menus open already stay open — the submenu is one more flyout on top.</summary>
    /// <param name="item">The item's row, in the form's coordinates.</param>
    internal void ShowSubmenu(MenuList submenu, Rectangle item)
    {
        AttachOverlay(submenu);

        submenu.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        Size size = submenu.DesiredSize;
        Size window = ClientSize;

        // the submenu's first row level with the item, overlapping the menu by a hair
        const float overlap = 2f;

        float x = item.X + item.Width - overlap;

        if (x + size.Width > window.Width && item.X - size.Width + overlap >= 0)
            x = item.X - size.Width + overlap;

        float y = item.Y - submenu.Padding.Top;

        submenu.Position = new Point(
            Math.Clamp(x, 0, Math.Max(0, window.Width - size.Width)),
            Math.Clamp(y, 0, Math.Max(0, window.Height - size.Height)));

        _flyouts.Add(submenu);

        Invalidate();
    }

    public void ShowContextMenu(List<MenuItem> items, Point position)
    {
        CloseAllFlyouts();

        var menu = new MenuList { Items = items };
        menu.ItemInvoked += (_, _) => CloseAllFlyouts();

        AttachOverlay(menu);

        menu.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        float x = Math.Min(position.X, Math.Max(0, ClientSize.Width - menu.DesiredSize.Width));
        float y = Math.Min(position.Y, Math.Max(0, ClientSize.Height - menu.DesiredSize.Height));

        menu.Position = new Point(x, y);

        _flyouts.Add(menu);   // closes on a click outside — as a menu should

        Invalidate();
    }
}
