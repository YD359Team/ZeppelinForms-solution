using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Показывает часть содержимого и прокручивает его.
/// Технически панель: содержимое и полоса прокрутки — обычные Children,
/// поэтому хит-тестинг, клип и рендер работают без правок.
/// </summary>
public class ScrollViewer : PanelControl
{
    private readonly ScrollBar _verticalScrollBar = new();
    private UIElement? _content;
    private bool _syncing;

    public float ScrollOffset { get; private set; }
    public float WheelStep { get; set; } = 40f;

    public UIElement? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value)) return;

            if (_content is not null)
                Children.Remove(_content);

            _content = value;

            if (value is not null)
                Children.Insert(0, value);   // полоса прокрутки всегда последняя

            Invalidate();
        }
    }

    public ScrollViewer()
    {
        Children.Add(_verticalScrollBar);
        _verticalScrollBar.ValueChanged += OnScrollBarValueChanged;
    }

    private void OnScrollBarValueChanged(object? sender, EventArgs e)
    {
        if (_syncing) return;

        ScrollOffset = _verticalScrollBar.Value;
        Invalidate();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        _verticalScrollBar.Measure(inner);
        float barWidth = _verticalScrollBar.DesiredSize.Width;

        // содержимое меряем без ограничения по высоте — пусть покажет,
        // сколько ему нужно на самом деле; лишнее уйдёт под прокрутку
        _content?.Measure(new Size(Math.Max(0, inner.Width - barWidth), float.PositiveInfinity));

        var content = new Size(
            (_content?.DesiredSize.Width ?? 0) + barWidth + Padding.Horizontal,
            (_content?.DesiredSize.Height ?? 0) + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var area = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical)));

        float barWidth = _verticalScrollBar.DesiredSize.Width;
        float viewportWidth = Math.Max(0, area.Width - barWidth);
        float contentHeight = _content?.DesiredSize.Height ?? 0;

        _syncing = true;
        _verticalScrollBar.ContentSize = contentHeight;
        _verticalScrollBar.ViewportSize = area.Height;
        ScrollOffset = Math.Clamp(ScrollOffset, 0, _verticalScrollBar.MaxValue);
        _verticalScrollBar.Value = ScrollOffset;
        _syncing = false;

        _content?.Arrange(new Rectangle(
            new Point(area.X, area.Y - ScrollOffset),
            new Size(viewportWidth, contentHeight)));

        _verticalScrollBar.Arrange(new Rectangle(
            new Point(area.X + viewportWidth, area.Y),
            new Size(barWidth, area.Height)));

        return finalSize;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        float before = ScrollOffset;

        ScrollOffset = Math.Clamp(
            ScrollOffset - e.Delta / 120f * WheelStep, 0, _verticalScrollBar.MaxValue);

        if (Math.Abs(before - ScrollOffset) > 0.01f)
        {
            e.Handled = true;
            Invalidate();
        }
    }

    public override void Draw(Graphics g)
    {
        // ...
    }
}