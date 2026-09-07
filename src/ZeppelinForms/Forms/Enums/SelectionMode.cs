namespace ZeppelinForms.Forms.Enums;

public enum SelectionMode
{
    /// <summary>Одна строка. Клик переносит выделение.</summary>
    Single,

    /// <summary>Клик переключает строку, не снимая остальные.
    /// Модификаторы не нужны — удобно для списка с галочками и на тач-экране.</summary>
    Multiple,

    /// <summary>Как в проводнике: клик заменяет выделение,
    /// Ctrl переключает одну строку, Shift выделяет диапазон от опорной.</summary>
    Extended,
}