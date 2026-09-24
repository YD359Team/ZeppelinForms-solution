using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <summary>Кто умеет прокручиваться пальцем. Реализуют панель и таблица —
/// у них общая механика и совсем разное внутреннее устройство.</summary>
internal interface ITouchScrollTarget
{
    UIElement Element { get; }

    bool CanPanHorizontally { get; }

    bool CanPanVertically { get; }

    /// <summary>Видимая область содержимого: по ней считается сопротивление
    /// перелёта за край.</summary>
    Size PanViewport { get; }

    Point PanScroll { get; }

    Point PanMaxScroll { get; }

    /// <summary>Применить прокрутку в допустимых пределах и перелёт за край
    /// отдельно. Как показать перелёт — дело самого контрола: панель сдвигает
    /// детей, таблица рисует строки со смещением.</summary>
    void ApplyPanScroll(Point scroll, Point overscroll);
}

/// <summary>
/// Прокрутка пальцем: перетаскивание, инерция после броска и отскок
/// от края. Общая для всех, кто прокручивается.
/// </summary>
/// <remarks>
/// Отдельным объектом, а не методами в панели, ровно по одной причине:
/// DataGridView прокручивается сам, минуя PanelControl, — он рисует
/// строки, а не раскладывает контролы. Иначе пришлось бы держать две
/// копии физики, которые обязаны совпадать до ощущения под пальцем.
/// </remarks>
internal sealed class TouchScroller
{
    /// <summary>Скорость броска, с которой начинается инерция, пикселей
    /// в секунду. Ниже — палец просто отпустили, а не бросили.</summary>
    private const float FlingStartVelocity = 50f;

    private readonly ITouchScrollTarget _target;
    private readonly PanGestureRecognizer _pan;

    private KineticScroll? _kinetic;

    /// <summary>Куда палец хочет увести содержимое, без ограничений.
    /// Разница с допустимым и превращается в перелёт.</summary>
    private Point _panPosition;

    private TouchScroller(ITouchScrollTarget target)
    {
        _target = target;

        _pan = new PanGestureRecognizer { CanBegin = CanBegin };

        _pan.Started += OnStarted;
        _pan.Updated += OnUpdated;
        _pan.Completed += OnCompleted;
        _pan.Cancelled += OnCancelled;
    }

    /// <summary>Перелёт за край, который контрол должен показать.</summary>
    public Point Overscroll { get; private set; }

    public static TouchScroller Attach(ITouchScrollTarget target)
    {
        var scroller = new TouchScroller(target);

        target.Element.AddGesture(scroller._pan);

        return scroller;
    }

    public void Detach()
    {
        _target.Element.GestureRecognizers.Remove(_pan);

        Stop();
    }

    /// <summary>Погасить инерцию, оставив отскок доигрывать: палец лёг
    /// на экран, но содержимое, оттянутое за край, должно вернуться.</summary>
    public void StopFling() => _kinetic?.StopFling();

    /// <summary>Остановить всё: явная прокрутка из кода или колесом
    /// главнее инерции броска.</summary>
    public void Stop(bool keepOverscroll = false)
    {
        if (_kinetic is not null)
        {
            // сначала отцепляем, потом снимаем с часов: отмена, увидев,
            // что она уже не текущая, перелёт не тронет — им распоряжаемся здесь
            _kinetic = null;
            _target.Element.FindOwner()?.RemoveAnimation(_target.Element, KineticScroll.AnimationKey);
        }

        if (keepOverscroll || Overscroll == Point.Empty) return;

        Apply(_target.PanScroll, Point.Empty);
    }

    /// <summary>Брать ли контакт. Отказываемся, если прокручивать некуда:
    /// иначе панель с коротким содержимым отбирала бы жест у внешней,
    /// которой ехать есть куда.</summary>
    private bool CanBegin(PointerContact contact)
    {
        if (contact.Kind == PointerKind.Mouse) return false;

        bool canX = _target.CanPanHorizontally;
        bool canY = _target.CanPanVertically;

        if (!canX && !canY) return false;

        // направление — по тому, куда вообще можно ехать: вертикальный
        // список не должен забирать горизонтальный свайп страницы
        _pan.Direction = canX && canY
            ? PanDirection.Both
            : canX ? PanDirection.Horizontal : PanDirection.Vertical;

        return true;
    }

    private void OnStarted(object? sender, PanGestureEventArgs e)
    {
        // палец схватил содержимое — инерция и отскок больше не ведут его
        Stop(keepOverscroll: true);

        Point scroll = _target.PanScroll;
        _panPosition = new Point(scroll.X + Overscroll.X, scroll.Y + Overscroll.Y);

        // порог срыва — это задержка распознавания, а не потерянное
        // расстояние: пройденное до него тоже применяем
        Drag(e.Delta);
    }

    private void OnUpdated(object? sender, PanGestureEventArgs e) => Drag(e.Delta);

    private void OnCompleted(object? sender, PanGestureEventArgs e) =>
        StartKinetic(new Point(-e.Velocity.X, -e.Velocity.Y));

    // оборвали посреди жеста — бросать нечем, но оттянутое надо вернуть
    private void OnCancelled(object? sender, EventArgs e) => StartKinetic(Point.Empty);

    private void Drag(Point delta)
    {
        _panPosition = new Point(_panPosition.X - delta.X, _panPosition.Y - delta.Y);

        Point scroll = _target.PanScroll;
        Point max = _target.PanMaxScroll;
        Size viewport = _target.PanViewport;

        (float x, float overX) = _target.CanPanHorizontally
            ? Stretch(_panPosition.X, max.X, viewport.Width)
            : (scroll.X, 0f);

        (float y, float overY) = _target.CanPanVertically
            ? Stretch(_panPosition.Y, max.Y, viewport.Height)
            : (scroll.Y, 0f);

        Apply(new Point(x, y), new Point(overX, overY));
    }

    /// <summary>Разложить желаемое положение на допустимую прокрутку
    /// и перелёт. Перелёт растёт медленнее пальца и упирается в предел —
    /// так тянется резина, и край ощущается, а не просто наступает.</summary>
    private static (float Scroll, float Overscroll) Stretch(float desired, float max, float extent)
    {
        float scroll = Math.Clamp(desired, 0, max);
        float excess = desired - scroll;

        if (excess == 0 || extent <= 0) return (scroll, 0f);

        // та же кривая, что у iOS: при малом перелёте почти линейна,
        // при большом приближается к размеру окна, но никогда его не достигает
        float magnitude = (1f - 1f / (MathF.Abs(excess) * 0.55f / extent + 1f)) * extent;

        return (scroll, MathF.CopySign(magnitude, excess));
    }

    private void Apply(Point scroll, Point overscroll)
    {
        Overscroll = overscroll;

        _target.ApplyPanScroll(scroll, overscroll);
    }

    private void StartKinetic(Point velocity)
    {
        if (_target.Element.FindOwner() is not { } owner)
        {
            // контрол уже вне формы — вести отскок некому, а оттянутое
            // содержимое не должно так и остаться оттянутым
            Apply(_target.PanScroll, Point.Empty);
            return;
        }

        // скорость по оси, по которой ехать нельзя, выбрасываем сразу:
        // иначе инерция жила бы, ничего не двигая
        velocity = new Point(
            _target.CanPanHorizontally ? velocity.X : 0f,
            _target.CanPanVertically ? velocity.Y : 0f);

        bool fling = MathF.Abs(velocity.X) >= FlingStartVelocity
            || MathF.Abs(velocity.Y) >= FlingStartVelocity;

        if (!fling && Overscroll == Point.Empty) return;

        _kinetic = new KineticScroll(this, fling ? velocity : Point.Empty);

        owner.AddAnimation(_kinetic);
    }

    /// <summary>
    /// Инерция после броска и отскок от края — одна анимация на контрол.
    /// </summary>
    /// <remarks>
    /// По каждой оси идёт одно из двух. Либо свободный бег с трением:
    /// скорость гаснет экспоненциально, и дистанция броска пропорциональна
    /// его скорости. Либо пружина к краю, если содержимое оттянуто за него
    /// или бег в край упёрся — тогда остаток скорости уходит в отскок.
    /// Пружина критически затухающая: возвращается быстро и без колебаний
    /// вокруг края.
    /// </remarks>
    private sealed class KineticScroll(TouchScroller scroller, Point velocity) : IAnimation
    {
        internal const string AnimationKey = "kinetic-scroll";

        /// <summary>Трение свободного бега, 1/с: за секунду скорость
        /// падает в e³ ≈ 20 раз.</summary>
        private const float Friction = 3f;

        private const float StopVelocity = 15f;

        /// <summary>Жёсткость пружины отскока, 1/с².</summary>
        private const float Stiffness = 170f;

        /// <summary>Какая доля скорости удара о край уходит в отскок.</summary>
        private const float BounceShare = 0.35f;

        /// <summary>Шаг интегрирования. Кадр бывает и в 100 мс, а пружина
        /// с такой жёсткостью на крупном шаге идёт вразнос.</summary>
        private const float MaxStep = 0.008f;

        private Point _velocity = velocity;
        private Point _springVelocity;

        public object Target => scroller._target.Element;

        public string Key => AnimationKey;

        public void StopFling() => _velocity = Point.Empty;

        public bool Advance(TimeSpan elapsed)
        {
            float remaining = (float)elapsed.TotalSeconds;

            Point scroll = scroller._target.PanScroll;
            Point max = scroller._target.PanMaxScroll;

            float scrollX = scroll.X, scrollY = scroll.Y;
            float overX = scroller.Overscroll.X, overY = scroller.Overscroll.Y;
            float vx = _velocity.X, vy = _velocity.Y;
            float sx = _springVelocity.X, sy = _springVelocity.Y;

            bool aliveX = false, aliveY = false;

            while (remaining > 0f)
            {
                float dt = MathF.Min(remaining, MaxStep);
                remaining -= dt;

                aliveX = Step(ref scrollX, ref overX, ref vx, ref sx, max.X, dt);
                aliveY = Step(ref scrollY, ref overY, ref vy, ref sy, max.Y, dt);
            }

            _velocity = new Point(vx, vy);
            _springVelocity = new Point(sx, sy);

            scroller.Apply(new Point(scrollX, scrollY), new Point(overX, overY));

            if (aliveX || aliveY) return true;

            if (ReferenceEquals(scroller._kinetic, this)) scroller._kinetic = null;

            return false;
        }

        /// <summary>Шаг одной оси. true — на ней ещё есть движение.</summary>
        private static bool Step(
            ref float scroll, ref float over, ref float velocity, ref float spring,
            float max, float dt)
        {
            if (over != 0f || spring != 0f)
            {
                // пружина к краю, критическое затухание: c = 2√k
                float omega = MathF.Sqrt(Stiffness);
                float acceleration = -Stiffness * over - 2f * omega * spring;

                spring += acceleration * dt;
                over += spring * dt;

                // пока идёт отскок, свободного бега нет
                velocity = 0f;

                if (MathF.Abs(over) < 0.5f && MathF.Abs(spring) < 10f)
                {
                    over = 0f;
                    spring = 0f;

                    return false;
                }

                return true;
            }

            if (MathF.Abs(velocity) < StopVelocity)
            {
                velocity = 0f;
                return false;
            }

            velocity *= MathF.Exp(-Friction * dt);

            float next = scroll + velocity * dt;

            if (next < 0f || next > max)
            {
                // упёрлись в край на бегу: остаток скорости уходит в отскок
                scroll = Math.Clamp(next, 0f, max);
                spring = velocity * BounceShare;
                velocity = 0f;

                return true;
            }

            scroll = next;
            return true;
        }

        public void Cancel(bool applyFinalValue)
        {
            if (!ReferenceEquals(scroller._kinetic, this)) return;

            scroller._kinetic = null;

            // снять могли посреди отскока — оттянутое возвращаем сразу
            if (scroller.Overscroll == Point.Empty) return;

            scroller.Apply(scroller._target.PanScroll, Point.Empty);
        }
    }
}