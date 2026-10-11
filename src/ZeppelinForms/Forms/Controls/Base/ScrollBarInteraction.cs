using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// The mouse on one scroll bar: the thumb dragged, the track pressed to page and held
/// to keep paging. One implementation for every bar of the framework — the
/// <see cref="ScrollBar"/> control, the bars a scrolling panel draws, the data grid's.
/// </summary>
/// <remarks>
/// <para>
/// Each of the three used to have its own copy of this code, and all three paged
/// once per press: holding the mouse on the track did nothing more, where every
/// desktop system keeps paging. A fix in one copy would not have reached the others.
/// </para>
/// <para>
/// Paging follows the system's rules. The first page comes with the press, the next
/// after <see cref="PointerThresholds.RepeatDelayMs"/>, then one every
/// <see cref="PointerThresholds.RepeatIntervalMs"/>. It goes towards the pointer and
/// stops once the thumb is under it. It pauses while the pointer is off the track and
/// goes on when it is back, and it never turns: pressed below the thumb, it pages only
/// down.
/// </para>
/// <para>
/// The bar's geometry is read anew on every step, through delegates, rather than
/// kept: paging scrolls, scrolling can change the layout — a virtualized list
/// measures new rows — and a kept rectangle would page by a stale thumb.
/// </para>
/// </remarks>
internal sealed class ScrollBarInteraction
{
    private enum Mode : byte
    {
        None,
        Dragging,
        Paging,
    }

    private readonly UIElement _owner;
    private readonly Orientation _orientation;
    private readonly Func<Rectangle> _track;
    private readonly Func<(float Position, float Length)> _thumb;
    private readonly Func<float> _scroll;
    private readonly Func<float> _maxScroll;
    private readonly Func<float> _page;
    private readonly Action<float> _scrollTo;

    private Mode _mode;

    /// <summary>Where in the thumb it was grabbed: the thumb follows the pointer by
    /// that point rather than jumping its edge to it.</summary>
    private float _dragOffset;

    /// <summary>Paging: −1 towards the start, +1 towards the end.</summary>
    private int _direction;

    /// <summary>The pointer while paging, in the owner's coordinates: the repeat
    /// pages towards where it is now, not where it was pressed.</summary>
    private Point _pointer;

    private IDisposable? _repeat;

    /// <param name="owner">The element the bar belongs to: it gets the mouse capture
    /// and its form runs the repeat.</param>
    /// <param name="orientation">Vertical — the bar scrolls up and down.</param>
    /// <param name="track">The track in the owner's own coordinates.</param>
    /// <param name="thumb">The thumb along the track: its offset from the track's
    /// start and its length.</param>
    /// <param name="scroll">The scroll position now.</param>
    /// <param name="maxScroll">The largest scroll position.</param>
    /// <param name="page">How far one page scrolls: the visible extent.</param>
    /// <param name="scrollTo">Scroll there; the owner clamps.</param>
    public ScrollBarInteraction(
        UIElement owner,
        Orientation orientation,
        Func<Rectangle> track,
        Func<(float Position, float Length)> thumb,
        Func<float> scroll,
        Func<float> maxScroll,
        Func<float> page,
        Action<float> scrollTo)
    {
        _owner = owner;
        _orientation = orientation;
        _track = track;
        _thumb = thumb;
        _scroll = scroll;
        _maxScroll = maxScroll;
        _page = page;
        _scrollTo = scrollTo;
    }

    /// <summary>The press is the bar's: the thumb is held or the track pages.</summary>
    public bool IsActive => _mode != Mode.None;

    public bool IsDragging => _mode == Mode.Dragging;

    public bool IsPaging => _mode == Mode.Paging;

    /// <summary>A press at a point in the owner's coordinates. False — the point is
    /// not on the track, and the press is someone else's.</summary>
    public bool Press(Point local)
    {
        Rectangle track = _track();

        if (!Contains(track, local)) return false;

        Stop();

        float along = Along(local, track);
        var (position, length) = _thumb();

        if (along >= position && along <= position + length)
        {
            _mode = Mode.Dragging;
            _dragOffset = along - position;
        }
        else
        {
            _mode = Mode.Paging;
            _direction = along < position ? -1 : 1;
            _pointer = local;

            Page();
            _repeat = _owner.FindOwner()?.Schedule(PointerThresholds.RepeatDelayMs, Repeat);
        }

        // without capture a drag breaks off as soon as the pointer leaves the window,
        // and paging would never hear the release that stops it
        _owner.FindOwner()?.CaptureMouse(_owner);

        return true;
    }

    /// <summary>The pointer moved, in the owner's coordinates. False — the bar holds
    /// no press, and the move is someone else's.</summary>
    public bool Move(Point local)
    {
        switch (_mode)
        {
            case Mode.Dragging:
                Drag(local);
                return true;

            case Mode.Paging:
                _pointer = local;
                return true;

            default:
                return false;
        }
    }

    /// <summary>The press ended — released, or cancelled by the system. False — the
    /// bar held none.</summary>
    public bool Release()
    {
        if (_mode == Mode.None) return false;

        Stop();
        _owner.FindOwner()?.ReleaseMouseCapture(_owner);

        return true;
    }

    private void Stop()
    {
        _mode = Mode.None;

        _repeat?.Dispose();
        _repeat = null;
    }

    private void Drag(Point local)
    {
        Rectangle track = _track();
        float free = Extent(track) - _thumb().Length;

        if (free <= 0) return;

        float t = (Along(local, track) - _dragOffset) / free;
        _scrollTo(_maxScroll() * Math.Clamp(t, 0f, 1f));
    }

    private void Page() => _scrollTo(_scroll() + _direction * _page());

    /// <summary>A step of the held press. The wake-up may come after the release —
    /// cancellation races with it — so it checks for itself that it is still wanted.</summary>
    private void Repeat()
    {
        _repeat = null;

        if (_mode != Mode.Paging) return;

        // nothing further to page to: the end was reached in the only direction
        // this press goes, so there is no point waking up any more
        float scroll = _scroll();
        if (_direction < 0 ? scroll <= 0 : scroll >= _maxScroll()) return;

        if (PointerIsAhead()) Page();

        _repeat = _owner.FindOwner()?.Schedule(PointerThresholds.RepeatIntervalMs, Repeat);
    }

    /// <summary>The pointer is on the track and beyond the thumb in the direction of
    /// paging: once the thumb has come under it, paging waits.</summary>
    private bool PointerIsAhead()
    {
        Rectangle track = _track();

        if (!Contains(track, _pointer)) return false;

        float along = Along(_pointer, track);
        var (position, length) = _thumb();

        return _direction < 0 ? along < position : along > position + length;
    }

    private float Along(Point local, Rectangle track) =>
        _orientation == Orientation.Vertical ? local.Y - track.Y : local.X - track.X;

    private float Extent(Rectangle track) =>
        _orientation == Orientation.Vertical ? track.Height : track.Width;

    private static bool Contains(Rectangle r, Point p) =>
        p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;
}