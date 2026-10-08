using System.IO;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace ZeppelinForms.VisualStudio;

/// <summary>Which project is being edited, and where its build puts the assembly.</summary>
internal static class ProjectOutput
{
    /// <summary>The built assembly of the active document's project — or of the project
    /// selected in Solution Explorer, or of the startup project. Null — none of them.</summary>
    public static string? ForActiveProject(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Project? project = ActiveProject(dte);

        return project is null ? null : AssemblyPath(project);
    }

    private static Project? ActiveProject(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (Try(() => dte.ActiveDocument?.ProjectItem?.ContainingProject) is { } fromDocument && HasFile(fromDocument))
            return fromDocument;

        if (Try(() => dte.ActiveSolutionProjects as Array) is { Length: > 0 } selected &&
            selected.GetValue(0) is Project fromSelection && HasFile(fromSelection))
            return fromSelection;

        if (Try(() => dte.Solution.SolutionBuild.StartupProjects as Array) is { Length: > 0 } startup &&
            startup.GetValue(0) is string uniqueName &&
            Try(() => dte.Solution.Item(uniqueName)) is { } fromStartup && HasFile(fromStartup))
            return fromStartup;

        return null;
    }

    /// <summary>A real project, not a solution folder or "Miscellaneous Files".</summary>
    private static bool HasFile(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? file = Try(() => project.FullName);
        return !string.IsNullOrEmpty(file) && File.Exists(file);
    }

    /// <summary>The output assembly by the project's active configuration; when that
    /// can't be read — a project system without the properties, a multi-targeting
    /// project — the newest assembly of that name under bin.</summary>
    public static string? AssemblyPath(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string directory = Path.GetDirectoryName(project.FullName)!;
        string? assemblyName = Property(project.Properties, "AssemblyName");

        string? outputPath = Try(() => Property(project.ConfigurationManager.ActiveConfiguration.Properties, "OutputPath"));
        string? fileName = Property(project.Properties, "OutputFileName");

        if (outputPath is not null && fileName is not null)
        {
            string candidate = Path.GetFullPath(Path.Combine(directory, outputPath, fileName));

            // an application on .NET is built into Name.dll; Name.exe next to it is
            // only the launcher
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string managed = Path.ChangeExtension(candidate, ".dll");
                if (File.Exists(managed)) candidate = managed;
            }

            if (File.Exists(candidate)) return candidate;
        }

        if (assemblyName is null) return null;

        string bin = Path.Combine(directory, "bin");

        if (!Directory.Exists(bin)) return Path.Combine(bin, assemblyName + ".dll");

        return Directory.EnumerateFiles(bin, assemblyName + ".dll", SearchOption.AllDirectories)
                   .Where(f => !f.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar))
                   .OrderByDescending(File.GetLastWriteTimeUtc)
                   .FirstOrDefault()
               ?? Path.Combine(bin, assemblyName + ".dll");
    }

    private static string? Property(Properties? properties, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return Try(() => properties?.Item(name)?.Value as string) is { Length: > 0 } value ? value : null;
    }

    /// <summary>DTE answers a missing property or a half-loaded project with an
    /// exception rather than a null.</summary>
    private static T? Try<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is COMException or ArgumentException or NotImplementedException or InvalidCastException)
        {
            return null;
        }
    }
}