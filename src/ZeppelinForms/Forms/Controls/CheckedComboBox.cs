using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class CheckedComboBox : UnitControl, IInputElement, IBorderedElement
{
    private const float ArrowWidth = 20f;

    private UIElement? _flyout;
    private CheckedListBox? _list;

    public List<object> Items { get; init; } = [];
    public Func<object, string>? DisplaySelector { get; set; }

    public string PlaceholderText { get; set; } = "Не выбрано";
    public float DropDownHeight { get; set; } = 200f;

    /// <summary>Со скольких отмеченных показывать «выбрано N» вместо перечисления.</summary>
    public int SummaryThreshold { get; set; } = 3;

    public event EventHandler? SelectionChanged;

    public Color TextColor { get; set; } = Colors.Black;
    public Color PlaceholderColor { get; set; } = new Color(255, 160, 160, 160);
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    protected override bool IsKeyActivatable => true;

    private readonly HashSet<int> _checked = [];

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
        Background = Colors.White;
        Padding = new Thickness(6, 3);
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

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRoundRectangle(this.LocalBounds, CornerRadius, Background);

        if (BorderWidth > 0)
            g.DrawRoundRectangle(this.LocalBounds, CornerRadius,
                IsFocused ? LightThemeColors.ButtonFill : BorderColor, BorderWidth);

        var content = this.ContentBounds;

        var textArea = new Rectangle(
            content.Position, new Size(Math.Max(0, content.Width - ArrowWidth), content.Height));

        g.DrawText(DisplayText, textArea,
            _checked.Count == 0 ? PlaceholderColor : TextColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

        float cx = content.X + content.Width - ArrowWidth / 2f;
        float cy = content.Y + content.Height / 2f;

        ReadOnlySpan<Point> arrow =
        [
            new(cx - 4.5f, cy - 2f),
            new(cx, cy + 3f),
            new(cx + 4.5f, cy - 2f),
        ];

        g.DrawPolyline(arrow, TextColor, 1.6f);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        e.Handled = true;

        if (_flyout is not null)
        {
            Close();
            return;
        }

        Open();
    }

    private void Open()
    {
        Form? owner = FindOwner();
        if (owner is null || Items.Count == 0) return;

        _list = new CheckedListBox
        {
            ToggleOnRowClick = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        foreach (object item in Items)
            _list.Items.Add(TextOf(item));

        foreach (int index in _checked)
            _list.SetChecked(index, true);

        _list.ItemCheckedChanged += (_, index) =>
        {
            // флаут не закрываем: смысл контрола в том, чтобы отметить несколько
            if (_list!.IsChecked(index)) _checked.Add(index);
            else _checked.Remove(index);

            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        };

        _flyout = new Border
        {
            Background = Colors.White,
            BorderColor = new Color(255, 190, 190, 190),
            BorderWidth = 1,
            CornerRadius = new CornerRadius(4f),
            Child = new ScrollViewer
            {
                Content = _list,
                Size = new Size(ActualSize.Width, DropDownHeight),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            },
        };

        owner.ShowFlyout(this, _flyout, FlyoutPlacement.Bottom);
    }

    private void Close()
    {
        if (_flyout is null) return;

        FindOwner()?.CloseFlyout(_flyout);
        _flyout = null;
        _list = null;
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