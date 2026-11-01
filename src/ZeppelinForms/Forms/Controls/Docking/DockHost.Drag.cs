using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.Docking;

/// <summary>A tab or a window dragged: the compass, the preview of where it goes, the drop;
/// and the host's own pointer — the strips of the auto-hidden items.</summary>
public partial class DockHost
{
    private const float CompassButton = 30f;
    private const float CompassGap = 4f;
    private const float EdgeMargin = 10f;

    /// <summary>The share a group docked on the host's edge takes of the layout.</summary>
    private const float EdgeShare = 0.25f;

    private DockDrag? _drag;

    /// <summary>The form whose keys the drag listens to while it is on: Esc cancels it
    /// wherever the focus is.</summary>
    private Form? _dragForm;

    /// <summary>Where the last press inside the host was, in the form's coordinates: the
    /// menu that comes with it is for what was there.</summary>
    internal Point LastPressLocation { get; private set; }

    /// <summary>A drop place: a button of the compass, the group it docks against (null —
    /// the whole layout) and the preview of where the dragged one would be.</summary>
    internal readonly record struct DockTarget(DockGroup? Group, DockPosition Position, Rectangle Button, Rectangle Preview);

    private sealed class DockDrag
    {
        /// <summary>The tab dragged; null — a window.</summary>
        public DockItem? Item { get; init; }

        public required DockGroup Source { get; init; }

        public DockFloat? Window { get; init; }

        /// <summary>The pointer's offset from the window's corner, kept as it moves.</summary>
        public Point Grab { get; init; }

        public Rectangle WindowStart { get; init; }

        public Size FloatSize { get; init; }

        /// <summary>The pointer, in the host's coordinates.</summary>
        public Point Pointer { get; set; }

        /// <summary>The group the compass is over, in the host's coordinates; empty — none.</summary>
        public DockGroup? Over { get; set; }

        public Rectangle OverRect { get; set; }

        public List<DockTarget> Targets { get; } = [];

        public DockTarget? Target { get; set; }
    }

    /// <summary>A drag is on: the compass shows and the drop decides.</summary>
    internal bool IsDragging => _drag is not null;

    /// <summary>The drop places right now; for tests and the inspector.</summary>
    internal IReadOnlyList<DockTarget> DragTargets => _drag?.Targets ?? [];

    private Point ToHost(Point location)
    {
        Point at = GetAbsolutePosition();
        return new Point(location.X - at.X, location.Y - at.Y);
    }

    /// <summary>A tab left its strip: the host carries the drag on.</summary>
    internal void BeginItemDrag(DockItem item, DockGroup source, Point location)
    {
        // the only tab of a window drags the window
        if (source.Float is { } window && source.Items.Count == 1)
        {
            BeginWindowDrag(window, location);
            return;
        }

        Size size = source.View is { ActualSize: { Width: > 0 } actual }
            ? new Size(Math.Clamp(actual.Width, 220f, 560f), Math.Clamp(actual.Height, 160f, 420f))
            : new Size(320f, 240f);

        _drag = new DockDrag
        {
            Item = item,
            Source = source,
            Grab = new Point(Math.Min(60f, size.Width / 3f), DockFloatingWindow.TitleHeight / 2f),
            FloatSize = item.LastFloatBounds?.Size ?? size,
        };

        StartDrag();
        UpdateDrag(location);
    }

    internal void BeginWindowDrag(DockFloat window, Point location)
    {
        BringToFront(window);

        Point local = ToHost(location);

        _drag = new DockDrag
        {
            Source = window.Group,
            Window = window,
            Grab = new Point(local.X - window.Bounds.X, local.Y - window.Bounds.Y),
            WindowStart = window.Bounds,
            FloatSize = window.Bounds.Size,
            Pointer = local,
        };

        StartDrag();
    }

    private void StartDrag()
    {
        CaptureMouse();

        StopListening();

        _dragForm = FindOwner();

        if (_dragForm is not null) _dragForm.PreviewKeyDown += OnDragKey;
    }

    private void StopListening()
    {
        if (_dragForm is not null) _dragForm.PreviewKeyDown -= OnDragKey;

        _dragForm = null;
    }

    private void OnDragKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _drag is null) return;

        CancelDrag();
        e.Handled = true;
    }

    private void UpdateDrag(Point location)
    {
        if (_drag is not { } drag) return;

        Point local = ToHost(location);
        drag.Pointer = local;

        if (drag.Window is { } window)
        {
            window.Bounds = ClampWindow(new Rectangle(local.X - drag.Grab.X, local.Y - drag.Grab.Y, window.Bounds.Width, window.Bounds.Height));
            Invalidate();
        }

        FindTargets(drag);

        drag.Target = null;

        foreach (DockTarget target in drag.Targets)
        {
            if (!target.Button.Contains(local)) continue;

            drag.Target = target;
            break;
        }

        // over a group's tabs, without a button: a tab of it, as the compass' middle would be
        if (drag.Target is null && drag.Over is { } over && over.View is { } view && local.Y < drag.OverRect.Y + view.ContentArea.Y)
            drag.Target = drag.Targets.FirstOrDefault(t => ReferenceEquals(t.Group, over) && t.Position == DockPosition.Tabbed);

        InvalidateVisual();
    }

    /// <summary>The compass of the group under the pointer — a floating window's has only
    /// the middle — and the four buttons on the host's edges.</summary>
    private void FindTargets(DockDrag drag)
    {
        drag.Targets.Clear();
        drag.Over = null;
        drag.OverRect = Rectangle.Empty;

        Rectangle inner = InnerRect(ActualSize);
        Point p = drag.Pointer;

        if (_root is null)
        {
            // an empty host: the dragged one fills it
            AddCompass(drag, null, inner, middleOnly: true);
            return;
        }

        // the windows lie over the layout: the topmost one under the pointer is the one
        bool overWindow = false;

        for (int i = _floats.Count - 1; i >= 0; i--)
        {
            DockFloat window = _floats[i];

            if (ReferenceEquals(window, drag.Window) || !ClampWindow(window.Bounds).Contains(p)) continue;

            overWindow = true;

            if (!IsDropOnItself(drag, window.Group) && window.Group.View is { } view)
                AddCompass(drag, window.Group, RectOf(view), middleOnly: true);

            break;
        }

        if (!overWindow && !(_openAutoHide is not null && AutoPanelRect(inner).Contains(p)))
        {
            foreach (DockGroup group in DockTree.Groups(_root))
            {
                if (group.View is not { } view) continue;

                Rectangle rect = RectOf(view);

                if (!rect.Contains(p)) continue;

                if (!IsDropOnItself(drag, group)) AddCompass(drag, group, rect, middleOnly: false);

                break;
            }
        }

        // the edges of the whole layout
        float half = CompassButton / 2f;
        Point c = inner.Center;

        AddTarget(drag, null, DockPosition.Left, new Rectangle(inner.X + EdgeMargin, c.Y - half, CompassButton, CompassButton),
            new Rectangle(inner.X, inner.Y, inner.Width * EdgeShare, inner.Height));
        AddTarget(drag, null, DockPosition.Top, new Rectangle(c.X - half, inner.Y + EdgeMargin, CompassButton, CompassButton),
            new Rectangle(inner.X, inner.Y, inner.Width, inner.Height * EdgeShare));
        AddTarget(drag, null, DockPosition.Right, new Rectangle(inner.Right - EdgeMargin - CompassButton, c.Y - half, CompassButton, CompassButton),
            new Rectangle(inner.Right - inner.Width * EdgeShare, inner.Y, inner.Width * EdgeShare, inner.Height));
        AddTarget(drag, null, DockPosition.Bottom, new Rectangle(c.X - half, inner.Bottom - EdgeMargin - CompassButton, CompassButton, CompassButton),
            new Rectangle(inner.X, inner.Bottom - inner.Height * EdgeShare, inner.Width, inner.Height * EdgeShare));
    }

    /// <summary>A tab dropped on its own group alone, or beside it, would go nowhere.</summary>
    private static bool IsDropOnItself(DockDrag drag, DockGroup group) =>
        ReferenceEquals(group, drag.Source) && (drag.Window is not null || group.Items.Count == 1);

    private Rectangle RectOf(UIElement view)
    {
        Point at = view.GetAbsolutePosition();
        Point origin = GetAbsolutePosition();

        return new Rectangle(at.X - origin.X, at.Y - origin.Y, view.ActualSize.Width, view.ActualSize.Height);
    }

    private static void AddCompass(DockDrag drag, DockGroup? group, Rectangle rect, bool middleOnly)
    {
        drag.Over = group;
        drag.OverRect = rect;

        Point c = rect.Center;
        float half = CompassButton / 2f;
        float step = CompassButton + CompassGap;

        var middle = new Rectangle(c.X - half, c.Y - half, CompassButton, CompassButton);

        AddTarget(drag, group, DockPosition.Tabbed, middle, rect);

        if (middleOnly) return;

        float w = rect.Width / 2f;
        float h = rect.Height / 2f;

        AddTarget(drag, group, DockPosition.Left, middle.Offset(-step, 0), new Rectangle(rect.X, rect.Y, w, rect.Height));
        AddTarget(drag, group, DockPosition.Top, middle.Offset(0, -step), new Rectangle(rect.X, rect.Y, rect.Width, h));
        AddTarget(drag, group, DockPosition.Right, middle.Offset(step, 0), new Rectangle(rect.X + w, rect.Y, w, rect.Height));
        AddTarget(drag, group, DockPosition.Bottom, middle.Offset(0, step), new Rectangle(rect.X, rect.Y + h, rect.Width, h));
    }

    private static void AddTarget(DockDrag drag, DockGroup? group, DockPosition position, Rectangle button, Rectangle preview)
    {
        // a narrow group by the host's edge has its compass over the edge's button: the
        // group's is the one shown, the edge's is reached past the group
        if (group is null)
        {
            foreach (DockTarget existing in drag.Targets)
                if (existing.Button.Inflate(CompassGap).IntersectsWith(button)) return;
        }

        drag.Targets.Add(new DockTarget(group, position, button, preview));
    }

    /// <summary>The drop: where the target says, or a window of its own where the pointer is.</summary>
    private void EndDrag(Point location)
    {
        UpdateDrag(location);

        if (_drag is not { } drag) return;

        _drag = null;
        StopListening();
        ReleaseMouseCapture();

        if (drag.Target is { } target)
        {
            DropOn(drag, target);
        }
        else if (drag.Item is { } item)
        {
            if (item.CanFloat)
            {
                Detach(item);
                PlaceFloating(item, ClampWindow(new Rectangle(
                    drag.Pointer.X - drag.Grab.X, drag.Pointer.Y - drag.Grab.Y, drag.FloatSize.Width, drag.FloatSize.Height)));

                SetActive(item);
            }
        }

        Changed();
    }

    private void DropOn(DockDrag drag, DockTarget target)
    {
        float share = target.Group is null ? EdgeShare : 0.5f;

        if (drag.Item is { } item)
        {
            Detach(item);

            // the target's group may have gone with the item: an item dropped beside the
            // group it was the last of goes beside where it was
            DockGroup? group = target.Group is { } g && (g.Float is not null ? _floats.Contains(g.Float) : IsInLayout(g)) ? g : null;

            Insert(item, target.Position, group, share);
            SetActive(item);
            return;
        }

        if (drag.Window is not { } window) return;

        DockGroup moving = window.Group;
        DockItem? selected = moving.Selected;

        if (target.Position == DockPosition.Tabbed || target.Group?.Float is not null)
        {
            DockGroup into = target.Group ?? PrimaryGroup() ?? NewRootGroup();

            foreach (DockItem each in moving.Items.ToArray())
            {
                Detach(each);
                AddToGroup(into, each, select: false);
            }

            if (selected is not null) into.Selected = selected;
        }
        else
        {
            // the window's group itself goes into the layout, its tabs as they were
            _floats.Remove(window);
            moving.Float = null;

            foreach (DockItem each in moving.Items)
            {
                each.State = DockState.Docked;
                each.Side = SideOf(target.Position);
                each.LastFloatBounds = window.Bounds;
            }

            _root = _root is null
                ? moving
                : DockTree.InsertBeside(_root, target.Group ?? _root, moving, target.Position, share);
        }

        if (selected is not null) SetActive(selected);
    }

    private void CancelDrag()
    {
        if (_drag is not { } drag) return;

        _drag = null;
        StopListening();
        ReleaseMouseCapture();

        if (drag.Window is { } window) window.Bounds = drag.WindowStart;

        Invalidate();
    }

    // ===== drawing the drag =====

    protected override void DrawDecoration(Graphics g)
    {
        base.DrawDecoration(g);

        if (_drag is not { } drag) return;

        if (drag.Target is { } target)
        {
            g.FillRectangle(target.Preview, PreviewColor);
            g.DrawRectangle(target.Preview.Inflate(-1), AccentColor, 2f);
        }
        else if (drag.Item is not null && drag.Item.CanFloat)
        {
            // dropped here, it floats here
            var ghost = new Rectangle(drag.Pointer.X - drag.Grab.X, drag.Pointer.Y - drag.Grab.Y, drag.FloatSize.Width, drag.FloatSize.Height);
            g.DrawRectangle(ghost, AccentColor, 1.5f);
        }

        foreach (DockTarget each in drag.Targets)
            DrawCompassButton(g, each, ReferenceEquals(each.Group, drag.Target?.Group) && each.Position == drag.Target?.Position);
    }

    private void DrawCompassButton(Graphics g, DockTarget target, bool hot)
    {
        Rectangle b = target.Button;

        g.FillRoundRectangle(b, new CornerRadius(4), hot ? AccentColor : CompassColor);
        g.DrawRoundRectangle(b, new CornerRadius(4), AccentColor, 1f);

        Color ink = hot ? CompassColor : AccentColor;
        Rectangle face = b.Inflate(-7);

        // a window in the middle; a half of one on a side
        g.DrawRectangle(face, ink, 1.2f);

        Rectangle half = target.Position switch
        {
            DockPosition.Left => new Rectangle(face.X, face.Y, face.Width / 2f, face.Height),
            DockPosition.Right => new Rectangle(face.X + face.Width / 2f, face.Y, face.Width / 2f, face.Height),
            DockPosition.Top => new Rectangle(face.X, face.Y, face.Width, face.Height / 2f),
            DockPosition.Bottom => new Rectangle(face.X, face.Y + face.Height / 2f, face.Width, face.Height / 2f),
            _ => new Rectangle(face.X, face.Y, face.Width, 3f),
        };

        g.FillRectangle(half, ink.WithA((byte)(ink.A * 0.55f)));
    }

    // ===== the host's own pointer =====

    protected internal override bool HitTestSelfFirst(Point localPoint) =>
        _drag is not null || OnStrip(localPoint) || base.HitTestSelfFirst(localPoint);

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        LastPressLocation = e.Location;

        // a press outside the slid-out panel slides it back; its strip toggles it itself
        if (_openAutoHide is not null && _autoPanel is not null)
        {
            Point local = ToHost(e.Location);

            if (!OnStrip(local) && !AutoPanelRect(InnerRect(ActualSize)).Contains(local))
                CloseAutoHide(sync: true);
        }
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (_drag is not null)
        {
            UpdateDrag(e.Location);
            return;
        }

        DockItem? hovered = StripItemAt(ToHost(e.Location));

        if (!ReferenceEquals(hovered, _hoveredStripItem))
        {
            _hoveredStripItem = hovered;
            InvalidateVisual();
            RefreshToolTip();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hoveredStripItem is null) return;

        _hoveredStripItem = null;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button == MouseButton.Left && StripItemAt(ToHost(e.Location)) is { } item)
        {
            ToggleAutoHide(item);
            e.Handled = true;
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_drag is not null)
        {
            EndDrag(e.Location);
            return;
        }

        base.OnMouseUp(e);
    }

    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        CancelDrag();

        base.OnPointerCanceled(e);
    }

    protected internal override string? GetToolTip(Point location) =>
        StripItemAt(ToHost(location)) is { } item ? item.ToolTip ?? item.Title : base.GetToolTip(location);
}
