using System.Diagnostics.CodeAnalysis;
using ZeppelinForms.Animation;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Transitions: animated changes of styled properties, of visibility and of the
/// place a layout gives.</summary>
public abstract partial class UIElement
{
    private List<Transition>? _transitionRules;
    private List<IPropertyTransition>? _running;

    /// <summary>Where the renderer last drew the element while a transition was
    /// running, in absolute coordinates. The next transition frame repaints that
    /// place too: the element has moved away from it, and nothing else knows it
    /// was ever there.</summary>
    /// <remarks>
    /// Reported by the renderer rather than computed here: only the renderer knows
    /// for sure where a frame put the element. It also covers the frame a transition
    /// starts in — that frame is drawn right after the layout that started it, before
    /// any transition tick. When the element recorded its own position on each tick,
    /// that first position was never recorded, and on platforms that repaint only
    /// dirty areas the row's text stayed at its starting place over its neighbours.
    /// </remarks>
    private Rectangle? _lastPresentedBounds;

    /// <summary>A transition is running: the renderer reports where it draws the element.</summary>
    internal bool IsInTransition => _running is not null;

    /// <summary>Called by the renderer for an element in a transition, with the
    /// absolute bounds it has just been drawn at.</summary>
    internal void NotePresentedBounds(Rectangle bounds) => _lastPresentedBounds = bounds;

    [ThreadStatic] private static int s_presentationDepth;

    /// <summary>The global switch for transitions: for the system "reduce motion"
    /// setting and for tests that animation gets in the way of.</summary>
    public static bool TransitionsEnabled { get; set; } = true;

    /// <summary>Whether transitions run right now: they weren't turned off in code,
    /// and the system doesn't ask to reduce motion.</summary>
    internal static bool TransitionsActive => TransitionsEnabled && !Motion.IsReduced;

    /// <summary>This element's transition rules: which property gets to a new
    /// value and over what time.</summary>
    /// <remarks>
    /// The assignment stays instant. The property reads as the target — that is
    /// how code, bindings and PropertyGrid see it. The intermediate value
    /// is returned only inside drawing, through Presented.
    /// </remarks>
    public IList<Transition> Transitions => _transitionRules ??= [];

    /// <summary>Drawing is in progress: reading properties returns the intermediate
    /// transition values instead of the targets.</summary>
    internal static bool IsPresenting => s_presentationDepth > 0;

    /// <summary>Open a presentation scope. The tree walk holds it for the whole
    /// walk — nesting is needed in case an element snapshot is taken in the middle
    /// of someone else's drawing.</summary>
    internal static PresentationScope BeginPresentation()
    {
        s_presentationDepth++;

        return default;
    }

    internal readonly struct PresentationScope : IDisposable
    {
        public void Dispose() => s_presentationDepth--;
    }

    /// <summary>The value that should go into drawing: the intermediate one while
    /// a transition runs, and the target in all other cases. Called
    /// from generated getters.</summary>
    protected T Presented<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property, T target)
    {
        if (!IsPresenting || _running is null) return target;

        foreach (IPropertyTransition running in _running)
            if (ReferenceEquals(running.Property, property))
                return ((PropertyTransition<T>)running).Current;

        return target;
    }

    /// <summary>Start a transition to a new value. Called before the write:
    /// it must start from what is visible now.</summary>
    private void BeginTransition<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property, T from)
    {
        if (!TransitionsActive) return;

        if ((_transitionRules is null || _transitionRules.Count == 0) && _styleTransitions is null)
            return;

        // an element that hasn't been shown yet doesn't transition, it appears:
        // otherwise every form would open with all theme values sliding
        // from the defaults to the real ones
        if (!_hasBeenArranged) return;

        if (FindOwner() is not { } owner) return;

        // the element's own rules first, then the matched styles'
        Transition? rule = FindTransitionRule(property);

        if (rule is null) return;

        if (property.AffectsLayout)
        {
            // only drawing sees the intermediate value, while sizes are computed
            // from the target — a transition of such a property would look like
            // the picture and the layout falling out of sync
            ZfContract.Fail(
                $"A transition on {property.Name} is impossible: the property affects " +
                "layout. Animate what is drawn — color, " +
                "opacity, rotation.");

            return;
        }

        if (Interpolator.Find<T>() is not { } interpolate)
        {
            ZfContract.Fail(
                $"A transition on {property.Name} is impossible: there is no interpolator " +
                $"for {typeof(T).Name}. Register one with Interpolator.Register.");

            return;
        }

        // without a form there is nobody to drive the transition — the frame clock lives on it
        if (FindOwner() is null) return;

        // an interrupted transition continues from where it was
        // rather than jumping back to the old start
        StartTransition(property, PresentedOrTarget(property, from), rule);
    }

    /// <summary>The value visible right now: the intermediate one if a transition
    /// is already running, and the given one otherwise.</summary>
    private T PresentedOrTarget<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property, T target)
    {
        if (_running is null) return target;

        foreach (IPropertyTransition running in _running)
            if (ReferenceEquals(running.Property, property))
                return ((PropertyTransition<T>)running).Current;

        return target;
    }

    /// <summary>Start a property transition from the given value to its current
    /// target. The target is taken from the property itself and read every frame.</summary>
    internal void StartTransition<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property, T from, Transition rule)
    {
        if (FindOwner() is not { } owner) return;

        if (Interpolator.Find<T>() is not { } interpolate)
        {
            ZfContract.Fail(
                $"A transition on {property.Name} is impossible: there is no interpolator " +
                $"for {typeof(T).Name}. Register one with Interpolator.Register.");

            return;
        }

        var transition = new PropertyTransition<T>(this, property, from, rule, interpolate);

        _running ??= [];
        _running.Add(transition);

        // the previous transition of the same property is displaced by key, in the clock
        owner.AddAnimation(transition);
    }

    internal void RemoveTransition(IPropertyTransition transition)
    {
        if (_running is null) return;

        _running.Remove(transition);

        if (_running.Count == 0) _running = null;

        // the element returns to where its properties put it: that place is
        // repainted, and so is the one it was last drawn at mid-transition —
        // otherwise the last intermediate frame would stay on screen
        if (_lastPresentedBounds is { } before &&
            float.IsFinite(ActualSize.Width) && float.IsFinite(ActualSize.Height))
        {
            FindOwner()?.InvalidateRect(before.Union(DirtyBounds));

            if (_running is null) _lastPresentedBounds = null;

            return;
        }

        InvalidateVisual();
    }

    /// <summary>A transition frame: redraw the element without touching layout.</summary>
    /// <remarks>
    /// The dirty area is where the element will be drawn now — computed inside
    /// a presentation scope, so it sees the intermediate values — united with where
    /// the renderer last drew it. Plain DirtyBounds is computed outside presentation
    /// and sees the transition's target: a row sliding into place used to invalidate
    /// its final position every frame, never the one it was actually drawn at, and
    /// platforms that repaint only dirty areas (X11, the Windows software surface)
    /// left its text smeared over its neighbours until something else repainted there.
    /// </remarks>
    internal void InvalidateTransitionVisual()
    {
        if (!float.IsFinite(ActualSize.Width) || !float.IsFinite(ActualSize.Height))
            return;

        Rectangle now;

        using (BeginPresentation())
            now = DirtyBounds;

        FindOwner()?.InvalidateRect(
            _lastPresentedBounds is { } drawn ? drawn.Union(now) : now);
    }

    private Point _arrangedPosition;
    private bool _skipLayoutTransition;
    private bool _skipEnterTransition;
    private bool _skipExitTransition;

    /// <summary>How the element appears on an already shown screen: added to a
    /// panel, or shown again with IsVisible. null — instantly, as before.</summary>
    public VisibilityTransition? EnterTransition { get; set; }

    /// <summary>How the element disappears when it is removed from a panel or
    /// hidden with IsVisible. null — instantly, as before.</summary>
    public VisibilityTransition? ExitTransition { get; set; }

    internal VisibilityTransition? EffectiveEnterTransition =>
        EnterTransition ?? (Parent as PanelControl)?.ChildrenEnterTransition;

    /// <summary>The exit rule relative to a specific panel: asked at the moment
    /// of removal, when Parent may already be reset.</summary>
    internal VisibilityTransition? ExitTransitionIn(PanelControl panel) =>
        ExitTransition ?? panel.ChildrenExitTransition;

    /// <summary>Don't treat the next insertion and removal as appearing
    /// and disappearing. Needed by those who create and discard containers
    /// while scrolling: a row that rolled in from beyond the edge didn't
    /// appear — it just became visible.</summary>
    internal void SkipNextVisibilityTransitions()
    {
        _skipEnterTransition = true;
        _skipExitTransition = true;
    }

    /// <summary>Take the skip-exit mark: it is one-shot.</summary>
    internal bool ConsumeSkipExit()
    {
        bool skip = _skipExitTransition;
        _skipExitTransition = false;

        return skip;
    }

    /// <summary>Whether the element qualifies as exiting: only what was
    /// already on screen can disappear.</summary>
    internal bool CanAnimateExit => TransitionsActive && _hasBeenArranged && IsVisible;

    /// <summary>Shown again on a screen already seen: the next arrange plays the
    /// enter transition, as the first arrange of an added element does.</summary>
    private bool _enterOnNextArrange;

    /// <summary>The visibility OnVisibilityChanged saw last.</summary>
    private bool _lastVisible = true;

    /// <summary>IsVisible changed: the element appears and disappears the way an
    /// added and a removed one do.</summary>
    /// <remarks>
    /// Before 0.15 EnterTransition and ExitTransition played only when a panel got
    /// or lost the element; hiding and showing it was instant, whatever its rules.
    /// </remarks>
    private void OnVisibilityChanged()
    {
        // a binding writes its value without comparing: the same value again is no change
        if (IsVisible == _lastVisible) return;

        _lastVisible = IsVisible;

        var panel = Parent as PanelControl;

        if (IsVisible)
        {
            // back before its disappearance has ended: it must not be drawn twice
            panel?.CancelExit(this);

            // only what has been on screen appears: an element hidden before the
            // form was first laid out simply turns up with it
            _enterOnNextArrange = _hasBeenArranged;
        }
        else
        {
            _enterOnNextArrange = false;

            if (panel is not null && TransitionsActive && _hasBeenArranged)
                panel.BeginHide(this);
        }
    }

    private void StartEnterTransition()
    {
        if (!TransitionsActive) return;
        if (EffectiveEnterTransition is not { } rule) return;
        if (!IsEffectivelyVisible) return;

        // from the invisible look to what lies in the properties: the properties
        // themselves are not touched, the model sees the final values from the first frame
        if (rule.Opacity != 1f)
            StartTransition(OpacityProperty, Opacity * rule.Opacity, rule.ForOpacity);

        if (rule.Scale != 1f)
        {
            StartTransition(ScaleXProperty, ScaleX * rule.Scale, rule.ForScaleX);
            StartTransition(ScaleYProperty, ScaleY * rule.Scale, rule.ForScaleY);
        }

        if (rule.OffsetX != 0f)
            StartTransition(TranslateXProperty, TranslateX + rule.OffsetX, rule.ForTranslateX);

        if (rule.OffsetY != 0f)
            StartTransition(TranslateYProperty, TranslateY + rule.OffsetY, rule.ForTranslateY);
    }

    /// <summary>Don't treat the next move as a move. Needed by those who
    /// reuse containers: a row rebound to another list item didn't move —
    /// it became a different row, and there is no point driving it
    /// across half the screen.</summary>
    internal void SkipNextLayoutTransition() => _skipLayoutTransition = true;

    /// <summary>How the element travels when layout puts it in a new place.
    /// null — instantly, as before.</summary>
    /// <remarks>
    /// The rule is taken from the element itself, or if it has none — from the
    /// parent panel: one line on a list turns on smooth rearrangement of all its
    /// rows, and the controls inside need to know nothing about it.
    /// </remarks>
    public LayoutTransition? LayoutTransition { get; set; }

    internal LayoutTransition? EffectiveLayoutTransition =>
        LayoutTransition ?? (Parent as PanelControl)?.ChildrenLayoutTransition;

    /// <summary>A move by the FLIP rule: the element already stands in its new
    /// place, but is drawn from the old one and travels to a zero offset.</summary>
    private void StartLayoutTransition(Point previous, Point placed)
    {
        if (!TransitionsActive) return;
        if (EffectiveLayoutTransition is not { } rule) return;

        // an invisible subtree doesn't travel: a page rearranged off screen
        // must open with everything already in place
        if (!IsEffectivelyVisible) return;

        float dx = previous.X - placed.X;
        float dy = previous.Y - placed.Y;

        // the duration may depend on how far to travel: a move by one row
        // and a move across the whole screen taking the same time
        // look wrong in different ways
        if (dx != 0f)
            StartTransition(
                TranslateXProperty,
                PresentedOrTarget(TranslateXProperty, TranslateX) + dx,
                rule.For(TranslateXProperty, dx));

        if (dy != 0f)
            StartTransition(
                TranslateYProperty,
                PresentedOrTarget(TranslateYProperty, TranslateY) + dy,
                rule.For(TranslateYProperty, dy));
    }
}
