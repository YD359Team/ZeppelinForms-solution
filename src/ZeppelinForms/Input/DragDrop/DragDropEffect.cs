namespace ZeppelinForms.Input.DragDrop;

/// <summary>Что произойдёт, если отпустить здесь. Источник показывает это
/// курсором, поэтому значение надо выставлять уже в DragOver, а не в Drop.</summary>
public enum DragDropEffect
{
    /// <summary>Здесь бросать нельзя.</summary>
    None,

    Copy,
    Move,
    Link,
}