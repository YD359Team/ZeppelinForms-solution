using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Рамка с градиентной заливкой вокруг одного элемента.
/// Обводки градиентом в Graphics нет, поэтому кольцо собирается из двух
/// заливок: градиент на весь прямоугольник и фон поверх внутренней части.
/// Отсюда требование: <see cref="UIElement.Padding"/> не меньше
/// <see cref="DecoratedWrapControl.BorderWidth"/>, иначе ребёнок наедет на рамку.
/// </summary>
public class GradientBorder : DecoratedWrapControl
{
    /// <summary>Точки градиента. Меньше двух — рамка рисуется обычным
    /// <see cref="DecoratedWrapControl.BorderColor"/>, как у Border.</summary>
    public List<GradientStop> Stops { get; init; } = [];

    /// <summary>Направление градиента в градусах: 0 — слева направо.</summary>
    public float Angle { get; set; }

    private bool HasGradient => Stops.Count >= 2;

    public GradientBorder()
    {
        BorderWidth = 1f;
        Padding = new Thickness(1f);
    }

    public GradientBorder(UIElement child) : base(child)
    {
        BorderWidth = 1f;
        Padding = new Thickness(1f);
    }

    public GradientBorder SetStops(params GradientStop[] stops)
    {
        Stops.Clear();
        Stops.AddRange(stops);
        InvalidateVisual();

        return this;
    }

    /// <summary>Ровный переход между двумя цветами.</summary>
    public GradientBorder SetStops(Color from, Color to) =>
        SetStops(new GradientStop(from, 0f), new GradientStop(to, 1f));

    /// <summary>Фон заливаем сами, внутри кольца — иначе он лёг бы
    /// поверх всей площади и закрасил градиент.</summary>
    protected override Color CurrentBackground => Colors.Transparent;

    /// <summary>Пока точек хватает на градиент, сплошную рамку базы гасим.</summary>
    protected override Color CurrentBorderColor =>
        HasGradient ? Colors.Transparent : base.CurrentBorderColor;

    protected override void DrawContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (HasGradient)
            g.FillGradient(bounds, CornerRadius, [.. Stops], Angle);

        if (Background.A == 0) return;

        Rectangle inner = HasGradient ? bounds.Inflate(-BorderWidth) : bounds;

        if (inner.Width <= 0 || inner.Height <= 0) return;

        g.FillRoundRectangle(inner, Deflate(CornerRadius, HasGradient ? BorderWidth : 0f), Background);
    }

    private static CornerRadius Deflate(CornerRadius radius, float amount)
    {
        if (amount <= 0f || radius.IsZero) return radius;

        return new CornerRadius(
            Math.Max(0f, radius.TopLeft - amount),
            Math.Max(0f, radius.TopRight - amount),
            Math.Max(0f, radius.BottomRight - amount),
            Math.Max(0f, radius.BottomLeft - amount));
    }
}