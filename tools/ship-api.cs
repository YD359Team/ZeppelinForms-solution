#:property ImportDirectoryBuildProps=false
#:property ImportDirectoryPackagesProps=false
#:property ImportDirectoryBuildTargets=false
#:property NuGetAudit=false
#:property PublishAot=false

// Moves the API of a released version from PublicAPI.Unshipped.txt to PublicAPI.Shipped.txt.
//
// Run it once at the start of every new release branch, right after the previous
// version went out to NuGet:
//
//     dotnet run tools/ship-api.cs              — ship every project of the repository
//     dotnet run tools/ship-api.cs -- --check   — only report, change nothing
//     dotnet run tools/ship-api.cs -- src/ZeppelinForms   — only the given folders
//
// For each pair of files next to each other:
//   * an ordinary line of Unshipped is added to Shipped;
//   * a "*REMOVED*X" line of Unshipped deletes the line X from Shipped;
//   * Shipped is kept sorted ordinally — the order it already has;
//   * Unshipped is left with its "#nullable enable" header only.
//
// A *REMOVED* line whose target is not in Shipped is reported and the files are
// left untouched: it means Shipped is out of date, and shipping on top of that
// would hide the mismatch instead of fixing it.

using System.Text;

const string Header = "#nullable enable";
const string Removed = "*REMOVED*";

bool check = args.Contains("--check");
string[] roots = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

if (roots.Length == 0)
    roots = [FindRepositoryRoot()];

var unshippedFiles = roots
    .SelectMany(root => Directory.EnumerateFiles(root, "PublicAPI.Unshipped.txt", SearchOption.AllDirectories))
    .Where(path => !IsBuildOutput(path))
    .Distinct(StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToList();

if (unshippedFiles.Count == 0)
{
    Console.Error.WriteLine("No PublicAPI.Unshipped.txt found.");
    return 1;
}

int failed = 0;

foreach (string unshippedPath in unshippedFiles)
{
    string shippedPath = Path.Combine(Path.GetDirectoryName(unshippedPath)!, "PublicAPI.Shipped.txt");

    if (!File.Exists(shippedPath))
    {
        Console.Error.WriteLine($"{unshippedPath}: no PublicAPI.Shipped.txt next to it, skipped.");
        failed++;
        continue;
    }

    if (!Ship(shippedPath, unshippedPath, check))
        failed++;
}

return failed == 0 ? 0 : 1;

static bool Ship(string shippedPath, string unshippedPath, bool check)
{
    List<string> shipped = ReadEntries(shippedPath);
    List<string> unshipped = ReadEntries(unshippedPath);

    // a line may appear more than once only by mistake; a set keeps one
    var result = new HashSet<string>(shipped, StringComparer.Ordinal);
    var missing = new List<string>();
    int added = 0, removed = 0;

    // removals first: a changed signature is a *REMOVED* old line plus the new one,
    // and the same text may be removed and added back in one release
    foreach (string line in unshipped.Where(l => l.StartsWith(Removed, StringComparison.Ordinal)))
    {
        string target = line[Removed.Length..];

        if (result.Remove(target)) removed++;
        else missing.Add(target);
    }

    foreach (string line in unshipped.Where(l => !l.StartsWith(Removed, StringComparison.Ordinal)))
    {
        if (result.Add(line)) added++;
    }

    string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), unshippedPath);
    string name = relative.StartsWith("..", StringComparison.Ordinal) ? Path.GetFullPath(unshippedPath) : relative;

    if (missing.Count > 0)
    {
        Console.Error.WriteLine($"{name}: {missing.Count} *REMOVED* line(s) not found in Shipped, nothing changed:");

        foreach (string target in missing)
            Console.Error.WriteLine($"    {target}");

        return false;
    }

    Console.WriteLine($"{name}: +{added} shipped, -{removed} removed, {result.Count} lines in Shipped" +
        (check ? " (check only)" : string.Empty));

    if (check || (added == 0 && removed == 0 && unshipped.Count == 0)) return true;

    string newline = DetectNewline(shippedPath);

    WriteEntries(shippedPath, result.Order(StringComparer.Ordinal), newline);
    WriteEntries(unshippedPath, [], DetectNewline(unshippedPath));

    return true;
}

/// <summary>The API lines of a file: without the header, blank lines and comments.</summary>
static List<string> ReadEntries(string path) =>
    File.ReadAllLines(path)
        .Select(line => line.TrimEnd())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToList();

static void WriteEntries(string path, IEnumerable<string> entries, string newline)
{
    var text = new StringBuilder(Header).Append(newline);

    foreach (string entry in entries)
        text.Append(entry).Append(newline);

    // the files are kept without a BOM, as the analyzer's code fix writes them
    File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

/// <summary>The line ending the file already uses: a checkout with autocrlf must
/// not turn into a diff of every line.</summary>
static string DetectNewline(string path) =>
    File.ReadAllText(path).Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

static bool IsBuildOutput(string path)
{
    string[] parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return parts.Contains("bin") || parts.Contains("obj");
}

/// <summary>The folder with the .slnx: the script works from any current directory
/// inside the repository.</summary>
static string FindRepositoryRoot()
{
    for (DirectoryInfo? dir = new(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (dir.EnumerateFiles("*.slnx").Any() || Directory.Exists(Path.Combine(dir.FullName, ".git")))
            return dir.FullName;
    }

    return Directory.GetCurrentDirectory();
}
