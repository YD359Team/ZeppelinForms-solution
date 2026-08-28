using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Иконка из SVG path data (атрибут d одиночного &lt;path&gt;).
/// Полные SVG-документы со слоями/градиентами не поддерживаются —
/// для них нужен отдельный парсер.
/// </summary>
public class SvgIcon : UnitControl
{
    public string? PathData { get; set; }

    public Color Color { get; set; } = Colors.Black;

    /// <summary>0 — заливка, больше нуля — обводка указанной толщины.</summary>
    public float StrokeWidth { get; set; }

    public float IconSize { get; set; } = 24f;

    public override void Draw(Graphics g)
    {
        if (string.IsNullOrWhiteSpace(PathData)) return;

        g.DrawSvgPath(PathData, this.ContentBounds, Color, StrokeWidth);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(IconSize + Padding.Horizontal, IconSize + Padding.Vertical), availableSize);
}