using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Forms.Dispatchers;

public class FocusDispatcher
{
    public bool FocusElement(IInputElement element)
    {
        // add focus state?
        element.IsFocused = true;
        return true;
    }
}
