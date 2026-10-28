﻿﻿using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using ZeppelinForms.Analyzers;
using ZeppelinForms.Analyzers.Markup;

namespace ZeppelinForms.LanguageServer.Workspace;

/// <summary>
/// A project as the build sees it, without building it: its C# files, the assemblies its
/// packages and projects give it, and the generators of the framework run over its markup.
/// </summary>
/// <remarks>
/// <para>
/// The references come from <c>obj/project.assets.json</c> — what the restore an IDE runs on
/// opening a project wrote — so a project that was never built is understood as well. A
/// referenced project gives its built assembly; the framework's, when it isn't built, is the
/// one next to the server.
/// </para>
/// <para>
/// The C# files are parsed once and again only when they change on the disk or in the
/// editor. The open text of a document wins over its file: completion and problems are of
/// what is being typed.
/// </para>
/// </remarks>
internal sealed partial class ProjectContext
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, (DateTime Stamp, string? Open, SyntaxTree Tree)> _trees = new(StringComparer.OrdinalIgnoreCase);

    private ImmutableArray<MetadataReference> _references;
    private DateTime _referencesStamp;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private ProjectContext(string projectFile)
    {
        ProjectFile = projectFile;
        Directory = Path.GetDirectoryName(projectFile)!;

        string text = File.ReadAllText(projectFile);

        AssemblyName = Element(text, "AssemblyName") ?? Path.GetFileNameWithoutExtension(projectFile);
        ImplicitUsings = Element(text, "ImplicitUsings") is "enable" or "true";
    }

    public string ProjectFile { get; }

    public string Directory { get; }

    public string AssemblyName { get; }

    private bool ImplicitUsings { get; }

    private static readonly Dictionary<string, ProjectContext> Projects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The project a file belongs to: the nearest .csproj above it. Null — none.</summary>
    public static ProjectContext? For(string file)
    {
        for (string? directory = Path.GetDirectoryName(file); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            string[] projects;

            try
            {
                projects = System.IO.Directory.GetFiles(directory, "*.csproj");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (projects.Length == 0) continue;

            // the one named after its folder, as projects usually are, or the first
            string project = projects.FirstOrDefault(p =>
                Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase))
                ?? projects.OrderBy(p => p, StringComparer.Ordinal).First();

            lock (Projects)
            {
                if (!Projects.TryGetValue(project, out ProjectContext? context))
                    Projects[project] = context = new ProjectContext(project);

                return context;
            }
        }

        return null;
    }

    /// <summary>The project now: its compilation as the generators get it, and as they
    /// leave it — with the markup's classes — and what they reported.</summary>
    public ProjectSnapshot Snapshot(Documents documents, CancellationToken cancellationToken)
    {
        ImmutableArray<MetadataReference> references = References();
        List<SyntaxTree> trees = SourceTrees(documents);

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
            // private handlers of a class from an assembly are found, as from its source
            .WithMetadataImportOptions(MetadataImportOptions.All);

        CSharpCompilation input = CSharpCompilation.Create(AssemblyName, trees, references, options);

        var additional = new List<AdditionalText>();

        foreach (string file in ProjectFiles("*.zfml").Concat(ProjectFiles("*.zss")))
            additional.Add(new TextFile(file, documents.Read(file) ?? string.Empty));

        // a document open but not saved yet, so not on the disk
        foreach ((string path, string text) in documents.Under(Directory))
        {
            if (additional.Any(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            if (path.EndsWith(".zfml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zss", StringComparison.OrdinalIgnoreCase))
                additional.Add(new TextFile(path, text));
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new StyledPropertyGenerator().AsSourceGenerator(), new MarkupGenerator().AsSourceGenerator()],
            additional, ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out ImmutableArray<Diagnostic> diagnostics, cancellationToken);

        return new ProjectSnapshot(this, input, output, diagnostics, [.. additional.Select(a => a.Path)]);
    }

    // ===== sources =====

    private List<SyntaxTree> SourceTrees(Documents documents)
    {
        var trees = new List<SyntaxTree>();

        lock (_sync)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in ProjectFiles("*.cs"))
            {
                seen.Add(file);
                trees.Add(Tree(file, documents.Get(file)));
            }

            // the global usings the SDK writes for ImplicitUsings: under obj/, which the
            // project's own files leave out
            if (GlobalUsingsFile() is { } globalUsings)
                trees.Add(Tree(globalUsings, null));
            else if (ImplicitUsings)
                trees.Add(CSharpSyntaxTree.ParseText(DefaultGlobalUsings, ParseOptions, "GlobalUsings.g.cs"));

            foreach (string gone in _trees.Keys.Where(k => !seen.Contains(k) && !k.EndsWith(".GlobalUsings.g.cs", StringComparison.OrdinalIgnoreCase)).ToArray())
                _trees.Remove(gone);
        }

        return trees;
    }

    private SyntaxTree Tree(string file, string? open)
    {
        DateTime stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;

        if (_trees.TryGetValue(file, out var cached) && cached.Stamp == stamp && cached.Open == open)
            return cached.Tree;

        string text = open ?? (File.Exists(file) ? File.ReadAllText(file) : string.Empty);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(SourceText.From(text, System.Text.Encoding.UTF8), ParseOptions, file);

        _trees[file] = (stamp, open, tree);
        return tree;
    }

    private const string DefaultGlobalUsings = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Net.Http;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    private string? GlobalUsingsFile()
    {
        string obj = Path.Combine(Directory, "obj");
        if (!System.IO.Directory.Exists(obj)) return null;

        return System.IO.Directory.EnumerateFiles(obj, "*.GlobalUsings.g.cs", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>The project's files of a kind: under its folder, not in bin/ or obj/, not in
    /// a folder of another project.</summary>
    public IEnumerable<string> ProjectFiles(string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(Directory);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            string[] files, directories;

            try
            {
                files = System.IO.Directory.GetFiles(directory, pattern);
                directories = System.IO.Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files) yield return file;

            foreach (string inner in directories)
            {
                string name = Path.GetFileName(inner);

                if (name is "bin" or "obj" or "node_modules" || name.StartsWith('.')) continue;

                // another project's folder is its own
                if (System.IO.Directory.EnumerateFiles(inner, "*.csproj").Any()) continue;

                pending.Push(inner);
            }
        }
    }

    // ===== references =====

    private ImmutableArray<MetadataReference> References()
    {
        string assets = Path.Combine(Directory, "obj", "project.assets.json");
        DateTime stamp = File.Exists(assets) ? File.GetLastWriteTimeUtc(assets) : DateTime.MinValue;

        lock (_sync)
        {
            // the restore wrote it again — a package added — or the first time
            if (!_references.IsDefault && stamp == _referencesStamp) return _references;

            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // the runtime's own assemblies: the server's .NET, the one the project builds against
            foreach (string file in System.IO.Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
                paths[Path.GetFileNameWithoutExtension(file)] = file;

            if (File.Exists(assets))
                AddRestored(assets, paths);
            else
                AddBuilt(paths);

            // the framework, when nothing else gave it: the server's own copy
            if (!paths.ContainsKey("ZeppelinForms"))
                paths["ZeppelinForms"] = typeof(Forms.Form).Assembly.Location;

            _references = [.. paths.Values.Where(IsAssembly).Select(Reference)];
            _referencesStamp = stamp;

            return _references;
        }
    }

    /// <summary>What the restore says the project compiles against: the packages' compile
    /// assets, the referenced projects' built assemblies.</summary>
    private void AddRestored(string assets, Dictionary<string, string> paths)
    {
        JsonObject? root;

        try
        {
            root = JsonNode.Parse(File.ReadAllText(assets)) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        if (root is null) return;

        string[] folders = root["packageFolders"] is JsonObject packageFolders ? [.. packageFolders.Select(p => p.Key)] : [];

        // the first framework the project targets: a multi-targeted one is read as its first
        if (root["targets"] is not JsonObject targets || targets.FirstOrDefault().Value is not JsonObject target) return;

        JsonObject? libraries = root["libraries"] as JsonObject;

        foreach ((string key, JsonNode? value) in target)
        {
            if (value is not JsonObject library) continue;

            string type = (string?)library["type"] ?? string.Empty;
            JsonObject? info = libraries?[key] as JsonObject;

            if (type == "package" && library["compile"] is JsonObject compile && info?["path"] is JsonValue packagePath)
            {
                foreach ((string asset, _) in compile)
                {
                    if (asset.EndsWith("/_._", StringComparison.Ordinal)) continue;

                    foreach (string folder in folders)
                    {
                        string file = Path.Combine(folder, (string)packagePath!, asset.Replace('/', Path.DirectorySeparatorChar));

                        if (File.Exists(file))
                        {
                            paths[Path.GetFileNameWithoutExtension(file)] = file;
                            break;
                        }
                    }
                }
            }
            else if (type == "project" && info?["path"] is JsonValue projectPath)
            {
                string project = Path.GetFullPath(Path.Combine(Directory, (string)projectPath!));
                string name = key.Split('/')[0];

                if (BuiltAssembly(Path.GetDirectoryName(project)!, name) is { } built)
                    paths[name] = built;
            }
        }
    }

    /// <summary>No restore: whatever the project's last build put beside its assembly.</summary>
    private void AddBuilt(Dictionary<string, string> paths)
    {
        if (BuiltAssembly(Directory, AssemblyName) is not { } own) return;

        foreach (string file in System.IO.Directory.GetFiles(Path.GetDirectoryName(own)!, "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name != AssemblyName) paths[name] = file;
        }
    }

    /// <summary>The newest build of a project's assembly under its bin/.</summary>
    private static string? BuiltAssembly(string projectDirectory, string assemblyName)
    {
        string bin = Path.Combine(projectDirectory, "bin");
        if (!System.IO.Directory.Exists(bin)) return null;

        return System.IO.Directory.EnumerateFiles(bin, assemblyName + ".dll", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>An assembly with its documentation, where the .xml lies beside it — as a
    /// package's and a project's build put it: hover shows the summaries.</summary>
    private static MetadataReference Reference(string path)
    {
        string xml = Path.ChangeExtension(path, ".xml");

        return File.Exists(xml)
            ? MetadataReference.CreateFromFile(path, documentation: new XmlDocs(xml))
            : MetadataReference.CreateFromFile(path);
    }

    /// <summary>A file with metadata: the runtime's folder has native libraries too.</summary>
    private static bool IsAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
            return reader.HasMetadata;
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Element(string projectText, string name) =>
        ElementPattern().Matches(projectText).FirstOrDefault(m => m.Groups[1].Value == name)?.Groups[2].Value.Trim();

    [GeneratedRegex(@"<(\w+)>([^<]*)</\1>")]
    private static partial Regex ElementPattern();

    private sealed class TextFile(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}

/// <summary>The project at one moment: what completion, hover and problems are read from.</summary>
internal sealed class ProjectSnapshot(
    ProjectContext project, Compilation input, Compilation output, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<string> markupFiles)
{
    public ProjectContext Project { get; } = project;

    /// <summary>The compilation the generators got: the project's own C# and its references.</summary>
    public Compilation Input { get; } = input;

    /// <summary>With what the generators added: the classes the markup declares, the styled
    /// properties' code.</summary>
    public Compilation Output { get; } = output;

    /// <summary>What the generators reported: the markup's ZF2xxx.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;

    public ImmutableArray<string> MarkupFiles { get; } = markupFiles;

    /// <summary>UIElement, the root of everything markup and sheets name; null — the project
    /// doesn't reference the framework.</summary>
    public INamedTypeSymbol? Element => Output.GetTypeByMetadataName("ZeppelinForms.Forms.Controls.Base.UIElement");
}

/// <summary>The documentation of an assembly from its .xml: the <c>&lt;member&gt;</c> of a
/// symbol by its documentation id, read once when first asked.</summary>
internal sealed class XmlDocs(string path) : DocumentationProvider
{
    private readonly string _path = path;
    private Dictionary<string, string>? _members;

    protected override string? GetDocumentationForSymbol(
        string documentationMemberID, System.Globalization.CultureInfo preferredCulture, CancellationToken cancellationToken = default)
    {
        _members ??= Load();
        return _members.TryGetValue(documentationMemberID, out string? xml) ? xml : null;
    }

    private Dictionary<string, string> Load()
    {
        var members = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (System.Xml.Linq.XElement member in System.Xml.Linq.XDocument.Load(_path).Descendants("member"))
                if ((string?)member.Attribute("name") is { } name)
                    members[name] = member.ToString();
        }
        catch (Exception exception) when (exception is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            // no documentation is no summaries, not a broken server
        }

        return members;
    }

    public override bool Equals(object? obj) => obj is XmlDocs other && other._path == _path;

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(_path);
}
