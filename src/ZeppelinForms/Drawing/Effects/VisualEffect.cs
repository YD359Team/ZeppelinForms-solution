using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>
/// A transformation of how an element gets onto the screen. Effects are applied
/// as a chain and know nothing about each other.
/// </summary>
public abstract class VisualEffect
{
    /// <summary>How far the effect goes beyond the element's bounds on each side.
    /// Shadow, blur and reflection must report this, otherwise the dirty region
    /// cuts them off on a partial redraw.</summary>
    public virtual Thickness Bleed(Rectangle bounds) => Thickness.Zero;

    /// <summary>Prepare the canvas before the element is drawn.</summary>
    public abstract void Begin(Graphics g, Rectangle bounds);

    /// <summary>Finish: close the layer, draw on top.</summary>
    public virtual void End(Graphics g, Rectangle bounds) { }
}