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
        this.HorizontalAlignment = Enums.HorizontalAlignment.Center;
        this.VerticalAlignment = Enums.VerticalAlignment.Center;
    }
}
