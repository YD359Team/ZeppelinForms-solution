using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Показывает часть содержимого и прокручивает его.
/// Технически панель: содержимое и полоса прокрутки — обычные Children,
/// поэтому хит-тестинг, клип и рендер работают без правок.
/// </summary>
[Obsolete]
public class ScrollViewer : StackPanel
{
    public UIElement? Content
    {
        get => Children.FirstOrDefault();
        set
        {
            Children.Clear();
            if (value is not null) Children.Add(value);
        }
    }

    public ScrollViewer() => OverflowY = Overflow.Auto;
}