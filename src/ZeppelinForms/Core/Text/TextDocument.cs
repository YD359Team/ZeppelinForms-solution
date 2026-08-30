using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Core.Text;

/// <summary>Текст с кареткой и выделением. Ничего не знает про отрисовку и ввод.</summary>
public sealed class TextDocument
{
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            CaretIndex = Math.Min(CaretIndex, _text.Length);
            SelectionAnchor = CaretIndex;
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

    public void SetCaret(int index, bool extendSelection = false)
    {
        CaretIndex = Math.Clamp(index, 0, _text.Length);

        if (!extendSelection)
            SelectionAnchor = CaretIndex;

        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    public void MoveLeft(bool extend) => SetCaret(TextElements.Previous(_text, CaretIndex), extend);
    public void MoveRight(bool extend) => SetCaret(TextElements.Next(_text, CaretIndex), extend);

    public void MoveToLineStart(bool extend) => SetCaret(LineStart(CaretIndex), extend);
    public void MoveToLineEnd(bool extend) => SetCaret(LineEnd(CaretIndex), extend);

    public void MoveVertical(int delta, bool extend)
    {
        var (line, column) = ToPosition(CaretIndex);
        SetCaret(FromPosition(line + delta, column), extend);
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

        int start = SelectionStart;
        _text = _text.Remove(start, SelectionLength);

        CaretIndex = start;
        SelectionAnchor = start;

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Insert(string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        DeleteSelection();

        // считаем в видимых символах: эмодзи не должен съедать два лимита
        if (Length + TextElements.Count(value) > MaxLength) return;

        _text = _text.Insert(CaretIndex, value);
        SetCaret(CaretIndex + value.Length);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Backspace()
    {
        if (DeleteSelection()) return;
        if (CaretIndex == 0) return;

        int start = TextElements.Previous(_text, CaretIndex);
        _text = _text.Remove(start, CaretIndex - start);
        SetCaret(start);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete()
    {
        if (DeleteSelection()) return;
        if (CaretIndex >= _text.Length) return;

        int end = TextElements.Next(_text, CaretIndex);
        _text = _text.Remove(CaretIndex, end - CaretIndex);

        Changed?.Invoke(this, EventArgs.Empty);
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
}
