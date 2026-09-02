using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class CheckedListBox : ListBox
{
    private const float BoxSize = 15f;
    private const float BoxGap = 6f;

    private readonly HashSet<int> _checked = [];

    public Color BoxBorderColor { get; set; } = new Color(255, 120, 120, 120);
    public Color BoxBackground { get; set; } = Colors.White;
    public Color CheckColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);

    /// <summary>Отметка ставится по клику в любом месте строки, а не только по квадратику.</summary>
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
        // место под квадратик слева от содержимого строки
        Padding = new Thickness(BoxSize + BoxGap + 4f, 2f, 4f, 2f);
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
        // копия, потому что SetChecked меняет коллекцию во время обхода
        foreach (int index in _checked.ToArray())
            SetChecked(index, false);
    }

    private Rectangle BoxRect(UIElement container)
    {
        float y = container.Position.Y + (container.ActualSize.Height - BoxSize) / 2f;
        return new Rectangle(new Point(4f, y), new Size(BoxSize, BoxSize));
    }

    public override void Draw(Graphics g)
    {
        // фон, подсветка выбранной строки и рамка — из ListBox
        base.Draw(g);

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

    protected override void OnPreviewMouseDown(MouseClickEventArgs args)
    {
        base.OnPreviewMouseDown(args);   // выбор строки из ListBox
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // пробел переключает отметку текущей строки
        if (e.Key == Key.Space && SelectedIndex >= 0)
        {
            ToggleChecked(SelectedIndex);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}