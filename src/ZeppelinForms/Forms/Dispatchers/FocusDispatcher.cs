using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Dispatchers;

public class FocusDispatcher
{
    // add collection with form children (IInputElement)

    public bool FocusElement(IInputElement element)
    {
        if (!element.TabStop) return false;

        // add focus state?
        element.IsFocused = true;
        return true;
    }

    public void MoveBack()
    {

    }

    public void MoveNext()
    {

    }
}
