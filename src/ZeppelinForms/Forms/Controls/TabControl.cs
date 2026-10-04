using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class TabControl : DecoratedPanel, IInputElement
{
    private const float HeaderPaddingX = 14f;
    private const float HeaderPaddingY = 8f;
    private const float IconSize = 14f;
    private const float IconGap = 6f;

    private int _selectedIndex = -1;
    private int _hoveredIndex = -1;

    public List<TabItem> Tabs { get; init; } = [];

    public TabStripPlacement TabStripPlacement
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the strip moves to another side, and the content area with it
            Invalidate();
        }
    } = TabStripPlacement.Top;

    /// <summary>The width of the tab strip in the vertical placement.</summary>
    public float VerticalStripWidth
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 140f;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = value < 0 || value >= Tabs.Count ? -1 : value;
            if (_selectedIndex == clamped) return;

            _selectedIndex = clamped;
            SwapContent();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    // Tabs is a plain list and may shrink under a stale index:
    // indexing it directly threw ArgumentOutOfRangeException
    public TabItem? SelectedTab =>
        _selectedIndex >= 0 && _selectedIndex < Tabs.Count ? Tabs[_selectedIndex] : null;

    public event EventHandler? SelectionChanged;

    [Styled(Category = "Headers")]
    public partial Color HeaderColor { get; set; }
    private static Color HeaderColorDefault => new(255, 244, 244, 244);

    [Styled(Category = "Headers")]
    public partial Color HeaderHoverColor { get; set; }
    private static Color HeaderHoverColorDefault => new(255, 234, 234, 234);

    [Styled(Category = "Headers")]
    public partial Color SelectedHeaderColor { get; set; }
    private static Color SelectedHeaderColorDefault => Colors.White;

    [Styled(Category = "Headers")]
    public partial Color AccentColor { get; set; }
    private static Color AccentColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "States")]
    public partial Color DisabledTextColor { get; set; }
    private static Color DisabledTextColorDefault => new(255, 165, 165, 165);

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    protected override bool IsKeyActivatable => false;

    public TabControl()
    {
        SetControlDefault(BorderColorProperty, new Color(255, 205, 205, 205));
        SetControlDefault(BorderWidthProperty, 1f);
    }

    private bool IsVertical =>
        TabStripPlacement is TabStripPlacement.Left or TabStripPlacement.Right;

    /// <summary>Show the first tab if no choice has been made yet.</summary>
    public void EnsureSelection()
    {
        if (_selectedIndex < 0 && Tabs.Count > 0)
            SelectedIndex = 0;
    }

    private void SwapContent()
    {
        // only the active tab's content is kept in the tree: the others
        // must neither be measured nor catch events
        while (Children.Count > 0)
            Children.RemoveAt(Children.Count - 1);

        if (SelectedTab?.Content is UIElement content)
            Children.Add(content);
    }

    // ===== tab strip geometry =====

    private float HeaderExtent(TabItem tab)
    {
        Size text = string.IsNullOrEmpty(tab.Header)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(ApplyTextTransform(tab.Header), EffectiveFont);

        if (IsVertical)
            return Math.Max(text.Height, IconSize) + HeaderPaddingY * 2;

        float width = text.Width + HeaderPaddingX * 2;

        if (!string.IsNullOrEmpty(tab.PathData))
            width += IconSize + IconGap;

        return width;
    }

    private float StripThickness
    {
        get
        {
            if (IsVertical) return VerticalStripWidth;

            Size probe = TextMeasurer.Current.MeasureText("Wg", EffectiveFont);
            return probe.Height + HeaderPaddingY * 2;
        }
    }

    // internal for the accessibility peer: tabs are not elements, and their
    // bounds come from here
    internal Rectangle HeaderRect(int index)
    {
        float offset = 0;

        for (int i = 0; i < index; i++)
            offset += HeaderExtent(Tabs[i]);

        float extent = HeaderExtent(Tabs[index]);
        float thickness = StripThickness;

        return TabStripPlacement switch
        {
            TabStripPlacement.Top => new Rectangle(new Point(offset, 0), new Size(extent, thickness)),

            TabStripPlacement.Bottom => new Rectangle(
                new Point(offset, ActualSize.Height - thickness), new Size(extent, thickness)),

            TabStripPlacement.Left => new Rectangle(new Point(0, offset), new Size(thickness, extent)),

            _ => new Rectangle(
                new Point(ActualSize.Width - thickness, offset), new Size(thickness, extent)),
        };
    }

    private Rectangle ContentArea(Size total)
    {
        float thickness = StripThickness;

        return TabStripPlacement switch
        {
            TabStripPlacement.Top => new Rectangle(
                new Point(0, thickness), new Size(total.Width, Math.Max(0, total.Height - thickness))),

            TabStripPlacement.Bottom => new Rectangle(
                Point.Empty, new Size(total.Width, Math.Max(0, total.Height - thickness))),

            TabStripPlacement.Left => new Rectangle(
                new Point(thickness, 0), new Size(Math.Max(0, total.Width - thickness), total.Height)),

            _ => new Rectangle(
                Point.Empty, new Size(Math.Max(0, total.Width - thickness), total.Height)),
        };
    }

    // ===== drawing =====

    // the border is drawn by us around the content area, not along the control's bounds
    protected override Color CurrentBorderColor => Colors.Transparent;

    protected override void DrawContent(Graphics g)
    {
        for (int i = 0; i < Tabs.Count; i++)
        {
            TabItem tab = Tabs[i];
            Rectangle rect = HeaderRect(i);

            bool selected = i == _selectedIndex;

            Color fill = selected
                ? SelectedHeaderColor
                : (i == _hoveredIndex && tab.IsEnabled ? HeaderHoverColor : HeaderColor);

            g.FillRectangle(rect, fill);

            if (selected)
                DrawSelectionMarker(g, rect);

            Color textColor = tab.IsEnabled ? TextColor : DisabledTextColor;
            float textX = rect.X + HeaderPaddingX;

            if (!string.IsNullOrEmpty(tab.PathData))
            {
                var icon = new Rectangle(
                    new Point(textX, rect.Y + (rect.Height - IconSize) / 2f),
                    new Size(IconSize, IconSize));

                g.DrawSvgPath(tab.PathData, icon, textColor);
                textX += IconSize + IconGap;
            }

            var textRect = new Rectangle(
                new Point(textX, rect.Y),
                new Size(Math.Max(0, rect.X + rect.Width - textX - HeaderPaddingX), rect.Height));

            g.DrawText(ApplyTextTransform(tab.Header ?? string.Empty), textRect, textColor, EffectiveFont,
                IsVertical ? HorizontalContentAlignment.Left : HorizontalContentAlignment.Center,
                VerticalContentAlignment.Center);
        }
    }

    protected override void DrawDecoration(Graphics g)
    {
        if (BorderWidth <= 0 || BorderColor.A == 0) return;

        g.DrawRectangle(ContentArea(ActualSize), BorderColor, BorderWidth);
    }

    private void DrawSelectionMarker(Graphics g, Rectangle rect)
    {
        const float thickness = 3f;

        // the accent stripe on the content side — that way it's visible
        // which tab "adjoins" the panel
        Rectangle marker = TabStripPlacement switch
        {
            TabStripPlacement.Top => new Rectangle(
                new Point(rect.X, rect.Y + rect.Height - thickness), new Size(rect.Width, thickness)),

            TabStripPlacement.Bottom => new Rectangle(rect.Position, new Size(rect.Width, thickness)),

            TabStripPlacement.Left => new Rectangle(
                new Point(rect.X + rect.Width - thickness, rect.Y), new Size(thickness, rect.Height)),

            _ => new Rectangle(rect.Position, new Size(thickness, rect.Height)),
        };

        g.FillRectangle(marker, AccentColor);
    }

    // ===== input =====

    private int IndexFromPoint(Point location)
    {
        Point abs = GetAbsolutePosition();
        var local = new Point(location.X - abs.X, location.Y - abs.Y);

        for (int i = 0; i < Tabs.Count; i++)
        {
            Rectangle rect = HeaderRect(i);

            if (local.X >= rect.X && local.X < rect.X + rect.Width &&
                local.Y >= rect.Y && local.Y < rect.Y + rect.Height)
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        int index = IndexFromPoint(e.Location);
        if (index == _hoveredIndex) return;

        _hoveredIndex = index;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        _hoveredIndex = -1;
        InvalidateVisual();
    }

    /// <summary>A tab is selected on press: the header's content
    /// must not intercept the switch.</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        // the preview reaches the tab control even when the hit is an enabled
        // element inside it, so the form's check for a disabled hit doesn't apply
        if (!IsEnabled) return;

        int index = IndexFromPoint(e.Location);

        if (index >= 0 && Tabs[index].IsEnabled)
            SelectedIndex = index;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+Tab and Ctrl+PageDown from anywhere inside: the key bubbles up from
        // the focused element, so the innermost tab control takes it. Wrapping
        // around, as browsers and every tabbed editor do
        if (e.Modifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.Tab or Key.PageDown or Key.PageUp)
        {
            bool back = e.Key == Key.PageUp || (e.Key == Key.Tab && e.Modifiers.HasFlag(KeyModifiers.Shift));

            if (SelectNextEnabled(back ? -1 : 1, wrap: true))
            {
                // the focused element left with its page: the strip takes the focus,
                // so the next Ctrl+Tab still has somewhere to come from
                FindOwner()?.FocusForAccessibility(this);
                e.Handled = true;
            }

            return;
        }

        // the arrows only while the strip itself has the focus: they bubbled up
        // here from a button inside a page as well, and switched the tab under it
        if (!IsFocused) return;

        bool forward = IsVertical ? e.Key == Key.Down : e.Key == Key.Right;
        bool backward = IsVertical ? e.Key == Key.Up : e.Key == Key.Left;

        if (!forward && !backward) return;

        // skip disabled tabs, otherwise the arrow would "get stuck"
        if (SelectNextEnabled(forward ? 1 : -1, wrap: false))
            e.Handled = true;
    }

    /// <summary>Select the next enabled tab in the direction of <paramref name="step"/>.
    /// False — there is none.</summary>
    private bool SelectNextEnabled(int step, bool wrap)
    {
        for (int n = 1; n < Tabs.Count; n++)
        {
            int i = _selectedIndex + step * n;

            if (wrap) i = (i % Tabs.Count + Tabs.Count) % Tabs.Count;
            else if (i < 0 || i >= Tabs.Count) return false;

            if (Tabs[i].IsEnabled)
            {
                SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    // ===== layout =====

    protected override Size MeasureContentOverride(Size availableSize)
    {
        EnsureSelection();

        float stripExtent = 0;

        foreach (TabItem tab in Tabs)
            stripExtent += HeaderExtent(tab);

        float thickness = StripThickness;

        var contentAvailable = IsVertical
            ? new Size(Math.Max(0, availableSize.Width - thickness), availableSize.Height)
            : new Size(availableSize.Width, Math.Max(0, availableSize.Height - thickness));

        Size contentDesired = Size.Empty;

        if (Children.Count > 0)
        {
            Children[0].Measure(contentAvailable);
            contentDesired = Children[0].DesiredSize;
        }

        Size content = IsVertical
            ? new Size(contentDesired.Width + thickness, Math.Max(contentDesired.Height, stripExtent))
            : new Size(Math.Max(contentDesired.Width, stripExtent), contentDesired.Height + thickness);

        return ResolveSize(content, availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        if (Children.Count == 0) return;

        Rectangle area = ContentArea(contentSize);

        Children[0].Arrange(new Rectangle(
            new Point(area.X + BorderWidth, area.Y + BorderWidth),
            new Size(
                Math.Max(0, area.Width - BorderWidth * 2),
                Math.Max(0, area.Height - BorderWidth * 2))));
    }
}