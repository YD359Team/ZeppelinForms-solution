using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// The control types a style sheet can name, by their short names. Knowing them also
/// initializes them: styled properties and pseudo-classes are registered by static
/// initializers, and a sheet can only check a property or a pseudo-class against the
/// registry once every control has run its.
/// </summary>
/// <remarks>
/// <para>
/// Every assembly built with the ZeppelinForms package lists its controls itself: the
/// generator writes a module initializer that hands them to
/// <see cref="StyleTypeRegistry.Register"/>, naming each with <c>typeof</c>, so trimming
/// and native AOT keep what a sheet may name. Before 0.15 the types were found by
/// enumerating every loaded assembly, which a trimmed application can't do — and
/// native AOT doesn't even list the types it compiled without metadata.
/// </para>
/// <para>
/// The enumeration stays for assemblies built without the generator — a project
/// reference doesn't carry the package's analyzers. It is a feature switch,
/// <c>ZeppelinForms.Styling.ScanAssemblies</c>, on by default and turned off by the
/// package for trimmed and AOT publishing.
/// </para>
/// </remarks>
internal static class StyleTypes
{
    private const string ScanSwitch = "ZeppelinForms.Styling.ScanAssemblies";

    private static readonly System.Threading.Lock Sync = new();

    private static readonly Dictionary<string, List<Type>> s_byName = new(StringComparer.Ordinal);
    private static readonly HashSet<Type> s_known = [];

    /// <summary>The initializers of registered types not run yet: they run on the
    /// first question, not at registration — a module initializer runs early, and a
    /// control's static initializer may need an application that isn't set up yet.</summary>
    private static readonly List<Action> s_pending = [];

    private static readonly HashSet<Assembly> s_seen = [];
    private static readonly HashSet<Assembly> s_scanned = [];

    /// <summary>Pseudo-classes that only style sheets have named: no control registered
    /// them. A selector naming one of these never matches — most likely a typo.</summary>
    private static readonly HashSet<PseudoClass> s_namedOnlyBySheets = [];

    /// <summary>Whether the loaded assemblies are also searched for controls, for those
    /// built without the generator.</summary>
    [FeatureSwitchDefinition(ScanSwitch)]
    private static bool ScansAssemblies =>
        AppContext.TryGetSwitch(ScanSwitch, out bool enabled) ? enabled : true;

    /// <summary>Remember an assembly's controls. Called by the generated module
    /// initializer of every assembly that has some.</summary>
    public static void Register(Type[] types, Action initialize)
    {
        lock (Sync)
        {
            foreach (Type type in types)
                Add(type);

            s_pending.Add(initialize);
        }
    }

    /// <summary>The control types with this short name: usually one.</summary>
    public static IReadOnlyList<Type> Find(string name)
    {
        Scan();

        lock (Sync)
            return s_byName.TryGetValue(name, out List<Type>? types) ? [.. types] : [];
    }

    /// <summary>Know and initialize every control type of every loaded assembly. Cheap
    /// after the first call: each assembly is looked at once.</summary>
    public static void Scan()
    {
        List<Action>? initializers = null;
        List<Type>? scanned = null;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;

            lock (Sync)
            {
                if (!s_seen.Add(assembly)) continue;
            }

            // an assembly's registration runs with its module initializer, which runs
            // only once something of the assembly is touched: a sheet may name a
            // control of a library before the application has used it
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

            if (ScansAssemblies)
                (scanned ??= []).AddRange(ScanByReflection(assembly));
        }

        lock (Sync)
        {
            if (s_pending.Count > 0)
            {
                initializers = [.. s_pending];
                s_pending.Clear();
            }
        }

        // outside the lock: a type initializer may register properties and
        // pseudo-classes under locks of its own
        if (initializers is not null)
            foreach (Action initialize in initializers)
                initialize();

        if (scanned is not null)
            Initialize(scanned);
    }

    /// <summary>Hot Reload added types: a control among them was written after its
    /// assembly's module initializer ran, and a sheet may name it at once.</summary>
    public static void NoteUpdated(Type[] types)
    {
        List<Type>? added = null;

        lock (Sync)
        {
            foreach (Type type in types)
            {
                if (type.IsGenericTypeDefinition || !typeof(UIElement).IsAssignableFrom(type)) continue;
                if (s_known.Contains(type)) continue;

                Add(type);
                (added ??= []).Add(type);
            }
        }

        // outside the lock, as Scan does: a type initializer registers under locks of its own
        if (added is not null)
            Initialize(added);
    }

    private static void Add(Type type)
    {
        if (!s_known.Add(type)) return;

        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];

        if (!s_byName.TryGetValue(name, out List<Type>? list))
            s_byName[name] = list = [];

        list.Add(type);
    }

    /// <summary>The controls of an assembly found by enumerating its types: the way
    /// before the generator, kept for assemblies built without it.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Runs only while the ZeppelinForms.Styling.ScanAssemblies switch is on; the package " +
                        "turns it off for trimmed and AOT publishing, and the switch removes this call there. " +
                        "Trimmed applications know their controls from the generated registrations.")]
    private static List<Type> ScanByReflection(Assembly assembly)
    {
        var found = new List<Type>();
        Assembly framework = typeof(UIElement).Assembly;

        lock (Sync)
        {
            if (!s_scanned.Add(assembly)) return found;

            // only the framework and what references it can derive from UIElement
            if (assembly != framework &&
                !assembly.GetReferencedAssemblies().Any(r => r.Name == framework.GetName().Name))
                return found;

            foreach (Type type in TypesOf(assembly))
            {
                if (!typeof(UIElement).IsAssignableFrom(type) || type.IsGenericTypeDefinition) continue;
                if (s_known.Contains(type)) continue;

                Add(type);
                found.Add(type);
            }
        }

        return found;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2059",
        Justification = "The types come from ScanByReflection, which runs only where trimming doesn't.")]
    private static void Initialize(List<Type> types)
    {
        foreach (Type type in types)
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
    }

    [RequiresUnreferencedCode("Enumerates the types of an assembly.")]
    private static Type[] TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return [.. e.Types.Where(t => t is not null)!];
        }
    }

    /// <summary>The registered styled properties with this name, after scanning.</summary>
    public static StyledProperty[] PropertiesNamed(string name)
    {
        Scan();

        return [.. StyledProperty.Registered.Where(p => string.Equals(p.Name, name, StringComparison.Ordinal))];
    }

    /// <summary>Remember the pseudo-classes a sheet's selector registered — no control had.</summary>
    public static void NoteNamedBySheet(IEnumerable<PseudoClass> pseudoClasses)
    {
        lock (Sync)
            s_namedOnlyBySheets.UnionWith(pseudoClasses);
    }

    public static bool IsNamedOnlyBySheets(PseudoClass pseudoClass)
    {
        lock (Sync)
            return s_namedOnlyBySheets.Contains(pseudoClass);
    }
}
