using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>Горизонтальная строка меню окна. Подменю открывает через
/// тот же overlay-слой, что и контекстное меню.</summary>
public class MenuBar : UnitControl
{
    private const float ItemPadding = 12f;

    private int _hoveredIndex = -1;
    private int _openIndex = -1;

    public List<MenuItem> Items { get; init; } = [];

    public Color TextColor { get; set; } = Colors.Black;
    public Color HoverColor { get; set; } = new Color(255, 232, 240, 254);
    public Color OpenColor { get; set; } = new Color(255, 214, 228, 252);

    public MenuBar()
    {
        Background = new Color(255, 248, 248, 248);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Top;
    }

    private float WidthOf(MenuItem item) =>
        TextMeasurer.Current.MeasureText(item.Text, EffectiveFont).Width + ItemPadding * 2;

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        float x = 0;

        for (int i = 0; i < Items.Count; i++)
        {
            float width = WidthOf(Items[i]);
            var cell = new Rectangle(new Point(x, 0), new Size(width, ActualSize.Height));

            if (i == _openIndex)
                g.FillRectangle(cell, OpenColor);
            else if (i == _hoveredIndex)
                g.FillRectangle(cell, HoverColor);

            g.DrawText(Items[i].Text, cell, TextColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

            x += width;
        }
    }

    private int IndexFromPoint(Point location)
    {
        float localX = location.X - GetAbsolutePosition().X;

        float x = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            float width = WidthOf(Items[i]);
            if (localX >= x && localX < x + width)
                return i;

            x += width;
        }

        return -1;
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        int index = IndexFromPoint(args.Location);
        if (index == _hoveredIndex) return;

        _hoveredIndex = index;

        // мышь ведут вдоль строки при уже открытом меню — переключаем на лету,
        // как это делают системные меню
        if (_openIndex >= 0 && index >= 0 && index != _openIndex)
            OpenSubmenu(index);

        Invalidate();
    }

    protected override void OnMouseLeave() => _hoveredIndex = -1;

    protected override void OnClick(MouseClickEventArgs e)
    {
        int index = IndexFromPoint(e.Location);
        if (index < 0) return;

        e.Handled = true;

        if (_openIndex == index)
        {
            FindOwner()?.CloseAllFlyouts();
            _openIndex = -1;
            Invalidate();
            return;
        }

        OpenSubmenu(index);
    }

    private void OpenSubmenu(int index)
    {
        Form? owner = FindOwner();
        if (owner is null || Items[index].Items.Count == 0) return;

        owner.CloseAllFlyouts();

        float x = GetAbsolutePosition().X;
        for (int i = 0; i < index; i++)
            x += WidthOf(Items[i]);

        owner.ShowContextMenu(Items[index].Items,
            new Point(x, GetAbsolutePosition().Y + ActualSize.Height));

        _openIndex = index;
        Invalidate();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float width = 0;
        foreach (MenuItem item in Items)
            width += WidthOf(item);

        Size probe = TextMeasurer.Current.MeasureText("Wg", EffectiveFont);
        return ResolveSize(new Size(width, probe.Height + 10), availableSize);
    }
}