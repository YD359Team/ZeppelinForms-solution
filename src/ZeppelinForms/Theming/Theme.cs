using System.Runtime.CompilerServices;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Theming;

public sealed class Theme
{
    /// <summary>Rules per element type. A rule takes the theme it is applied from
    /// rather than capturing one: that is what lets <see cref="WithColors"/> copy
    /// the rules into another theme — a captured theme would keep painting the copy
    /// in the original's colors.</summary>
    private readonly Dictionary<Type, Action<UIElement, Theme>> _appliers = [];

    /// <summary>
    /// Ready-made applier chains per element type, from base to derived.
    /// Computed once per type: a chain depends only on the hierarchy and
    /// the set of appliers. Previously every element got its own Stack
    /// and walked all base types again with a dictionary lookup at each
    /// step — on a form with three hundred labels that was three hundred
    /// stacks and three hundred walks.
    /// </summary>
    private readonly Dictionary<Type, Action<UIElement, Theme>[]> _chains = [];

    /// <summary>Guards both <see cref="_appliers"/> and <see cref="_chains"/>:
    /// a chain is built from the appliers, so reading one while the other
    /// is being changed is the same race as reading a half-written dictionary.</summary>
    private readonly System.Threading.Lock _chainSync = new();

    public required string Name { get; init; }
    public required ThemeColors Colors { get; init; }
    public Font BaseFont { get; init; } = Font.Default;

    /// <summary>Shape tokens: roundings and stroke thicknesses.</summary>
    public ThemeMetrics Metrics { get; init; } = ThemeMetrics.Default;

    /// <summary>The type ramp elements pick their text style from.</summary>
    public TypeRamp TypeRamp { get; init; } = TypeRamp.Default;

    /// <summary>How to style a control of this type. Derived types pick up
    /// the ancestor's styling when they have none of their own.</summary>
    /// <remarks>
    /// The rule gets the whole theme: colors, shape tokens, the type ramp.
    /// The priority attribute settles a lambda that fits both overloads — one
    /// that ignores its second parameter, <c>(x, _) =&gt; ...</c>: without it such a
    /// call would stop compiling the moment this overload appeared. A lambda that
    /// uses the parameter fits only one of them and is not affected.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public Theme For<T>(Action<T, Theme> apply) where T : UIElement
    {
        // the set of appliers changed — the computed chains no longer
        // describe the theme. Both changes happen under one lock:
        // GetChain reads _appliers and must not see it mid-mutation
        lock (_chainSync)
        {
            _appliers[typeof(T)] = (element, theme) => apply((T)element, theme);
            _chains.Clear();
        }

        return this;
    }

    /// <summary>How to style a control of this type, from the colors alone.
    /// Derived types pick up the ancestor's styling when they have none of their own.</summary>
    public Theme For<T>(Action<T, ThemeColors> apply) where T : UIElement =>
        For<T>((T element, Theme theme) => apply(element, theme.Colors));

    /// <summary>A copy of the theme with another palette: the same rules,
    /// shapes, font and type ramp.</summary>
    /// <remarks>
    /// The copy is independent: a rule added to either theme afterwards
    /// doesn't appear in the other. The name is kept unless a new one is given —
    /// a theme recolored from the system accent is still the same theme to the user.
    /// </remarks>
    public Theme WithColors(ThemeColors colors, string? name = null)
    {
        var copy = new Theme
        {
            Name = name ?? Name,
            Colors = colors,
            BaseFont = BaseFont,
            Metrics = Metrics,
            TypeRamp = TypeRamp,
        };

        // only the rules are copied: the chains are derived from them and are
        // built by the copy itself on first use
        lock (_chainSync)
        {
            foreach (KeyValuePair<Type, Action<UIElement, Theme>> rule in _appliers)
                copy._appliers[rule.Key] = rule.Value;
        }

        return copy;
    }

    /// <summary>A copy of the theme with another accent color;
    /// see <see cref="ThemeColors.WithAccent"/> for what follows the accent.</summary>
    public Theme WithAccent(Color accent) => WithColors(Colors.WithAccent(accent));

    internal void Apply(UIElement element)
    {
        Action<UIElement, Theme>[] chain = GetChain(element.GetType());

        if (chain.Length == 0) return;

        // for the duration of the walk, setters mark entries as "from the theme".
        // The previous value is saved rather than reset to false:
        // an applier may create or attach a child element, and the nested
        // call would otherwise clear the flag for the outer walk
        bool wasApplying = UIElement.ApplyingTheme;
        UIElement.ApplyingTheme = true;

        try
        {
            // from base type to derived: a specialization adds to
            // the general styling rather than replacing it wholesale
            foreach (Action<UIElement, Theme> apply in chain)
                apply(element, this);
        }
        finally
        {
            UIElement.ApplyingTheme = wasApplying;
        }
    }

    /// <summary>The applier chain for a type, from base to derived.</summary>
    private Action<UIElement, Theme>[] GetChain(Type type)
    {
        // the whole computation runs under the lock: it reads _appliers,
        // which For may be changing on another thread at this very moment.
        // A chain is built once per type, so holding the lock for the walk
        // costs nothing noticeable
        lock (_chainSync)
        {
            if (_chains.TryGetValue(type, out Action<UIElement, Theme>[]? cached))
                return cached;

            var chain = new List<Action<UIElement, Theme>>();

            for (Type? current = type; current is not null; current = current.BaseType)
            {
                if (_appliers.TryGetValue(current, out Action<UIElement, Theme>? apply))
                    chain.Add(apply);
            }

            // collected from derived to base — reverse it,
            // the order of application is the opposite
            chain.Reverse();

            Action<UIElement, Theme>[] result = chain.Count == 0 ? [] : [.. chain];

            _chains[type] = result;

            return result;
        }
    }

    internal static void Apply(UIElement element, ControlStyle style)
    {
        if (style.Background is Color bg) element.Background = bg;
        if (style.CornerRadius is CornerRadius radius) element.CornerRadius = radius;

        if (element is DecoratedControl decorated)
        {
            if (style.Border is Color border) decorated.BorderColor = border;
        }

        if (element is InteractiveControl interactive)
        {
            if (style.BorderFocus is Color focus) interactive.FocusBorderColor = focus;
        }

        if (element is ITextElement text)
        {
            if (style.Text is Color color) text.TextColor = color;
        }
    }
}