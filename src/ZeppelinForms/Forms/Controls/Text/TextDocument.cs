using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Core.Text;

/// <summary>Text with a caret and selection. Knows nothing about drawing or input.</summary>
public sealed class TextDocument
{
    private readonly Stack<TextEdit> _undo = new();
    private readonly Stack<TextEdit> _redo = new();
    private bool _applyingHistory;

    /// <summary>The window for merging sequential typing into one operation.</summary>
    public TimeSpan MergeWindow { get; set; } = TimeSpan.FromSeconds(1);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    private string _text = string.Empty;

    /// <summary>Where each line starts, made on the first question after a change: the
    /// line of an index and the index of a line are a binary search and a lookup instead
    /// of a scan of the text and a split of it into a new array.</summary>
    private int[]? _lineStarts;

    public string Text
    {
        get => _text;
        set
        {
            SetText(value ?? string.Empty);
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

    /// <summary>A text put in whole: the line index is made again when it is next
    /// asked for.</summary>
    private void SetText(string text)
    {
        _text = text;
        _lineStarts = null;
    }

    /// <summary>
    /// <paramref name="length"/> characters at <paramref name="position"/> replaced by
    /// <paramref name="insertion"/> — every edit, undo and redo goes through here.
    /// </summary>
    /// <remarks>
    /// One new string: Remove and then Insert made two and copied the whole text
    /// twice when both had something to do, as when typing over a selection.
    /// An edit without line breaks on either side — a typed key, a deleted
    /// character — keeps the line index and moves the starts after it, instead of
    /// making it again: the lines are the same, only shifted.
    /// </remarks>
    private void Splice(int position, int length, string insertion)
    {
        bool sameLines = _lineStarts is not null &&
                         insertion.IndexOf('\n') < 0 &&
                         _text.AsSpan(position, length).IndexOf('\n') < 0;

        _text = string.Concat(_text.AsSpan(0, position), insertion, _text.AsSpan(position + length));

        if (!sameLines)
        {
            _lineStarts = null;
            return;
        }

        int shift = insertion.Length - length;
        if (shift == 0) return;

        int[] starts = _lineStarts!;

        // a line starting at the position keeps its start: the edit is inside it.
        // No start lies inside the removed characters — they hold no line break
        int first = Array.BinarySearch(starts, position);
        first = first < 0 ? ~first : first + 1;

        for (int line = first; line < starts.Length; line++)
            starts[line] += shift;
    }

    private int[] LineStarts
    {
        get
        {
            if (_lineStarts is not null) return _lineStarts;

            // counted first: one array of the right size, not a list grown and copied
            var starts = new int[_text.AsSpan().Count('\n') + 1];
            int line = 1;

            for (int i = _text.IndexOf('\n'); i >= 0; i = _text.IndexOf('\n', i + 1))
                starts[line++] = i + 1;

            return _lineStarts = starts;
        }
    }

    /// <summary>The characters of a line, its line break left out.</summary>
    private int LineLength(int line)
    {
        int[] starts = LineStarts;

        return (line + 1 < starts.Length ? starts[line + 1] - 1 : _text.Length) - starts[line];
    }

    // ===== movement =====

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

        // keep the original column while moving vertically: a short line
        // must not "eat" the position forever
        _desiredColumn ??= column;

        SetCaret(FromPosition(line + delta, _desiredColumn.Value), extend, keepDesiredColumn: true);
    }

    public void SelectAll()
    {
        SelectionAnchor = 0;
        SetCaret(_text.Length, extendSelection: true);
    }

    // ===== editing =====

    public bool DeleteSelection()
    {
        if (SelectionLength == 0) return false;

        Replace(SelectionStart, SelectionLength, string.Empty);
        return true;
    }

    /// <summary>Insert text in place of the selection. What doesn't fit into
    /// MaxLength is cut off by visible characters.</summary>
    /// <remarks>
    /// Previously an insertion that didn't fit was dropped entirely: pasting
    /// a long text into a field with a limit inserted nothing at all. System
    /// fields insert as much as fits, and so does this now. The quick check
    /// by chars goes first: for ordinary typing it is enough, and counting
    /// text elements is needed only near the limit.
    /// </remarks>
    public void Insert(string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        int start = SelectionStart;
        int length = SelectionLength;

        if (_text.Length - length + value.Length > MaxLength)
        {
            int room = MaxLength - (Length - TextElements.Count(SelectedText));

            if (room <= 0) return;

            if (TextElements.Count(value) > room)
                value = new StringInfo(value).SubstringByTextElements(0, room);
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

    // ===== coordinates =====

    public (int Line, int Column) ToPosition(int index)
    {
        int[] starts = LineStarts;
        index = Math.Clamp(index, 0, _text.Length);

        // the last line starting at or before the index
        int line = Array.BinarySearch(starts, index);
        if (line < 0) line = ~line - 1;

        return (line, index - starts[line]);
    }

    public int FromPosition(int line, int column)
    {
        int[] starts = LineStarts;
        line = Math.Clamp(line, 0, starts.Length - 1);

        return starts[line] + Math.Clamp(column, 0, LineLength(line));
    }

    private int LineStart(int index)
    {
        var (line, _) = ToPosition(index);
        return FromPosition(line, 0);
    }

    private int LineEnd(int index)
    {
        var (line, _) = ToPosition(index);
        return FromPosition(line, LineLength(line));
    }

    /// <summary>The single point of editing: any change of the text passes
    /// through here and goes into the history.</summary>
    private void Replace(int position, int length, string insertion)
    {
        string removed = length > 0 ? _text.Substring(position, length) : string.Empty;

        if (removed.Length == 0 && insertion.Length == 0) return;

        int caretBefore = CaretIndex;
        int anchorBefore = SelectionAnchor;

        Splice(position, length, insertion);

        CaretIndex = position + insertion.Length;
        SelectionAnchor = CaretIndex;

        if (!_applyingHistory)
        {
            var edit = new TextEdit(position, removed, insertion, caretBefore, anchorBefore, CaretIndex);

            if (!(_undo.Count > 0 && _undo.Peek().TryMerge(edit, MergeWindow)))
                _undo.Push(edit);

            _redo.Clear();   // a new edit cuts off the redo branch
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
            Splice(edit.Position, edit.InsertedText.Length, edit.RemovedText);

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
            Splice(edit.Position, edit.RemovedText.Length, edit.InsertedText);

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
        SetCaret(FromPosition(line, LineLength(line)), extendSelection: true);
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