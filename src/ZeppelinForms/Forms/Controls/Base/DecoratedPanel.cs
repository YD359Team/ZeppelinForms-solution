using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Панель с фоном, рамкой и скруглением. Оформление рисует сама,
/// наследники добавляют только своё содержимое.
/// </summary>
public abstract class DecoratedPanel : PanelControl
{
    public sealed override void Draw(Graphics g)
    {
        Rectangle bounds = LocalBounds;

        if (CurrentBackground.A > 0)
            g.FillRoundRectangle(bounds, CornerRadius, CurrentBackground);

        // содержимое панели рисуется до потомков: SkiaRenderer вызывает
        // Draw, а затем обходит Children
        DrawContent(g);
    }

    /// <summary>Своя отрисовка под потомками — подсветка строк, сетка, направляющие.</summary>
    protected virtual void DrawContent(Graphics g) { }

    /// <summary>
    /// Рамка и всё, что поверх потомков. DrawOverlay вызывается после
    /// обхода Children и вне их отсечения.
    /// </summary>
    protected internal override void DrawOverlay(Graphics g)
    {
        if (BorderWidth > 0 && CurrentBorderColor.A > 0)
            g.DrawRoundRectangle(LocalBounds, CornerRadius, CurrentBorderColor, BorderWidth);

        // полосы прокрутки из PanelControl
        base.DrawOverlay(g);

        DrawDecoration(g);
    }

    protected virtual void DrawDecoration(Graphics g) { }
}
