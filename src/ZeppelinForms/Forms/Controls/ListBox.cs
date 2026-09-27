using System.Collections.Specialized;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class ListBox : ItemsControl, IInputElement
{
    // a set instead of a single index: _selectedIndex stays the lead,
    // it is moved by the arrows and the range is counted from it
    private readonly SortedSet<int> _selected = [];

    private int _selectedIndex = -1;
    private int _anchor = -1;

    [Styled(Category = "Selection")]
    public partial SelectionMode SelectionMode { get; set; }

    /// <summary>The lead row. With multiple selection — the one
    /// selected last.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SelectOnly(value < 0 || value >= Items.Count ? -1 : value);
    }

    public object? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;

    /// <summary>All selected indices in ascending order.</summary>
    public IReadOnlyCollection<int> SelectedIndices => _selected;

    public IReadOnlyList<object> SelectedItems =>
        [.. _selected.Where(i => i < Items.Count).Select(i => Items[i])];

    public event EventHandler? SelectionChanged;

    public bool IsSelected(int index) => _selected.Contains(index);

    public void ClearSelection()
    {
        if (_selected.Count == 0 && _selectedIndex < 0) return;

        _selected.Clear();
        _selectedIndex = -1;
        _anchor = -1;

        RaiseSelectionChanged();
    }

    public void SelectAll()
    {
        if (SelectionMode == SelectionMode.Single) return;

        _selected.Clear();

        for (int i = 0; i < Items.Count; i++)
            _selected.Add(i);

        _selectedIndex = Items.Count - 1;
        _anchor = 0;

        RaiseSelectionChanged();
    }

    public void SetSelected(int index, bool selected)
    {
        if (index < 0 || index >= Items.Count) return;

        bool changed = selected ? _selected.Add(index) : _selected.Remove(index);

        if (!changed) return;

        if (selected) _selectedIndex = index;
        else if (_selectedIndex == index) _selectedIndex = _selected.Count > 0 ? _selected.Max : -1;

        RaiseSelectionChanged();
    }

    private void SelectOnly(int index)
    {
        bool same = _selectedIndex == index && _selected.Count <= 1 &&
                    (index < 0 || _selected.Contains(index));

        if (same) return;

        _selected.Clear();

        if (index >= 0) _selected.Add(index);

        _selectedIndex = index;
        _anchor = index;

        RaiseSelectionChanged();
    }

    /// <summary>A range from the anchor row to the given one, the rest are deselected.</summary>
    private void SelectRange(int to)
    {
        if (_anchor < 0) { SelectOnly(to); return; }

        _selected.Clear();

        int from = Math.Min(_anchor, to);
        int last = Math.Max(_anchor, to);

        for (int i = from; i <= last; i++)
            _selected.Add(i);

        _selectedIndex = to;

        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Move the selection along with the items. The selection is stored
    /// by index, and previously the indices stayed in place: after removing
    /// a row above the selected one, its neighbour became selected, and
    /// SelectedItem returned someone else's object.</summary>
    /// <remarks>
    /// Subscribed after ItemsControl's handler, so the containers are already
    /// updated by the time SelectionChanged goes out.
    /// </remarks>
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_selected.Count == 0 && _selectedIndex < 0 && _anchor < 0) return;

        Func<int, int> map = e.Action switch
        {
            NotifyCollectionChangedAction.Add =>
                i => i >= e.NewStartingIndex ? i + (e.NewItems?.Count ?? 0) : i,

            NotifyCollectionChangedAction.Remove =>
                i => MapRemoved(i, e.OldStartingIndex, e.OldItems?.Count ?? 0),

            // a replaced item is a different item: its selection does not carry over
            NotifyCollectionChangedAction.Replace =>
                i => i >= e.OldStartingIndex && i < e.OldStartingIndex + (e.OldItems?.Count ?? 0) ? -1 : i,

            NotifyCollectionChangedAction.Move =>
                i => MapMoved(i, e.OldStartingIndex, e.NewStartingIndex),

            // Reset does not say what stayed — nothing can be matched
            _ => static _ => -1,
        };

        int[] before = [.. _selected];
        int leadBefore = _selectedIndex;

        _selected.Clear();

        foreach (int index in before)
            if (map(index) is int mapped and >= 0)
                _selected.Add(mapped);

        _selectedIndex = _selectedIndex >= 0 ? map(_selectedIndex) : -1;

        // the lead row itself is gone: the last selected one takes its place,
        // as SetSelected does when the lead is deselected
        if (_selectedIndex < 0 && _selected.Count > 0)
            _selectedIndex = _selected.Max;

        _anchor = _anchor >= 0 ? map(_anchor) : -1;

        // a shift alone changes SelectedIndex too, and whoever shows it must know
        bool changed = _selectedIndex != leadBefore || !before.SequenceEqual(_selected);

        if (changed) RaiseSelectionChanged();
        else InvalidateVisual();
    }

    private static int MapRemoved(int index, int start, int count) =>
        index < start ? index
        : index < start + count ? -1
        : index - count;

    // exactly what ObservableCollection.Move does: remove at from, insert at to
    private static int MapMoved(int index, int from, int to)
    {
        if (index == from) return to;

        int shifted = index > from ? index - 1 : index;

        return shifted >= to ? shifted + 1 : shifted;
    }

    [Styled(Category = "Selection")]
    public partial Color SelectionColor { get; set; }
    private static Color SelectionColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Appearance")]
    public partial Color FocusBorderColor { get; set; }
    private static Color FocusBorderColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public ListBox()
    {
        SetControlDefault(BackgroundProperty, Colors.White);

        Items.CollectionChanged += OnItemsChanged;
    }

    protected override void DrawContent(Graphics g)
    {
        // the highlight is drawn before the children: the renderer calls Draw,
        // then walks Children
        foreach (int index in _selected)
        {
            if (index >= Children.Count) continue;

            UIElement container = Children[index];

            g.FillRectangle(
                new Rectangle(
                    new Point(ContentBounds.X, container.Position.Y),
                    new Size(ContentBounds.Width, container.ActualSize.Height)),
                SelectionColor);
        }
    }

    /// <summary>Selection on press, not on click: the row's content may swallow
    /// the click, and the selection must change anyway.</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        // the hit lands on a row, which is itself enabled, so the form's check
        // for a disabled hit doesn't stop a disabled list from selecting
        if (!IsEnabled) return;

        int index = IndexAt(e.Location);
        if (index < 0) return;

        switch (SelectionMode)
        {
            case SelectionMode.Multiple:
                SetSelected(index, !IsSelected(index));
                break;

            case SelectionMode.Extended when e.Modifiers.HasFlag(KeyModifiers.Shift):
                SelectRange(index);
                break;

            case SelectionMode.Extended when e.Modifiers.HasFlag(KeyModifiers.Control):
                SetSelected(index, !IsSelected(index));
                _anchor = index;
                break;

            default:
                SelectOnly(index);
                break;
        }
    }

    private int IndexAt(Point absolute)
    {
        float localY = absolute.Y - GetAbsolutePosition().Y;

        for (int i = 0; i < Children.Count; i++)
        {
            UIElement child = Children[i];

            if (localY >= child.Position.Y && localY < child.Position.Y + child.ActualSize.Height)
                return i;
        }

        return -1;
    }

    /// <summary>When focused the border is highlighted — the base draws it itself.</summary>
    protected override Color CurrentBorderColor =>
        IsFocused && FocusBorderColor.A > 0 ? FocusBorderColor : BorderColor;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int target = e.Key switch
        {
            Key.Up => Math.Max(0, _selectedIndex - 1),
            Key.Down => Math.Min(Items.Count - 1, _selectedIndex + 1),
            Key.Home => 0,
            Key.End => Items.Count - 1,
            _ => -1,
        };

        if (target >= 0)
        {
            // Shift with the arrows extends the range from the anchor row,
            // as in any file manager
            if (SelectionMode == SelectionMode.Extended && e.Modifiers.HasFlag(KeyModifiers.Shift))
                SelectRange(target);
            else
                SelectOnly(target);

            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && e.Modifiers.HasFlag(KeyModifiers.Control))
        {
            SelectAll();
            e.Handled = true;
        }
    }
}