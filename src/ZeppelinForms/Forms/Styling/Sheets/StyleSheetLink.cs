namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// A style sheet in a <see cref="Styles"/> collection: its styles keep their place
/// in the collection across reloads, and a watched sheet reloads itself when one of
/// its files is saved.
/// </summary>
/// <remarks>
/// <para>
/// A reload replaces the sheet's styles where they stand and restyles once: an edit
/// in the editor shows up in the running application in the next frame, transitions
/// included, with no rebuild and no restart.
/// </para>
/// <para>
/// Watching needs a file system that reports changes: on Windows, Linux and macOS it
/// works; in the browser and on Android <see cref="IsWatching"/> stays false and the
/// sheet is loaded once. Disposing stops watching and removes the styles.
/// </para>
/// </remarks>
public sealed class StyleSheetLink : IDisposable
{
    private readonly Styles _owner;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Threading.Lock _sync = new();
    private Timer? _debounce;
    private bool _disposed;

    /// <summary>Which set of watchers is the current one. Every stop of watching moves
    /// it on, and a reload started under an older set gives up instead of finishing.</summary>
    /// <remarks>
    /// Disposing a <see cref="Timer"/> does not wait for a callback that is already
    /// running on the pool: the callback could still read the file and post a swap
    /// to a window that was closing. <see cref="_disposed"/> alone stopped only the
    /// swap itself; with the generation the callback stops at every step — before
    /// reading, after reading, and on the UI thread before swapping.
    /// </remarks>
    private int _generation;

    /// <summary>The UI thread's context when watching started, if it had one:
    /// a reload is handed back to it.</summary>
    private SynchronizationContext? _context;

    internal StyleSheetLink(Styles owner, StyleSheet sheet)
    {
        _owner = owner;
        Sheet = sheet;
    }

    /// <summary>The sheet as last loaded.</summary>
    public StyleSheet Sheet { get; private set; }

    /// <summary>Whether a save of the sheet or of one of its imports reloads it.</summary>
    public bool IsWatching => _watchers.Count > 0;

    /// <summary>Raised on the UI thread after a reload, with the new sheet and its
    /// diagnostics.</summary>
    public event EventHandler<StyleSheet>? Reloaded;

    /// <summary>Read the sheet's file again and put the new styles in place of the old.</summary>
    public void Reload()
    {
        if (Sheet.Path is not { } path)
            throw new InvalidOperationException("A sheet parsed from text has no file to reload from.");

        Swap(StyleSheet.Load(path), generation: null);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;

            StopWatching();
        }

        _owner.ReplaceRange(Sheet.Styles, []);
    }

    // ===== watching =====

    internal void Watch()
    {
        if (Sheet.Path is null) return;

        // a browser tab and an Android package have no files to edit live
        if (OperatingSystem.IsBrowser() || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()) return;

        _context = SynchronizationContext.Current;

        lock (_sync)
            WatchFiles(Sheet.Files);
    }

    /// <summary>Called under <see cref="_sync"/>.</summary>
    private void WatchFiles(IReadOnlyList<string> files)
    {
        StopWatching();

        // one watcher per folder: imports usually lie next to the sheet. A path the
        // folder can't be taken from — a root, an empty string — has nothing to
        // watch and is skipped rather than trusted
        IEnumerable<(string Folder, string Name)> located = files
            .Select(Locate)
            .Where(file => file is not null)
            .Select(file => file!.Value);

        foreach (IGrouping<string, string> folder in located.GroupBy(f => f.Folder, f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder.Key)) continue;

            var names = new HashSet<string>(folder, StringComparer.OrdinalIgnoreCase);
            var watcher = new FileSystemWatcher(folder.Key)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };

            // editors save in different ways: in place, through a temporary file and
            // a rename, by deleting and creating. All of them end in one of these
            void OnChange(object sender, FileSystemEventArgs e)
            {
                if (names.Contains(e.Name ?? string.Empty)) ScheduleReload();
            }

            watcher.Changed += OnChange;
            watcher.Created += OnChange;
            watcher.Renamed += (sender, e) =>
            {
                if (names.Contains(e.Name ?? string.Empty)) ScheduleReload();
            };

            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    /// <summary>The folder and the file name of a sheet's file, by its full path;
    /// null — a path with no folder or no name to watch.</summary>
    private static (string Folder, string Name)? Locate(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;

        string full;

        try
        {
            full = Path.GetFullPath(file);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        string? folder = Path.GetDirectoryName(full);
        string name = Path.GetFileName(full);

        return string.IsNullOrEmpty(folder) || name.Length == 0 ? null : (folder, name);
    }

    /// <summary>Called under <see cref="_sync"/>.</summary>
    private void StopWatching()
    {
        // a reload already running for the old watchers must not finish
        _generation++;

        foreach (FileSystemWatcher watcher in _watchers)
            watcher.Dispose();

        _watchers.Clear();

        _debounce?.Dispose();
        _debounce = null;
    }

    /// <summary>An editor raises several events for one save; the sheet is read once,
    /// a moment after the last of them.</summary>
    private void ScheduleReload()
    {
        lock (_sync)
        {
            if (_disposed) return;

            // the timer belongs to one generation: StopWatching disposes it, and the
            // next one is made for the next generation
            _debounce ??= new Timer(
                static state =>
                {
                    var (link, generation) = ((StyleSheetLink, int))state!;
                    link.ReloadFromWatcher(generation);
                },
                (this, _generation),
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);

            _debounce.Change(TimeSpan.FromMilliseconds(120), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Whether a reload started under the given generation may go on.</summary>
    private bool IsCurrent(int generation)
    {
        lock (_sync)
            return !_disposed && generation == _generation;
    }

    private void ReloadFromWatcher(int generation)
    {
        if (!IsCurrent(generation) || Sheet.Path is not { } path) return;

        // the editor may still hold the file: a few short retries rather than a
        // sheet that silently stops following saves
        StyleSheet? fresh = null;

        for (int attempt = 0; attempt < 5 && fresh is null; attempt++)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
                fresh = StyleSheet.Load(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // stopped while waiting for the editor: nobody needs the sheet anymore
                if (!IsCurrent(generation)) return;

                Thread.Sleep(50);
            }
        }

        if (fresh is null || !IsCurrent(generation)) return;

        // the collection belongs to the UI thread: the context watching started on,
        // or — for a sheet loaded before the application ran, which is the usual
        // place for App.Styles.Load — the window of an open form
        if (_context is { } context)
            context.Post(_ => Swap(fresh, generation), null);
        else if (Form.OpenForms.FirstOrDefault(f => f.PlatformWindow is not null)?.PlatformWindow is { } window)
            window.Invoke(() => Swap(fresh, generation));
        else
            Swap(fresh, generation);
    }

    /// <param name="fresh">The sheet just loaded.</param>
    /// <param name="generation">The generation of the watchers that asked for the
    /// reload; null — <see cref="Reload"/>, asked by the application itself.</param>
    private void Swap(StyleSheet fresh, int? generation)
    {
        StyleSheet old;

        lock (_sync)
        {
            if (_disposed) return;

            // the watchers that saw the save were replaced while the swap was on its
            // way to the UI thread: the newer ones will bring their own
            if (generation is { } g && g != _generation) return;

            old = Sheet;
            Sheet = fresh;

            // an import added or removed: the set of files to follow changed
            if (IsWatching && !old.Files.SequenceEqual(fresh.Files, StringComparer.OrdinalIgnoreCase))
                WatchFiles(fresh.Files);
        }

        _owner.ReplaceRange(old.Styles, fresh.Styles);
        Reloaded?.Invoke(this, fresh);
    }
}
