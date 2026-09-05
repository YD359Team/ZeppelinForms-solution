using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Иконка из SVG path data (атрибут d одиночного &lt;path&gt;).
/// Полные SVG-документы со слоями/градиентами не поддерживаются —
/// для них нужен отдельный парсер.
/// </summary>
public class SvgIcon : DecoratedControl
{
    public string? PathData { get; set; }

    public Color Color { get; set; } = Colors.Black;

    /// <summary>0 — заливка, больше нуля — обводка указанной толщины.</summary>
    public float StrokeWidth { get; set; }

    public float IconSize { get; set; } = 24f;

    protected override void DrawContent(Graphics g)
    {
        if (string.IsNullOrWhiteSpace(PathData)) return;

        g.DrawSvgPath(PathData, this.ContentBounds, Color, StrokeWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // если места дали меньше желаемого — вписываемся в него,
        // DrawSvgPath всё равно сохраняет пропорции
        float size = Math.Min(IconSize, Math.Min(availableSize.Width, availableSize.Height));

        return ResolveSize(new Size(size + Padding.Horizontal, size + Padding.Vertical), availableSize);
    }
}