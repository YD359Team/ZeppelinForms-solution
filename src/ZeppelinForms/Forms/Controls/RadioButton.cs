using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class RadioButton : UnitControl, ITextElement, IInputElement
{
    private const float CircleSize = 16f;
    private const float Gap = 6f;

    public bool IsChecked { get; private set; }
    public string? GroupName { get; set; }

    public event EventHandler? CheckedChanged;

    // ITextElement
    public string? Text { get; set; }
    public HorizontalAlign HorizontalAlign { get; set; } = HorizontalAlign.Left;
    public VerticalAlign VerticalAlign { get; set; } = VerticalAlign.Center;
    public Color TextColor { get; set; } = Colors.Black;

    public Color CircleBorderColor { get; set; } = Colors.Black;
    public Color CheckColor { get; set; } = LightThemeColors.ButtonFill;

    // IInputElement
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (!IsChecked)
            SetChecked(true);
    }

    public void SetChecked(bool value)
    {
        if (IsChecked == value) return;

        if (value)
            UncheckSiblings();

        IsChecked = value;
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void UncheckSiblings()
    {
        if (Parent is not PanelControl panel) return;

        foreach (var sibling in panel.Children.OfType<RadioButton>())
            if (!ReferenceEquals(sibling, this) && sibling.GroupName == GroupName)
                sibling.SetChecked(false);
    }

    public override void Draw(Graphics g)
    {
        var content = this.ContentBounds;
        float circleY = content.Y + (content.Height - CircleSize) / 2f;
        var circleRect = new Rectangle(new Point(content.X, circleY), new Size(CircleSize, CircleSize));

        g.FillEllipse(circleRect, Colors.White);
        g.DrawEllipse(circleRect, CircleBorderColor, 1.5f);

        if (IsChecked)
        {
            const float inset = 4f;
            var dot = new Rectangle(
                new Point(circleRect.X + inset, circleRect.Y + inset),
                new Size(CircleSize - inset * 2, CircleSize - inset * 2));
            g.FillEllipse(dot, CheckColor);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(
                new Point(content.X + CircleSize + Gap, content.Y),
                new Size(Math.Max(0, content.Width - CircleSize - Gap), content.Height));

            g.DrawText(Text, textRect, TextColor, EffectiveFont, HorizontalAlign, VerticalAlign);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(Text, EffectiveFont);

        float width = CircleSize + (textSize.Width > 0 ? Gap + textSize.Width : 0) + Padding.Horizontal;
        float height = Math.Max(CircleSize, textSize.Height) + Padding.Vertical;
        return ResolveSize(new Size(width, height), availableSize);
    }
}