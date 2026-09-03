using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Контейнер с одним ребёнком, рисующий фон, рамку и скругление.
/// Порядок как у панели: своё оформление до потомка, рамка после.
/// </summary>
public abstract class DecoratedWrapControl : WrapControl, IBorderedElement
{
    public Color BorderColor { get; set; } = Colors.Transparent;
    public float BorderWidth { get; set; }

    protected virtual Color CurrentBackground => Background;
    protected virtual Color CurrentBorderColor => BorderColor;

    public sealed override void Draw(Graphics g)
    {
        if (CurrentBackground.A > 0)
            g.FillRoundRectangle(LocalBounds, CornerRadius, CurrentBackground);

        DrawContent(g);
    }

    /// <summary>Своё содержимое под потомком — заголовок, подложка.</summary>
    protected virtual void DrawContent(Graphics g) { }

    /// <summary>Рамка и всё поверх потомка. Вызывается после его отрисовки
    /// и вне его отсечения.</summary>
    protected internal override void DrawOverlay(Graphics g)
    {
        if (BorderWidth > 0 && CurrentBorderColor.A > 0)
            g.DrawRoundRectangle(LocalBounds, CornerRadius, CurrentBorderColor, BorderWidth);

        DrawDecoration(g);
    }

    protected virtual void DrawDecoration(Graphics g) { }
}