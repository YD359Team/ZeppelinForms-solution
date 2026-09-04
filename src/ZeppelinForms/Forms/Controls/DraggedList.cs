using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class DraggedList : ItemsControl, IInputElement
{
    public bool IsFocused { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public bool TabStop { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public uint TabIndex { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
}