using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <summary>Whoever can scroll with a finger. Implemented by the panel and
/// the grid — they share the mechanics and have quite different internals.</summary>
internal interface ITouchScrollTarget
{
    UIElement Element { get; }

    bool CanPanHorizontally { get; }

    bool CanPanVertically { get; }

    /// <summary>The visible content area: the resistance of pulling
    /// past the edge is computed from it.</summary>
    Size PanViewport { get; }

    Point PanScroll { get; }

    Point PanMaxScroll { get; }

    /// <summary>Apply the scroll within the allowed range and the overscroll
    /// separately. How to show the overscroll is the control's own business:
    /// the panel shifts its children, the grid draws its rows with an offset.</summary>
    void ApplyPanScroll(Point scroll, Point overscroll);
}

/// <summary>
/// Scrolling with a finger: dragging, inertia after a fling and bouncing
/// from the edge. Shared by everyone who scrolls.
/// </summary>
/// <remarks>
/// A separate object rather than methods in the panel, for exactly one reason:
/// the data grid scrolls by itself, bypassing PanelControl — it draws rows
/// rather than laying out controls. Otherwise there would have to be two copies
/// of the physics that must match down to the feel under the finger.
/// </remarks>
internal sealed class TouchScroller
{
    /// <summary>The fling velocity at which inertia starts, pixels per second.
    /// Below it the finger was simply lifted, not flung.</summary>
    private const float FlingStartVelocity = 50f;

    private readonly ITouchScrollTarget _target;
    private readonly PanGestureRecognizer _pan;

    private KineticScroll? _kinetic;

    /// <summary>Where the finger wants to take the content, without limits.
    /// The difference with the allowed position becomes the overscroll.</summary>
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

    /// <summary>The overscroll the control must show.</summary>
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

    /// <summary>Put out the inertia, leaving the bounce to play out: a finger
    /// touched the screen, but content pulled past the edge must return.</summary>
    public void StopFling() => _kinetic?.StopFling();

    /// <summary>Stop everything: an explicit scroll from code or the wheel
    /// takes priority over fling inertia.</summary>
    public void Stop(bool keepOverscroll = false)
    {
        if (_kinetic is not null)
        {
            // first unhook, then remove from the clock: the cancel, seeing that
            // it is no longer the current one, won't touch the overscroll —
            // that is decided here
            _kinetic = null;
            _target.Element.FindOwner()?.RemoveAnimation(_target.Element, KineticScroll.AnimationKey);
        }

        if (keepOverscroll || Overscroll == Point.Empty) return;

        Apply(_target.PanScroll, Point.Empty);
    }

    /// <summary>Whether to take the contact. Refuse if there is nowhere to scroll:
    /// otherwise a panel with short content would take the gesture away from
    /// an outer one that has somewhere to go.</summary>
    private bool CanBegin(PointerContact contact)
    {
        if (contact.Kind == PointerKind.Mouse) return false;

        bool canX = _target.CanPanHorizontally;
        bool canY = _target.CanPanVertically;

        if (!canX && !canY) return false;

        // the direction follows where it is possible to go at all: a vertical
        // list must not take a horizontal page swipe
        _pan.Direction = canX && canY
            ? PanDirection.Both
            : canX ? PanDirection.Horizontal : PanDirection.Vertical;

        return true;
    }

    private void OnStarted(object? sender, PanGestureEventArgs e)
    {
        // the finger has grabbed the content — inertia and bounce no longer drive it
        Stop(keepOverscroll: true);

        Point scroll = _target.PanScroll;
        _panPosition = new Point(scroll.X + Overscroll.X, scroll.Y + Overscroll.Y);

        // the break threshold is a recognition delay, not a lost distance:
        // what was travelled before it is applied too
        Drag(e.Delta);
    }

    private void OnUpdated(object? sender, PanGestureEventArgs e) => Drag(e.Delta);

    private void OnCompleted(object? sender, PanGestureEventArgs e) =>
        StartKinetic(new Point(-e.Velocity.X, -e.Velocity.Y));

    // cut off in the middle of the gesture — nothing to fling with, but what
    // was pulled must return. Comes only for a pan that actually started:
    // a pan that refused the contact while a bounce was playing out used to
    // restart the bounce here from zero velocity, and the spring jerked
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

    /// <summary>Split the desired position into the allowed scroll and the
    /// overscroll. The overscroll grows slower than the finger and approaches
    /// a limit — that is how rubber stretches, and the edge is felt rather
    /// than simply reached.</summary>
    private static (float Scroll, float Overscroll) Stretch(float desired, float max, float extent)
    {
        float scroll = Math.Clamp(desired, 0, max);
        float excess = desired - scroll;

        if (excess == 0 || extent <= 0) return (scroll, 0f);

        // the same curve as in iOS: almost linear for a small overscroll,
        // for a large one it approaches the window size but never reaches it
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
            // the control is already outside the form — there is nobody to drive
            // the bounce, and the pulled content must not stay pulled
            Apply(_target.PanScroll, Point.Empty);
            return;
        }

        // the velocity along an axis that can't be scrolled is discarded right away:
        // otherwise the inertia would live on without moving anything
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
    /// Inertia after a fling and the bounce from the edge — one animation per control.
    /// </summary>
    /// <remarks>
    /// Along each axis one of two things happens. Either a free run with friction:
    /// the velocity decays exponentially, and the fling distance is proportional
    /// to its velocity. Or a spring to the edge, if the content is pulled past it
    /// or the run hit the edge — then the rest of the velocity goes into the bounce.
    /// The spring is critically damped: it returns fast and without oscillating
    /// around the edge.
    /// </remarks>
    private sealed class KineticScroll(TouchScroller scroller, Point velocity) : IAnimation
    {
        internal const string AnimationKey = "kinetic-scroll";

        /// <summary>Free-run friction, 1/s: in a second the velocity
        /// drops by e³ ≈ 20 times.</summary>
        private const float Friction = 3f;

        private const float StopVelocity = 15f;

        /// <summary>Stiffness of the bounce spring, 1/s².</summary>
        private const float Stiffness = 170f;

        /// <summary>What share of the velocity of hitting the edge goes into the bounce.</summary>
        private const float BounceShare = 0.35f;

        /// <summary>The integration step. A frame may take 100 ms, and a spring
        /// with this stiffness goes haywire on a large step.</summary>
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

        /// <summary>A step of one axis. true — there is still movement on it.</summary>
        private static bool Step(
            ref float scroll, ref float over, ref float velocity, ref float spring,
            float max, float dt)
        {
            if (over != 0f || spring != 0f)
            {
                // the spring to the edge, critically damped: c = 2√k
                float omega = MathF.Sqrt(Stiffness);
                float acceleration = -Stiffness * over - 2f * omega * spring;

                spring += acceleration * dt;
                over += spring * dt;

                // while the bounce goes on, there is no free run
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
                // hit the edge while running: the rest of the velocity goes into the bounce
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

            // it may have been removed in the middle of a bounce —
            // what was pulled is returned right away
            if (scroller.Overscroll == Point.Empty) return;

            scroller.Apply(scroller._target.PanScroll, Point.Empty);
        }
    }
}