using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Design;

/// <summary>
/// Loads the project's built assembly into the previewer's process.
/// </summary>
/// <remarks>
/// <para>
/// From a copy, not from the build output: an assembly loaded from a file locks the
/// file on Windows, and the next build in the IDE would fail to overwrite it. The
/// output folder is copied to a temporary one, which is deleted when the process exits.
/// </para>
/// <para>
/// One assembly per process, and no unloading. A collectible context would let the
/// host reload in place, but the framework keeps registries — styled properties,
/// pseudo-classes, style sheet types — that would hold every old version of the
/// project's controls. The IDE starts a new host after each build instead: a process
/// starts in a fraction of the build's time and leaves nothing behind.
/// </para>
/// <para>
/// The framework's own assemblies are the host's: they are already loaded, and the
/// project's references bind to them. A project built against another version of the
/// framework is loaded with a warning.
/// </para>
/// </remarks>
public static class PreviewAssemblies
{
    private static string? s_shadow;

    /// <summary>Load the assembly at this path, from a copy of its folder.</summary>
    /// <param name="path">The assembly in the project's build output.</param>
    /// <param name="warning">Set when the project's framework version differs from the host's.</param>
    public static Assembly Load(string path, out string? warning)
    {
        string full = Path.GetFullPath(path);

        if (!File.Exists(full))
            throw new FileNotFoundException($"The project's assembly is not built yet: {full}", full);

        if (s_shadow is not null)
            throw new InvalidOperationException("The previewer loads one assembly per process; start a new one for another build.");

        string root = Path.Combine(Path.GetTempPath(), "ZeppelinForms.Designer");
        RemoveStaleCopies(root);

        string shadow = Path.Combine(root, $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        CopyDirectory(Path.GetDirectoryName(full)!, shadow);

        s_shadow = shadow;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(shadow);

        string copy = Path.Combine(shadow, Path.GetFileName(full));
        var resolver = new AssemblyDependencyResolver(copy);

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string? found = resolver.ResolveAssemblyToPath(name);

            // a project without a deps.json — a plain library — has its references
            // lying next to it
            if (found is null)
            {
                string candidate = Path.Combine(shadow, name.Name + ".dll");
                if (File.Exists(candidate)) found = candidate;
            }

            return found is null ? null : context.LoadFromAssemblyPath(found);
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
            resolver.ResolveUnmanagedDllToPath(name) is { } native ? NativeLibrary.Load(native) : IntPtr.Zero;

        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(copy);

        warning = CheckFrameworkVersion(assembly);

        return assembly;
    }

    private static string? CheckFrameworkVersion(Assembly assembly)
    {
        AssemblyName framework = typeof(UIElement).Assembly.GetName();

        AssemblyName? referenced = assembly.GetReferencedAssemblies()
            .FirstOrDefault(r => string.Equals(r.Name, framework.Name, StringComparison.Ordinal));

        if (referenced?.Version is not { } wanted || framework.Version is not { } own) return null;

        if (wanted.Major == own.Major && wanted.Minor == own.Minor) return null;

        return $"The project is built against ZeppelinForms {wanted.ToString(3)}, the previewer runs {own.ToString(3)}: " +
               "controls added or changed between the two may fail. Update the extension or the package.";
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string destination = Path.Combine(target, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    /// <summary>Copies left by hosts that could not delete them on exit — killed by the
    /// IDE, or exited with a file still mapped. A day is far beyond any session.</summary>
    private static void RemoveStaleCopies(string root)
    {
        if (!Directory.Exists(root)) return;

        foreach (string folder in Directory.EnumerateDirectories(root))
            if (Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddDays(-1))
                TryDelete(folder);
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a file still mapped by the exiting process: a later host removes
            // the folder, see RemoveStaleCopies
        }
    }
}