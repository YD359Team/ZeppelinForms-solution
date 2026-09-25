using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// A single-child container that draws a background, a border and a corner radius.
/// Same order as a panel: its own decoration before the child, the border after.
/// </summary>
public abstract class DecoratedWrapControl : WrapControl
{
    protected DecoratedWrapControl() : base()
    {

    }

    protected DecoratedWrapControl(UIElement child) : base(child)
    {

    }

    public sealed override void Draw(Graphics g)
    {
        FillBackground(g, LocalBounds);

        DrawContent(g);
    }

    /// <summary>Own content under the child — a header, a backdrop.</summary>
    protected virtual void DrawContent(Graphics g) { }

    /// <summary>The border and everything on top of the child. Called after the child
    /// is drawn and outside its clip.</summary>
    protected internal override void DrawOverlay(Graphics g)
    {
        if (BorderWidth > 0 && CurrentBorderColor.A > 0)
            g.DrawRoundRectangle(LocalBounds, CornerRadius, CurrentBorderColor, BorderWidth);

        DrawDecoration(g);
    }

    protected virtual void DrawDecoration(Graphics g) { }
}