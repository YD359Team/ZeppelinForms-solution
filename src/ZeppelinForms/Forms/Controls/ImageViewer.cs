using ZeppelinForms.Animation;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// One picture of a list at a time, fitted whole, with the way to the previous and
/// the next one: buttons at the sides, the arrows, a swipe.
/// </summary>
/// <remarks>
/// <para>
/// Takes the same <see cref="GalleryItem"/>s as <see cref="ImageGallery"/> — the
/// gallery opens one over the whole window — and loads each picture when it gets
/// to it, showing the thumbnail meanwhile, with the neighbours loaded ahead.
/// </para>
/// <para>
/// With <see cref="ShowCloseButton"/> it behaves as a dialog: a close button,
/// Escape asks to close, Tab stays inside.
/// </para>
/// </remarks>
public partial class ImageViewer : InteractiveControl
{
    private const string SpinAnimation = "viewer-spin";

    private enum Part : byte { None, Previous, Next, Close }

    private IReadOnlyList<GalleryItem>? _items;
    private GalleryItem? _shown;
    private int _index = -1;

    private Part _hovered;
    private float _spin;
    private bool _spinning;

    private CancellationTokenSource _loads = new();

    public ImageViewer()
    {
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Stretch);

        var swipe = new SwipeGestureRecognizer
        {
            AllowedDirections = [SwipeDirection.Left, SwipeDirection.Right],
        };

        // the finger drags the next picture in from the side it is on: a swipe to
        // the left brings the one on the right
        swipe.Swiped += (_, e) =>
        {
            bool towardStart = e.Direction == SwipeDirection.Right;

            if (towardStart != IsRightToLeft) Previous();
            else Next();
        };

        this.AddGesture(swipe);
    }

    // ===== content =====

    /// <summary>The pictures to go through.</summary>
    public IReadOnlyList<GalleryItem>? Items
    {
        get => _items;
        set
        {
            if (ReferenceEquals(_items, value)) return;

            _items = value;
            Index = Count > 0 ? Math.Clamp(_index, 0, Count - 1) : -1;

            Sync();
        }
    }

    /// <summary>The picture shown, −1 when there is none.</summary>
    public int Index
    {
        get => _index;
        set
        {
            int index = Count == 0 ? -1 : Math.Clamp(value, 0, Count - 1);

            if (_index == index) return;

            _index = index;

            Sync();
            IndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public GalleryItem? CurrentItem => _index >= 0 && _index < Count ? _items![_index] : null;

    private int Count => _items?.Count ?? 0;

    /// <summary>Whether the last picture leads on to the first and back.</summary>
    public bool IsLooping { get; set; }

    public bool ShowNavigationButtons
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = true;

    /// <summary>The title and the "3 / 20" counter under the picture.</summary>
    public bool ShowCaption
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = true;

    /// <summary>A close button in the corner: the viewer is a dialog over something,
    /// and Escape and the button raise <see cref="CloseRequested"/>.</summary>
    public bool ShowCloseButton
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    public event EventHandler? IndexChanged;

    /// <summary>The close button or Escape: whoever showed the viewer takes it away.</summary>
    public event EventHandler? CloseRequested;

    public bool CanGoPrevious => Count > 1 && (IsLooping || _index > 0);

    public bool CanGoNext => Count > 1 && (IsLooping || _index < Count - 1);

    public void Previous()
    {
        if (!CanGoPrevious) return;

        Index = _index > 0 ? _index - 1 : Count - 1;
    }

    public void Next()
    {
        if (!CanGoNext) return;

        Index = _index < Count - 1 ? _index + 1 : 0;
    }

    // ===== look =====

    /// <summary>The discs of the buttons; translucent, they lie over the picture.</summary>
    [Styled(Category = "Buttons")]
    public partial Color ButtonBackground { get; set; }
    private static Color ButtonBackgroundDefault => new(140, 0, 0, 0);

    [Styled(Category = "Buttons")]
    public partial Color ButtonHoverBackground { get; set; }
    private static Color ButtonHoverBackgroundDefault => new(200, 0, 0, 0);

    /// <summary>The arrows and the cross on the buttons.</summary>
    [Styled(Category = "Buttons")]
    public partial Color ButtonForeground { get; set; }
    private static Color ButtonForegroundDefault => Colors.White;

    [Styled(Category = "Caption")]
    public partial Color CaptionColor { get; set; }
    private static Color CaptionColorDefault => Colors.White;

    /// <summary>The spinner while a picture loads, and the mark of one that failed.</summary>
    [Styled(Category = "Appearance")]
    public partial Color GlyphColor { get; set; }
    private static Color GlyphColorDefault => new(200, 255, 255, 255);

    // ===== loading =====

    /// <summary>The item shown changed: follow its pictures, load it and its
    /// neighbours, redraw.</summary>
    private void Sync()
    {
        GalleryItem? current = CurrentItem;

        if (!ReferenceEquals(current, _shown))
        {
            if (_shown is not null) _shown.Changed -= OnItemChanged;

            _shown = current;

            if (_shown is not null) _shown.Changed += OnItemChanged;
        }

        LoadAround();
        UpdateSpinner();
        RefreshToolTip();
        InvalidateVisual();
    }

    private void OnItemChanged(object? sender, EventArgs e)
    {
        UpdateSpinner();
        InvalidateVisual();
    }

    /// <summary>The picture shown first, then the neighbours: the next swipe
    /// should find its picture already there.</summary>
    private void LoadAround()
    {
        if (FindOwner() is not { } form || _index < 0) return;

        foreach (int index in (int[])[_index, _index + 1, _index - 1])
        {
            if (index < 0 || index >= Count) continue;

            GalleryItem item = _items![index];

            if (item.NeedsImage)
                item.Load(full: true, form, _loads.Token, static () => { });
        }
    }

    /// <summary>A spinner only while there is nothing at all to show: the thumbnail
    /// stands in for the picture while it loads.</summary>
    private void UpdateSpinner()
    {
        bool spin = CurrentItem is { ImageState: GalleryImageState.Loading } item && item.ViewImage is null
            && FindOwner() is not null;

        if (spin == _spinning) return;

        _spinning = spin;

        if (spin)
        {
            this.AnimateLoop(SpinAnimation, TimeSpan.FromSeconds(1), phase =>
            {
                _spin = phase;
                InvalidateVisual();
            });
        }
        else
        {
            this.StopAnimation(SpinAnimation);
        }
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        LoadAround();
        UpdateSpinner();
    }

    protected override void OnDetached()
    {
        // whatever is still loading belongs to a viewer nobody sees
        _loads.Cancel();
        _loads.Dispose();
        _loads = new CancellationTokenSource();

        _spinning = false;

        base.OnDetached();
    }

    // ===== geometry =====

    private const float ButtonSize = 40f;
    private const float CloseSize = 36f;
    private const float Gap = 12f;

    private float CaptionHeight =>
        ShowCaption && Count > 0
            ? TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height * 2f + Gap * 2f
            : 0f;

    /// <summary>Where the picture is fitted: the content above the caption.</summary>
    internal Rectangle PictureArea
    {
        get
        {
            Rectangle content = ContentBounds;

            return new Rectangle(content.Position,
                new Size(content.Width, Math.Max(0f, content.Height - CaptionHeight)));
        }
    }

    /// <summary>The button that goes back: on the left, on the right under right-to-left.</summary>
    internal Rectangle PreviousButton => SideButton(atRight: IsRightToLeft);

    internal Rectangle NextButton => SideButton(atRight: !IsRightToLeft);

    private Rectangle SideButton(bool atRight)
    {
        Rectangle area = PictureArea;
        float x = atRight ? area.X + area.Width - Gap - ButtonSize : area.X + Gap;

        return new Rectangle(
            new Point(x, area.Y + (area.Height - ButtonSize) / 2f),
            new Size(ButtonSize, ButtonSize));
    }

    /// <summary>The close button: in the corner where lines end.</summary>
    internal Rectangle CloseButton
    {
        get
        {
            Rectangle content = ContentBounds;
            float x = IsRightToLeft ? content.X + Gap : content.X + content.Width - Gap - CloseSize;

            return new Rectangle(new Point(x, content.Y + Gap), new Size(CloseSize, CloseSize));
        }
    }

    private Part PartAt(Point local)
    {
        if (ShowCloseButton && Contains(CloseButton, local)) return Part.Close;

        if (ShowNavigationButtons)
        {
            if (CanGoPrevious && Contains(PreviousButton, local)) return Part.Previous;
            if (CanGoNext && Contains(NextButton, local)) return Part.Next;
        }

        return Part.None;
    }

    private static bool Contains(Rectangle rect, Point point) =>
        point.X >= rect.X && point.X <= rect.X + rect.Width &&
        point.Y >= rect.Y && point.Y <= rect.Y + rect.Height;

    private Point ToLocal(Point absolute)
    {
        Point origin = GetAbsolutePosition();

        return new Point(absolute.X - origin.X, absolute.Y - origin.Y);
    }

    // ===== drawing =====

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(
            new Size(
                float.IsFinite(availableSize.Width) ? availableSize.Width : 320f,
                float.IsFinite(availableSize.Height) ? availableSize.Height : 240f),
            availableSize);

    protected override void DrawContent(Graphics g)
    {
        Rectangle area = PictureArea;
        GalleryItem? item = CurrentItem;

        if (item?.ViewImage is { } image)
        {
            g.DrawImage(area, image, ImageFlip.None, ImageLayout.Zoom);
        }
        else if (item is not null)
        {
            var center = new Point(area.X + area.Width / 2f, area.Y + area.Height / 2f);

            if (item.ImageState == GalleryImageState.Loading)
            {
                var spinner = new Rectangle(new Point(center.X - 16f, center.Y - 16f), new Size(32f, 32f));
                g.DrawArc(spinner, _spin * 360f - 90f, 270f, GlyphColor, 3f);
            }
            else
            {
                ImageGallery.DrawBrokenImage(g, center, 40f, GlyphColor);
            }
        }

        if (ShowCaption && item is not null)
            DrawCaption(g, item);

        if (ShowNavigationButtons)
        {
            if (CanGoPrevious) DrawSideButton(g, PreviousButton, Part.Previous, pointsRight: IsRightToLeft);
            if (CanGoNext) DrawSideButton(g, NextButton, Part.Next, pointsRight: !IsRightToLeft);
        }

        if (ShowCloseButton)
            DrawCloseButton(g);
    }

    private void DrawCaption(Graphics g, GalleryItem item)
    {
        Rectangle content = ContentBounds;
        float height = CaptionHeight;
        float line = (height - Gap * 2f) / 2f;

        var title = new Rectangle(
            new Point(content.X + Gap, content.Y + content.Height - height + Gap),
            new Size(Math.Max(0f, content.Width - Gap * 2f), line));

        var counter = new Rectangle(new Point(title.X, title.Y + line), title.Size);

        g.Save();
        g.ClipRect(new Rectangle(new Point(content.X, content.Y + content.Height - height), new Size(content.Width, height)));

        if (!string.IsNullOrEmpty(item.Title))
            g.DrawText(ApplyTextTransform(item.Title), title, CaptionColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

        if (Count > 1)
            g.DrawText(Localization.Get(ZfText.ImageCounter, _index + 1, Count), counter,
                new Color((byte)(CaptionColor.A * 0.75f), CaptionColor.R, CaptionColor.G, CaptionColor.B),
                EffectiveFont, HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

        g.Restore();
    }

    private void DrawSideButton(Graphics g, Rectangle rect, Part part, bool pointsRight)
    {
        g.FillEllipse(rect, _hovered == part ? ButtonHoverBackground : ButtonBackground);

        float cx = rect.X + rect.Width / 2f;
        float cy = rect.Y + rect.Height / 2f;
        float r = rect.Width * 0.16f;

        // a chevron pointing the way it goes, nudged a little toward that way
        float dx = pointsRight ? r * 0.3f : -r * 0.3f;

        ReadOnlySpan<Point> chevron = pointsRight
            ? [new(cx - r * 0.6f + dx, cy - r), new(cx + r * 0.6f + dx, cy), new(cx - r * 0.6f + dx, cy + r)]
            : [new(cx + r * 0.6f + dx, cy - r), new(cx - r * 0.6f + dx, cy), new(cx + r * 0.6f + dx, cy + r)];

        g.DrawPolyline(chevron, ButtonForeground, 2.2f);
    }

    private void DrawCloseButton(Graphics g)
    {
        Rectangle rect = CloseButton;

        g.FillEllipse(rect, _hovered == Part.Close ? ButtonHoverBackground : ButtonBackground);

        float cx = rect.X + rect.Width / 2f;
        float cy = rect.Y + rect.Height / 2f;
        float r = rect.Width * 0.18f;

        g.DrawLine(new Point(cx - r, cy - r), new Point(cx + r, cy + r), ButtonForeground, 2f);
        g.DrawLine(new Point(cx - r, cy + r), new Point(cx + r, cy - r), ButtonForeground, 2f);
    }

    protected override void DrawDecoration(Graphics g) =>
        DrawFocusRing(g, Grow(LocalBounds, -1f), CornerRadius);

    // ===== input =====

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        Part part = PartAt(ToLocal(e.Location));

        if (part == _hovered) return;

        _hovered = part;
        Cursor = part == Part.None ? CursorKind.Default : CursorKind.Hand;

        RefreshToolTip();
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hovered == Part.None) return;

        _hovered = Part.None;
        Cursor = CursorKind.Default;

        InvalidateVisual();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (e.Button != MouseButton.Left) return;

        switch (PartAt(ToLocal(e.Location)))
        {
            case Part.Previous:
                Previous();
                e.Handled = true;
                break;

            case Part.Next:
                Next();
                e.Handled = true;
                break;

            case Part.Close:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Space and Enter don't click the viewer as a whole: it has no one
    /// action, and Enter must stay free for whoever shows it.</summary>
    protected override bool IsKeyActivatable => false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool rtl = IsRightToLeft;

        switch (e.Key)
        {
            case Key.Left:
                if (rtl) Next(); else Previous();
                break;

            case Key.Right:
                if (rtl) Previous(); else Next();
                break;

            case Key.PageUp:
                Previous();
                break;

            case Key.PageDown:
                Next();
                break;

            case Key.Home:
                Index = 0;
                break;

            case Key.End:
                Index = Count - 1;
                break;

            case Key.Escape when ShowCloseButton:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;

            // a dialog keeps the focus: Tab would walk into the page under it
            case Key.Tab when ShowCloseButton:
                break;

            default:
                base.OnKeyDown(e);
                return;
        }

        e.Handled = true;
    }

    protected internal override string? GetToolTip(Point location) => PartAt(ToLocal(location)) switch
    {
        Part.Previous => Localization.Get(ZfText.PreviousImage),
        Part.Next => Localization.Get(ZfText.NextImage),
        Part.Close => Localization.Get(ZfText.CloseViewer),
        _ => ToolTip,
    };
}