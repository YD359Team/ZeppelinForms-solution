using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Gestures;

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

    /// <summary>Готовить содержимое ещё не показанных страниц в простое.</summary>
    /// <remarks>
    /// Фабрика страницы отрабатывает при первом показе — прямо в обработчике
    /// нажатия. Для страницы из трёх десятков контролов с картинками
    /// и графиками это заметная пауза между кликом и началом перехода,
    /// и видна она только в первый раз: дальше содержимое уже построено.
    /// Поэтому строим остальные страницы заранее, пока пользователь читает
    /// текущую, и по одной за раз — чтобы не собрать все паузы в одну.
    /// </remarks>
    public bool PreloadPages { get; set; } = true;

    /// <summary>Пауза перед очередной порцией подготовки.</summary>
    public int PreloadDelayMs { get; set; } = 150;

    private IDisposable? _preloadWake;

    public Page? CurrentPage => _current;

    public bool CanGoBack => _history.Count > 1;

    public event EventHandler<Page>? Navigated;

    private SwipeGestureRecognizer? _swipe;

    /// <summary>Как реагировать на свайп по содержимому. Работает и мышью:
    /// распознаватель различает пальцы и мышь только порогами.</summary>
    public PageSwipeMode SwipeMode
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            UpdateSwipeRecognizer();
        }
    } = PageSwipeMode.Back;

    public PageControl() => UpdateSwipeRecognizer();

    protected override void OnAttached()
    {
        base.OnAttached();

        // до присоединения часов нет, а значит и отложить подготовку некуда
        SchedulePreload(PreloadDelayMs);
    }

    protected override void OnDetached()
    {
        base.OnDetached();

        _preloadWake?.Dispose();
        _preloadWake = null;
    }

    private void SchedulePreload(int delayMs)
    {
        if (!PreloadPages) return;
        if (FindOwner() is not { } owner) return;

        _preloadWake?.Dispose();
        _preloadWake = owner.Schedule(delayMs, PreloadNext);
    }

    /// <summary>Построить одну неготовую страницу и записаться на следующую
    /// порцию. Именно по одной: построение страницы — это создание десятков
    /// контролов с загрузкой их ресурсов, и пачкой это даст ту же заметную
    /// паузу, только в другом месте.</summary>
    private void PreloadNext()
    {
        _preloadWake = null;

        foreach (UIElement child in Children)
        {
            if (child is not Page { IsBuilt: false } page) continue;

            try
            {
                page.EnsureBuilt();
            }
            catch (Exception exception)
            {
                // подготовка — работа на опережение, и ронять из неё
                // приложение нельзя: страницу, которую пользователь
                // ещё не открывал, он не должен и терять. Страница
                // остаётся непостроенной, так что при переходе на неё
                // ошибка возникнет там же, где и без подготовки
                System.Diagnostics.Debug.WriteLine(
                    $"ZF: страницу \"{page.Title ?? "без заголовка"}\" " +
                    $"не удалось подготовить заранее. {exception}");
            }

            SchedulePreload(PreloadDelayMs);

            return;
        }
    }

    private void UpdateSwipeRecognizer()
    {
        if (SwipeMode == PageSwipeMode.None)
        {
            if (_swipe is null) return;

            _swipe.Swiped -= OnSwiped;
            GestureRecognizers.Remove(_swipe);
            _swipe = null;

            return;
        }

        if (_swipe is null)
        {
            _swipe = this.AddGesture(new SwipeGestureRecognizer());
            _swipe.Swiped += OnSwiped;
        }

        _swipe.AllowedDirections = SwipeMode == PageSwipeMode.Back
            ? [SwipeDirection.Right]
            : [SwipeDirection.Left, SwipeDirection.Right];
    }

    private void OnSwiped(object? sender, SwipeGestureEventArgs e)
    {
        // переход уже идёт: второй поверх него оставит _outgoing
        // недорисованным на полпути
        if (_progress < 1f) return;

        if (SwipeMode == PageSwipeMode.Back)
        {
            GoBack();
            return;
        }

        // влево — вперёд: содержимое уезжает в ту же сторону, что палец
        int step = e.Direction == SwipeDirection.Left ? 1 : -1;

        if (PageAtOffset(step) is not Page target) return;

        Navigate(target.Name, step > 0 ? PageTransition.SlideLeft : PageTransition.SlideRight);
    }

    private Page? PageAtOffset(int offset)
    {
        List<Page> pages = [];

        foreach (UIElement child in Children)
            if (child is Page page)
                pages.Add(page);

        int index = _current is null ? -1 : pages.IndexOf(_current);
        if (index < 0) return null;

        int next = index + offset;

        // по кругу не листаем: перескок с последней на первую читается
        // как сбой, а не как переход
        return next >= 0 && next < pages.Count ? pages[next] : null;
    }

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

    /// <summary>Довести незакрытый переход до конца. Animate заменяет
    /// анимацию с тем же ключом, не вызывая её completed, поэтому уходящая
    /// страница иначе осталась бы видимой и сдвинутой навсегда.</summary>
    private void FinishTransition()
    {
        if (_outgoing is not Page outgoing) return;

        // состояние сбрасываем первым делом: StopAnimation вызовет Cancel,
        // тот — completed, а он снова зайдёт сюда. Обнулённое _outgoing
        // обрывает повторный вход на первой строке, а захваченная
        // сопоставлением ссылка от этого не страдает
        _outgoing = null;
        _progress = 1f;
        _activeTransition = PageTransition.None;

        this.StopAnimation("page");

        outgoing.IsVisible = false;
        outgoing.Opacity = 1f;
        outgoing.Position = _baseSlot.Position;
    }

    private void Switch(Page target, PageTransition transition)
    {
        FinishTransition();

        Page? previous = _current;

        // без окна тик кадра не идёт: анимация не завершится
        // и уходящая страница останется висеть поверх новой
        bool canAnimate = transition != PageTransition.None
            && TransitionDurationMs > 0
            && previous is not null
            && FindOwner()?.PlatformWindow is not null;

        // состояние перехода выставляем до IsVisible. Его сеттер запускает
        // раскладку синхронно, а ArrangeContentOverride восстанавливает
        // смещения только при заполненных _outgoing и _progress — иначе обе
        // страницы окажутся в одном слоте, и этот кадр успеет отрисоваться
        if (canAnimate)
        {
            _outgoing = previous;
            _progress = 0f;
            _activeTransition = transition;
        }

        previous?.RaiseDisappearing();

        _current = target;
        target.RaiseAppearing();
        target.IsVisible = true;

        Navigated?.Invoke(this, target);

        if (!canAnimate)
        {
            if (previous is not null)
            {
                previous.IsVisible = false;
                previous.Opacity = 1f;
            }

            target.Opacity = 1f;
            Invalidate();

            SchedulePreload(PreloadDelayMs);

            return;
        }

        // раскладка отложена до кадра, а первый же кадр перехода сдвигает
        // страницы от их слота — значит слот должен быть посчитан сейчас.
        // Invalidate для этого не годится: он только помечает
        FindOwner()?.UpdateLayout();

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
                FinishTransition();
                target.Opacity = 1f;

                Invalidate();
            });
        // не во время перехода: построение страницы посреди анимации
        // съело бы ровно те кадры, которые она показывает
        SchedulePreload(TransitionDurationMs + PreloadDelayMs);
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
