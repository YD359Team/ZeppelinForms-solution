using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Dispatchers;

public class FocusDispatcher
{
    public UIElement? FocusedElement => _focused;

    private UIElement? _focused;

    public bool FocusElement(UIElement element)
    {
        if (element is not IInputElement input || !input.TabStop)
            return false;

        if (ReferenceEquals(_focused, element))
            return true;

        if (_focused is IInputElement prevInput)
        {
            prevInput.IsFocused = false;
            _focused.RaiseLostFocus();
            _focused.Invalidate();
        }

        input.IsFocused = true;
        element.RaiseGotFocus();
        element.Invalidate();
        _focused = element;
        return true;
    }
}
