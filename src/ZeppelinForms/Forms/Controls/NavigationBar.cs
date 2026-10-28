using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// A destination of a <see cref="NavigationBar"/>: an icon, a label, and what the
/// <see cref="Scaffold"/> around the bar shows when it is chosen.
/// </summary>
/// <remarks>
/// The content is given ready, as <see cref="Content"/>, or made on demand, as
/// <see cref="ContentFactory"/>: the factory runs when the destination is chosen for the
/// first time, and what it made is kept — coming back finds it as it was left. A
/// <see cref="Navigation.NavigationHost"/> as the content gives the destination a stack of
/// its own, as the tabs of a phone application have.
/// </remarks>
public sealed class NavigationBarItem
{
    internal NavigationBar? Owner { get; set; }

    /// <summary>The content the factory made, kept for the next visit.</summary>
    internal UIElement? MadeContent { get; set; }

    public NavigationBarItem() { }

    public NavigationBarItem(string text, IconSource? icon = null, Func<UIElement>? content = null)
    {
        Text = text;
        Icon = icon;
        ContentFactory = content;
    }

    public string Text
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.OnItemChanged();
        }
    } = string.Empty;

    public IconSource? Icon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.OnItemChanged();
        }
    }

    /// <summary>The icon while the destination is chosen — a filled one for an outlined
    /// <see cref="Icon"/>. Null — <see cref="Icon"/>.</summary>
    public IconSource? SelectedIcon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.OnItemChanged();
        }
    }

    /// <summary>A mark on the icon: a count of new messages; an empty string — a dot.
    /// Null — none.</summary>
    public string? Badge
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.OnItemChanged();
        }
    }

    public bool IsEnabled
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.OnItemChanged();
        }
    } = true;

    /// <summary>The content a <see cref="Scaffold"/> shows for the destination, ready-made.</summary>
    public UIElement? Content { get; set; }

    /// <summary>Makes the content when the destination is chosen for the first time; what
    /// it made is kept. <see cref="Content"/>, when set, is used instead.</summary>
    public Func<UIElement>? ContentFactory { get; set; }

    /// <summary>Whatever the application ties to the destination.</summary>
    public object? Tag { get; set; }

    internal UIElement? ResolveContent() => Content ?? (MadeContent ??= ContentFactory?.Invoke());
}

/// <summary>The selection of a <see cref="NavigationBar"/> changed.</summary>
public sealed class NavigationBarSelectionChangedEventArgs(NavigationBarItem? oldItem, NavigationBarItem? newItem) : EventArgs
{
    public NavigationBarItem? OldItem { get; } = oldItem;

    public NavigationBarItem? NewItem { get; } = newItem;
}

/// <summary>
/// The bar across the bottom of a phone application: three to five destinations, each an
/// icon with a label under it, the chosen one marked by a pill behind its icon. Inside a
/// <see cref="Scaffold"/> it switches the scaffold's body.
/// </summary>
/// <remarks>
/// Its background runs down under the gesture bar of a form that draws edge to edge, and
/// its destinations keep above it: <see cref="Form.SafeAreaInsets"/>' bottom. Choosing the
/// chosen destination again raises <see cref="Reselected"/>: a scaffold takes its stack
/// back to the root then, as phone applications do.
/// </remarks>
public partial class NavigationBar : DecoratedPanel
{
    private Form? _form;

    public NavigationBar()
    {
        Items.CollectionChanged += OnItemsChanged;

        AccessibleRole = AccessibilityRole.TabList;

        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Bottom);
    }

    public ObservableCollection<NavigationBarItem> Items { get; } = [];

    /// <summary>The chosen destination's index; -1 — none.</summary>
    public int SelectedIndex
    {
        get;
        set
        {
            int index = value >= 0 && value < Items.Count ? value : -1;

            if (index == field) return;

            NavigationBarItem? old = SelectedItem;
            field = index;

            Sync();
            SelectionChanged?.Invoke(this, new NavigationBarSelectionChangedEventArgs(old, SelectedItem));
        }
    } = -1;

    public NavigationBarItem? SelectedItem
    {
        get => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.IndexOf(value);
    }

    public event EventHandler<NavigationBarSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>The chosen destination was chosen again.</summary>
    public event EventHandler<NavigationBarItem>? Reselected;

    /// <summary>Whether the bar keeps above the bottom of <see cref="Form.SafeAreaInsets"/>.</summary>
    public bool UsesSafeArea
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = true;

    /// <summary>The height of the row of destinations, without the safe area under it.</summary>
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial float BarHeight { get; set; }
    private static float BarHeightDefault => 72f;

    /// <summary>The pill behind the chosen destination's icon.</summary>
    [Styled(Category = "Selection")]
    public partial Color IndicatorColor { get; set; }
    private static Color IndicatorColorDefault => new(60, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Selection")]
    public partial Color SelectedColor { get; set; }
    private static Color SelectedColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    /// <summary>The icons and labels of the destinations not chosen.</summary>
    [Styled(Category = "Appearance")]
    public partial Color ItemColor { get; set; }
    private static Color ItemColorDefault => new(255, 0x6C, 0x75, 0x7D);

    [Styled(Category = "Appearance")]
    public partial Color DisabledColor { get; set; }
    private static Color DisabledColorDefault => new(255, 0xA8, 0xA8, 0xA8);

    [Styled(Category = "Badge")]
    public partial Color BadgeColor { get; set; }
    private static Color BadgeColorDefault => new(255, 0xDC, 0x35, 0x45);

    [Styled(Category = "Badge")]
    public partial Color BadgeTextColor { get; set; }
    private static Color BadgeTextColorDefault => Colors.White;

    /// <summary>The line along the bar's top edge. Transparent — none.</summary>
    [Styled(Category = "Appearance")]
    public partial Color DividerColor { get; set; }
    private static Color DividerColorDefault => Colors.Transparent;

    // ===== the destinations =====

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (NavigationBarItem item in e.OldItems) item.Owner = null;

        if (e.NewItems is not null)
            foreach (NavigationBarItem item in e.NewItems) item.Owner = this;

        // the first destination is chosen as soon as there is one
        if (SelectedIndex < 0 && Items.Count > 0) SelectedIndex = 0;
        else if (SelectedIndex >= Items.Count) SelectedIndex = Items.Count - 1;

        Sync();
    }

    internal void OnItemChanged() => Sync();

    /// <summary>One button per destination.</summary>
    private void Sync()
    {
        while (Children.Count > Items.Count) Children.RemoveAt(Children.Count - 1);
        while (Children.Count < Items.Count) Children.Add(new NavigationBarButton(this));

        for (int i = 0; i < Items.Count; i++)
        {
            var button = (NavigationBarButton)Children[i];
            button.Item = Items[i];
            button.IsSelected = i == SelectedIndex;
            button.IsEnabled = Items[i].IsEnabled;
            button.AccessibleName = Items[i].Text;
            button.Invalidate();
        }

        Invalidate();
    }

    internal void Choose(NavigationBarItem item)
    {
        if (!item.IsEnabled) return;

        if (ReferenceEquals(item, SelectedItem))
        {
            Reselected?.Invoke(this, item);
            return;
        }

        SelectedItem = item;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // the arrows move between the destinations, as across tabs
        int step = e.Key switch
        {
            Key.Right => IsRightToLeft ? -1 : 1,
            Key.Left => IsRightToLeft ? 1 : -1,
            _ => 0,
        };

        if (step != 0 && Items.Count > 0)
        {
            for (int i = 1; i <= Items.Count; i++)
            {
                int index = ((SelectedIndex < 0 ? 0 : SelectedIndex) + step * i + Items.Count * i) % Items.Count;

                if (!Items[index].IsEnabled) continue;

                SelectedIndex = index;
                FindOwner()?.FocusElement(Children[index]);
                break;
            }

            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    // ===== the safe area =====

    protected override void OnAttached()
    {
        base.OnAttached();

        _form = FindOwner();

        if (_form is not null) _form.SafeAreaInsetsChanged += OnInsetsChanged;
    }

    protected override void OnDetached()
    {
        base.OnDetached();

        if (_form is not null) _form.SafeAreaInsetsChanged -= OnInsetsChanged;

        _form = null;
    }

    private void OnInsetsChanged(object? sender, EventArgs e) => Invalidate();

    private Thickness Insets => UsesSafeArea && _form is not null
        ? SafeArea.Pick(_form.SafeAreaInsets, SafeAreaEdges.Left | SafeAreaEdges.Bottom | SafeAreaEdges.Right)
        : Thickness.Zero;

    // ===== layout =====

    protected override Size MeasureContentOverride(Size availableSize)
    {
        Thickness inset = Insets;
        float row = BarHeight;

        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(1, Items.Count) * 96f;
        float each = Items.Count > 0 ? (width - inset.Horizontal - Padding.Horizontal) / Items.Count : 0f;

        foreach (UIElement child in Children)
            child.Measure(new Size(Math.Max(0, each), row));

        return ResolveSize(new Size(width, row + inset.Bottom + Padding.Vertical), availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        Thickness inset = Insets;

        float left = inset.Left + Padding.Left;
        float width = Math.Max(0, contentSize.Width - inset.Horizontal - Padding.Horizontal);
        float each = Children.Count > 0 ? width / Children.Count : 0f;
        float top = Padding.Top;

        for (int i = 0; i < Children.Count; i++)
        {
            // the order of reading: from the right in a right-to-left layout
            int slot = IsRightToLeft ? Children.Count - 1 - i : i;

            Children[i].Arrange(new Rectangle(new Point(left + slot * each, top), new Size(each, BarHeight)));
        }
    }

    protected override void DrawDecoration(Graphics g)
    {
        Color divider = DividerColor;

        if (divider.A > 0 && ActualSize.Width > 0)
            g.FillRectangle(new Rectangle(Point.Empty, new Size(ActualSize.Width, 1f)), divider);
    }
}

/// <summary>One destination of a <see cref="NavigationBar"/>: the pill, the icon, the
/// label and the badge.</summary>
internal sealed class NavigationBarButton : InteractiveControl
{
    private readonly NavigationBar _bar;

    public NavigationBarButton(NavigationBar bar)
    {
        _bar = bar;

        AccessibleRole = AccessibilityRole.Tab;
        Cursor = CursorKind.Hand;
    }

    public NavigationBarItem? Item { get; set; }

    public bool IsSelected
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            SetPseudoClass(PseudoClass.Selected, value);
        }
    }

    private const float IconSize = 24f;
    private const float PillWidth = 56f;
    private const float PillHeight = 32f;

    protected override void DrawContent(Graphics g)
    {
        if (Item is not { } item) return;

        Rectangle bounds = LocalBounds;

        Color color = !item.IsEnabled ? _bar.DisabledColor : IsSelected ? _bar.SelectedColor : _bar.ItemColor;

        Size label = string.IsNullOrEmpty(item.Text) ? Size.Empty : TextMeasurer.MeasureLine(item.Text, EffectiveFont);
        float gap = label.Height > 0 ? 4f : 0f;
        float column = PillHeight + gap + label.Height;
        float top = bounds.Y + (bounds.Height - column) / 2f;

        var pill = new Rectangle(
            new Point(bounds.X + (bounds.Width - PillWidth) / 2f, top),
            new Size(PillWidth, PillHeight));

        // the pill behind the chosen icon; hovered and pressed shades on the others
        Color fill = IsSelected ? _bar.IndicatorColor
            : IsPressed ? WithAlpha(_bar.IndicatorColor, 0.6f)
            : IsHovered && item.IsEnabled ? WithAlpha(_bar.IndicatorColor, 0.35f)
            : Colors.Transparent;

        if (fill.A > 0) g.FillRoundRectangle(pill, new CornerRadius(PillHeight / 2f), fill);

        var icon = new Rectangle(
            new Point(pill.X + (pill.Width - IconSize) / 2f, pill.Y + (pill.Height - IconSize) / 2f),
            new Size(IconSize, IconSize));

        IconSource? glyph = IsSelected ? item.SelectedIcon ?? item.Icon : item.Icon;
        glyph?.Draw(g, icon, color);

        if (label.Height > 0)
        {
            var area = new Rectangle(new Point(bounds.X + 2f, pill.Bottom + gap), new Size(Math.Max(0, bounds.Width - 4f), label.Height));
            g.DrawText(item.Text, area, color, EffectiveFont);
        }

        if (item.Badge is { } badge) DrawBadge(g, icon, badge);

        DrawFocusRing(g, pill.Inflate(2f), new CornerRadius(PillHeight / 2f + 2f), _bar.SelectedColor);
    }

    private void DrawBadge(Graphics g, Rectangle icon, string badge)
    {
        if (badge.Length == 0)
        {
            // a dot at the icon's corner
            const float dot = 8f;
            g.FillEllipse(new Rectangle(new Point(icon.Right - dot / 2f, icon.Y - 1f), new Size(dot, dot)), _bar.BadgeColor);
            return;
        }

        Font font = EffectiveFont with { Size = Math.Max(9f, EffectiveFont.Size - 3f) };
        Size text = TextMeasurer.MeasureLine(badge, font);

        float height = Math.Max(16f, text.Height + 2f);
        float width = Math.Max(height, text.Width + 8f);

        var rect = new Rectangle(new Point(icon.Right - height / 2f, icon.Y - height / 3f), new Size(width, height));

        g.FillRoundRectangle(rect, new CornerRadius(height / 2f), _bar.BadgeColor);
        g.DrawText(badge, rect, _bar.BadgeTextColor, font);
    }

    private static Color WithAlpha(Color color, float factor) =>
        new((byte)Math.Round(color.A * factor), color.R, color.G, color.B);

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (Item is { } item) _bar.Choose(item);

        e.Handled = true;
    }

    protected internal override string? GetToolTip(Point location) => null;

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(
            float.IsFinite(availableSize.Width) ? availableSize.Width : 80f,
            float.IsFinite(availableSize.Height) ? availableSize.Height : _bar.BarHeight), availableSize);
}
