using System.Collections.Concurrent;
using System.Numerics;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Values the theme owns: which properties the current theme set on the
/// element, and what each of them goes back to when a theme no longer sets it.</summary>
/// <remarks>
/// <para>
/// A theme used to only write. Switching themes applied the new rules over the old
/// values, and whatever the old theme had set but the new one doesn't stayed behind:
/// back from FluentLight to Light, a check box kept Fluent's 20 px box, a button
/// stayed without its ripple. The classic themes never noticed — they set the same
/// properties — but Fluent shapes controls the classic themes leave alone.
/// </para>
/// <para>
/// So every theme pass remembers what it wrote, and at its end gives back what the
/// previous pass wrote and this one didn't: to the control's own default if it has
/// one, otherwise to the property's. A value set from code or by a binding is not
/// the theme's and is never touched.
/// </para>
/// </remarks>
public abstract partial class UIElement
{
    /// <summary>The third bit: the current theme wrote this value during the
    /// element's own theme pass.</summary>
    private ulong[]? _themed;

    /// <summary>The element whose theme pass is running on this thread. Only its
    /// writes are recorded: a rule may touch another element too — a child it
    /// creates — and such a write must not be withdrawn by that element's own pass,
    /// which knows nothing about it.</summary>
    [ThreadStatic]
    private static UIElement? _themePassTarget;

    /// <summary>The control's own defaults, by concrete type and property.</summary>
    /// <remarks>
    /// Per type, not per element: SetControlDefault is called from constructors,
    /// and a type's constructor writes the same defaults into every instance — that
    /// is what makes them the control's defaults. Keyed by the concrete type, so a
    /// derived constructor that restates a base default wins for its own type: the
    /// base constructor runs first and the derived one overwrites the entry.
    /// </remarks>
    private static readonly ConcurrentDictionary<(Type Type, int Index), object?> ControlDefaults = new();

    /// <summary>What a theme pass has to restore when it ends.</summary>
    internal readonly record struct ThemePassState(UIElement? OuterTarget, ulong[]? Previous);

    /// <summary>Start a theme pass: the values written from here on are this pass's.</summary>
    internal ThemePassState BeginThemePass()
    {
        var state = new ThemePassState(_themePassTarget, _themed);

        _themed = null;
        _themePassTarget = this;

        return state;
    }

    /// <summary>End a theme pass: withdraw what the previous pass wrote and this one
    /// didn't. Runs after the new values are written, not before — a reset first
    /// would start a transition from the default instead of the old theme's value,
    /// and every switch would flash the defaults for a frame.</summary>
    internal void EndThemePass(ThemePassState state)
    {
        _themePassTarget = state.OuterTarget;

        ulong[]? previous = state.Previous;

        if (previous is null) return;

        for (int word = 0; word < previous.Length; word++)
        {
            ulong current = _themed is not null && word < _themed.Length ? _themed[word] : 0UL;
            ulong dropped = previous[word] & ~current;

            while (dropped != 0UL)
            {
                int index = word * 64 + BitOperations.TrailingZeroCount(dropped);
                dropped &= dropped - 1UL;

                StyledProperty property = StyledProperty.ByIndex(index);

                // taken over by code or a binding since: no longer the theme's
                if (IsLocal(property) || IsBound(property)) continue;

                property.WithdrawThemeValue(this);
            }
        }
    }

    /// <summary>Record a theme write: only inside this element's own pass.</summary>
    private void NoteThemeWrite(int index)
    {
        if (ReferenceEquals(_themePassTarget, this))
            SetBit(ref _themed, index);
    }

    /// <summary>Remember a control's default for its type, see <see cref="ControlDefaults"/>.</summary>
    private void RememberControlDefault<T>(StyledProperty<T> property, T value) =>
        ControlDefaults[(GetType(), property.Index)] = value;

    /// <summary>The value the property goes back to: the control's own default
    /// if its type has one, otherwise the property's. <paramref name="own"/> —
    /// whether it is the control's own.</summary>
    private T DefaultFor<T>(StyledProperty<T> property, out bool own)
    {
        own = ControlDefaults.TryGetValue((GetType(), property.Index), out object? boxed);

        if (!own) return property.DefaultValue;

        // the null trap again: "null is T" is false even for a nullable T
        return boxed is T typed ? typed : default!;
    }

    /// <summary>Put the property back to its default after the theme stopped
    /// setting it — as a visible change: with a transition, the element's own
    /// reaction, the invalidation and the notification which properties the 
    /// current theme — and the styles applied in its pass — set on the element, 
    /// and what each of them goes back to when neither sets it any longer.</summary>
    internal void WithdrawThemeValue<T>(StyledProperty<T> property)
    {
        T fallback = DefaultFor(property, out bool own);
        T current = property.GetValue(this);

        // without a default of its own the property returns to "never set":
        // the same state it was in before any theme touched it
        if (!own) ClearBit(_assigned, property.Index);

        // neither the theme nor a style sets it any longer
        ClearBit(_styled, property.Index);

        if (EqualityComparer<T>.Default.Equals(current, fallback)) return;

        BeginTransition(property, current);
        property.Write(this, fallback);

        OnStyledPropertyChanged(property);

        if (property.AffectsLayout) InvalidateLayoutFor(property);
        else InvalidateVisual();

        OnStyledWriteForPseudoClasses(property);

        RaisePropertyChanged(property);
    }
}