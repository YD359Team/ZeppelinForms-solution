using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.Navigation;

/// <summary>
/// Контейнер представлений с историей переходов. Скрытые страницы
/// остаются в дереве, но не рисуются и не получают события.
/// </summary>
public class PageControl : DecoratedPanel
{
    private readonly List<string> _history = [];
    private Page? _current;
    private Page? _outgoing;

    private float _progress = 1f;

    /// <summary>Создать индикатор, привязанный к этому контейнеру.</summary>
    public PageIndicator CreateIndicator(PageIndicatorStyle style = PageIndicatorStyle.Dots) =>
        new() { Target = this, Style = style };

    public PageTransition Transition { get; set; } = PageTransition.SlideLeft;
    public int TransitionDurationMs { get; set; } = 220;

    public Page? CurrentPage => _current;

    public bool CanGoBack => _history.Count > 1;

    public event EventHandler<Page>? Navigated;

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

    private Rectangle _baseSlot;

    private void Switch(Page target, PageTransition transition)
    {
        Page? previous = _current;

        previous?.RaiseDisappearing();

        _current = target;
        target.RaiseAppearing();
        target.IsVisible = true;

        Navigated?.Invoke(this, target);

        // без окна тик кадра не идёт: анимация не завершится
        // и уходящая страница останется висеть поверх новой
        bool canAnimate = transition != PageTransition.None
            && TransitionDurationMs > 0
            && previous is not null
            && FindOwner()?.PlatformWindow is not null;

        if (!canAnimate)
        {
            if (previous is not null)
            {
                previous.IsVisible = false;
                previous.Opacity = 1f;
            }

            target.Opacity = 1f;

            _outgoing = null;
            _progress = 1f;
            _activeTransition = PageTransition.None;

            Invalidate();
            return;
        }

        _outgoing = previous;
        _progress = 0f;
        _activeTransition = transition;

        // пересчитываем раскладку один раз, чтобы обе страницы получили
        // базовые позиции; дальше двигаем их напрямую, без Arrange
        Invalidate();

        Page outgoing = previous!;

        this.Animate("page", 0f, 1f, TimeSpan.FromMilliseconds(TransitionDurationMs),
            Interpolators.Float,
            value =>
            {
                _progress = value;
                ApplyTransition(target, outgoing);
                InvalidateVisual();
            },
            Easing.EaseInOut,
            completed: () =>
            {
                outgoing.IsVisible = false;
                outgoing.Opacity = 1f;
                target.Opacity = 1f;

                _outgoing = null;
                _progress = 1f;
                _activeTransition = PageTransition.None;

                Invalidate();
            });
    }

    /// <summary>Сдвигает и подкрашивает страницы по текущему прогрессу.
    /// Меняет только Position и Opacity — полная раскладка на каждый кадр не нужна.</summary>
    private void ApplyTransition(Page incoming, Page outgoing)
    {
        if (_activeTransition == PageTransition.Fade)
        {
            incoming.Opacity = _progress;
            outgoing.Opacity = 1f - _progress;
            return;
        }

        (float dxIn, float dyIn) = OffsetDelta(incoming: true);
        (float dxOut, float dyOut) = OffsetDelta(incoming: false);

        incoming.Position = new Point(_baseSlot.X + dxIn, _baseSlot.Y + dyIn);
        outgoing.Position = new Point(_baseSlot.X + dxOut, _baseSlot.Y + dyOut);
    }

    private (float Dx, float Dy) OffsetDelta(bool incoming)
    {
        float t = incoming ? 1f - _progress : -_progress;

        return _activeTransition switch
        {
            PageTransition.SlideLeft => (_baseSlot.Width * t, 0f),
            PageTransition.SlideRight => (-_baseSlot.Width * t, 0f),
            PageTransition.SlideUp => (0f, _baseSlot.Height * t),
            PageTransition.SlideDown => (0f, -_baseSlot.Height * t),
            _ => (0f, 0f),
        };
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

        // запоминаем базовый слот: анимация двигает страницы относительно него
        _baseSlot = area;

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            child.Arrange(area);
        }

        // если раскладка случилась посреди перехода, восстанавливаем смещения
        if (_progress < 1f && _outgoing is not null && _current is not null)
            ApplyTransition(_current, _outgoing);
    }
}
