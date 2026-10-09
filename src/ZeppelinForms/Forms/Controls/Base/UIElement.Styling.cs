using ZeppelinForms.Animation;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Styles: classes, pseudo-classes, scoped style collections, and the
/// part of the theme pass that applies them.</summary>
/// <remarks>
/// <para>
/// The ladder of value sources gains a step: explicit assignment, binding,
/// <b>style</b>, theme, the control's default. A style is applied inside the theme
/// pass, after the theme's rules, so the pass's bookkeeping — what it wrote, what it
/// withdraws at the end — covers styles without a second mechanism: a style that
/// stops matching simply isn't written in the next pass, and the property goes back
/// to the theme's value or the default.
/// </para>
/// <para>
/// While styles match, the pass is staged: writes of styled properties are collected
/// and committed once at the end, the last one of each property winning. Without
/// staging a theme rule and a style setting the same property would write it twice
/// per pass — every hover would flip the value to the theme's and back, start
/// a transition towards the wrong color and notify bindings and subscribers twice.
/// A pass without matching styles is not staged and costs what it cost before styles.
/// </para>
/// </remarks>
public abstract partial class UIElement
{
    // ===== classes =====

    private StyleClasses? _classes;

    /// <summary>The style classes of the element, for selectors like <c>Button.primary</c>.</summary>
    public StyleClasses Classes => _classes ??= new StyleClasses(OnClassChanged);

    internal StyleClasses? ClassesOrNull => _classes;

    private void OnClassChanged(string name)
    {
        (bool used, bool subtree) = StyleUsage.Of(name);

        if (used) Restyle(subtree);
    }

    // ===== scoped styles =====

    private Styles? _styles;

    /// <summary>How many elements have a style collection of their own: while there are
    /// none, the cascade doesn't walk the ancestors looking for them.</summary>
    private static int s_elementStylesCount;

    internal static int ElementStylesCount => Volatile.Read(ref s_elementStylesCount);

    /// <summary>Styles for this element and everything inside it. They beat the form's
    /// and the application's styles of equal specificity.</summary>
    public Styles Styles
    {
        get
        {
            if (_styles is null)
            {
                _styles = new Styles(() => Restyle(subtree: true));
                Interlocked.Increment(ref s_elementStylesCount);
            }

            return _styles;
        }
    }

    internal Styles? StylesOrNull => _styles;

    /// <summary>The styles that match the element now, weakest first: for the
    /// inspector and for tests.</summary>
    public IReadOnlyList<Style> GetMatchedStyles() => StyleCascade.Match(this).Styles;

    // ===== pseudo-classes =====

    /// <summary>Stored pseudo-classes, by <see cref="PseudoClass"/> index.</summary>
    private ulong[]? _pseudo;

    /// <summary>Whether the element is in this state now. Computed pseudo-classes
    /// are answered from the element's state, stored ones from what the control raised.</summary>
    public bool HasPseudoClass(PseudoClass pseudoClass)
    {
        ArgumentNullException.ThrowIfNull(pseudoClass);

        return pseudoClass.Computed is { } computed
            ? computed(this)
            : GetBit(_pseudo, pseudoClass.Index);
    }

    /// <summary>The pseudo-classes the element is in now, computed ones included.</summary>
    public IEnumerable<PseudoClass> PseudoClasses
    {
        get
        {
            foreach (PseudoClass pseudo in PseudoClass.Registered)
                if (HasPseudoClass(pseudo))
                    yield return pseudo;
        }
    }

    /// <summary>Raise or lower a stored pseudo-class. Restyles the element if some
    /// style asks about it — and only then.</summary>
    protected internal void SetPseudoClass(PseudoClass pseudoClass, bool on)
    {
        ArgumentNullException.ThrowIfNull(pseudoClass);

        if (pseudoClass.IsComputed)
            throw new InvalidOperationException(
                $"{pseudoClass} is computed from the element's state and can't be set.");

        if (GetBit(_pseudo, pseudoClass.Index) == on) return;

        if (on) SetBit(ref _pseudo, pseudoClass.Index);
        else ClearBit(_pseudo, pseudoClass.Index);

        OnPseudoClassChanged(pseudoClass);
    }

    /// <summary>A pseudo-class changed — stored, or the state a computed one is
    /// answered from. Restyles what the styles in use make depend on it.</summary>
    internal void OnPseudoClassChanged(PseudoClass pseudoClass)
    {
        (bool used, bool subtree) = StyleUsage.Of(pseudoClass);

        if (used) Restyle(subtree);
    }

    /// <summary>The focus is on this element or inside it.</summary>
    public bool IsFocusWithin
    {
        get
        {
            if (FindOwner()?.FocusedElementForAccessibility is not { } focused) return false;

            for (UIElement? current = focused; current is not null; current = current.Parent)
                if (ReferenceEquals(current, this)) return true;

            return false;
        }
    }

    /// <summary>A write of a styled property happened: keep the computed pseudo-classes
    /// that depend on it current. Called from every path that writes a value.</summary>
    private void OnStyledWriteForPseudoClasses(StyledProperty property)
    {
        if (ReferenceEquals(property, IsEnabledProperty))
        {
            // effective: disabling a panel disables what is inside it
            if (StyleUsage.Of(PseudoClass.Disabled).Used || StyleUsage.Of(PseudoClass.Enabled).Used)
                Restyle(subtree: true);
        }
        else if (ReferenceEquals(property, FlowDirectionProperty))
        {
            // inherited: the direction of a panel is the direction of its content
            if (StyleUsage.Of(PseudoClass.RightToLeft).Used || StyleUsage.Of(PseudoClass.LeftToRight).Used)
                Restyle(subtree: true);
        }
    }

    // ===== tree structure =====

    /// <summary>Whether the element has children: a panel with any, a wrapper with content.</summary>
    internal static bool HasChildren(UIElement element) => element switch
    {
        // a gallery draws its items rather than holding them as children
        ImageGallery gallery => gallery.Items.Count > 0,
        PanelControl panel => panel.Children.Count > 0,
        WrapControl wrap => wrap.Child is not null,
        _ => false,
    };

    /// <summary>The element's place among its siblings, zero-based, and their number.
    /// False — the element has no parent to count in.</summary>
    internal static bool TryGetSiblingPosition(UIElement element, out int index, out int count)
    {
        switch (element.Parent)
        {
            case PanelControl panel:
                index = panel.Children.IndexOf(element);
                count = panel.Children.Count;
                return index >= 0;

            case WrapControl wrap when ReferenceEquals(wrap.Child, element):
                index = 0;
                count = 1;
                return true;

            default:
                index = -1;
                count = 0;
                return false;
        }
    }

    /// <summary>The children of a panel or a wrapper changed: what selectors on places
    /// among siblings and on emptiness select may have changed too.</summary>
    /// <remarks>
    /// Not restyled on the spot but with the next layout pass, the way browsers
    /// recalculate styles once before a frame. Filling a list of a thousand rows adds
    /// a thousand children, and with <c>:odd</c> in use every addition would restyle
    /// every sibling — half a million theme passes for one list. Deferred, the panel
    /// is restyled once. Values the change affects are therefore current after
    /// <see cref="Form.UpdateLayout"/>, like the geometry.
    /// </remarks>
    internal void RestyleAfterChildrenChanged()
    {
        if (!StyleUsage.UsesPositions && !StyleUsage.Of(PseudoClass.Empty).Used) return;

        FindOwner()?.QueueStructureRestyle(this);
    }

    /// <summary>The deferred part of <see cref="RestyleAfterChildrenChanged"/>, run
    /// by the form before its layout pass.</summary>
    internal void RestyleStructureNow()
    {
        if (StyleUsage.Of(PseudoClass.Empty) is { Used: true } empty)
            Restyle(empty.Subtree);

        if (!StyleUsage.UsesPositions || this is not PanelControl panel) return;

        bool subtree = StyleUsage.UsesPositionsInContext;

        // a snapshot: restyling may run code that touches the collection
        foreach (UIElement child in panel.Children.ToArray())
            child.Restyle(subtree);
    }

    /// <summary>Visit the element and everything inside it, the same walk attaching
    /// to a form makes.</summary>
    internal static void VisitTree(UIElement root, Action<UIElement> action)
    {
        action(root);

        switch (root)
        {
            case WrapControl wrap when wrap.Child is not null:
                VisitTree(wrap.Child, action);
                break;

            case PanelControl panel:
                foreach (UIElement child in panel.Children.ToArray())
                    VisitTree(child, action);
                break;
        }
    }

    // ===== restyling =====

    /// <summary>A restyle was asked for while the element's own pass was running —
    /// a style changed a property a pseudo-class depends on. Run once more after it.</summary>
    private bool _restylePending;

    /// <summary>Apply the theme and the styles to the element again, now.</summary>
    /// <param name="subtree">And to everything inside it: a selector names what
    /// changed above its subject, so descendants' matches may change too.</param>
    internal void Restyle(bool subtree)
    {
        // not in a form yet: attaching applies the theme and the styles anyway
        if (FindOwner() is null) return;

        Theme theme = App.Theme;

        if (!subtree)
        {
            RestyleSelf(theme);
            return;
        }

        VisitTree(this, element => element.RestyleSelf(theme));
    }

    private void RestyleSelf(Theme theme)
    {
        if (ReferenceEquals(_themePassTarget, this))
        {
            _restylePending = true;
            return;
        }

        theme.Apply(this);
    }

    /// <summary>Called by the theme pass after it ends: run the one restyle that was
    /// asked for in the middle of it. Once — a style that keeps flipping the state
    /// it matches on would otherwise loop forever.</summary>
    internal void CompleteThemePass(Theme theme)
    {
        if (!_restylePending) return;

        _restylePending = false;
        theme.Apply(this);
        _restylePending = false;
    }

    // ===== the style's step on the ladder =====

    /// <summary>The fourth bit: the current value was written by a style.</summary>
    private ulong[]? _styled;

    /// <summary>The value comes from a style. Such a value is inherited past the
    /// theme's values of descendants, as a value set from code is.</summary>
    public bool IsSetByStyle(StyledProperty property) => GetBit(_styled, property.Index);

    /// <summary>Which step of the ladder the property's current value came from:
    /// for the inspector, for tests, and for whoever wonders why a style didn't apply.</summary>
    /// <remarks>An inherited property reports the element's own value; the value it
    /// shows may come from an ancestor.</remarks>
    public ValueSource GetValueSource(StyledProperty property)
    {
        if (IsLocal(property)) return ValueSource.Local;
        if (IsBound(property)) return ValueSource.Binding;
        if (IsSetByStyle(property)) return ValueSource.Style;
        if (GetBit(_themed, property.Index)) return ValueSource.Theme;

        return HasValue(property) && ControlDefaults.ContainsKey((GetType(), property.Index))
            ? ValueSource.ControlDefault
            : ValueSource.Default;
    }

    /// <summary>Write a style's value: the theme pass is running, and the write is
    /// staged like the theme's own.</summary>
    internal void WriteStyleValue<T>(StyledProperty<T> property, T value) =>
        SetValue(property, value);

    // ===== staging =====

    [ThreadStatic] private static UIElement? s_stagingTarget;
    [ThreadStatic] private static List<StagedWrite>? s_staged;
    [ThreadStatic] private static bool s_stagingFromStyle;

    /// <summary>What a staged pass replaced and must restore: a theme rule may attach
    /// a child, and the child's own pass stages for the child.</summary>
    internal readonly record struct StagingState(UIElement? OuterTarget, List<StagedWrite>? OuterList, bool OuterFromStyle);

    /// <summary>Whether the writes being staged come from a style rather than from
    /// the theme's rules.</summary>
    internal static bool StagingFromStyle
    {
        set => s_stagingFromStyle = value;
    }

    internal StagingState BeginStaging()
    {
        var outer = new StagingState(s_stagingTarget, s_staged, s_stagingFromStyle);

        s_stagingTarget = this;
        s_staged = [];
        s_stagingFromStyle = false;

        return outer;
    }

    /// <summary>Write what was staged, once per property. Staging stops first:
    /// the commit goes through the same setters, which would stage again.</summary>
    internal void CommitStaging(StagingState outer)
    {
        List<StagedWrite> staged = s_staged ?? [];

        EndStaging(outer);

        foreach (StagedWrite write in staged)
            write.Commit(this);
    }

    internal static void EndStaging(StagingState outer)
    {
        s_stagingTarget = outer.OuterTarget;
        s_staged = outer.OuterList;
        s_stagingFromStyle = outer.OuterFromStyle;
    }

    /// <summary>Stage a write if this element's pass is staged. False — write now.</summary>
    private bool TryStage<T>(StyledProperty<T> property, T value)
    {
        if (!ReferenceEquals(s_stagingTarget, this) || s_staged is not { } staged) return false;

        var write = new StagedWrite<T>(property, value, s_stagingFromStyle);

        for (int i = 0; i < staged.Count; i++)
            if (staged[i].Index == property.Index)
            {
                staged[i] = write;
                return true;
            }

        staged.Add(write);
        return true;
    }

    internal abstract class StagedWrite(int index, bool fromStyle)
    {
        public int Index { get; } = index;
        public bool FromStyle { get; } = fromStyle;

        public abstract void Commit(UIElement element);
    }

    private sealed class StagedWrite<T>(StyledProperty<T> property, T value, bool fromStyle)
        : StagedWrite(property.Index, fromStyle)
    {
        public override void Commit(UIElement element)
        {
            // still the theme pass: the source bookkeeping marks it as the theme's,
            // and the style's own bit is put on top
            if (element.IsLocal(property) || element.IsBound(property)) return;

            element.SetValue(property, value);

            if (FromStyle) SetBit(ref element._styled, Index);
        }
    }

    // ===== transitions from styles =====

    /// <summary>The transition rules of the matched styles, weakest first.</summary>
    private Transition[]? _styleTransitions;

    internal void SetStyleTransitions(Transition[]? transitions) => _styleTransitions = transitions;

    /// <summary>The rule for a property: the element's own first, then the strongest
    /// matched style's.</summary>
    private Transition? FindTransitionRule(StyledProperty property)
    {
        if (_transitionRules is not null)
            foreach (Transition candidate in _transitionRules)
                if (ReferenceEquals(candidate.Property, property))
                    return candidate;

        if (_styleTransitions is not null)
            for (int i = _styleTransitions.Length - 1; i >= 0; i--)
                if (ReferenceEquals(_styleTransitions[i].Property, property))
                    return _styleTransitions[i];

        return null;
    }
}