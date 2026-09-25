using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// A panel with a background, a border and a corner radius. Draws the decoration
/// itself; derived classes add only their own content.
/// </summary>
public abstract class DecoratedPanel : PanelControl
{
    public sealed override void Draw(Graphics g)
    {
        Rectangle bounds = LocalBounds;

        FillBackground(g, bounds);

        // the panel's content is drawn before the children: the renderer calls
        // Draw and then walks Children
        DrawContent(g);
    }

    /// <summary>Own drawing under the children — row highlighting, a grid, guides.</summary>
    protected virtual void DrawContent(Graphics g) { }

    /// <summary>
    /// The border and everything on top of the children. DrawOverlay is called
    /// after walking Children and outside their clip.
    /// </summary>
    protected internal override void DrawOverlay(Graphics g)
    {
        if (BorderWidth > 0 && CurrentBorderColor.A > 0)
            g.DrawRoundRectangle(LocalBounds, CornerRadius, CurrentBorderColor, BorderWidth);

        // scrollbars from PanelControl
        base.DrawOverlay(g);

        DrawDecoration(g);
    }

    protected virtual void DrawDecoration(Graphics g) { }
}