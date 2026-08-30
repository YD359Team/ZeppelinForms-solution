using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Core.Text;

/// <summary>Текст с кареткой и выделением. Ничего не знает про отрисовку и ввод.</summary>
public sealed class TextDocument
{
    private readonly Stack<TextEdit> _undo = new();
    private readonly Stack<TextEdit> _redo = new();
    private bool _applyingHistory;

    /// <summary>Окно склейки последовательного набора в одну операцию.</summary>
    public TimeSpan MergeWindow { get; set; } = TimeSpan.FromSeconds(1);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            CaretIndex = Math.Min(CaretIndex, _text.Length);
            SelectionAnchor = CaretIndex;

            ClearHistory();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public int CaretIndex { get; private set; }
    public int SelectionAnchor { get; private set; }

    public int SelectionStart => Math.Min(SelectionAnchor, CaretIndex);
    public int SelectionLength => Math.Abs(CaretIndex - SelectionAnchor);
    public string SelectedText => _text.Substring(SelectionStart, SelectionLength);

    public int MaxLength { get; set; } = int.MaxValue;
    public bool IsMultiline { get; set; }

    public event EventHandler? Changed;
    public event EventHandler? CaretMoved;

    public string[] Lines => _text.Split('\n');

    // ===== перемещение =====

    public void SetCaret(int index, bool extendSelection = false, bool keepDesiredColumn = false)
    {
        if (!keepDesiredColumn)
            _desiredColumn = null;

        CaretIndex = Math.Clamp(index, 0, _text.Length);

        if (!extendSelection)
            SelectionAnchor = CaretIndex;

        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    public void MoveLeft(bool extend) => SetCaret(TextElements.Previous(_text, CaretIndex), extend);
    public void MoveRight(bool extend) => SetCaret(TextElements.Next(_text, CaretIndex), extend);

    public void MoveToLineStart(bool extend) => SetCaret(LineStart(CaretIndex), extend);
    public void MoveToLineEnd(bool extend) => SetCaret(LineEnd(CaretIndex), extend);

    private int? _desiredColumn;

    public void MoveVertical(int delta, bool extend)
    {
        var (line, column) = ToPosition(CaretIndex);

        // держим исходную колонку, пока идём по вертикали: короткая строка
        // не должна «съедать» позицию навсегда
        _desiredColumn ??= column;

        SetCaret(FromPosition(line + delta, _desiredColumn.Value), extend, keepDesiredColumn: true);
    }

    public void SelectAll()
    {
        SelectionAnchor = 0;
        SetCaret(_text.Length, extendSelection: true);
    }

    // ===== правка =====

    public bool DeleteSelection()
    {
        if (SelectionLength == 0) return false;

        Replace(SelectionStart, SelectionLength, string.Empty);
        return true;
    }

    public void Insert(string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        int start = SelectionStart;
        int length = SelectionLength;

        if (_text.Length - length + value.Length > MaxLength &&
            Length - TextElements.Count(SelectedText) + TextElements.Count(value) > MaxLength)
        {
            return;
        }

        Replace(start, length, value);
    }

    public void Backspace()
    {
        if (DeleteSelection()) return;
        if (CaretIndex == 0) return;

        int start = TextElements.Previous(_text, CaretIndex);
        Replace(start, CaretIndex - start, string.Empty);
    }

    public void Delete()
    {
        if (DeleteSelection()) return;
        if (CaretIndex >= _text.Length) return;

        int end = TextElements.Next(_text, CaretIndex);
        Replace(CaretIndex, end - CaretIndex, string.Empty);
    }

    public int Length => TextElements.Count(_text);

    // ===== координаты =====

    public (int Line, int Column) ToPosition(int index)
    {
        int line = 0, lineStart = 0;

        for (int i = 0; i < index && i < _text.Length; i++)
            if (_text[i] == '\n') { line++; lineStart = i + 1; }

        return (line, index - lineStart);
    }

    public int FromPosition(int line, int column)
    {
        string[] lines = Lines;
        line = Math.Clamp(line, 0, lines.Length - 1);

        int index = 0;
        for (int i = 0; i < line; i++)
            index += lines[i].Length + 1;

        return index + Math.Clamp(column, 0, lines[line].Length);
    }

    private int LineStart(int index)
    {
        var (line, _) = ToPosition(index);
        return FromPosition(line, 0);
    }

    private int LineEnd(int index)
    {
        var (line, _) = ToPosition(index);
        return FromPosition(line, Lines[line].Length);
    }

    /// <summary>Единая точка правки: любое изменение текста проходит здесь
    /// и попадает в историю.</summary>
    private void Replace(int position, int length, string insertion)
    {
        string removed = length > 0 ? _text.Substring(position, length) : string.Empty;

        if (removed.Length == 0 && insertion.Length == 0) return;

        int caretBefore = CaretIndex;
        int anchorBefore = SelectionAnchor;

        _text = _text.Remove(position, length).Insert(position, insertion);

        CaretIndex = position + insertion.Length;
        SelectionAnchor = CaretIndex;

        if (!_applyingHistory)
        {
            var edit = new TextEdit(position, removed, insertion, caretBefore, anchorBefore, CaretIndex);

            if (!(_undo.Count > 0 && _undo.Peek().TryMerge(edit, MergeWindow)))
                _undo.Push(edit);

            _redo.Clear();   // новая правка обрывает ветку повтора
        }

        Changed?.Invoke(this, EventArgs.Empty);
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;

        TextEdit edit = _undo.Pop();

        _applyingHistory = true;
        try
        {
            _text = _text
                .Remove(edit.Position, edit.InsertedText.Length)
                .Insert(edit.Position, edit.RemovedText);

            CaretIndex = edit.CaretBefore;
            SelectionAnchor = edit.AnchorBefore;
        }
        finally
        {
            _applyingHistory = false;
        }

        _redo.Push(edit);

        Changed?.Invoke(this, EventArgs.Empty);
        CaretMoved?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;

        TextEdit edit = _redo.Pop();

        _applyingHistory = true;
        try
        {
            _text = _text
                .Remove(edit.Position, edit.RemovedText.Length)
                .Insert(edit.Position, edit.InsertedText);

            CaretIndex = edit.CaretAfter;
            SelectionAnchor = CaretIndex;
        }
        finally
        {
            _applyingHistory = false;
        }

        _undo.Push(edit);

        Changed?.Invoke(this, EventArgs.Empty);
        CaretMoved?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void SelectWord()
    {
        int start = CaretIndex, end = CaretIndex;

        while (start > 0 && !char.IsWhiteSpace(_text[start - 1])) start--;
        while (end < _text.Length && !char.IsWhiteSpace(_text[end])) end++;

        SelectionAnchor = start;
        SetCaret(end, extendSelection: true);
    }

    public void SelectLine()
    {
        var (line, _) = ToPosition(CaretIndex);

        SelectionAnchor = FromPosition(line, 0);
        SetCaret(FromPosition(line, Lines[line].Length), extendSelection: true);
    }

    public void MoveWordLeft(bool extend)
    {
        int index = CaretIndex;

        while (index > 0 && char.IsWhiteSpace(_text[index - 1])) index--;
        while (index > 0 && !char.IsWhiteSpace(_text[index - 1])) index--;

        SetCaret(index, extend);
    }

    public void MoveWordRight(bool extend)
    {
        int index = CaretIndex;

        while (index < _text.Length && !char.IsWhiteSpace(_text[index])) index++;
        while (index < _text.Length && char.IsWhiteSpace(_text[index])) index++;

        SetCaret(index, extend);
    }
}
