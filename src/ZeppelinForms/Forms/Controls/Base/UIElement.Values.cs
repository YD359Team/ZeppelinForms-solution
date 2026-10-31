using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>The value source of styled properties: whether a value was set, by whom,
/// and how it is read, inherited and cleared.</summary>
public abstract partial class UIElement
{
    // Values live in ordinary fields; only the source is here: two bits
    // per property — "was ever set" and "was set from user code". The third,
    // "set by the current theme", lives in UIElement.ThemeValues.cs.
    private ulong[]? _assigned;
    private ulong[]? _local;

    /// <summary>The theme is being applied: setters mark writes as "from the theme",
    /// so that the theme doesn't overwrite values the user set explicitly.</summary>
    /// <remarks>ThreadStatic, for the same reason as the brush pool in SkiaGraphics:
    /// xunit snapshot tests run in parallel, and a shared flag would make setters
    /// on one thread treat values as theme values while the theme is being
    /// applied on another.</remarks>
    [ThreadStatic]
    internal static bool ApplyingTheme;

    private static bool GetBit(ulong[]? bits, int index) =>
        bits is not null && (index >> 6) < bits.Length && (bits[index >> 6] & (1UL << index)) != 0;

    private static void SetBit(ref ulong[]? bits, int index)
    {
        int word = index >> 6;

        if (bits is null) bits = new ulong[word + 1];
        else if (word >= bits.Length) Array.Resize(ref bits, word + 1);

        bits[word] |= 1UL << index;
    }

    private static void ClearBit(ulong[]? bits, int index)
    {
        if (bits is null || (index >> 6) >= bits.Length) return;

        bits[index >> 6] &= ~(1UL << index);
    }

    /// <summary>The value has been set: it is neither the default nor inherited.</summary>
    public bool HasValue(StyledProperty property) => GetBit(_assigned, property.Index);

    /// <summary>The value was set from code — the theme won't touch it anymore.</summary>
    public bool IsLocal(StyledProperty property) => GetBit(_local, property.Index);

    /// <summary>Write the control's own default. The theme overrides such a value,
    /// and user code all the more so. For constructors only: a regular assignment
    /// from them would mark the property as set manually and close it
    /// to the theme forever.</summary>
    protected void SetControlDefault<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property, T value)
    {
        property.Write(this, value);

        SetBit(ref _assigned, property.Index);
        ClearBit(_local, property.Index);
        ClearBit(_themed, property.Index);
        ClearBit(_styled, property.Index);

        // remembered for the type: a value the theme stops setting, or the user
        // clears, goes back to this default rather than to the property's
        RememberControlDefault(property, value);
    }

    /// <summary>A bridge for property editors: write the value the same way
    /// a regular assignment would.</summary>
    internal bool SetStyledValue<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property, T value) =>
            SetValue(property, value);

    /// <summary>Write a value taking its source into account.
    /// false — the write was rejected: the theme is writing, and the property
    /// was set manually or is controlled by a binding.</summary>
    protected bool SetValue<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property, ref T storage, T value)
    {
        // The ladder of sources, top to bottom: explicit assignment, binding,
        // style, theme, the control's default. The theme and the styles override
        // neither the first nor the second.
        if (ApplyingTheme && (IsLocal(property) || IsBound(property))) return false;

        // a pass with matching styles collects its writes and commits each
        // property once at the end, see UIElement.Styling.cs
        if (ApplyingTheme && TryStage(property, value)) return true;

        bool assigned = HasValue(property);

        if (assigned && EqualityComparer<T>.Default.Equals(storage, value))
        {
            // same value, but the source may have changed: the user assigned
            // exactly what the theme had already put there — and now it is theirs
            if (!ApplyingTheme)
            {
                SetBit(ref _local, property.Index);
                ClearBit(_themed, property.Index);
            }

            // the theme set the value it already had: it still owns it, and the
            // end of the pass must not take it for a value the theme dropped
            else NoteThemeWrite(property.Index);

            // whoever wrote it now, it is not the style's unless a staged style
            // write puts the bit back right after this call
            ClearBit(_styled, property.Index);

            return false;
        }

        // an explicit assignment from code is the top of the ladder. In OneWay
        // it breaks the binding: otherwise the next change of the source would
        // silently overwrite what the user wrote, and they wouldn't understand why.
        // In TwoWay the value goes to the source, and the binding stays.
        if (!ApplyingTheme && IsBound(property))
            PushOrBreakBinding(property, value);

        // the transition starts from the previous visible value — before the write
        BeginTransition(property, storage);
        storage = value;

        SetBit(ref _assigned, property.Index);
        ClearBit(_styled, property.Index);

        if (ApplyingTheme)
        {
            ClearBit(_local, property.Index);
            NoteThemeWrite(property.Index);
        }
        else
        {
            SetBit(ref _local, property.Index);
            ClearBit(_themed, property.Index);
        }

        // the element's own reaction goes before the invalidation,
        // see OnStyledPropertyChanged
        OnStyledPropertyChanged(property);

        if (property.AffectsLayout) InvalidateLayoutFor(property);
        else InvalidateVisual();

        OnStyledWriteForPseudoClasses(property);

        RaisePropertyChanged(property);
        return true;
    }

    /// <summary>For properties without a field of their own: the value lives
    /// in another object, and here we only route it. The write goes through
    /// the property's delegate, so no ref storage is needed.</summary>
    protected bool SetValue<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property, T value)
    {
        if (ApplyingTheme && (IsLocal(property) || IsBound(property))) return false;

        if (ApplyingTheme && TryStage(property, value)) return true;

        bool assigned = HasValue(property);

        if (assigned && EqualityComparer<T>.Default.Equals(property.GetValue(this), value))
        {
            if (!ApplyingTheme)
            {
                SetBit(ref _local, property.Index);
                ClearBit(_themed, property.Index);
            }
            else NoteThemeWrite(property.Index);

            ClearBit(_styled, property.Index);

            return false;
        }

        if (!ApplyingTheme && IsBound(property))
            PushOrBreakBinding(property, value);

        BeginTransition(property, property.GetValue(this));
        property.Write(this, value);

        SetBit(ref _assigned, property.Index);
        ClearBit(_styled, property.Index);

        if (ApplyingTheme)
        {
            ClearBit(_local, property.Index);
            NoteThemeWrite(property.Index);
        }
        else
        {
            SetBit(ref _local, property.Index);
            ClearBit(_themed, property.Index);
        }

        OnStyledPropertyChanged(property);

        if (property.AffectsLayout) InvalidateLayoutFor(property);
        else InvalidateVisual();

        OnStyledWriteForPseudoClasses(property);

        RaisePropertyChanged(property);
        return true;
    }

    /// <summary>The value of a styled property changed. A hook for reactions that
    /// the write itself doesn't express: stop an animation on hide, reconcile
    /// dependent values, recompute a cache.</summary>
    /// <remarks>
    /// Called before the element requests layout or a redraw. Win32 and X11 used
    /// to paint right inside Invalidate, and a reaction that ran after it left that
    /// frame drawn from stale state: Label measured its new text with the lines
    /// split from the old one and lagged one keystroke behind a bound TextBox.
    /// Both paint deferred now, but the order stays: nothing guarantees that no
    /// code reads the element between the invalidation and the reaction —
    /// UpdateLayout, a handler, a future platform.
    /// </remarks>
    protected virtual void OnStyledPropertyChanged(StyledProperty property) { }

    /// <summary>Tell outside subscribers that a styled property changed. Goes last,
    /// after the element's own reaction and the invalidation: by the time
    /// a subscriber reads the value, the element is already consistent.</summary>
    // one instance per property, made with it: the arguments carry nothing but the
    // name, and every change of a property with a subscriber — a binding to this
    // element, an inspector — made new ones
    private void RaisePropertyChanged(StyledProperty property) =>
        PropertyChanged?.Invoke(this, property.ChangedEventArgs);

    /// <summary>Forget the manually set value and hand control back to the theme.</summary>
    public void ClearValue<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property)
    {
        // a binding is a value source too, and clearing removes it along with
        // the rest: otherwise the source would keep writing into a property
        // that is considered cleared
        Unbind(property);

        ClearBit(_local, property.Index);
        ClearBit(_themed, property.Index);
        ClearBit(_styled, property.Index);

        // back to the control's own default where it has one: the property's
        // default used to be written here, and a cleared Padding of a button
        // came back as zero instead of the button's 14×6
        T fallback = DefaultFor(property, out bool own);

        if (own) SetBit(ref _assigned, property.Index);
        else ClearBit(_assigned, property.Index);

        property.Write(this, fallback);

        // the write above bypassed SetValue, so the element's reaction is called
        // by hand: without it the OnStyledPropertyChanged reactions (Label's line
        // cache, Loader's animation) would keep the state from before the clear.
        // It goes before the theme: if the theme sets a value of its own, that
        // write brings its own notification on top of a consistent element
        OnStyledPropertyChanged(property);

        // the theme may have a value of its own — ask it again
        App.Theme.Apply(this);

        if (property.AffectsLayout) InvalidateLayoutFor(property);
        else InvalidateVisual();

        RaisePropertyChanged(property);
    }

    /// <summary>The value taking inheritance into account.
    /// A value set manually — on the element itself or on any ancestor — beats
    /// what the theme gave this element. Otherwise panel.TextColor would never
    /// reach the nested labels: the theme sets their color on each of them individually.</summary>
    /// <remarks>
    /// A binding counts the same as a manual value: on the ladder of sources it
    /// stands above the theme. Previously only explicit assignment was checked,
    /// so an element's own bound value lost to an ancestor's explicit one,
    /// A style's value stands above the theme too: <c>StackPanel.dark { TextColor }</c>
    /// reaches the labels inside, as CSS inheritance does, while a label's own
    /// style still beats it — the walk meets the element itself first.
    /// </remarks>
    public T GetInheritedValue<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property)
    {
        for (UIElement? current = this; current is not null; current = current.Parent)
            if (current.IsLocal(property) || current.IsBound(property) || current.IsSetByStyle(property))
                return property.GetValue(current);

        if (HasValue(property))
            return property.GetValue(this);

        return property.DefaultValue;
    }
}
