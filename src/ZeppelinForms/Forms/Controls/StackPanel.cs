using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

public class StackPanel : PanelControl
{
    public Orientation Orientation { get; set; } = Orientation.Vertical;
    public float Spacing { get; set; }
    public MainAxisAlignment MainAxisAlignment { get; set; } = MainAxisAlignment.Start;
    public CrossAxisAlignment CrossAxisAlignment { get; set; } = CrossAxisAlignment.Stretch;

    private bool IsVertical => Orientation == Orientation.Vertical;

    public override void Draw(Graphics g) { }

    private float MainOf(Size size) => IsVertical ? size.Height : size.Width;
    private float CrossOf(Size size) => IsVertical ? size.Width : size.Height;

    private float MainMargin(Thickness m) => IsVertical ? m.Vertical : m.Horizontal;
    private float CrossMargin(Thickness m) => IsVertical ? m.Horizontal : m.Vertical;

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float fixedMain = 0;
        float crossMax = 0;
        float totalFlex = 0;
        int visibleCount = 0;

        // первый проход: меряем негибких и собираем сумму весов
        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            visibleCount++;
            Thickness m = child.Margin;

            if (child.FlexGrow > 0)
            {
                totalFlex += child.FlexGrow;
                continue;
            }

            Size childAvailable = IsVertical
                ? new Size(Math.Max(0, inner.Width - m.Horizontal), float.PositiveInfinity)
                : new Size(float.PositiveInfinity, Math.Max(0, inner.Height - m.Vertical));

            child.Measure(childAvailable);

            fixedMain += MainOf(child.DesiredSize) + MainMargin(m);
            crossMax = Math.Max(crossMax, CrossOf(child.DesiredSize) + CrossMargin(m));
        }

        float spacingTotal = visibleCount > 1 ? Spacing * (visibleCount - 1) : 0;

        // гибкие делят остаток; при бесконечной оси делить нечего,
        // поэтому меряем их по содержимому
        float freeMain = float.IsFinite(MainOf(inner))
            ? Math.Max(0, MainOf(inner) - fixedMain - spacingTotal)
            : float.PositiveInfinity;

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible || child.FlexGrow <= 0) continue;

            Thickness m = child.Margin;

            float share = float.IsFinite(freeMain) && totalFlex > 0
                ? freeMain * (child.FlexGrow / totalFlex) - MainMargin(m)
                : float.PositiveInfinity;

            Size childAvailable = IsVertical
                ? new Size(Math.Max(0, inner.Width - m.Horizontal), Math.Max(0, share))
                : new Size(Math.Max(0, share), Math.Max(0, inner.Height - m.Vertical));

            child.Measure(childAvailable);

            crossMax = Math.Max(crossMax, CrossOf(child.DesiredSize) + CrossMargin(m));
        }

        // желаемый размер панели: негибкая часть плюс то, что запросили гибкие
        float desiredMain = fixedMain + spacingTotal;

        if (!float.IsFinite(freeMain))
        {
            foreach (UIElement child in Children)
                if (child.IsVisible && child.FlexGrow > 0)
                    desiredMain += MainOf(child.DesiredSize) + MainMargin(child.Margin);
        }
        else
        {
            desiredMain += freeMain;
        }

        Size content = IsVertical
            ? new Size(crossMax, desiredMain)
            : new Size(desiredMain, crossMax);

        return ResolveSize(
            new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical),
            availableSize);
    }

    protected override void ArrangeContentOverride(Size finalSize)
    {
        var content = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical)));

        List<UIElement> visible = [];

        foreach (UIElement child in Children)
            if (child.IsVisible)
                visible.Add(child);

        if (visible.Count == 0) return;

        float fixedMain = 0;
        float totalFlex = 0;

        foreach (UIElement child in visible)
        {
            if (child.FlexGrow > 0)
                totalFlex += child.FlexGrow;
            else
                fixedMain += MainOf(child.DesiredSize) + MainMargin(child.Margin);
        }

        float availableMain = MainOf(content.Size);
        float spacingTotal = Spacing * (visible.Count - 1);
        float freeMain = Math.Max(0, availableMain - fixedMain - spacingTotal);

        // при наличии гибких детей свободного места не остаётся —
        // распределять нечего, выравнивание вдоль оси не применяется
        float leftover = totalFlex > 0 ? 0 : freeMain;

        var (startOffset, gap) = Distribute(leftover, visible.Count);

        float offset = (IsVertical ? content.Y : content.X) + startOffset;
        bool first = true;

        foreach (UIElement child in visible)
        {
            if (!first) offset += Spacing + gap;
            first = false;

            Thickness m = child.Margin;

            float main = child.FlexGrow > 0 && totalFlex > 0
                ? Math.Max(0, freeMain * (child.FlexGrow / totalFlex) - MainMargin(m))
                : MainOf(child.DesiredSize);

            float crossAvailable = CrossOf(content.Size) - CrossMargin(m);
            float cross = CrossAxisAlignment == CrossAxisAlignment.Stretch
                ? crossAvailable
                : Math.Min(CrossOf(child.DesiredSize), crossAvailable);

            float crossOffset = CrossAxisAlignment switch
            {
                CrossAxisAlignment.Center => (crossAvailable - cross) / 2f,
                CrossAxisAlignment.End => crossAvailable - cross,
                _ => 0f,
            };

            if (IsVertical)
            {
                offset += m.Top;

                child.Arrange(new Rectangle(
                    new Point(content.X + m.Left + crossOffset, offset),
                    new Size(Math.Max(0, cross), main)));

                offset += child.ActualSize.Height + m.Bottom;
            }
            else
            {
                offset += m.Left;

                child.Arrange(new Rectangle(
                    new Point(offset, content.Y + m.Top + crossOffset),
                    new Size(main, Math.Max(0, cross))));

                offset += child.ActualSize.Width + m.Right;
            }
        }
    }

    /// <summary>Начальный отступ и добавочный промежуток между детьми.</summary>
    private (float Start, float Gap) Distribute(float leftover, int count)
    {
        if (leftover <= 0) return (0f, 0f);

        return MainAxisAlignment switch
        {
            MainAxisAlignment.Center => (leftover / 2f, 0f),
            MainAxisAlignment.End => (leftover, 0f),

            MainAxisAlignment.SpaceBetween => count > 1
                ? (0f, leftover / (count - 1))
                : (leftover / 2f, 0f),

            MainAxisAlignment.SpaceAround => (leftover / count / 2f, leftover / count),

            MainAxisAlignment.SpaceEvenly => (leftover / (count + 1), leftover / (count + 1)),

            _ => (0f, 0f),
        };
    }
}
