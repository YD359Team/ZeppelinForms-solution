using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class TabControl : DecoratedPanel, IInputElement
{
    private const float HeaderPaddingX = 14f;
    private const float HeaderPaddingY = 8f;
    private const float IconSize = 14f;
    private const float IconGap = 6f;

    private int _selectedIndex = -1;
    private int _hoveredIndex = -1;

    public List<TabItem> Tabs { get; init; } = [];

    public TabStripPlacement TabStripPlacement { get; set; } = TabStripPlacement.Top;

    /// <summary>Ширина полосы вкладок при вертикальном расположении.</summary>
    public float VerticalStripWidth { get; set; } = 140f;

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

    public TabItem? SelectedTab => _selectedIndex >= 0 ? Tabs[_selectedIndex] : null;

    public event EventHandler? SelectionChanged;

    public Color HeaderColor { get; set; } = new Color(255, 244, 244, 244);
    public Color HeaderHoverColor { get; set; } = new Color(255, 234, 234, 234);
    public Color SelectedHeaderColor { get; set; } = Colors.White;
    public Color TextColor { get; set; } = Colors.Black;
    public Color DisabledTextColor { get; set; } = new Color(255, 165, 165, 165);
    public Color AccentColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    protected override bool IsKeyActivatable => false;

    public TabControl()
    {
        BorderColor = new Color(255, 205, 205, 205);
        BorderWidth = 1f;
    }

    private bool IsVertical =>
        TabStripPlacement is TabStripPlacement.Left or TabStripPlacement.Right;

    /// <summary>Показать первую вкладку, если выбор ещё не сделан.</summary>
    public void EnsureSelection()
    {
        if (_selectedIndex < 0 && Tabs.Count > 0)
            SelectedIndex = 0;
    }

    private void SwapContent()
    {
        // держим в дереве только содержимое активной вкладки: остальные
        // не должны ни измеряться, ни ловить события
        while (Children.Count > 0)
            Children.RemoveAt(Children.Count - 1);

        if (SelectedTab?.Content is UIElement content)
            Children.Add(content);
    }

    // ===== геометрия полосы вкладок =====

    private float HeaderExtent(TabItem tab)
    {
        Size text = string.IsNullOrEmpty(tab.Header)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(tab.Header, EffectiveFont);

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

    private Rectangle HeaderRect(int index)
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

    // ===== отрисовка =====

    // рамку рисуем сами вокруг области содержимого, а не по границам контрола
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

            g.DrawText(tab.Header, textRect, textColor, EffectiveFont,
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

        // полоска акцента со стороны содержимого — так видно,
        // какая вкладка «прилегает» к панели
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

    // ===== ввод =====

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

    /// <summary>Вкладка выбирается по нажатию: содержимое заголовка
    /// не должно перехватывать переключение.</summary>
    protected override void OnPreviewMouseDown(Point location)
    {
        int index = IndexFromPoint(location);

        if (index >= 0 && Tabs[index].IsEnabled)
            SelectedIndex = index;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool forward = IsVertical ? e.Key == Key.Down : e.Key == Key.Right;
        bool backward = IsVertical ? e.Key == Key.Up : e.Key == Key.Left;

        if (!forward && !backward) return;

        // пропускаем выключенные вкладки, иначе стрелка «застрянет»
        int step = forward ? 1 : -1;

        for (int i = _selectedIndex + step; i >= 0 && i < Tabs.Count; i += step)
        {
            if (Tabs[i].IsEnabled)
            {
                SelectedIndex = i;
                e.Handled = true;
                return;
            }
        }
    }

    // ===== раскладка =====

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