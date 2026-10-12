namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Control without children
/// </summary>
/// <remarks>
/// <para>
/// The alignment a control starts with follows one rule, by what the control is:
/// </para>
/// <list type="bullet">
/// <item><description>A container — a panel, a border, a split view — fills the
/// space it is given: Stretch on both axes, from <see cref="UIElement"/>.</description></item>
/// <item><description>A control that is a view onto content of its own — a data grid,
/// a chart, a map, an image viewer — has no natural size: the content just shows
/// more or less of itself. It fills its space as a container does: Stretch on both
/// axes.</description></item>
/// <item><description>A bar — a progress bar, a track bar, a tab strip — has a
/// thickness of its own and a length that follows the room: Stretch along it, Center
/// across.</description></item>
/// <item><description>Anything else — a button, a check box, a text box, a label —
/// has a natural size and keeps it: Center on both axes, here. A button in a Grid
/// cell must stay a button.</description></item>
/// </list>
/// <para>
/// A DataGridView used to be centered vertically by the last rule: in a DockPanel
/// or a SplitView it stood as a strip of ten rows in the middle of the room, and only
/// Dock.Fill made it fill. An explicit Size beats every one of these: a stretched
/// element with a Size keeps it and is centered, see <see cref="UIElement.Arrange(ZeppelinForms.Drawing.Primitives.Rectangle)"/>.
/// </para>
/// </remarks>
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