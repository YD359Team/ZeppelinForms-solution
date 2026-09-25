namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Control without children
/// </summary>
public abstract class UnitControl : UIElement
{
    protected UnitControl()
    {
        // containers fill the space they are given, leaf controls don't:
        // a button in a Grid cell must stay a button
        SetControlDefault(HorizontalAlignmentProperty, Enums.HorizontalAlignment.Center);
        SetControlDefault(VerticalAlignmentProperty, Enums.VerticalAlignment.Center);
    }
}