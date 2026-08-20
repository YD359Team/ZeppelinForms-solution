using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Dispatchers;

public class FocusDispatcher
{
    private IInputElement? _focused;

    public bool FocusElement(IInputElement element)
    {
        if (!element.TabStop)
            return false;

        if (ReferenceEquals(_focused, element))
            return true;

        if (_focused is not null)
            _focused.IsFocused = false;

        element.IsFocused = true;
        _focused = element;
        return true;
    }
}
