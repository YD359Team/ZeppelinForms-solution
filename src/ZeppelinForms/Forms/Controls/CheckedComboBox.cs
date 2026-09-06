using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class CheckedComboBox : InteractiveControl
{
    private const float ArrowWidth = 20f;

    private readonly FlyoutHost _flyout;
    private readonly HashSet<int> _checked = [];

    public List<object> Items { get; init; } = [];
    public Func<object, string>? DisplaySelector { get; set; }

    public string PlaceholderText { get; set; } = "Не выбрано";
    public float DropDownHeight { get; set; } = 200f;

    /// <summary>Со скольких отмеченных показывать «выбрано N» вместо перечисления.</summary>
    public int SummaryThreshold { get; set; } = 3;

    public event EventHandler? SelectionChanged;

    [Styled(Category = "Text")]
    public partial Color PlaceholderColor { get; set; }
    private static Color PlaceholderColorDefault => new(255, 160, 160, 160);

    public bool IsDropDownOpen => _flyout.IsOpen;

    public IEnumerable<object> CheckedItems
    {
        get
        {
            foreach (int index in _checked.Order())
                if (index >= 0 && index < Items.Count)
                    yield return Items[index];
        }
    }

    public CheckedComboBox()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        Padding = new Thickness(6, 3);
        Cursor = CursorKind.Hand;
        SetControlDefault(BorderColorProperty, Colors.Black);
        SetControlDefault(BorderWidthProperty, 1f);

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    private string TextOf(object item) => DisplaySelector?.Invoke(item) ?? item?.ToString() ?? string.Empty;

    private string DisplayText
    {
        get
        {
            if (_checked.Count == 0) return PlaceholderText;

            if (_checked.Count >= SummaryThreshold)
                return $"Выбрано: {_checked.Count}";

            return string.Join(", ", CheckedItems.Select(TextOf));
        }
    }

    public void SetChecked(int index, bool value)
    {
        if (index < 0 || index >= Items.Count) return;

        bool changed = value ? _checked.Add(index) : _checked.Remove(index);
        if (!changed) return;

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void UncheckAll()
    {
        if (_checked.Count == 0) return;

        _checked.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void DrawContent(Graphics g)
    {
        var content = ContentBounds;

        g.DrawText(DisplayText,
            new Rectangle(content.Position, new Size(Math.Max(0, content.Width - ArrowWidth), content.Height)),
            _checked.Count == 0 ? PlaceholderColor : TextColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

        float cx = content.X + content.Width - ArrowWidth / 2f;
        float cy = content.Y + content.Height / 2f;

        ReadOnlySpan<Point> arrow = _flyout.IsOpen
            ? [new(cx - 4.5f, cy + 2f), new(cx, cy - 3f), new(cx + 4.5f, cy + 2f)]
            : [new(cx - 4.5f, cy - 2f), new(cx, cy + 3f), new(cx + 4.5f, cy - 2f)];

        g.DrawPolyline(arrow, TextColor, 1.6f);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        e.Handled = true;

        if (Items.Count == 0) return;

        _flyout.Toggle(BuildDropDown);
        InvalidateVisual();
    }

    private UIElement BuildDropDown()
    {
        var list = new CheckedListBox
        {
            ToggleOnRowClick = true,
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        foreach (object item in Items)
            list.Items.Add(TextOf(item));

        foreach (int index in _checked)
            list.SetChecked(index, true);

        // флаут остаётся открытым: смысл контрола в том,
        // чтобы отметить несколько пунктов подряд
        list.ItemCheckedChanged += (_, index) => SetChecked(index, list.IsChecked(index));

        // высоту подгоняем под содержимое, но не выше предела —
        // при трёх пунктах не должно оставаться пустого места
        list.Measure(new Size(ActualSize.Width, float.PositiveInfinity));

        list.Size = new Size(ActualSize.Width, Math.Min(list.DesiredSize.Height, DropDownHeight));

        return list;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _flyout.IsOpen)
        {
            _flyout.Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float widest = TextMeasurer.Current.MeasureText(PlaceholderText, EffectiveFont).Width;

        foreach (object item in Items)
            widest = Math.Max(widest, TextMeasurer.Current.MeasureText(TextOf(item), EffectiveFont).Width);

        Size probe = TextMeasurer.Current.MeasureText("Wg", EffectiveFont);

        return ResolveSize(
            new Size(widest + ArrowWidth + Padding.Horizontal + 8, probe.Height + Padding.Vertical + 6),
            availableSize);
    }
}