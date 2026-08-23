using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class CheckBox : UnitControl, ITextElement, IInputElement
{
    private const float BoxSize = 16f;
    private const float Gap = 6f;

    public bool IsChecked { get; set; }

    // ITextElement
    public string? Text { get; set; }
    public HorizontalAlign HorizontalAlign { get; set; } = HorizontalAlign.Left;
    public VerticalAlign VerticalAlign { get; set; } = VerticalAlign.Center;
    public Color TextColor { get; set; } = Colors.Black;

    public Color BoxBorderColor { get; set; } = Colors.Black;
    public Color CheckColor { get; set; } = LightThemeColors.ButtonFill;

    // IInputElement
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    protected override void OnClick(MouseClickEventArgs e)
    {
        IsChecked = !IsChecked;
        Invalidate();
    }

    public override void Draw(Graphics g)
    {
        var content = this.ContentBounds;

        float boxY = content.Y + (content.Height - BoxSize) / 2f;
        var boxRect = new Rectangle(new Point(content.X, boxY), new Size(BoxSize, BoxSize));

        g.FillRectangle(boxRect, Colors.White);
        g.DrawRectangle(boxRect, BoxBorderColor, 1.5f);

        if (IsChecked)
        {
            // Пока в Graphics нет DrawLine/DrawPath — рисуем упрощённую
            // "галочку" залитым внутренним квадратом. Настоящий чекмарк
            // потребует line/path-примитивов в Graphics/SkiaGraphics.
            const float inset = 3f;
            var checkRect = new Rectangle(
                new Point(boxRect.X + inset, boxRect.Y + inset),
                new Size(BoxSize - inset * 2, BoxSize - inset * 2));

            g.FillRectangle(checkRect, CheckColor);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(
                new Point(content.X + BoxSize + Gap, content.Y),
                new Size(Math.Max(0, content.Width - BoxSize - Gap), content.Height));

            g.DrawText(Text, textRect, TextColor, HorizontalAlign, VerticalAlign);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text) ? Size.Empty : TextMeasurer.Current.MeasureText(Text);

        float width = BoxSize + (textSize.Width > 0 ? Gap + textSize.Width : 0) + Padding.Horizontal;
        float height = Math.Max(BoxSize, textSize.Height) + Padding.Vertical;

        return ResolveSize(new Size(width, height), availableSize);
    }
}