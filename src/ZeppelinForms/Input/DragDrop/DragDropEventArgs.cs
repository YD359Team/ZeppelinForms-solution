using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Input.DragDrop;

public sealed record class DragDropEventArgs(
    DragDropData Data,
    Point Location,
    KeyModifiers Modifiers = KeyModifiers.None) : ZfEventArgs
{
    /// <summary>Что приёмник готов сделать. Выставляется в DragEnter или
    /// DragOver — источник по этому значению рисует курсор. Осталось None —
    /// бросок не состоится.</summary>
    public DragDropEffect Effect { get; set; } = DragDropEffect.None;

    public bool Handled { get; set; }
}