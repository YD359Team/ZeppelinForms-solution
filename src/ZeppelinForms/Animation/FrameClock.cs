using System.Diagnostics;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Animation;

/// <summary>
/// Единственные часы формы: время кадра, анимации и отложенные дела.
/// </summary>
/// <remarks>
/// Три причины, по которым это один объект, а не три.
///
/// Время монотонно и с высоким разрешением. Environment.TickCount64 на
/// Windows тикает раз в 15,6 мс, и при кадре в 16 мс шаг анимации скакал
/// между нулём и тридцатью миллисекундами — это видно глазом даже
/// на переключателе.
///
/// Кадры идут только пока есть что двигать. Анимация на скрытом
/// поддереве не продвигается — и теперь не держит выдачу кадров:
/// свёрнутый Loader будил окно шестьдесят раз в секунду впустую.
/// Саму анимацию при этом не снимаем: страница вернётся, и она должна ожить.
///
/// Отложенные дела делят один таймер. Раньше каретка, подсказка, тост
/// и длинное нажатие завели по System.Threading.Timer каждый, и каждый
/// сам маршалил себя в поток UI. Каретка вдобавок хранила своё состояние
/// вместо того, чтобы вычислять его из времени, и отложенный тик,
/// добравшийся до очереди после потери фокуса, включал её обратно.
/// </remarks>
internal sealed class FrameClock(Form form) : IDisposable
{
    /// <summary>Потолок шага анимации. Кадр мог не прийти полсекунды —
    /// окно тащили за угол, приложение уходило в фон. Без потолка первый
    /// же кадр после паузы доводит все анимации до конца, и переход,
    /// который зритель так и не увидел, просто схлопывается.</summary>
    private const double MaxFrameDeltaMs = 100;

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private readonly List<IAnimation> _animations = [];
    private readonly List<Wake> _wakes = [];
    private readonly List<Wake> _due = [];

    private System.Threading.Timer? _timer;
    private TimeSpan _lastFrame;
    private bool _suspended;

    /// <summary>Монотонное время с создания формы.</summary>
    public TimeSpan Now => _stopwatch.Elapsed;

    /// <summary>Есть ли анимация, которую видно. Невидимую не двигаем,
    /// а значит и кадры под неё не нужны.</summary>
    private bool HasVisibleAnimation
    {
        get
        {
            foreach (IAnimation animation in _animations)
                if (animation.Target is not UIElement element || element.IsEffectivelyVisible)
                    return true;

            return false;
        }
    }

    // ===== анимации =====

    public void Add(IAnimation animation)
    {
        // одна анимация на связку «объект + свойство».
        // Вытесняемую снимаем с вызовом её completed, иначе состояние,
        // которое она должна была привести в порядок, останется в середине —
        // именно из-за этого PageControl оставлял страницы висеть
        for (int i = _animations.Count - 1; i >= 0; i--)
        {
            IAnimation existing = _animations[i];

            if (!ReferenceEquals(existing.Target, animation.Target) ||
                existing.Key != animation.Key)
                continue;

            _animations.RemoveAt(i);

            // без доведения значения: новая анимация начнёт со своего from,
            // и прыжок в конец дал бы мелькание
            existing.Cancel(applyFinalValue: false);
        }

        _animations.Add(animation);

        Review();
    }

    public void Remove(object target, string key)
    {
        for (int i = _animations.Count - 1; i >= 0; i--)
        {
            IAnimation existing = _animations[i];

            if (!ReferenceEquals(existing.Target, target) || existing.Key != key)
                continue;

            _animations.RemoveAt(i);
            existing.Cancel(applyFinalValue: false);
        }

        Review();
    }

    /// <summary>Снять анимации, цели которых уходят из дерева.</summary>
    public void CancelIn(UIElement root)
    {
        for (int i = _animations.Count - 1; i >= 0; i--)
        {
            if (_animations[i].Target is not UIElement element || !IsInTree(root, element))
                continue;

            IAnimation animation = _animations[i];
            _animations.RemoveAt(i);

            // цель уходит из дерева: ни значение доводить, ни completed
            // звать не нужно — приводить в порядок больше нечего
            animation.Cancel(applyFinalValue: false);
        }

        Review();
    }

    private static bool IsInTree(UIElement root, UIElement candidate)
    {
        for (UIElement? current = candidate; current is not null; current = current.Parent)
            if (ReferenceEquals(current, root))
                return true;

        return false;
    }

    /// <summary>Кадр от платформы.</summary>
    public void Tick()
    {
        TimeSpan now = _stopwatch.Elapsed;
        double deltaMs = (now - _lastFrame).TotalMilliseconds;
        _lastFrame = now;

        var elapsed = TimeSpan.FromMilliseconds(Math.Clamp(deltaMs, 0, MaxFrameDeltaMs));

        bool wholeWindow = false;

        // по снимку, а не по живому списку: Advance вызывает completed
        // прямо внутри себя, а тот может и снять анимации, и добавить —
        // переход страницы делает ровно это. Индексы при таком раскладе
        // разъезжаются под ногами
        IAnimation[] running = [.. _animations];

        foreach (IAnimation animation in running)
        {
            // могли снять из completed соседней анимации
            if (!_animations.Contains(animation)) continue;

            // анимация на скрытом поддереве не продвигается и не перерисовывается.
            // Не снимаем её: страница вернётся, и анимация должна ожить.
            // PageControl прячет страницы, не отвязывая, поэтому опираться
            // на Detached здесь нельзя
            if (animation.Target is UIElement hidden && !hidden.IsEffectivelyVisible)
                continue;

            bool alive = animation.Advance(elapsed);

            // перерисовываем цель независимо от того, дожила ли анимация
            // до следующего кадра: последний её кадр тоже надо показать
            switch (animation.Target)
            {
                // анимация самой формы — например, волна смены темы —
                // выходит за пределы любого отдельного элемента
                case Form: wholeWindow = true; break;
                case UIElement element: element.InvalidateVisual(); break;
            }

            if (!alive) _animations.Remove(animation);
        }

        Review();

        if (wholeWindow) form.InvalidateVisual();
    }

    // ===== выдача кадров =====

    /// <summary>Пересмотреть, нужны ли кадры прямо сейчас. Зовётся после
    /// любого изменения состава анимаций и из Form.Invalidate: видимость —
    /// свойство раскладки, и её смена всегда проходит через него.</summary>
    public void Review()
    {
        if (form.PlatformWindow?.Frames is not { } frames) return;

        if (_suspended || !HasVisibleAnimation)
        {
            if (frames.IsRunning) frames.Stop();
            return;
        }

        if (frames.IsRunning) return;

        // между остановкой и запуском прошло неизвестно сколько:
        // первый шаг считаем от этого момента, а не от давнего кадра
        _lastFrame = _stopwatch.Elapsed;
        frames.Start(form.FrameIntervalMs);
    }

    /// <summary>Приложение ушло в фон или окно свернули.</summary>
    public void Suspend()
    {
        _suspended = true;
        form.PlatformWindow?.Frames.Stop();
    }

    public void Resume()
    {
        _suspended = false;
        Review();
    }

    /// <summary>Окна больше нет: таймер кадров жил в нём, а отложенным
    /// делам некуда возвращаться.</summary>
    public void Stop()
    {
        form.PlatformWindow?.Frames.Stop();
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    // ===== отложенные дела =====

    /// <summary>Выполнить действие через задержку в потоке UI.</summary>
    /// <returns>Отмена: освободите результат, чтобы вызова не было.</returns>
    /// <remarks>
    /// Отмена и срабатывание — это гонка: будильник мог уже уйти в очередь
    /// UI к моменту, когда его отменяют. Поэтому вызываемый код обязан
    /// сам проверить, актуален ли он ещё, а не полагаться на отмену.
    /// </remarks>
    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var wake = new Wake(_stopwatch.Elapsed + delay, action);

        _wakes.Add(wake);
        Rearm();

        return wake;
    }

    private void Rearm()
    {
        TimeSpan? earliest = null;

        for (int i = _wakes.Count - 1; i >= 0; i--)
        {
            Wake wake = _wakes[i];

            if (wake.Action is null)
            {
                _wakes.RemoveAt(i);
                continue;
            }

            if (earliest is null || wake.Due < earliest) earliest = wake.Due;
        }

        if (earliest is null)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        _timer ??= new System.Threading.Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);

        double delayMs = Math.Max(0, (earliest.Value - _stopwatch.Elapsed).TotalMilliseconds);

        _timer.Change((long)delayMs, Timeout.Infinite);
    }

    // тикает на потоке пула — маршалим, дальше всё в потоке UI
    private void OnTimer(object? state) => form.Invoke(RunDue);

    private void RunDue()
    {
        TimeSpan now = _stopwatch.Elapsed;

        // сначала снимаем со списка, потом вызываем: дело может завести
        // новое отложенное дело — каретка так и перевзводит себя
        for (int i = _wakes.Count - 1; i >= 0; i--)
        {
            Wake wake = _wakes[i];

            if (wake.Action is not null && wake.Due > now) continue;

            _wakes.RemoveAt(i);

            if (wake.Action is not null) _due.Add(wake);
        }

        try
        {
            foreach (Wake wake in _due)
                wake.Action?.Invoke();
        }
        finally
        {
            _due.Clear();
            Rearm();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;

        _wakes.Clear();
        _animations.Clear();
    }

    private sealed class Wake(TimeSpan due, Action action) : IDisposable
    {
        public TimeSpan Due { get; } = due;

        /// <summary>null — дело отменили. Из списка его вынет ближайший
        /// Rearm: удалять из середины сейчас значит подраться с RunDue,
        /// который может идти прямо в эту минуту.</summary>
        public Action? Action { get; private set; } = action;

        public void Dispose() => Action = null;
    }
}