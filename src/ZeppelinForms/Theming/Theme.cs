using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Theming;

public sealed class Theme
{
    private readonly Dictionary<Type, Action<UIElement>> _appliers = [];

    /// <summary>
    /// Ready-made applier chains per element type, from base to derived.
    /// Computed once per type: a chain depends only on the hierarchy and
    /// the set of appliers. Previously every element got its own Stack
    /// and walked all base types again with a dictionary lookup at each
    /// step — on a form with three hundred labels that was three hundred
    /// stacks and three hundred walks.
    /// </summary>
    private readonly Dictionary<Type, Action<UIElement>[]> _chains = [];

    /// <summary>Guards both <see cref="_appliers"/> and <see cref="_chains"/>:
    /// a chain is built from the appliers, so reading one while the other
    /// is being changed is the same race as reading a half-written dictionary.</summary>
    private readonly System.Threading.Lock _chainSync = new();

    public required string Name { get; init; }
    public required ThemeColors Colors { get; init; }
    public Font BaseFont { get; init; } = Font.Default;

    /// <summary>How to style a control of this type. Derived types pick up
    /// the ancestor's styling when they have none of their own.</summary>
    public Theme For<T>(Action<T, ThemeColors> apply) where T : UIElement
    {
        // the set of appliers changed — the computed chains no longer
        // describe the theme. Both changes happen under one lock:
        // GetChain reads _appliers and must not see it mid-mutation
        lock (_chainSync)
        {
            _appliers[typeof(T)] = element => apply((T)element, Colors);
            _chains.Clear();
        }

        return this;
    }

    internal void Apply(UIElement element)
    {
        Action<UIElement>[] chain = GetChain(element.GetType());

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
            foreach (Action<UIElement> apply in chain)
                apply(element);
        }
        finally
        {
            UIElement.ApplyingTheme = wasApplying;
        }
    }

    /// <summary>The applier chain for a type, from base to derived.</summary>
    private Action<UIElement>[] GetChain(Type type)
    {
        // the whole computation runs under the lock: it reads _appliers,
        // which For may be changing on another thread at this very moment.
        // A chain is built once per type, so holding the lock for the walk
        // costs nothing noticeable
        lock (_chainSync)
        {
            if (_chains.TryGetValue(type, out Action<UIElement>[]? cached))
                return cached;

            var chain = new List<Action<UIElement>>();

            for (Type? current = type; current is not null; current = current.BaseType)
            {
                if (_appliers.TryGetValue(current, out Action<UIElement>? apply))
                    chain.Add(apply);
            }

            // collected from derived to base — reverse it,
            // the order of application is the opposite
            chain.Reverse();

            Action<UIElement>[] result = chain.Count == 0 ? [] : [.. chain];

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