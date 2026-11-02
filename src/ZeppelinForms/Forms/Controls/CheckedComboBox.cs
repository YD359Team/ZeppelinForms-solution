using ZeppelinForms.Core.Globalization;
using System.Collections.Specialized;
using ZeppelinForms.Core.Collections;
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

    /// <summary>The items. An item added or removed shows at once, and the checked ones
    /// stay checked wherever they move: since 0.20 a <see cref="ZfCollection{T}"/>, it
    /// was a list.</summary>
    public ZfCollection<object> Items
    {
        get => _items;
        init => ZfCollectionOwner.Replace(ref _items, value, OnItemsChanged);
    }

    private ZfCollection<object> _items = [];

    /// <summary>The items as they were before the last change: the checked indices are
    /// read through it to the items they meant.</summary>
    private object[] _itemsBefore = [];
    public Func<object, string>? DisplaySelector { get; set; }

    /// <summary>Text when nothing is checked. Null — the localized default,
    /// resolved when drawn, so it follows the language.</summary>
    public string? PlaceholderText
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the width is computed from the placeholder too
            Invalidate();
        }
    }

    public float DropDownHeight { get; set; } = 200f;

    /// <summary>From how many checked items to show "N selected" instead of listing them.</summary>
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
        _items.CollectionChanged += OnItemsChanged;

        SetControlDefault(BackgroundProperty, Colors.White);
        SetControlDefault(PaddingProperty, new(6, 3));
        Cursor = CursorKind.Hand;
        SetControlDefault(BorderColorProperty, Colors.Black);
        SetControlDefault(BorderWidthProperty, 1f);

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    /// <summary>The items changed: the checked ones stay checked at their new places,
    /// the ones taken out are no longer checked — and that is a change of the selection.</summary>
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        object[] before = _itemsBefore;
        _itemsBefore = [.. Items];

        if (_checked.Count > 0)
        {
            int[] was = [.. _checked];
            _checked.Clear();

            // by reference: a record's Equals must not check another record
            foreach (int index in was)
                if (index < before.Length && IndexOfInstance(before[index]) is var now and >= 0)
                    _checked.Add(now);

            if (_checked.Count != was.Length)
                SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        Invalidate();
    }

    private int IndexOfInstance(object item)
    {
        for (int i = 0; i < Items.Count; i++)
            if (ReferenceEquals(Items[i], item)) return i;

        return -1;
    }

    private string TextOf(object item) => DisplaySelector?.Invoke(item) ?? item?.ToString() ?? string.Empty;

    private string DisplayText
    {
        get
        {
            if (_checked.Count == 0) return PlaceholderText ?? Localization.Get(ZfText.NothingSelected);

            if (_checked.Count >= SummaryThreshold)
                return Localization.Get(ZfText.SelectedCount, _checked.Count);

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

        g.DrawTextLine(DisplayText,
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
        // Space and Enter come here too, bypassing hit testing:
        // a disabled combo box must not open from the keyboard
        if (!IsEnabled) return;

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

        // the flyout stays open: the point of the control
        // is to check several items in a row
        list.ItemCheckedChanged += (_, index) => SetChecked(index, list.IsChecked(index));

        // the height is fitted to the content, but not above the limit —
        // with three items there must be no empty space left
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
        float widest = TextMeasurer.Current.MeasureText(PlaceholderText ?? Localization.Get(ZfText.NothingSelected), EffectiveFont).Width;

        foreach (object item in Items)
            widest = Math.Max(widest, TextMeasurer.Current.MeasureText(TextOf(item), EffectiveFont).Width);

        return ResolveSize(
            new Size(widest + ArrowWidth + Padding.Horizontal + 8, TextMeasurer.LineHeight(EffectiveFont) + Padding.Vertical + 6),
            availableSize);
    }
}