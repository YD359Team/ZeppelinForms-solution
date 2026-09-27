using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

/// <summary>A border around a single element.</summary>
public class Border : DecoratedWrapControl
{
    public Border()
    {

    }

    public Border(UIElement child) : base(child)
    {

    }
}