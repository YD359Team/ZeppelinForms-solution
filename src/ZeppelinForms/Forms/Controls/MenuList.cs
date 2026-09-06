using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>Вертикальный список пунктов меню. Рисуется целиком сам,
/// без вложенных контролов — так проще с наведением и разделителями.</summary>
public partial class MenuList : DecoratedControl
{
    private const float ItemHeight = 26f;
    private const float SeparatorHeight = 7f;
    private const float IconWidth = 22f;

    private int _hoveredIndex = -1;

    public List<MenuItem> Items { get; init; } = [];

    public event EventHandler<MenuItem>? ItemInvoked;

    [Styled(Category = "Menu")]
    public partial Color DisabledColor { get; set; }
    private static Color DisabledColorDefault => new(255, 160, 160, 160);

    [Styled(Category = "Menu")]
    public partial Color HoverColor { get; set; }
    private static Color HoverColorDefault => new(255, 232, 240, 254);

    [Styled(Category = "Menu")]
    public partial Color SeparatorColor { get; set; }
    private static Color SeparatorColorDefault => new(255, 220, 220, 220);

    public MenuList()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        Padding = new Thickness(2, 4);
    }

    private float HeightOf(MenuItem item) => item.IsSeparator ? SeparatorHeight : ItemHeight;

    // фон, рамку и скругление рисует база — здесь только пункты меню
    protected override void DrawContent(Graphics g)
    {
        var content = this.ContentBounds;
        float y = content.Y;

        for (int i = 0; i < Items.Count; i++)
        {
            MenuItem item = Items[i];
            float height = HeightOf(item);

            if (item.IsSeparator)
            {
                float lineY = y + height / 2f;
                g.DrawLine(new Point(content.X + 6, lineY),
                    new Point(content.X + content.Width - 6, lineY), SeparatorColor, 1f);
            }
            else
            {
                var row = new Rectangle(new Point(content.X, y), new Size(content.Width, height));

                if (i == _hoveredIndex && item.IsEnabled)
                    g.FillRectangle(row, HoverColor);

                Color color = item.IsEnabled ? TextColor : DisabledColor;

                if (!string.IsNullOrEmpty(item.PathData))
                {
                    var icon = new Rectangle(
                        new Point(content.X + 4, y + (height - 14) / 2f), new Size(14, 14));

                    g.DrawSvgPath(item.PathData, icon, color);
                }

                var text = new Rectangle(
                    new Point(content.X + IconWidth, y),
                    new Size(Math.Max(0, content.Width - IconWidth - 6), height));

                g.DrawText(item.Text, text, color, EffectiveFont,
                    HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
            }

            y += height;
        }
    }

    private int IndexFromPoint(Point location)
    {
        Point abs = GetAbsolutePosition();
        float localY = location.Y - abs.Y - Padding.Top;

        float y = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            float height = HeightOf(Items[i]);
            if (localY >= y && localY < y + height)
                return i;

            y += height;
        }

        return -1;
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        int index = IndexFromPoint(args.Location);
        if (index == _hoveredIndex) return;

        _hoveredIndex = index;
        Invalidate();
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        _hoveredIndex = -1;
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        int index = IndexFromPoint(e.Location);
        if (index < 0) return;

        MenuItem item = Items[index];
        e.Handled = true;

        if (item.IsSeparator || !item.IsEnabled)
            return;

        item.RaiseClick();
        ItemInvoked?.Invoke(this, item);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float height = 0;
        float width = 0;

        foreach (MenuItem item in Items)
        {
            height += HeightOf(item);

            if (!item.IsSeparator)
                width = Math.Max(width, TextMeasurer.Current.MeasureText(item.Text, EffectiveFont).Width);
        }

        var content = new Size(
            width + IconWidth + 20 + Padding.Horizontal,
            height + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}