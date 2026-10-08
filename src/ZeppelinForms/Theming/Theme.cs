using System.Runtime.CompilerServices;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;

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

    /// <summary>How the theme takes another accent: the palette it had and the new
    /// accent in, the palette with the accent in place out. By default —
    /// <see cref="ThemeColors.WithAccent"/> with the accent as it is.</summary>
    /// <remarks>
    /// A theme decides this, not the palette: the same accent goes into a classic
    /// theme as it is, with darker hover shades, while a Fluent theme takes the
    /// shade for its page — Dark1 on light, Light2 on dark — and lightens it on
    /// hover, as WinUI does. Without the rule <see cref="App.UseSystemTheme(Theme, Theme, bool)"/>
    /// would have to know which kind of theme it was given.
    /// </remarks>
    public Func<ThemeColors, AccentPalette, ThemeColors> AccentRule { get; init; } = ClassicAccent;

    /// <summary>The default <see cref="AccentRule"/>: the accent as it is.</summary>
    internal static ThemeColors ClassicAccent(ThemeColors colors, AccentPalette accent) =>
        colors.WithAccent(accent.Accent);

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
            AccentRule = AccentRule,
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

    /// <summary>A copy of the theme with another accent color, taken in
    /// by the theme's <see cref="AccentRule"/>.</summary>
    public Theme WithAccent(Color accent) => WithAccent(new AccentPalette(accent));

    /// <summary>A copy of the theme with another accent and the shades the system
    /// gave with it, taken in by the theme's <see cref="AccentRule"/>.</summary>
    public Theme WithAccent(AccentPalette accent) => WithColors(AccentRule(Colors, accent));

    /// <summary>Style an element: the theme's rules, then the styles that match it.</summary>
    /// <remarks>
    /// <para>
    /// Styles are applied in the theme's pass rather than in one of their own, so the
    /// pass's bookkeeping covers them: what the pass doesn't write any more — a rule
    /// of the previous theme, a style that stopped matching — goes back to the
    /// default at its end. See UIElement.Styling.cs for the ladder of sources.
    /// </para>
    /// <para>
    /// With matching styles the pass is staged: each property is written once, with
    /// the strongest value, instead of the theme's and then the style's.
    /// </para>
    /// </remarks>
    internal void Apply(UIElement element)
    {
        Action<UIElement, Theme>[] chain = GetChain(element.GetType());

        // matched before the pass starts: matching reads the tree and the element's
        // state, not its styled values, and a pass with nothing to match is not staged
        StyleCascade.Result styles = StyleCascade.Match(element);

        // the pass runs even without rules: an element the previous theme
        // styled must give those values back when this one has nothing for it
        UIElement.ThemePassState pass = element.BeginThemePass();

        // for the duration of the walk, setters mark entries as "from the theme".
        // The previous value is saved rather than reset to false:
        // an applier may create or attach a child element, and the nested
        // call would otherwise clear the flag for the outer walk
        bool wasApplying = UIElement.ApplyingTheme;
        UIElement.ApplyingTheme = true;

        bool staged = styles.Styles.Length > 0;
        UIElement.StagingState outer = staged ? element.BeginStaging() : default;

        try
        {
            // from base type to derived: a specialization adds to
            // the general styling rather than replacing it wholesale
            foreach (Action<UIElement, Theme> apply in chain)
                apply(element, this);

            // before anything is committed: the commit starts the transitions,
            // and the matched styles' rules must already be in place
            element.SetStyleTransitions(styles.Transitions);

            if (staged)
            {
                UIElement.StagingFromStyle = true;

                // weakest first: a later write of the same property replaces
                // the staged one, so the strongest style wins
                foreach (Style style in styles.Styles)
                    foreach (Setter setter in style.Setters)
                        setter.Apply(element, this);

                staged = false;
                element.CommitStaging(outer);
            }
        }
        finally
        {
            // an exception in a rule: the staged writes are dropped, and staging
            // must not stay switched on for whatever runs next on this thread
            if (staged) UIElement.EndStaging(outer);

            UIElement.ApplyingTheme = wasApplying;

            // after the flag is lowered: what goes back to the defaults is not
            // a theme value, and must not be recorded as one
            element.EndThemePass(pass);
        }

        element.CompleteThemePass(this);
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