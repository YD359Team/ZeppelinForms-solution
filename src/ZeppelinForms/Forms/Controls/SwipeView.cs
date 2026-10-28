using System.Collections.ObjectModel;
using System.Windows.Input;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>An action a <see cref="SwipeView"/> uncovers beside its content: an icon, a
/// label and a color — Delete in red, Archive in green.</summary>
public sealed class SwipeItem
{
    public SwipeItem() { }

    public SwipeItem(string text, IconSource? icon = null, Color? background = null, EventHandler? invoked = null)
    {
        Text = text;
        Icon = icon;
        if (background is { } color) Background = color;
        if (invoked is not null) Invoked += invoked;
    }

    public string Text { get; set; } = string.Empty;

    public IconSource? Icon { get; set; }

    /// <summary>The action's own color. Transparent — the swipe view's <see cref="SwipeView.ItemBackground"/>.</summary>
    public Color Background { get; set; } = Colors.Transparent;

    /// <summary>The icon's and the label's color. Transparent — the swipe view's.</summary>
    public Color Foreground { get; set; } = Colors.Transparent;

    /// <summary>Runs when the action is chosen, after <see cref="Invoked"/>.</summary>
    public ICommand? Command { get; set; }

    public object? CommandParameter { get; set; }

    /// <summary>The action was chosen: tapped, swiped all the way in
    /// <see cref="SwipeMode.Execute"/>, or picked from the context menu.</summary>
    public event EventHandler? Invoked;

    /// <summary>Whatever the application ties to the action.</summary>
    public object? Tag { get; set; }

    internal void Invoke()
    {
        Invoked?.Invoke(this, EventArgs.Empty);

        if (Command is { } command && command.CanExecute(CommandParameter))
            command.Execute(CommandParameter);
    }
}

/// <summary>What a swipe to one side of a <see cref="SwipeView"/> does.</summary>
public enum SwipeMode : byte
{
    /// <summary>It uncovers the side's actions, and they stay until one is tapped or the
    /// content is tapped or swiped back.</summary>
    Reveal,

    /// <summary>Swiped far enough, it runs the side's first action at once: a swipe that
    /// deletes, as a mail application's.</summary>
    Execute,
}

/// <summary>
/// Content — a row of a list, mostly — that slides aside under the finger and uncovers
/// actions behind it: <see cref="LeftItems"/> on a swipe to the right,
/// <see cref="RightItems"/> on a swipe to the left. It works with the mouse too, and its
/// actions are in its context menu: a right click, a long press, and a screen reader's
/// way to reach them without swiping.
/// </summary>
public partial class SwipeView : DecoratedWrapControl
{
    /// <summary>How far the content is moved: positive — to the right, uncovering the left items.</summary>
    private float _offset;

    /// <summary>The offset when the drag started.</summary>
    private float _dragStart;

    private bool _dragging;

    public SwipeView()
    {
        LeftItems.CollectionChanged += (_, _) => Close(animated: false);
        RightItems.CollectionChanged += (_, _) => Close(animated: false);

        var pan = this.AddGesture(new PanGestureRecognizer
        {
            Direction = PanDirection.Horizontal,
            CanBegin = _ => LeftItems.Count > 0 || RightItems.Count > 0,
        });

        pan.Started += (_, _) =>
        {
            this.StopAnimation("swipe");
            _dragging = true;
            _dragStart = _offset;
        };

        pan.Updated += (_, e) =>
        {
            SetOffset(Clamp(_dragStart + e.TotalOffset.X));
        };

        pan.Completed += (_, e) => OnDragEnded(e.Velocity.X);
        pan.Cancelled += (_, _) => OnDragEnded(0f);
    }

    public SwipeView(UIElement content) : this() => Child = content;

    /// <summary>The actions a swipe to the right uncovers, at the left edge.</summary>
    public ObservableCollection<SwipeItem> LeftItems { get; } = [];

    /// <summary>The actions a swipe to the left uncovers, at the right edge.</summary>
    public ObservableCollection<SwipeItem> RightItems { get; } = [];

    public SwipeMode LeftMode { get; set; } = SwipeMode.Reveal;

    public SwipeMode RightMode { get; set; } = SwipeMode.Reveal;

    /// <summary>The width each uncovered action takes.</summary>
    [Styled(Category = "Layout")]
    public partial float ItemWidth { get; set; }
    private static float ItemWidthDefault => 80f;

    /// <summary>The background of an action without a color of its own.</summary>
    [Styled(Category = "Appearance")]
    public partial Color ItemBackground { get; set; }
    private static Color ItemBackgroundDefault => new(255, 0x6C, 0x75, 0x7D);

    /// <summary>The icon and label of an action without a color of their own.</summary>
    [Styled(Category = "Appearance")]
    public partial Color ItemForeground { get; set; }
    private static Color ItemForegroundDefault => Colors.White;

    public int TransitionDurationMs { get; set; } = 200;

    /// <summary>Whether the actions of one side are uncovered.</summary>
    public bool IsOpen => _offset != 0f;

    /// <summary>An action was chosen.</summary>
    public event EventHandler<SwipeItem>? ItemInvoked;

    /// <summary>Uncover the actions of a side: <see cref="SwipeDirection.Right"/> — the left
    /// ones, as a swipe to the right does.</summary>
    public void Open(SwipeDirection direction)
    {
        float target = direction switch
        {
            SwipeDirection.Right => LeftItems.Count * ItemWidth,
            SwipeDirection.Left => -RightItems.Count * ItemWidth,
            _ => 0f,
        };

        AnimateTo(Clamp(target));
    }

    /// <summary>Cover the actions again.</summary>
    public void Close(bool animated = true)
    {
        if (!animated)
        {
            this.StopAnimation("swipe");
            SetOffset(0f);
            return;
        }

        AnimateTo(0f);
    }

    // ===== the drag =====

    private float RevealedLeft => LeftItems.Count * ItemWidth;

    private float RevealedRight => RightItems.Count * ItemWidth;

    /// <summary>The range the content moves in: as far as the actions reach, or the whole
    /// width on a side that executes.</summary>
    private float Clamp(float offset)
    {
        float width = Math.Max(1f, ActualSize.Width);

        float maxRight = LeftItems.Count == 0 ? 0f : LeftMode == SwipeMode.Execute ? width : RevealedLeft;
        float maxLeft = RightItems.Count == 0 ? 0f : RightMode == SwipeMode.Execute ? width : RevealedRight;

        return Math.Clamp(offset, -maxLeft, maxRight);
    }

    private void SetOffset(float offset)
    {
        if (_offset == offset) return;

        _offset = offset;

        // a tap on content moved aside closes it rather than pressing what is in it
        if (Child is not null) Child.IsHitTestVisible = offset == 0f;

        Invalidate();
    }

    private void OnDragEnded(float velocity)
    {
        if (!_dragging) return;

        _dragging = false;

        float width = Math.Max(1f, ActualSize.Width);

        // a side that executes: far enough — the first action runs, and the content goes
        // all the way out first
        if (_offset > 0 && LeftMode == SwipeMode.Execute && (_offset > width * 0.5f || velocity > 1200f))
        {
            Execute(LeftItems[0], width);
            return;
        }

        if (_offset < 0 && RightMode == SwipeMode.Execute && (-_offset > width * 0.5f || velocity < -1200f))
        {
            Execute(RightItems[0], -width);
            return;
        }

        // open past half the actions, or flung that way; closed otherwise
        if (_offset > 0)
            AnimateTo(_offset > RevealedLeft / 2f || velocity > 500f ? RevealedLeft : 0f);
        else if (_offset < 0)
            AnimateTo(-_offset > RevealedRight / 2f || velocity < -500f ? -RevealedRight : 0f);
    }

    private void Execute(SwipeItem item, float end) =>
        AnimateTo(end, () =>
        {
            Invoke(item);
            SetOffset(0f);
        });

    private void AnimateTo(float target, Action? done = null)
    {
        this.StopAnimation("swipe");

        if (TransitionDurationMs <= 0 || FindOwner()?.PlatformWindow is null || Motion.IsReduced)
        {
            SetOffset(target);
            done?.Invoke();
            return;
        }

        this.Animate("swipe", _offset, target, TimeSpan.FromMilliseconds(TransitionDurationMs), Interpolators.Float,
            SetOffset, Easing.EaseOut, done);
    }

    private void Invoke(SwipeItem item)
    {
        item.Invoke();
        ItemInvoked?.Invoke(this, item);
    }

    // ===== taps =====

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (_offset == 0f)
        {
            base.OnClick(e);
            return;
        }

        Point absolute = GetAbsolutePosition();
        var local = new Point(e.Location.X - absolute.X, e.Location.Y - absolute.Y);

        // a tap on an uncovered action runs it; anywhere else closes
        if (ItemAt(local) is { } item)
        {
            Close();
            Invoke(item);
        }
        else
        {
            Close();
        }

        e.Handled = true;
    }

    /// <summary>The uncovered action under a point of the view's own. Taps come to a strip
    /// at rest — open to the actions' full width —, where each takes the same room.</summary>
    private SwipeItem? ItemAt(Point local)
    {
        if (_offset > 0 && local.X < _offset && LeftItems.Count > 0)
        {
            // the first action is the one next to the content: counted from the strip's end
            int fromContent = (int)((_offset - local.X) / (_offset / LeftItems.Count));
            return LeftItems[Math.Clamp(fromContent, 0, LeftItems.Count - 1)];
        }

        float start = ActualSize.Width + _offset;

        if (_offset < 0 && local.X > start && RightItems.Count > 0)
        {
            int fromContent = (int)((local.X - start) / (-_offset / RightItems.Count));
            return RightItems[Math.Clamp(fromContent, 0, RightItems.Count - 1)];
        }

        return null;
    }

    /// <summary>The actions of both sides, for a right click, a long press and a screen
    /// reader: a swipe is not the only way to them.</summary>
    protected override List<MenuItem>? GetDefaultContextMenu()
    {
        if (LeftItems.Count == 0 && RightItems.Count == 0) return null;

        var items = new List<MenuItem>();

        foreach (SwipeItem item in LeftItems.Concat(RightItems))
        {
            var menuItem = new MenuItem { Text = item.Text, PathData = (item.Icon as PathIconSource)?.Data };
            menuItem.Click += (_, _) => Invoke(item);
            items.Add(menuItem);
        }

        return items;
    }

    // ===== look =====

    protected override Size ArrangeOverride(Size finalSize)
    {
        // the content at its place moved by the offset: the actions show in the room it left
        Child?.Arrange(new Rectangle(
            new Point(Padding.Left + _offset, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical))));

        return finalSize;
    }

    protected override void DrawContent(Graphics g)
    {
        if (_offset > 0) DrawItems(g, LeftItems, 0f, _offset, LeftMode == SwipeMode.Execute);
        else if (_offset < 0) DrawItems(g, RightItems, ActualSize.Width + _offset, -_offset, RightMode == SwipeMode.Execute);
    }

    /// <summary>The actions in the uncovered strip, the first one next to the content —
    /// the closest to the finger. While the strip is narrower than the actions they share
    /// it; pulled past their width, an executing side's first action takes what is more.</summary>
    private void DrawItems(Graphics g, ObservableCollection<SwipeItem> items, float x, float width, bool executes)
    {
        int count = items.Count;
        if (count == 0 || width <= 0) return;

        float height = ActualSize.Height;
        bool stretched = executes && width > count * ItemWidth;
        bool fromRight = ReferenceEquals(items, LeftItems);

        // the left side's actions are placed from the content leftwards, the right
        // side's from the content rightwards
        float cursor = fromRight ? x + width : x;

        for (int i = 0; i < count; i++)
        {
            SwipeItem item = items[i];

            float each = stretched
                ? (i == 0 ? width - (count - 1) * ItemWidth : ItemWidth)
                : width / count;

            float left = fromRight ? cursor - each : cursor;
            cursor = fromRight ? left : left + each;

            var rect = new Rectangle(new Point(left, 0f), new Size(each, height));

            Color back = item.Background.A > 0 ? item.Background : ItemBackground;
            Color fore = item.Foreground.A > 0 ? item.Foreground : ItemForeground;

            g.FillRectangle(rect, back);

            // the icon and the label stand in the action's own width, next to the content
            float slotWidth = Math.Min(each, ItemWidth);
            float slotX = fromRight ? rect.Right - slotWidth : rect.X;

            DrawItemContent(g, item, new Rectangle(new Point(slotX, 0f), new Size(slotWidth, height)), fore);
        }
    }

    private void DrawItemContent(Graphics g, SwipeItem item, Rectangle slot, Color color)
    {
        const float iconSize = 20f;
        const float gap = 4f;

        Size label = string.IsNullOrEmpty(item.Text) ? Size.Empty : TextMeasurer.MeasureLine(item.Text, EffectiveFont);
        bool hasIcon = item.Icon is not null;

        float column = (hasIcon ? iconSize : 0f) + (hasIcon && label.Height > 0 ? gap : 0f) + label.Height;
        float top = slot.Y + (slot.Height - column) / 2f;

        if (item.Icon is { } icon)
        {
            icon.Draw(g, new Rectangle(new Point(slot.X + (slot.Width - iconSize) / 2f, top), new Size(iconSize, iconSize)), color);
            top += iconSize + gap;
        }

        if (label.Height > 0)
            g.DrawText(item.Text, new Rectangle(new Point(slot.X + 2f, top), new Size(Math.Max(0, slot.Width - 4f), label.Height)), color, EffectiveFont);
    }
}
