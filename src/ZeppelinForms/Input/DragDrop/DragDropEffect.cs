namespace ZeppelinForms.Input.DragDrop;

/// <summary>What will happen if released here. The source shows this with
/// the cursor, so the value must already be set in DragOver, not in Drop.</summary>
public enum DragDropEffect
{
    /// <summary>Dropping here is not allowed.</summary>
    None,

    Copy,
    Move,
    Link,
}