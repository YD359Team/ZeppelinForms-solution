using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Контрол с фоном, рамкой и скруглением. Рисует оформление сам,
/// наследники добавляют только содержимое.</summary>
public abstract class DecoratedControl : UnitControl
{
    public sealed override void Draw(Graphics g)
    {
        Rectangle bounds = LocalBounds;

        if (CurrentBackground.A > 0)
            g.FillRoundRectangle(bounds, CornerRadius, CurrentBackground);

        DrawContent(g);

        // рамка поверх содержимого: иначе длинный текст её перекроет
        if (BorderWidth > 0 && CurrentBorderColor.A > 0)
            g.DrawRoundRectangle(bounds, CornerRadius, CurrentBorderColor, BorderWidth);

        DrawDecoration(g);
    }

    protected abstract void DrawContent(Graphics g);

    /// <summary>Поверх рамки — кольцо фокуса, индикаторы, полосы.</summary>
    protected virtual void DrawDecoration(Graphics g) { }
}
