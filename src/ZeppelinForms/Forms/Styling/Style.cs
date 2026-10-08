using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// Property values for the elements a selector matches. Styles sit between the theme
/// and the application's own code: a style beats the theme, and a value set from code
/// or by a binding beats the style.
/// </summary>
/// <example>
/// <code>
/// App.Styles.Add(new Style("Button.primary")
/// {
///     [ButtonBase.BackgroundColorProperty] = Style.FromTheme(t =&gt; t.Colors.Accent),
///     [UIElement.TextColorProperty] = Style.FromTheme(t =&gt; t.Colors.TextOnAccent),
///     Transitions = { Transition.Ease(ButtonBase.BackgroundColorProperty, 150) },
/// });
///
/// App.Styles.Add(new Style("Button.primary:hover")
///     .Set(ButtonBase.BackgroundColorProperty, t =&gt; t.Colors.AccentHover));
/// </code>
/// </example>
/// <remarks>
/// <para>
/// A style is applied in the same pass as the theme, after it: whatever both set,
/// the element receives once, with the style's value. That is why a style that
/// stops matching — the pointer left, the class was removed — gives the property
/// back to the theme by itself, through a transition if there is one.
/// </para>
/// <para>
/// A setter for a property the element doesn't have is skipped:
/// <c>*.accent { BackgroundColor }</c> colors buttons and leaves labels alone.
/// </para>
/// <para>
/// While the system's high contrast is on, setters of colors are skipped, as
/// browsers ignore author colors in forced-colors mode: the person needs the
/// system's colors more than the application needs its own. Shapes and sizes
/// still apply.
/// </para>
/// </remarks>
public sealed class Style
{
    private readonly List<Setter> _setters = [];
    private readonly TransitionList _transitions;

    /// <summary>Raised when a setter or a transition is added or replaced.</summary>
    internal event EventHandler? Changed;

    public Style(string selector) : this(Selector.Parse(selector))
    {
    }

    public Style(Selector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        Selector = selector;
        _transitions = new TransitionList(this);
    }

    public Selector Selector { get; }

    public IReadOnlyList<Setter> Setters => _setters;

    /// <summary>Transition rules for the elements the style matches, as if they were in
    /// each element's own <see cref="UIElement.Transitions"/>. The element's own rules
    /// win over a style's, and a more specific style's over a less specific one's.</summary>
    /// <remarks>
    /// Put them on the style that matches in both states: a rule on
    /// <c>Button:hover</c> alone animates the way in and not the way out, because
    /// when the pointer leaves, that style no longer applies — as in CSS.
    /// </remarks>
    public IList<Transition> Transitions => _transitions;

    /// <summary>Where the style was declared — a style sheet's file and line.
    /// Shown by the inspector; null for styles made in code.</summary>
    public string? Source { get; init; }

    /// <summary>A value of a property, read or written without knowing its type.
    /// Writing checks the type: a <c>float</c> where a <c>Color</c> is expected
    /// throws at once rather than when the style is applied. A
    /// <see cref="FromTheme{T}"/> value is taken from the theme on application.</summary>
    public object? this[StyledProperty property]
    {
        get
        {
            foreach (Setter setter in _setters)
                if (ReferenceEquals(setter.Property, property))
                    return setter.Value;

            return null;
        }
        set => Add(property.CreateSetter(value));
    }

    /// <summary>Set a value.</summary>
    public Style Set<T>(StyledProperty<T> property, T value)
    {
        Add(new Setter<T>(property, value));
        return this;
    }

    /// <summary>Set a value taken from the theme the style is applied with: switching
    /// to a dark theme recolors the style without touching it.</summary>
    public Style Set<T>(StyledProperty<T> property, Func<Theme, T> fromTheme)
    {
        Add(new Setter<T>(property, fromTheme));
        return this;
    }

    /// <summary>A value for the indexer that is taken from the theme on application:
    /// <c>[ButtonBase.BackgroundColorProperty] = Style.FromTheme(t =&gt; t.Colors.Accent)</c>.</summary>
    public static ThemeValue<T> FromTheme<T>(Func<Theme, T> pick) => new(pick);

    /// <summary>Add a setter, replacing one for the same property.</summary>
    public void Add(Setter setter)
    {
        ArgumentNullException.ThrowIfNull(setter);

        int existing = _setters.FindIndex(s => ReferenceEquals(s.Property, setter.Property));

        if (existing >= 0) _setters[existing] = setter;
        else _setters.Add(setter);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Remove the setter of a property. False — the style has none.</summary>
    public bool Remove(StyledProperty property)
    {
        int index = _setters.FindIndex(s => ReferenceEquals(s.Property, property));

        if (index < 0) return false;

        _setters.RemoveAt(index);
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    public override string ToString() => $"{Selector} {{ {_setters.Count} setters }}";

    internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>A list of transitions that tells its style about changes: a rule
    /// added to a style already in use must reach the elements it styles.</summary>
    private sealed class TransitionList(Style owner) : System.Collections.ObjectModel.Collection<Transition>
    {
        protected override void InsertItem(int index, Transition item)
        {
            ArgumentNullException.ThrowIfNull(item);
            base.InsertItem(index, item);
            owner.RaiseChanged();
        }

        protected override void SetItem(int index, Transition item)
        {
            ArgumentNullException.ThrowIfNull(item);
            base.SetItem(index, item);
            owner.RaiseChanged();
        }

        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);
            owner.RaiseChanged();
        }

        protected override void ClearItems()
        {
            base.ClearItems();
            owner.RaiseChanged();
        }
    }
}

/// <summary>One property value of a <see cref="Style"/>.</summary>
public abstract class Setter
{
    private protected Setter(StyledProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        Property = property;
    }

    public StyledProperty Property { get; }

    /// <summary>The value, or the <see cref="ThemeValue{T}"/> it is taken from.</summary>
    public abstract object? Value { get; }

    /// <summary>Whether the value comes from the theme.</summary>
    public abstract bool IsFromTheme { get; }

    /// <summary>The value for a theme, boxed: for the inspector.</summary>
    public abstract object? Resolve(Theme theme);

    /// <summary>Write the value into the element, inside its theme pass.</summary>
    internal abstract void Apply(UIElement element, Theme theme);

    /// <summary>A color yields to the system's high contrast, see <see cref="Style"/>.</summary>
    private protected static bool YieldsToHighContrast(Type valueType) =>
        App.IsHighContrastActive && (valueType == typeof(Color) || valueType == typeof(Color?));
}

/// <summary>A typed setter: a value, or a way to take it from the theme.</summary>
public sealed class Setter<T> : Setter
{
    private readonly T _value = default!;
    private readonly Func<Theme, T>? _fromTheme;

    public Setter(StyledProperty<T> property, T value) : base(property)
    {
        _value = value;
    }

    public Setter(StyledProperty<T> property, Func<Theme, T> fromTheme) : base(property)
    {
        ArgumentNullException.ThrowIfNull(fromTheme);
        _fromTheme = fromTheme;
    }

    public new StyledProperty<T> Property => (StyledProperty<T>)base.Property;

    public override object? Value => _fromTheme is null ? _value : new ThemeValue<T>(_fromTheme);

    public override bool IsFromTheme => _fromTheme is not null;

    public override object? Resolve(Theme theme) => _fromTheme is null ? _value : _fromTheme(theme);

    internal override void Apply(UIElement element, Theme theme)
    {
        // a property of another branch of the hierarchy: the style matched by
        // a broad selector, and this element simply doesn't have it
        if (!Property.OwnerType.IsInstanceOfType(element)) return;

        if (YieldsToHighContrast(typeof(T))) return;

        element.WriteStyleValue(Property, _fromTheme is null ? _value : _fromTheme(theme));
    }
}

/// <summary>A style value taken from the theme the style is applied with.
/// Made by <see cref="Style.FromTheme{T}"/>.</summary>
public sealed class ThemeValue<T>(Func<Theme, T> pick) : IThemeValue
{
    public Func<Theme, T> Pick { get; } = pick ?? throw new ArgumentNullException(nameof(pick));

    Type IThemeValue.ValueType => typeof(T);

    object? IThemeValue.Resolve(Theme theme) => Pick(theme);
}

/// <summary>The untyped face of <see cref="ThemeValue{T}"/>: the style's boxed
/// indexer and the style sheets work with it.</summary>
public interface IThemeValue
{
    Type ValueType { get; }

    object? Resolve(Theme theme);
}