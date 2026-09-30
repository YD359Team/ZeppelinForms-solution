using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class ToggleSwitch : InteractiveControl, ITextElement
{
    private const float Gap = 8f;

    /// <summary>How far the focus ring runs outside the track.</summary>
    private const float FocusRingGap = 2f;

    private bool _isOn;
    private float _thumbProgress;   // 0 — off, 1 — on

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;

            _isOn = value;
            AnimateThumb();
            Toggled?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Toggled;

    public string? Text
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the switch's size is computed from its text, as with a button
            Invalidate();
        }
    }

    [Styled(Category = "States")]
    public partial Color OnColor { get; set; }
    private static Color OnColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "States")]
    public partial Color OffColor { get; set; }
    private static Color OffColorDefault => new(255, 200, 200, 200);

    [Styled(Category = "States")]
    public partial Color ThumbColor { get; set; }
    private static Color ThumbColorDefault => Colors.White;

    /// <summary>The thumb when on. Fluent draws the off thumb in the secondary text
    /// color on a hollow track and the on thumb in the text-on-accent color.
    /// Transparent — <see cref="ThumbColor"/> in both states.</summary>
    [Styled(Category = "States")]
    public partial Color OnThumbColor { get; set; }
    private static Color OnThumbColorDefault => Colors.Transparent;

    /// <summary>The track's stroke when off. It fades into <see cref="OnColor"/>
    /// along with the thumb, so the on state has none of its own.
    /// Transparent — no stroke.</summary>
    [Styled(Category = "States")]
    public partial Color OffBorderColor { get; set; }
    private static Color OffBorderColorDefault => Colors.Transparent;

    // ===== geometry =====
    //
    // Constants before 0.13. Fluent keeps the 40×20 track but sets the thumb
    // farther in (4 px against 2) and strokes the track while it is off.

    /// <summary>The size of the track. Its ends are always fully rounded.</summary>
    [Styled(Category = "Track", AffectsLayout = true)]
    public partial Size TrackSize { get; set; }
    private static Size TrackSizeDefault => new(40f, 20f);

    /// <summary>The gap between the thumb and the edge of the track.</summary>
    [Styled(Category = "Track")]
    public partial float ThumbInset { get; set; }
    private static float ThumbInsetDefault => 2f;

    /// <summary>The thickness of the track's stroke; see <see cref="OffBorderColor"/>.</summary>
    [Styled(Category = "Track")]
    public partial float TrackBorderWidth { get; set; }

    public HorizontalContentAlignment HorizontalContentAlign { get; set; } = HorizontalContentAlignment.Left;
    public VerticalContentAlignment VerticalContentAlign { get; set; } = VerticalContentAlignment.Center;

    public ToggleSwitch()
    {
        // the ring used to be drawn here by hand at 1.5 px in the on color;
        // the shared ring keeps that look while the theme gives it no color
        SetControlDefault(FocusRingThicknessProperty, 1.5f);
    }

    /// <summary>The ring runs around the track, and the track stands at the very
    /// edge of the control when there is no padding.</summary>
    protected override Thickness VisualOverflow => new(FocusRingGap + FocusRingThickness);

    /// <summary>The track, centered vertically at the start of the content.
    /// Shared by the content and the focus ring, so the two never drift apart.</summary>
    private Rectangle TrackRect
    {
        get
        {
            Rectangle content = ContentBounds;
            Size size = TrackSize;

            return new Rectangle(
                new Point(content.X, content.Y + (content.Height - size.Height) / 2f),
                size);
        }
    }

    private void AnimateThumb()
    {
        float from = _thumbProgress;
        float to = _isOn ? 1f : 0f;

        this.Animate("toggle", from, to, TimeSpan.FromMilliseconds(140),
            Interpolators.Float,
            value => { _thumbProgress = value; InvalidateVisual(); });
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        // Space and Enter come here too, bypassing hit testing:
        // a disabled switch must not toggle from the keyboard
        if (!IsEnabled) return;

        IsOn = !IsOn;
        e.Handled = true;
    }

    protected override void DrawContent(Graphics g)
    {
        var content = this.ContentBounds;
        Rectangle track = TrackRect;
        var trackRadius = new CornerRadius(track.Height / 2f);

        // the track color flows along with the thumb
        var trackColor = Interpolators.Color(OffColor, OnColor, _thumbProgress);
        // hover blends a little of the text color into the track: darker on a light
        // theme, lighter on a dark one — the switch shows it can be clicked
        if (IsHovered && IsEnabled)
            trackColor = Interpolators.Color(trackColor, TextColor, 0.12f);
        g.FillRoundRectangle(track, trackRadius, trackColor);

        // the stroke lies inside the track, so the switch doesn't grow when it
        // appears, and fades into the on color: the on state has no stroke of its own
        float strokeWidth = TrackBorderWidth;

        if (strokeWidth > 0f && OffBorderColor.A > 0)
        {
            Rectangle stroke = Grow(track, -strokeWidth / 2f);

            g.DrawRoundRectangle(
                stroke,
                new CornerRadius(stroke.Height / 2f),
                Interpolators.Color(OffBorderColor, OnColor, _thumbProgress),
                strokeWidth);
        }

        float inset = ThumbInset;
        float thumbSize = Math.Max(0f, track.Height - inset * 2);
        float travel = Math.Max(0f, track.Width - thumbSize - inset * 2);

        var thumb = new Rectangle(
            new Point(track.X + inset + travel * _thumbProgress, track.Y + inset),
            new Size(thumbSize, thumbSize));

        Color onThumb = OnThumbColor.A > 0 ? OnThumbColor : ThumbColor;
        g.FillEllipse(thumb, Interpolators.Color(ThumbColor, onThumb, _thumbProgress));

        if (!string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(
                new Point(content.X + track.Width + Gap, content.Y),
                new Size(Math.Max(0, content.Width - track.Width - Gap), content.Height));

            g.DrawText(ApplyTextTransform(Text), textRect, TextColor, EffectiveFont, this.HorizontalContentAlign, this.VerticalContentAlign);
        }
    }

    /// <summary>The focus ring goes around the track. It used to show on any focus,
    /// a click included; now — only while focus is visible.</summary>
    protected override void DrawDecoration(Graphics g)
    {
        Rectangle track = TrackRect;

        DrawFocusRing(
            g,
            Grow(track, FocusRingGap),
            new CornerRadius(track.Height / 2f + FocusRingGap),
            OnColor);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(ApplyTextTransform(Text), EffectiveFont);

        Size track = TrackSize;

        float width = track.Width + (textSize.Width > 0 ? Gap + textSize.Width : 0) + Padding.Horizontal;
        float height = Math.Max(track.Height, textSize.Height) + Padding.Vertical;

        return ResolveSize(new Size(width, height), availableSize);
    }
}