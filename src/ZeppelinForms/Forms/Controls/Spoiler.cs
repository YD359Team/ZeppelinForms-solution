using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Control can collapse\expand child content
/// </summary>
public class Spoiler : WrapControl, IBorderedElement
{
    private bool _headerHovered;

    public string? Header { get; set; }
    public float HeaderHeight { get; set; } = 26f;

    public Color HeaderColor { get; set; } = new Color(255, 245, 245, 245);
    public Color HeaderHoverColor { get; set; } = new Color(255, 232, 232, 232);
    public Color HeaderTextColor { get; set; } = Colors.Black;

    public bool IsCollapsed
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnCollapsedStateChanged(value);
            Invalidate();
        }
    }

    // IBorderedElement
    public Color BorderColor { get; set; } = new Color(255, 200, 200, 200);
    public float BorderWidth { get; set; } = 1f;

    public Spoiler()
    {
        // свёрнутый спойлер должен схлопываться до заголовка,
        // а не растягиваться на всю выделенную высоту
        VerticalAlignment = VerticalAlignment.Top;
    }

    private Rectangle HeaderRect => new(Point.Empty, new Size(ActualSize.Width, HeaderHeight));

    // Ребёнок прячется через IsVisible, а не через "пропустим Arrange":
    // рендер и панели уважают этот флаг, а неразмещённый элемент
    // сохранил бы старую геометрию и продолжил рисоваться.
    private void SyncChildVisibility()
    {
        if (Child is not null)
            Child.IsVisible = !IsCollapsed;
    }

    public override void Draw(Graphics g)
    {
        var header = HeaderRect;

        g.FillRectangle(header, _headerHovered ? HeaderHoverColor : HeaderColor);

        // треугольник-указатель: вправо когда свёрнут, вниз когда раскрыт
        float cx = 12f;
        float cy = HeaderHeight / 2f;
        const float r = 4f;

        ReadOnlySpan<Point> arrow = IsCollapsed
            ? [new(cx - r * 0.6f, cy - r), new(cx + r * 0.8f, cy), new(cx - r * 0.6f, cy + r)]
            : [new(cx - r, cy - r * 0.6f), new(cx, cy + r * 0.8f), new(cx + r, cy - r * 0.6f)];

        g.DrawPolyline(arrow, HeaderTextColor, 1.8f);

        if (!string.IsNullOrEmpty(Header))
        {
            var textRect = new Rectangle(
                new Point(24f, 0),
                new Size(Math.Max(0, ActualSize.Width - 28f), HeaderHeight));

            g.DrawText(Header, textRect, HeaderTextColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        }

        if (BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, BorderColor, BorderWidth);
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        Point abs = GetAbsolutePosition();
        bool inHeader = args.Location.Y - abs.Y <= HeaderHeight;

        if (inHeader != _headerHovered)
        {
            _headerHovered = inHeader;
            Invalidate();
        }
    }

    protected override void OnMouseExit(MouseMoveEventArgs args) => _headerHovered = false;

    protected override void OnClick(MouseClickEventArgs e)
    {
        Point abs = GetAbsolutePosition();

        // переключаем только по клику в заголовок — клики по содержимому
        // должны доставаться самому содержимому
        if (e.Location.Y - abs.Y <= HeaderHeight)
        {
            IsCollapsed = !IsCollapsed;
            e.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        SyncChildVisibility();

        if (IsCollapsed)
            return ResolveSize(new Size(Padding.Horizontal, HeaderHeight + Padding.Vertical), availableSize);

        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical - HeaderHeight));

        Size childDesired = Size.Empty;
        if (Child is not null)
        {
            Child.Measure(inner);
            childDesired = Child.DesiredSize;
        }

        var content = new Size(
            childDesired.Width + Padding.Horizontal,
            childDesired.Height + Padding.Vertical + HeaderHeight);

        return ResolveSize(content, availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (IsCollapsed || Child is null)
            return finalSize;

        Child.Arrange(new Rectangle(
            new Point(Padding.Left, Padding.Top + HeaderHeight),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical - HeaderHeight))));

        return finalSize;
    }

    protected virtual void OnCollapsedStateChanged(bool isCollapsed)
    {
        SyncChildVisibility();
    }
}