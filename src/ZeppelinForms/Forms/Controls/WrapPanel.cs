using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Раскладывает потомков в ряд и переносит на следующую строку, когда
/// текущая кончилась. По неограниченной оси перенос не происходит:
/// переносить нечего, если места бесконечно много.
/// </summary>
public partial class WrapPanel : DecoratedPanel
{
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial Orientation Orientation { get; set; }

    /// <summary>Промежуток между соседями в одной строке.</summary>
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial float Spacing { get; set; }

    /// <summary>Промежуток между строками.</summary>
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial float LineSpacing { get; set; }

    /// <summary>Выравнивание строки по её поперечной оси: короткие элементы
    /// прижимаются к началу строки, к центру или растягиваются на её высоту.</summary>
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial CrossAxisAlignment LineAlignment { get; set; }

    private static CrossAxisAlignment LineAlignmentDefault => CrossAxisAlignment.Start;

    private bool IsHorizontal => Orientation == Orientation.Horizontal;

    private float MainOf(Size size) => IsHorizontal ? size.Width : size.Height;
    private float CrossOf(Size size) => IsHorizontal ? size.Height : size.Width;
    private float MainMargin(Thickness m) => IsHorizontal ? m.Horizontal : m.Vertical;
    private float CrossMargin(Thickness m) => IsHorizontal ? m.Vertical : m.Horizontal;

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float limit = IsHorizontal ? inner.Width : inner.Height;

        // по бесконечной оси переносить нечего: всё уляжется в одну строку
        bool wraps = float.IsFinite(limit);

        float lineMain = 0;
        float lineCross = 0;
        float totalMain = 0;
        float totalCross = 0;
        bool firstInLine = true;

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            Thickness m = child.Margin;

            // меряем в полной строке: элемент шире неё всё равно займёт
            // строку целиком, и обрезать его на этапе измерения нельзя
            child.Measure(IsHorizontal
                ? new Size(Math.Max(0, limit - m.Horizontal), float.PositiveInfinity)
                : new Size(float.PositiveInfinity, Math.Max(0, limit - m.Vertical)));

            float main = MainOf(child.DesiredSize) + MainMargin(m);
            float cross = CrossOf(child.DesiredSize) + CrossMargin(m);

            float advance = firstInLine ? main : main + Spacing;

            if (wraps && !firstInLine && lineMain + advance > limit)
            {
                totalMain = Math.Max(totalMain, lineMain);
                totalCross += lineCross + LineSpacing;

                lineMain = main;
                lineCross = cross;
                firstInLine = false;
                continue;
            }

            lineMain += advance;
            lineCross = Math.Max(lineCross, cross);
            firstInLine = false;
        }

        totalMain = Math.Max(totalMain, lineMain);
        totalCross += lineCross;

        return IsHorizontal
            ? new Size(totalMain + Padding.Horizontal, totalCross + Padding.Vertical)
            : new Size(totalCross + Padding.Horizontal, totalMain + Padding.Vertical);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var inner = new Size(
            Math.Max(0, contentSize.Width - Padding.Horizontal),
            Math.Max(0, contentSize.Height - Padding.Vertical));

        float limit = IsHorizontal ? inner.Width : inner.Height;

        float mainOffset = 0;
        float crossOffset = 0;
        float lineCross = 0;
        bool firstInLine = true;

        // строку раскладываем сразу, но её поперечный размер известен
        // только после того, как строка кончилась, — поэтому копим
        List<UIElement> line = [];

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            Thickness m = child.Margin;

            float main = MainOf(child.DesiredSize) + MainMargin(m);
            float cross = CrossOf(child.DesiredSize) + CrossMargin(m);
            float advance = firstInLine ? main : main + Spacing;

            if (!firstInLine && mainOffset + advance > limit)
            {
                PlaceLine(line, crossOffset, lineCross);

                crossOffset += lineCross + LineSpacing;
                mainOffset = 0;
                lineCross = 0;
                line.Clear();
                advance = main;
            }

            child.Arrange(ToPoint(mainOffset + (IsHorizontal ? m.Left : m.Top),
                          crossOffset + (IsHorizontal ? m.Top : m.Left)),
                          child.DesiredSize);

            line.Add(child);

            mainOffset += advance;
            lineCross = Math.Max(lineCross, cross);
            firstInLine = false;
        }

        PlaceLine(line, crossOffset, lineCross);
    }

    /// <summary>Собрать точку из продольной и поперечной координат.
    /// Имя не Position: так называется свойство UIElement, и совпадение
    /// перекрывало бы его внутри этого класса.</summary>
    private Point ToPoint(float main, float cross) => IsHorizontal
        ? new Point(Padding.Left + main, Padding.Top + cross)
        : new Point(Padding.Left + cross, Padding.Top + main);

    /// <summary>Досдвинуть элементы строки по её поперечной оси, когда
    /// её высота уже известна.</summary>
    private void PlaceLine(List<UIElement> line, float crossOffset, float lineCross)
    {
        if (LineAlignment == CrossAxisAlignment.Start || line.Count == 0) return;

        foreach (UIElement child in line)
        {
            Thickness m = child.Margin;
            float cross = CrossOf(child.ActualSize) + CrossMargin(m);

            if (LineAlignment == CrossAxisAlignment.Stretch)
            {
                child.Arrange(child.Position, IsHorizontal
                    ? new Size(child.ActualSize.Width, Math.Max(0, lineCross - CrossMargin(m)))
                    : new Size(Math.Max(0, lineCross - CrossMargin(m)), child.ActualSize.Height));

                continue;
            }

            float shift = LineAlignment == CrossAxisAlignment.Center
                ? (lineCross - cross) / 2f
                : lineCross - cross;

            child.Position = IsHorizontal
                ? new Point(child.Position.X, child.Position.Y + shift)
                : new Point(child.Position.X + shift, child.Position.Y);
        }
    }
}