using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Input.Pointer;

/// <summary>Природа контакта. Мышь отличается от пальца не точностью,
/// а наличием состояния «над элементом, но не нажат»: у касания его нет,
/// и код, который этого не различает, оставляет подсветку и тултип
/// висеть после отпускания.</summary>
public enum PointerKind
{
    Mouse,
    Touch,
    Pen,
}
