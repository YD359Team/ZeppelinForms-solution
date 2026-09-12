namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Control without children
/// </summary>
public abstract class UnitControl : UIElement
{
    protected UnitControl()
    {
        // контейнеры заполняют выделенное место, а конечные контролы —
        // нет: кнопка в ячейке Grid должна остаться кнопкой
        SetControlDefault(HorizontalAlignmentProperty, Enums.HorizontalAlignment.Center);
        SetControlDefault(VerticalAlignmentProperty, Enums.VerticalAlignment.Center);
    }
}
