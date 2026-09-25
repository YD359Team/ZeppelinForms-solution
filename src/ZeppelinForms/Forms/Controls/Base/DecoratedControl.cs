using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>A control with a background, a border and a corner radius. Draws the
/// decoration itself; derived classes add only the content.</summary>
public abstract class DecoratedControl : UnitControl
{
    public sealed override void Draw(Graphics g)
    {
        Rectangle bounds = LocalBounds;

        FillBackground(g, bounds);

        DrawContent(g);

        // the border goes on top of the content: otherwise long text would cover it
        if (BorderWidth > 0 && CurrentBorderColor.A > 0)
            g.DrawRoundRectangle(bounds, CornerRadius, CurrentBorderColor, BorderWidth);

        DrawDecoration(g);
    }

    protected abstract void DrawContent(Graphics g);

    /// <summary>On top of the border — focus ring, indicators, bars.</summary>
    protected virtual void DrawDecoration(Graphics g) { }
}