using System.Collections.Immutable;
using ZeppelinForms.Animation;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// What the styles in use ask about. A pseudo-class or a class no selector names
/// changes nothing, and toggling it must cost nothing: hovering a button in an
/// application without a single style should not run a theme pass. This index
/// answers "does anybody care" before any restyling happens.
/// </summary>
/// <remarks>
/// It only grows: a style removed from every collection still counts. The cost of
/// that is a restyle that changes nothing; the alternative — reference counts kept
/// in step with every collection — is not worth it for something that happens a
/// handful of times in an application's life.
/// </remarks>
internal static class StyleUsage
{
    private static readonly System.Threading.Lock Sync = new();

    private static ulong[] s_subjectPseudo = [];
    private static ulong[] s_contextPseudo = [];

    private static ImmutableHashSet<string> s_subjectClasses = ImmutableHashSet.Create<string>(StringComparer.Ordinal);
    private static ImmutableHashSet<string> s_contextClasses = ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    /// <summary>Some style has been registered: the cascade has work to do.</summary>
    public static volatile bool Any;

    /// <summary>Some selector names an element: renaming restyles.</summary>
    public static volatile bool UsesIds;

    /// <summary>Some selector asks about a place among siblings: adding or removing
    /// a child restyles its siblings.</summary>
    public static volatile bool UsesPositions;

    /// <summary>... above its subject: then the siblings' subtrees too.</summary>
    public static volatile bool UsesPositionsInContext;

    /// <summary>Changes with every change of any style collection. The inspector
    /// uses it to know its view of the matched styles is stale.</summary>
    public static int Version => s_version;

    private static int s_version;

    public static void BumpVersion() => Interlocked.Increment(ref s_version);

    public static void Register(Selector selector)
    {
        lock (Sync)
        {
            foreach (ComplexSelector complex in selector.Alternatives)
                for (int i = 0; i < complex.Compounds.Length; i++)
                    Register(complex.Compounds[i], subject: i == complex.Compounds.Length - 1);

            Any = true;
        }
    }

    private static void Register(CompoundSelector compound, bool subject)
    {
        foreach (PseudoClass pseudo in compound.PseudoClasses)
        {
            if (subject) SetBit(ref s_subjectPseudo, pseudo.Index);
            else SetBit(ref s_contextPseudo, pseudo.Index);
        }

        foreach (string name in compound.Classes)
        {
            if (subject) s_subjectClasses = s_subjectClasses.Add(name);
            else s_contextClasses = s_contextClasses.Add(name);
        }

        if (compound.Id is not null) UsesIds = true;

        if (compound.Positions.Length > 0)
        {
            UsesPositions = true;

            if (!subject) UsesPositionsInContext = true;
        }

        // what a negation asks about is asked about in the same position
        foreach (CompoundSelector[] negation in compound.Negations)
            foreach (CompoundSelector inner in negation)
                Register(inner, subject);
    }

    /// <summary>Whether a change of this pseudo-class can change any match, and
    /// whether descendants can be affected too.</summary>
    public static (bool Used, bool Subtree) Of(PseudoClass pseudo)
    {
        bool context = GetBit(Volatile.Read(ref s_contextPseudo), pseudo.Index);
        bool subject = GetBit(Volatile.Read(ref s_subjectPseudo), pseudo.Index);

        return (subject || context, context);
    }

    /// <summary>The same for a class.</summary>
    public static (bool Used, bool Subtree) Of(string className)
    {
        bool context = Volatile.Read(ref s_contextClasses).Contains(className);
        bool subject = Volatile.Read(ref s_subjectClasses).Contains(className);

        return (subject || context, context);
    }

    /// <summary>Copy-on-write: readers on the UI thread take the array without
    /// a lock, and a writer must not grow it under them.</summary>
    private static void SetBit(ref ulong[] bits, int index)
    {
        int word = index >> 6;
        ulong[] copy = word < bits.Length ? (ulong[])bits.Clone() : new ulong[word + 1];

        if (word >= bits.Length)
            Array.Copy(bits, copy, bits.Length);

        copy[word] |= 1UL << index;
        Volatile.Write(ref bits, copy);
    }

    private static bool GetBit(ulong[] bits, int index) =>
        (index >> 6) < bits.Length && (bits[index >> 6] & (1UL << index)) != 0;
}

/// <summary>Finds the styles that apply to an element and puts them in the order
/// of the cascade.</summary>
internal static class StyleCascade
{
    /// <summary>The matched styles, weakest first, and the transitions they bring.</summary>
    internal readonly record struct Result(Style[] Styles, Transition[]? Transitions)
    {
        public static Result None { get; } = new([], null);
    }

    private readonly record struct Hit(Style Style, Specificity Specificity, int Scope, int Order);

    private static readonly Comparison<Hit> Cascade = static (a, b) =>
    {
        int bySpecificity = a.Specificity.CompareTo(b.Specificity);
        if (bySpecificity != 0) return bySpecificity;

        int byScope = a.Scope.CompareTo(b.Scope);
        if (byScope != 0) return byScope;

        return a.Order.CompareTo(b.Order);
    };

    public static Result Match(UIElement element)
    {
        if (!StyleUsage.Any) return Result.None;

        List<Hit>? hits = null;

        // scopes from the farthest to the nearest: the rank is what settles
        // two styles of equal specificity
        int scope = 0;

        Collect(element, App.StylesOrNull, scope++, ref hits);
        Collect(element, element.FindOwner()?.StylesOrNull, scope++, ref hits);

        if (UIElement.ElementStylesCount > 0)
        {
            // ancestors with styles of their own, the root first
            Stack<Styles>? scoped = null;

            for (UIElement? current = element; current is not null; current = current.Parent)
                if (current.StylesOrNull is { Count: > 0 } own)
                    (scoped ??= new Stack<Styles>()).Push(own);

            if (scoped is not null)
                while (scoped.TryPop(out Styles? own))
                    Collect(element, own, scope++, ref hits);
        }

        if (hits is null) return Result.None;

        hits.Sort(Cascade);

        var styles = new Style[hits.Count];
        List<Transition>? transitions = null;

        for (int i = 0; i < hits.Count; i++)
        {
            Style style = hits[i].Style;
            styles[i] = style;

            if (style.Transitions.Count > 0)
                (transitions ??= []).AddRange(style.Transitions);
        }

        return new Result(styles, transitions?.ToArray());
    }

    private static void Collect(UIElement element, Styles? styles, int scope, ref List<Hit>? hits)
    {
        if (styles is null || styles.Count == 0) return;

        for (int i = 0; i < styles.Count; i++)
        {
            Style style = styles[i];

            if (style.Selector.Matches(element, out Specificity specificity))
                (hits ??= []).Add(new Hit(style, specificity, scope, i));
        }
    }
}