using System.Collections.Specialized;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class CheckedListBox : ListBox
{
    private const float BoxSize = 15f;
    private const float BoxGap = 6f;

    private readonly HashSet<int> _checked = [];

    [Styled(Category = "Box")]
    public partial Color BoxBorderColor { get; set; }
    private static Color BoxBorderColorDefault => Colors.Black;

    [Styled(Category = "Box")]
    public partial Color BoxBackground { get; set; }
    private static Color BoxBackgroundDefault => Colors.White;

    [Styled(Category = "Box")]
    public partial Color CheckColor { get; set; }
    private static Color CheckColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    /// <summary>The check is toggled by a click anywhere on the row, not only on the box.</summary>
    public bool ToggleOnRowClick { get; set; }

    public event EventHandler<int>? ItemCheckedChanged;

    public IReadOnlyCollection<int> CheckedIndices => _checked;

    public IEnumerable<object> CheckedItems
    {
        get
        {
            foreach (int index in _checked.Order())
                if (index >= 0 && index < Items.Count)
                    yield return Items[index];
        }
    }

    public CheckedListBox()
    {
        // room for the box to the left of the row content
        SetControlDefault(PaddingProperty, new(BoxSize + BoxGap + 4f, 2f, 4f, 2f));

        // checks are stored by index, so they must follow the items
        Items.CollectionChanged += OnItemsChanged;
    }

    public bool IsChecked(int index) => _checked.Contains(index);

    public void SetChecked(int index, bool value)
    {
        if (index < 0 || index >= Items.Count) return;

        bool changed = value ? _checked.Add(index) : _checked.Remove(index);
        if (!changed) return;

        ItemCheckedChanged?.Invoke(this, index);
        InvalidateVisual();
    }

    public void ToggleChecked(int index) => SetChecked(index, !IsChecked(index));

    public void CheckAll()
    {
        for (int i = 0; i < Items.Count; i++)
            SetChecked(i, true);
    }

    public void UncheckAll()
    {
        // a copy, because SetChecked changes the collection during the walk
        foreach (int index in _checked.ToArray())
            SetChecked(index, false);
    }

    /// <summary>Move the checks along with their items. Previously the indices stayed
    /// in place: after removing the first row the check jumped to its neighbour.
    /// No ItemCheckedChanged here — the items kept their state,
    /// only their positions changed.</summary>
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_checked.Count == 0) return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Shift(e.NewStartingIndex, e.NewItems?.Count ?? 0);
                break;

            case NotifyCollectionChangedAction.Remove:
                RemoveChecks(e.OldStartingIndex, e.OldItems?.Count ?? 0);
                break;

            case NotifyCollectionChangedAction.Replace:
                // a replaced item is a different item: its check does not carry over
                for (int i = 0; i < (e.OldItems?.Count ?? 0); i++)
                    _checked.Remove(e.OldStartingIndex + i);
                break;

            case NotifyCollectionChangedAction.Move:
                MoveCheck(e.OldStartingIndex, e.NewStartingIndex);
                break;

            default:
                // Reset does not say what stayed — nothing can be matched
                _checked.Clear();
                break;
        }

        InvalidateVisual();
    }

    /// <summary>Indices at and after start move by count (negative — towards the start).</summary>
    private void Shift(int start, int count)
    {
        if (count == 0) return;

        int[] moved = [.. _checked.Where(i => i >= start)];

        // remove all first, then add: otherwise a shifted index could collide
        // with one that hasn't moved yet
        foreach (int index in moved) _checked.Remove(index);
        foreach (int index in moved) _checked.Add(index + count);
    }

    /// <summary>The items in [start, start + count) are gone: their checks go with
    /// them, and the checks after them close the gap.</summary>
    /// <remarks>Not named Drop: UIElement already has a Drop event (drag-and-drop),
    /// and hiding it would break dropping onto the list.</remarks>
    private void RemoveChecks(int start, int count)
    {
        if (count == 0) return;

        for (int i = start; i < start + count; i++)
            _checked.Remove(i);

        Shift(start + count, -count);
    }

    private void MoveCheck(int from, int to)
    {
        if (from == to) return;

        bool wasChecked = _checked.Remove(from);

        // close the gap the item left, then open one where it lands —
        // exactly what ObservableCollection.Move does with the items
        Shift(from + 1, -1);
        Shift(to, 1);

        if (wasChecked) _checked.Add(to);
    }

    private Rectangle BoxRect(UIElement container)
    {
        float y = container.Position.Y + (container.ActualSize.Height - BoxSize) / 2f;
        return new Rectangle(new Point(4f, y), new Size(BoxSize, BoxSize));
    }

    protected override void DrawContent(Graphics g)
    {
        // the selected row highlight comes from ListBox. DecoratedPanel draws
        // the background and the border itself: the background before DrawContent,
        // the border in DrawOverlay
        base.DrawContent(g);

        for (int i = 0; i < Children.Count; i++)
        {
            Rectangle box = BoxRect(Children[i]);
            bool isChecked = _checked.Contains(i);

            g.FillRoundRectangle(box, new CornerRadius(3f), isChecked ? CheckColor : BoxBackground);
            g.DrawRoundRectangle(box, new CornerRadius(3f), isChecked ? CheckColor : BoxBorderColor, 1.4f);

            if (!isChecked) continue;

            ReadOnlySpan<Point> check =
            [
                new(box.X + box.Width * 0.22f, box.Y + box.Height * 0.52f),
                new(box.X + box.Width * 0.42f, box.Y + box.Height * 0.72f),
                new(box.X + box.Width * 0.78f, box.Y + box.Height * 0.30f),
            ];

            g.DrawPolyline(check, Colors.White, box.Width * 0.14f);
        }
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        Point abs = GetAbsolutePosition();
        float localX = e.Location.X - abs.X;
        float localY = e.Location.Y - abs.Y;

        for (int i = 0; i < Children.Count; i++)
        {
            UIElement container = Children[i];

            if (localY < container.Position.Y ||
                localY >= container.Position.Y + container.ActualSize.Height)
            {
                continue;
            }

            Rectangle box = BoxRect(container);
            bool inBox = localX >= box.X && localX <= box.X + box.Width;

            if (inBox || ToggleOnRowClick)
                ToggleChecked(i);

            e.Handled = true;
            return;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // space toggles the check of the current row
        if (e.Key == Key.Space && SelectedIndex >= 0)
        {
            ToggleChecked(SelectedIndex);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}