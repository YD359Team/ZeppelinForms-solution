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
    private const float TrackWidth = 40f;
    private const float TrackHeight = 20f;
    private const float ThumbPadding = 2f;
    private const float Gap = 8f;

    private bool _isOn;
    private float _thumbProgress;   // 0 — выключен, 1 — включён

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

    public string? Text { get; set; }

    [Styled(Category = "States")]
    public partial Color OnColor { get; set; }
    private static Color OnColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "States")]
    public partial Color OffColor { get; set; }
    private static Color OffColorDefault => new(255, 200, 200, 200);

    [Styled(Category = "States")]
    public partial Color ThumbColor { get; set; }
    private static Color ThumbColorDefault => Colors.White;

    public HorizontalContentAlignment HorizontalContentAlign { get; set; } = HorizontalContentAlignment.Left;
    public VerticalContentAlignment VerticalContentAlign { get; set; } = VerticalContentAlignment.Center;

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
        IsOn = !IsOn;
        e.Handled = true;
    }

    protected override void DrawContent(Graphics g)
    {
        var content = this.ContentBounds;

        float trackY = content.Y + (content.Height - TrackHeight) / 2f;
        var track = new Rectangle(new Point(content.X, trackY), new Size(TrackWidth, TrackHeight));

        // цвет дорожки перетекает вместе с ползунком
        var trackColor = Interpolators.Color(OffColor, OnColor, _thumbProgress);
        g.FillRoundRectangle(track, new CornerRadius(TrackHeight / 2f), trackColor);

        float thumbSize = TrackHeight - ThumbPadding * 2;
        float travel = TrackWidth - thumbSize - ThumbPadding * 2;

        var thumb = new Rectangle(
            new Point(track.X + ThumbPadding + travel * _thumbProgress, track.Y + ThumbPadding),
            new Size(thumbSize, thumbSize));

        g.FillEllipse(thumb, ThumbColor);

        if (IsFocused)
            g.DrawRoundRectangle(track.Inflate(2f), new CornerRadius(TrackHeight / 2f + 2f), OnColor, 1.5f);

        if (!string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(
                new Point(content.X + TrackWidth + Gap, content.Y),
                new Size(Math.Max(0, content.Width - TrackWidth - Gap), content.Height));

            g.DrawText(Text, textRect, TextColor, EffectiveFont, this.HorizontalContentAlign, this.VerticalContentAlign);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(Text, EffectiveFont);

        float width = TrackWidth + (textSize.Width > 0 ? Gap + textSize.Width : 0) + Padding.Horizontal;
        float height = Math.Max(TrackHeight, textSize.Height) + Padding.Vertical;

        return ResolveSize(new Size(width, height), availableSize);
    }
}