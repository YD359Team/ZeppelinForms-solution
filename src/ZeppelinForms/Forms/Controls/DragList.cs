using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls;

/// <summary>What was moved and where.</summary>
public sealed record class DragListDropEventArgs(
    object Item,
    DragList Source,
    int SourceIndex,
    DragList Target,
    int TargetIndex);

/// <summary>
/// A list with rows reordered by the mouse. Lists with the same
/// <see cref="Group"/> exchange rows with each other.
/// </summary>
public partial class DragList : ItemsControl
{
    // one drag for the whole application: only one row can be dragged,
    // and the receiving list must know what is flying to it
    private static DragList? _source;
    private static DragList? _target;
    private static object? _item;
    private static int _sourceIndex = -1;
    private static int _targetIndex = -1;
    private static UIElement? _preview;

    private static readonly Dictionary<string, List<DragList>> Groups = new(StringComparer.Ordinal);

    private string? _group;
    private bool _attached;
    /// <summary>What drives the current interaction. With the mouse a move starts
    /// at the movement threshold, with a finger only after a hold: otherwise
    /// scrolling the list with a finger would become impossible, every movement
    /// would carry a row away.</summary>
    private PointerKind _pointerKind = PointerKind.Mouse;
    private bool _dragging;
    private Point _pressOrigin;
    private int _pressIndex = -1;

    /// <summary>The group name. Lists of one group can pass rows
    /// to each other. Empty — the list is closed on itself.</summary>
    public string? Group
    {
        get => _group;
        set
        {
            if (string.Equals(_group, value, StringComparison.Ordinal)) return;

            Unregister();
            _group = value;
            Register();
        }
    }

    /// <summary>Whether rows may be taken from here.</summary>
    public bool CanSendItem { get; set; } = true;

    /// <summary>Whether rows may be put here.</summary>
    public bool CanReceiveItem { get; set; } = true;

    /// <summary>A fine-grained filter on top of <see cref="CanReceiveItem"/>:
    /// decides by the specific row and the source list.</summary>
    public Func<object, DragList, bool>? ReceivePredicate { get; set; }

    /// <summary>How many pixels must be dragged before it counts as dragging
    /// rather than a miss while clicking.</summary>
    public float DragThreshold { get; set; } = 4f;

    public float DropIndicatorHeight { get; set; } = 2f;

    [Styled(Category = "Drag")]
    public partial Color DropIndicatorColor { get; set; }
    private static Color DropIndicatorColorDefault => new(255, 0, 120, 215);

    [Styled(Category = "Drag")]
    public partial Color DragPreviewBackground { get; set; }
    private static Color DragPreviewBackgroundDefault => Colors.White;

    /// <summary>A row was taken away from here.</summary>
    public event EventHandler<DragListDropEventArgs>? ItemSent;

    /// <summary>A row was brought here.</summary>
    public event EventHandler<DragListDropEventArgs>? ItemReceived;

    public DragList()
    {
        // on a touch screen a move starts with a hold — the same as in the system
        // apps' lists. Having won the fight, the hold takes the contact away from
        // scrolling, and from then on the row is driven by pointer events,
        // not by the compatibility mouse events
        var longPress = new LongPressGestureRecognizer();
        longPress.Triggered += OnLongPressed;

        AddGesture(longPress);
    }

    // ===== group registration =====

    protected override void OnAttached()
    {
        _attached = true;
        Register();
    }

    protected override void OnDetached()
    {
        _pressIndex = -1;

        Unregister();
        _attached = false;

        // the list is carried out of the tree right in the middle of a drag —
        // drop everything, otherwise the static state would keep a reference to a dead one
        if (ReferenceEquals(_source, this) || ReferenceEquals(_target, this))
            CancelDrag();
    }

    private void Register()
    {
        if (!_attached || string.IsNullOrEmpty(_group)) return;

        if (!Groups.TryGetValue(_group, out List<DragList>? members))
            Groups[_group] = members = [];

        if (!members.Contains(this))
            members.Add(this);
    }

    private void Unregister()
    {
        if (string.IsNullOrEmpty(_group)) return;
        if (!Groups.TryGetValue(_group, out List<DragList>? members)) return;

        members.Remove(this);

        if (members.Count == 0)
            Groups.Remove(_group);
    }

    /// <summary>Where dropping is possible at all: the list itself plus its group.</summary>
    private IEnumerable<DragList> DropCandidates()
    {
        yield return this;

        if (string.IsNullOrEmpty(_group)) yield break;
        if (!Groups.TryGetValue(_group, out List<DragList>? members)) yield break;

        foreach (DragList list in members)
            if (!ReferenceEquals(list, this))
                yield return list;
    }

    // ===== mouse =====

    /// <summary>The press is only remembered. Taking capture is too early: this may
    /// still turn out to be an ordinary click, and capture for the duration
    /// of a click freezes hover and the cursor for the whole form.</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        // only the left button moves rows. A drag with the right button used to
        // start a move too, while only a left release completes it — after
        // releasing the right button the preview kept following the cursor
        if (e.Button != MouseButton.Left) return;

        // the hit lands on a row, which is itself enabled,
        // so the form's check for a disabled hit doesn't stop us
        if (!IsEnabled || !CanSendItem || Children.Count == 0) return;

        int index = IndexAt(ToLocal(e.Location));
        if (index < 0) return;

        _pressOrigin = e.Location;
        _pressIndex = index;
        _dragging = false;
    }

    /// <summary>The move comes through the preview because the hit went to a row:
    /// we don't want to intercept it — buttons inside rows must keep working.</summary>
    protected override void OnPreviewMouseMove(MouseMoveEventArgs e)
    {
        if (_pressIndex < 0) return;

        // with a finger a move starts only with a hold: movement is scrolling,
        // and it must not be taken away from the list
        if (_pointerKind != PointerKind.Mouse) return;

        if (!_dragging)
        {
            // the threshold: without it any click with a shaky hand would become a move
            if (Point.DistanceBetween(e.Location, _pressOrigin) < DragThreshold) return;

            BeginDrag();

            // BeginDrag may refuse — the row is gone, the list is empty
            if (!_dragging) return;
        }

        UpdateDrag(e.Location);
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left) return;

        if (_dragging)
        {
            CompleteDrop();
            ReleaseMouseCapture();
        }

        _pressIndex = -1;
        _dragging = false;
    }

    /// <summary>The contact was cut off: a gesture took it, the platform sent
    /// a cancel, the window lost focus. There will be no release after that,
    /// so we clean up ourselves — otherwise the dragged row would hang
    /// over the form forever.</summary>
    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        _pressIndex = -1;

        if (!_dragging) return;

        CancelDrag();
    }

    /// <summary>Whether a row is being moved right now.</summary>
    public bool IsDragging => _dragging;

    // ===== touch =====

    protected override void OnPointerDown(PointerEventArgs e)
    {
        base.OnPointerDown(e);

        _pointerKind = e.Kind;
    }

    /// <summary>The hold won: the row is taken for moving from here,
    /// not from the movement threshold.</summary>
    private void OnLongPressed(object? sender, LongPressGestureEventArgs e)
    {
        if (!IsEnabled || !CanSendItem || Children.Count == 0) return;

        // the index is taken anew from the hold point: the cancellation of
        // compatibility events that comes with the gesture's victory has
        // already reset the remembered press
        int index = IndexAt(ToLocal(e.Location));
        if (index < 0) return;

        _pressOrigin = e.Location;
        _pressIndex = index;

        BeginDrag();

        if (_dragging)
            UpdateDrag(e.Location);
    }

    /// <summary>A move by finger is driven by pointer events: compatibility
    /// mouse events no longer arrive after the gesture's victory.</summary>
    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (!_dragging || _pointerKind == PointerKind.Mouse) return;

        UpdateDrag(e.Location);
    }

    protected override void OnPointerUp(PointerEventArgs e)
    {
        if (!_dragging || _pointerKind == PointerKind.Mouse) return;

        CompleteDrop();
        ReleaseMouseCapture();

        _pressIndex = -1;
        _dragging = false;
    }

    // ===== dragging =====

    private void BeginDrag()
    {
        // CancelDrag resets _pressIndex of the list that owned the unfinished
        // drag, and that may be this very list — then Items[_pressIndex] below
        // read -1 and threw. So the press is taken before the cleanup
        int index = _pressIndex;

        // the previous drag may not have finished — for example,
        // if the element was removed from the tree halfway
        CancelDrag();

        if (index < 0 || index >= Items.Count || index >= Children.Count)
        {
            _pressIndex = -1;
            return;
        }

        _pressIndex = index;
        _dragging = true;
        _source = this;
        _sourceIndex = index;
        _item = Items[index];

        _preview = CreatePreview(Children[index], _item);

        FindOwner()?.AddOverlay(_preview);

        // capture from this moment on: releasing the button outside the window
        // must reach us, otherwise the drag would get stuck
        CaptureMouse();
    }

    private void UpdateDrag(Point location)
    {
        if (_preview is not null)
            _preview.Position = new Point(
                location.X - _preview.DesiredSize.Width / 2f,
                location.Y - _preview.DesiredSize.Height / 2f);

        DragList? target = null;
        int index = -1;

        foreach (DragList list in DropCandidates())
        {
            if (!list.AcceptsDrop(_item!, this)) continue;

            var bounds = new Rectangle(list.GetAbsolutePosition(), list.ActualSize);
            if (!bounds.Contains(location)) continue;

            target = list;
            index = list.InsertionIndexAt(list.ToLocal(location));
            break;
        }

        if (!ReferenceEquals(target, _target))
        {
            // both must be redrawn: remove the stripe from the old one, add it to the new one
            _target?.InvalidateVisual();
            target?.InvalidateVisual();
        }
        else if (index != _targetIndex)
        {
            target?.InvalidateVisual();
        }

        _target = target;
        _targetIndex = index;

        // exactly Invalidate, not InvalidateVisual: the preview is an overlay,
        // and its position is picked up only by a layout pass
        FindOwner()?.Invalidate();
    }

    private bool AcceptsDrop(object item, DragList source)
    {
        if (!CanReceiveItem) return false;

        // we always drop into ourselves: that is reordering, not transfer between lists
        if (!ReferenceEquals(source, this) && !SameGroup(source)) return false;

        return ReceivePredicate?.Invoke(item, source) ?? true;
    }

    private bool SameGroup(DragList other) =>
        !string.IsNullOrEmpty(_group) &&
        string.Equals(_group, other._group, StringComparison.Ordinal);

    /// <summary>Finish the drag by moving the row. Not named Drop:
    /// that is the name of a UIElement event, and the name would hide it.</summary>
    private void CompleteDrop()
    {
        DragList? target = _target;
        object? item = _item;
        int from = _sourceIndex;
        int to = _targetIndex;

        CancelDrag();

        if (target is null || item is null || to < 0) return;

        if (ReferenceEquals(target, this))
        {
            // the row is taken out first, so when moving down
            // all indices after it shift by one
            int corrected = to > from ? to - 1 : to;

            if (corrected == from) return;

            Items.Move(from, corrected);
        }
        else
        {
            Items.RemoveAt(from);
            target.Items.Insert(Math.Clamp(to, 0, target.Items.Count), item);
        }

        var args = new DragListDropEventArgs(item, this, from, target, to);

        ItemSent?.Invoke(this, args);

        if (!ReferenceEquals(target, this))
            target.ItemReceived?.Invoke(target, args);
    }

    private static void CancelDrag()
    {
        if (_preview is not null)
        {
            _source?.FindOwner()?.RemoveOverlay(_preview);
            _preview = null;
        }

        _target?.InvalidateVisual();

        if (_source is not null)
        {
            _source._dragging = false;
            _source._pressIndex = -1;
        }

        _source = null;
        _target = null;
        _item = null;
        _sourceIndex = -1;
        _targetIndex = -1;
    }

    // ===== geometry =====

    /// <summary>The index of the row under the point, or -1.</summary>
    private int IndexAt(Point local)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            UIElement child = Children[i];

            if (local.Y >= child.Position.Y &&
                local.Y < child.Position.Y + child.ActualSize.Height)
                return i;
        }

        return -1;
    }

    /// <summary>Where to insert: the middle of a row decides whether before it or after.</summary>
    private int InsertionIndexAt(Point local)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            UIElement child = Children[i];

            if (local.Y < child.Position.Y + child.ActualSize.Height / 2f)
                return i;
        }

        return Children.Count;
    }

    private Point ToLocal(Point absolute)
    {
        Point origin = GetAbsolutePosition();

        return new Point(absolute.X - origin.X, absolute.Y - origin.Y);
    }

    // ===== drawing =====

    /// <summary>The insertion stripe. DrawDecoration is called from DrawOverlay,
    /// that is, after the children — the rows won't cover the line.</summary>
    protected override void DrawDecoration(Graphics g)
    {
        if (!ReferenceEquals(_target, this) || _targetIndex < 0) return;

        Rectangle content = ContentBounds;

        float y = _targetIndex < Children.Count
            ? Children[_targetIndex].Position.Y
            : content.Y + content.Height;

        y = Math.Clamp(y, content.Y, Math.Max(content.Y, content.Y + content.Height - DropIndicatorHeight));

        g.FillRectangle(
            new Rectangle(new Point(content.X, y), new Size(content.Width, DropIndicatorHeight)),
            DropIndicatorColor);
    }

    private UIElement CreatePreview(UIElement container, object item)
    {
        return new Border
        {
            Background = DragPreviewBackground,
            BorderColor = DropIndicatorColor,
            BorderWidth = 1f,
            CornerRadius = new CornerRadius(4f),
            Padding = new Thickness(2f),
            Opacity = 0.85f,

            // overlays are hit tested first: without this the preview
            // would intercept the cursor from the lists under it
            IsHitTestVisible = false,

            Child = Snapshot(container) ?? Caption(item),
        };
    }

    /// <summary>A snapshot of the row. If no renderer is registered — make do with
    /// a caption, there is no point crashing the drag because of the preview.</summary>
    private static UIElement? Snapshot(UIElement container)
    {
        try
        {
            Image image = container.RenderToImage();

            var picture = new PictureBox { Size = new Size(image.Width, image.Height) };
            picture.SetImage(image);

            return picture;
        }
        catch
        {
            return null;
        }
    }

    private static UIElement Caption(object item) =>
        new Label
        {
            Text = item.ToString() ?? string.Empty,
            Padding = new Thickness(8, 4),
        };
}