namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Control with single child (or nothing)
/// </summary>
public abstract class SingleControl : UIElement
{
    public UIElement? Child
    {
        get;
        set
        {
            if (field == value)
                return;

            if (field is not null)
                field.Parent = null;

            field = value;

            if (value is not null)
                value.Parent = this;
        }
    }
}
