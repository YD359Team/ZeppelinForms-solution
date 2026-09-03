using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Контейнер представлений с историей переходов. Скрытые страницы
/// остаются в дереве, но не рисуются и не получают события.
/// </summary>
public class PageControl : PanelControl
{
    private readonly List<string> _history = [];
    private Page? _current;
    private Page? _outgoing;

    private float _progress = 1f;

    public PageTransition Transition { get; set; } = PageTransition.Fade;
    public int TransitionDurationMs { get; set; } = 220;

    public Page? CurrentPage => _current;

    public bool CanGoBack => _history.Count > 1;

    public event EventHandler<Page>? Navigated;

    public override void Draw(Graphics g) { }

    /// <summary>Добавить страницу. Первая добавленная становится текущей.</summary>
    public Page AddPage(string name, Func<UIElement> factory, string? title = null)
    {
        var page = new Page
        {
            Name = name,
            Title = title ?? name,
            ContentFactory = factory,
            IsVisible = false,
        };

        Children.Add(page);

        if (_current is null)
            Navigate(name, PageTransition.None);

        return page;
    }

    public void Navigate(string name) => Navigate(name, Transition);

    public void Navigate(string name, PageTransition transition)
    {
        Page? target = FindPage(name);
        if (target is null || ReferenceEquals(target, _current)) return;

        _history.Add(name);
        Switch(target, transition);
    }

    public void GoBack()
    {
        if (!CanGoBack) return;

        _history.RemoveAt(_history.Count - 1);

        Page? target = FindPage(_history[^1]);
        if (target is null) return;

        // назад — зеркальный переход, чтобы движение читалось как возврат
        Switch(target, Mirror(Transition));
    }

    private Page? FindPage(string name)
    {
        foreach (UIElement child in Children)
            if (child is Page page && page.Name == name)
                return page;

        return null;
    }

    private static PageTransition Mirror(PageTransition transition) => transition switch
    {
        PageTransition.SlideLeft => PageTransition.SlideRight,
        PageTransition.SlideRight => PageTransition.SlideLeft,
        PageTransition.SlideUp => PageTransition.SlideDown,
        PageTransition.SlideDown => PageTransition.SlideUp,
        _ => transition,
    };

    private void Switch(Page target, PageTransition transition)
    {
        Page? previous = _current;

        previous?.RaiseDisappearing();

        _current = target;
        target.RaiseAppearing();
        target.IsVisible = true;

        Navigated?.Invoke(this, target);

        if (transition == PageTransition.None || TransitionDurationMs <= 0)
        {
            if (previous is not null) previous.IsVisible = false;

            _outgoing = null;
            _progress = 1f;

            Invalidate();
            return;
        }

        // уходящая страница остаётся видимой до конца анимации,
        // иначе переход будет с пустым кадром
        _outgoing = previous;
        _progress = 0f;

        _activeTransition = transition;

        this.Animate("page", 0f, 1f, TimeSpan.FromMilliseconds(TransitionDurationMs),
            Interpolators.Float,
            value =>
            {
                _progress = value;
                InvalidateVisual();
            },
            Easing.EaseInOut,
            completed: () =>
            {
                if (_outgoing is not null) _outgoing.IsVisible = false;

                _outgoing = null;
                _progress = 1f;

                Invalidate();
            });

        Invalidate();
    }

    private PageTransition _activeTransition = PageTransition.None;

    // ===== раскладка =====

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float width = 0, height = 0;

        // меряем только видимые: скрытые страницы не должны влиять
        // на размер контейнера
        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            child.Measure(inner);

            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return ResolveSize(
            new Size(width + Padding.Horizontal, height + Padding.Vertical),
            availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var area = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical)));

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            Rectangle slot = area;

            // во время перехода страницы разъезжаются: входящая приходит
            // со своей стороны, уходящая уезжает в противоположную
            if (_progress < 1f && _activeTransition is not PageTransition.None and not PageTransition.Fade)
            {
                bool incoming = ReferenceEquals(child, _current);
                slot = OffsetFor(area, incoming);
            }

            child.Arrange(slot);
        }
    }

    private Rectangle OffsetFor(Rectangle area, bool incoming)
    {
        float t = incoming ? 1f - _progress : -_progress;

        (float dx, float dy) = _activeTransition switch
        {
            PageTransition.SlideLeft => (area.Width * t, 0f),
            PageTransition.SlideRight => (-area.Width * t, 0f),
            PageTransition.SlideUp => (0f, area.Height * t),
            PageTransition.SlideDown => (0f, -area.Height * t),
            _ => (0f, 0f),
        };

        return new Rectangle(new Point(area.X + dx, area.Y + dy), area.Size);
    }

    protected internal override void DrawOverlay(Graphics g)
    {
        base.DrawOverlay(g);
    }
}
