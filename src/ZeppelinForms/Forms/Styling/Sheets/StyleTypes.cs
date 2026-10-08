using System.Reflection;
using System.Runtime.CompilerServices;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// The control types a style sheet can name, found by their short names in the
/// loaded assemblies. Finding them also initializes them: styled properties and
/// pseudo-classes are registered by static initializers, and a sheet can only check
/// a property or a pseudo-class against the registry once every control has run its.
/// </summary>
internal static class StyleTypes
{
    private static readonly System.Threading.Lock Sync = new();

    private static Dictionary<string, List<Type>> s_byName = new(StringComparer.Ordinal);
    private static readonly HashSet<Assembly> s_scanned = [];

    /// <summary>Pseudo-classes that only style sheets have named: no control registered
    /// them. A selector naming one of these never matches — most likely a typo.</summary>
    private static readonly HashSet<PseudoClass> s_namedOnlyBySheets = [];

    /// <summary>The control types with this short name: usually one.</summary>
    public static IReadOnlyList<Type> Find(string name)
    {
        Scan();

        lock (Sync)
            return s_byName.TryGetValue(name, out List<Type>? types) ? [.. types] : [];
    }

    /// <summary>Initialize every control type of every loaded assembly that can contain
    /// controls. Cheap after the first call: assemblies are scanned once each.</summary>
    public static void Scan()
    {
        Assembly framework = typeof(UIElement).Assembly;
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();

        List<Type>? fresh = null;

        lock (Sync)
        {
            foreach (Assembly assembly in loaded)
            {
                if (assembly.IsDynamic || s_scanned.Contains(assembly)) continue;

                s_scanned.Add(assembly);

                // only the framework and what references it can derive from UIElement
                if (assembly != framework &&
                    !assembly.GetReferencedAssemblies().Any(r => r.Name == framework.GetName().Name))
                    continue;

                foreach (Type type in TypesOf(assembly))
                {
                    if (!typeof(UIElement).IsAssignableFrom(type) || type.IsGenericTypeDefinition) continue;

                    string name = type.Name;
                    int tick = name.IndexOf('`');
                    if (tick >= 0) name = name[..tick];

                    if (!s_byName.TryGetValue(name, out List<Type>? list))
                        s_byName[name] = list = [];

                    list.Add(type);
                    (fresh ??= []).Add(type);
                }
            }
        }

        // outside the lock: a type initializer may register properties and
        // pseudo-classes under locks of its own
        if (fresh is not null)
            foreach (Type type in fresh)
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
    }

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