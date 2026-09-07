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
    // множество вместо одного индекса: _selectedIndex остаётся ведущим,
    // именно он двигается стрелками и от него считается диапазон
    private readonly SortedSet<int> _selected = [];

    private int _selectedIndex = -1;
    private int _anchor = -1;

    [Styled(Category = "Selection")]
    public partial SelectionMode SelectionMode { get; set; }

    /// <summary>Ведущая строка. При множественном выделении — та,
    /// которую выбрали последней.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SelectOnly(value < 0 || value >= Items.Count ? -1 : value);
    }

    public object? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;

    /// <summary>Все выделенные индексы по возрастанию.</summary>
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

    /// <summary>Диапазон от опорной строки до указанной, остальные снимаются.</summary>
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
    }

    protected override void DrawContent(Graphics g)
    {
        // подсветка рисуется до потомков: рендерер вызывает Draw,
        // затем обходит Children
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

    /// <summary>Выбор по нажатию, а не по клику: содержимое строки может
    /// погасить клик, а выделение всё равно должно смениться.</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
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

    /// <summary>В фокусе рамку подсвечиваем — база нарисует её сама.</summary>
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
            // Shift со стрелками тянет диапазон от опорной строки,
            // как в любом файловом менеджере
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