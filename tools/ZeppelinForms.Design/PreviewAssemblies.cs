using System.Reflection;
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
/// <para>
/// The project lives in a context of its own, <see cref="ProjectLoadContext"/>, not in
/// the default one. The default context binds a reference to an assembly of the host
/// only when the host's version is not lower than the one asked for: a project built
/// against ZeppelinForms 0.14.1 could not bind to a host of 0.14.0, its forms failed
/// to load, and reflection quietly left them out — "no previews" with no reason given.
/// The project's context hands out the host's assemblies by name, whatever version the
/// project was built against.
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

        var context = new ProjectLoadContext(copy);
        Assembly assembly = context.LoadFromAssemblyPath(copy);

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

    /// <summary>
    /// The project's assemblies and whatever they bring along; everything the host has
    /// itself comes from the host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is "what the host has, it shares": an assembly of the host's trusted
    /// platform list — the runtime, ZeppelinForms, ZeppelinForms.Skia, SkiaSharp, the
    /// previewer — is loaded from the default context by its simple name, which binds
    /// whatever version is there. One copy of the framework in the process: the
    /// project's <c>Form</c> is the host's <c>Form</c>, and its styled properties go into
    /// the same registries.
    /// </para>
    /// <para>
    /// The rest — the project's own libraries, the platform package of an application
    /// project (ZeppelinForms.Windows), third-party packages — comes from the project's
    /// build: its deps.json first, then the files lying next to the assembly, as a plain
    /// library has its references, and last the NuGet packages folder, see
    /// <see cref="PackageProbe"/>.
    /// </para>
    /// <para>
    /// Not collectible, see <see cref="PreviewAssemblies"/>.
    /// </para>
    /// </remarks>
    private sealed class ProjectLoadContext : AssemblyLoadContext
    {
        private static readonly HashSet<string> HostAssemblies = ReadHostAssemblies();

        private readonly string _folder;
        private readonly AssemblyDependencyResolver? _resolver;
        private readonly PackageProbe _packages;

        public ProjectLoadContext(string mainAssembly) : base("ZeppelinForms preview", isCollectible: false)
        {
            _folder = Path.GetDirectoryName(mainAssembly)!;
            _packages = new PackageProbe(Path.ChangeExtension(mainAssembly, ".deps.json"));

            try
            {
                _resolver = new AssemblyDependencyResolver(mainAssembly);
            }
            catch (InvalidOperationException)
            {
                // a build without a usable deps.json: the files next to the assembly
                // are all there is to go by
                _resolver = null;
            }
        }

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is not { Length: > 0 } simple) return null;

            if (HostAssemblies.Contains(simple))
                return Default.LoadFromAssemblyName(new AssemblyName(simple));

            string? path = _resolver?.ResolveAssemblyToPath(name);

            if (path is null)
            {
                string candidate = Path.Combine(_folder, simple + ".dll");
                if (File.Exists(candidate)) path = candidate;
            }

            path ??= _packages.Find(simple);

            // null: the default context tries — and fails with the usual
            // FileNotFoundException, which the catalog reports
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName) is { } native
                ? LoadUnmanagedDllFromPath(native)
                : IntPtr.Zero;

        /// <summary>The simple names of the assemblies the host was started with.</summary>
        private static HashSet<string> ReadHostAssemblies()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string list)
            {
                foreach (string file in list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    names.Add(Path.GetFileNameWithoutExtension(file));
            }

            // a host started in an unusual way still shares the framework
            foreach (Assembly loaded in Default.Assemblies)
                if (loaded.GetName().Name is { } loadedName)
                    names.Add(loadedName);

            return names;
        }
    }

    /// <summary>
    /// The managed assemblies of a project's NuGet packages, found in the packages
    /// folder by its deps.json.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a library. An application's build copies its packages next to it, and its
    /// runtimeconfig tells the dependency resolver where else to look. A library has
    /// neither: its packages stay in the NuGet folder, and the resolver, given only a
    /// deps.json, can't find them. A form in a library that uses a third-party package
    /// failed to load, although the package was right there in the NuGet cache.
    /// </para>
    /// <para>
    /// Only the "package" libraries of the deps.json, only their runtime assets, and
    /// only files that exist: the folder is the one dotnet restore used —
    /// <c>NUGET_PACKAGES</c>, or <c>.nuget/packages</c> in the user's profile. Read once,
    /// on the first assembly the other ways didn't find.
    /// </para>
    /// </remarks>
    private sealed class PackageProbe(string depsFile)
    {
        private Dictionary<string, string>? _assemblies;

        /// <summary>The path of the package assembly with this simple name; null — none.</summary>
        public string? Find(string simpleName)
        {
            _assemblies ??= Read(depsFile);

            return _assemblies.TryGetValue(simpleName, out string? path) ? path : null;
        }

        private static Dictionary<string, string> Read(string depsFile)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(depsFile) || PackagesFolder() is not { } root) return found;

            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(depsFile));
                System.Text.Json.JsonElement json = document.RootElement;

                if (!json.TryGetProperty("libraries", out var libraries) ||
                    !json.TryGetProperty("targets", out var targets))
                    return found;

                foreach (var target in targets.EnumerateObject())
                {
                    foreach (var library in target.Value.EnumerateObject())
                    {
                        if (!libraries.TryGetProperty(library.Name, out var description) ||
                            !description.TryGetProperty("type", out var type) || type.GetString() != "package" ||
                            !description.TryGetProperty("path", out var packagePath) || packagePath.GetString() is not { } relative ||
                            !library.Value.TryGetProperty("runtime", out var runtime))
                            continue;

                        foreach (var asset in runtime.EnumerateObject())
                        {
                            string file = Path.Combine(root, relative, asset.Name.Replace('/', Path.DirectorySeparatorChar));

                            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                                found.TryAdd(Path.GetFileNameWithoutExtension(file), file);
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                // a broken or locked deps.json: the assembly is reported as not found,
                // with the reference that failed
            }

            return found;
        }

        private static string? PackagesFolder()
        {
            if (Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } configured)
                return configured;

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return profile.Length == 0 ? null : Path.Combine(profile, ".nuget", "packages");
        }
    }
}