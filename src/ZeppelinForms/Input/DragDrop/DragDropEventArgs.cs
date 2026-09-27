using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Input.DragDrop;

public sealed record class DragDropEventArgs(
    DragDropData Data,
    Point Location,
    KeyModifiers Modifiers = KeyModifiers.None) : ZfEventArgs
{
    /// <summary>What the target is ready to do. Set in DragEnter or DragOver —
    /// the source draws the cursor by this value. Left at None —
    /// the drop won't happen.</summary>
    public DragDropEffect Effect { get; set; } = DragDropEffect.None;

    public bool Handled { get; set; }
}