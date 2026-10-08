using System.Reflection;
using ZeppelinForms.Design.Protocol;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Design;

/// <summary>A thing the previewer can show: a <see cref="PreviewAttribute"/> method
/// or a form class.</summary>
public sealed class PreviewEntry
{
    private readonly Func<object> _build;

    /// <summary>An entry made by hand: a host may offer previews the catalog doesn't find.</summary>
    public PreviewEntry(PreviewInfo info, Func<object> build)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(build);

        Info = info;
        _build = build;
    }

    public PreviewInfo Info { get; }

    public string Id => Info.Id;

    /// <summary>Run the user's code: a <see cref="UIElement"/> or a <see cref="Form"/>.</summary>
    public object Build() => _build();
}

/// <summary>Finds the previews of an assembly.</summary>
public static class PreviewCatalog
{
    public static IReadOnlyList<PreviewEntry> Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var entries = new List<PreviewEntry>();

        foreach (Type type in TypesOf(assembly))
        {
            if (type.IsGenericTypeDefinition) continue;

            // forms: any with a constructor without parameters; the attribute is optional
            if (typeof(Form).IsAssignableFrom(type) && type != typeof(Form) && !type.IsAbstract &&
                type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes) is { } constructor)
            {
                PreviewAttribute? attribute = type.GetCustomAttribute<PreviewAttribute>();

                entries.Add(new PreviewEntry(
                    Describe(type, null, attribute, isForm: true),
                    () => constructor.Invoke(null)));
            }

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<PreviewAttribute>() is not { } attribute) continue;
                if (method.GetParameters().Length != 0 || method.IsGenericMethodDefinition) continue;

                bool isForm = typeof(Form).IsAssignableFrom(method.ReturnType);

                if (!isForm && !typeof(UIElement).IsAssignableFrom(method.ReturnType)) continue;

                entries.Add(new PreviewEntry(
                    Describe(type, method.Name, attribute, isForm),
                    () => method.Invoke(null, null)!));
            }
        }

        // the order of the source is lost to reflection; a stable one by group and name
        // keeps the list from jumping around between builds
        return [.. entries.OrderBy(e => e.Info.Group, StringComparer.Ordinal).ThenBy(e => e.Info.Name, StringComparer.Ordinal)];
    }

    /// <summary>The <see cref="PreviewSetupAttribute"/> methods of an assembly.</summary>
    public static IReadOnlyList<MethodInfo> SetupMethods(Assembly assembly) =>
        [.. TypesOf(assembly)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<PreviewSetupAttribute>() is not null && m.GetParameters().Length == 0)];

    private static PreviewInfo Describe(Type type, string? member, PreviewAttribute? attribute, bool isForm)
    {
        string typeName = type.FullName ?? type.Name;

        return new PreviewInfo(
            member is null ? typeName : $"{typeName}.{member}",
            attribute?.Name ?? member ?? type.Name,
            attribute?.Group ?? type.Name,
            typeName,
            member,
            isForm)
        {
            Defaults = new PreviewSettings
            {
                Width = attribute?.Width ?? 0,
                Height = attribute?.Height ?? 0,
                Theme = attribute?.Theme,
                Culture = attribute?.Culture,
                RightToLeft = attribute?.RightToLeft ?? false,
                TextScale = attribute?.TextScale ?? 0,
            },
        };
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
}