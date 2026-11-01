using System.Collections.Specialized;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Forms.Markup;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class ComboBox : InteractiveControl
{
    private const float ArrowWidth = 20f;

    private readonly FlyoutHost _flyout;
    private int _selectedIndex = -1;

    /// <summary>The items. An item added or removed shows at once, and the selected one
    /// stays selected wherever it moves: since 0.20 a <see cref="ZfCollection{T}"/>, it
    /// was a list.</summary>
    public ZfCollection<object> Items
    {
        get => _items;
        init => ZfCollectionOwner.Replace(ref _items, value, OnItemsChanged);
    }

    private ZfCollection<object> _items = [];

    /// <summary>The selected item itself: the selection follows it through a change of
    /// <see cref="Items"/>, not its index.</summary>
    private object? _selected;

    /// <summary>How to show an item. ToString() by default.</summary>
    public Func<object, string>? DisplaySelector
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the width is computed from the displayed texts
            Invalidate();
        }
    }

    [SetAfterContent]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = value < 0 || value >= Items.Count ? -1 : value;
            if (_selectedIndex == clamped) return;

            _selectedIndex = clamped;
            _selected = clamped >= 0 ? Items[clamped] : null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    [SetAfterContent]
    public object? SelectedItem
    {
        get => _selectedIndex >= 0 ? Items[_selectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.IndexOf(value);
    }

    public event EventHandler? SelectionChanged;

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

    public float DropDownHeight { get; set; } = 180f;

    [Styled(Category = "Text")]
    public partial Color PlaceholderColor { get; set; }
    private static Color PlaceholderColorDefault => new(255, 160, 160, 160);

    protected override bool IsKeyActivatable => true;

    public ComboBox()
    {
        _items.CollectionChanged += OnItemsChanged;

        SetControlDefault(BackgroundProperty, Colors.White);
        SetControlDefault(PaddingProperty, new(6, 3));
        Cursor = CursorKind.Hand;
        SetControlDefault(BorderWidthProperty, 1f);

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    /// <summary>Whether the drop-down is open — for the accessibility peer,
    /// which reports it as the expanded state.</summary>
    internal bool IsDropDownOpen => _flyout.IsOpen;

    /// <summary>The items changed: the selected one keeps the selection at its new place;
    /// taken out, the selection goes — and that is a change of it. The width is
    /// measured from the texts again.</summary>
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // a data source in the middle of writing the items: the pick is put back once it
        // is done, see OnDataSourceChanged — a move is a remove and an insert, and the
        // pick would be lost between them
        if (_pickRemembered)
        {
            Invalidate();
            return;
        }

        if (_selectedIndex >= 0)
        {
            int moved = IndexOfInstance(_selected);

            if (moved >= 0)
            {
                _selectedIndex = moved;
            }
            else
            {
                _selectedIndex = -1;
                _selected = null;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        Invalidate();
    }

    /// <summary>Where this very item is — by reference, as the data source finds it: a
    /// record's Equals must not hand the selection to another record.</summary>
    private int IndexOfInstance(object? item)
    {
        if (item is null) return -1;

        for (int i = 0; i < Items.Count; i++)
            if (ReferenceEquals(Items[i], item)) return i;

        return -1;
    }

    private string TextOf(object item) => DisplaySelector?.Invoke(item) ?? item?.ToString() ?? string.Empty;

    // the focused border is InteractiveControl's business through FocusBorderColor
    protected override void DrawContent(Graphics g)
    {
        var content = ContentBounds;

        var textArea = new Rectangle(
            content.Position,
            new Size(Math.Max(0, content.Width - ArrowWidth), content.Height));

        if (SelectedItem is object item)
            g.DrawTextLine(TextOf(item), textArea, TextColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        else if (!string.IsNullOrEmpty(PlaceholderText))
            g.DrawTextLine(PlaceholderText, textArea, PlaceholderColor, EffectiveFont,
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
        var list = new ListBox
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            OverflowY = Overflow.Auto,
        };

        foreach (object item in Items)
            list.Items.Add(TextOf(item));

        list.SelectedIndex = _selectedIndex;

        list.SelectionChanged += (_, _) =>
        {
            SelectedIndex = list.SelectedIndex;
            _flyout.Close();
        };

        // first learn how much the list needs, and only then limit it:
        // with three items there must be no empty space left
        list.Measure(new Size(ActualSize.Width, float.PositiveInfinity));

        float height = Math.Min(list.DesiredSize.Height, DropDownHeight);

        list.Size = new Size(ActualSize.Width, height);

        return list;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                SelectedIndex = Math.Max(0, _selectedIndex - 1);
                e.Handled = true;
                break;

            case Key.Down:
                SelectedIndex = Math.Min(Items.Count - 1, _selectedIndex + 1);
                e.Handled = true;
                break;

            case Key.Escape when _flyout.IsOpen:
                _flyout.Close();
                e.Handled = true;
                break;

            default:
                base.OnKeyDown(e);   // space/Enter open the list
                break;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_flyout.IsOpen) return;   // scrolling inside the list matters more

        // the form delivers the wheel to disabled elements too
        if (!IsEnabled) return;

        // an empty list has nothing to choose from: Math.Clamp with a maximum
        // of -1 threw here and brought the application down on a plain scroll
        if (Items.Count == 0) return;

        SelectedIndex = Math.Clamp(_selectedIndex - Math.Sign(e.Delta), 0, Items.Count - 1);
        e.Handled = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float widest = 0;

        foreach (object item in Items)
            widest = Math.Max(widest, TextMeasurer.Current.MeasureText(TextOf(item), EffectiveFont).Width);

        if (!string.IsNullOrEmpty(PlaceholderText))
            widest = Math.Max(widest, TextMeasurer.Current.MeasureText(PlaceholderText, EffectiveFont).Width);

        return ResolveSize(
            new Size(widest + ArrowWidth + Padding.Horizontal + 8, TextMeasurer.LineHeight(EffectiveFont) + Padding.Vertical + 6),
            availableSize);
    }
}