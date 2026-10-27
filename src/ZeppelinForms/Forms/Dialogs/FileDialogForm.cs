using System.IO.Enumeration;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Dialogs;

/// <summary>
/// File system browsing built on the framework's own controls. Works the same
/// on all platforms and needs neither COM nor the D-Bus portal — at the cost
/// of not looking like the system dialog.
/// </summary>
internal sealed class FileDialogForm : Form
{
    private readonly FileDialogOptions _options;
    private readonly FileDialogMode _mode;

    // the current folder, and the place to type another one or a part of a name
    private readonly TextBox _path = new();
    private readonly ListBox _list = new();
    private readonly TextBox _name = new();
    private readonly ComboBox _filter = new();
    private readonly Button _accept = new();

    private string _directory = string.Empty;

    /// <summary>What the path field asked to look for in the current folder: a part
    /// of a name, or a pattern with * and ?. Null — the whole folder.</summary>
    private string? _search;

    // the list holds the entries themselves: the row template draws a row from one,
    // and a row's index finds its path
    private sealed record Entry(string Path, bool IsDirectory, FileKind Kind)
    {
        public string Name { get; } = System.IO.Path.GetFileName(Path);

        // what a screen reader and the list's own text search take for the row
        public override string ToString() => Name;
    }

    private const string SearchIcon = "M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0M21 21l-4.3-4.3";

    public FileDialogForm(FileDialogOptions options, FileDialogMode mode)
    {
        _options = options;
        _mode = mode;

        _list.SelectionMode = options.AllowMultiple && mode == FileDialogMode.Open
              ? SelectionMode.Extended
              : SelectionMode.Single;

        // an explicit title stays as given; the default one follows the language
        if (options.Title is { } title)
        {
            Title = title;
        }
        else
        {
            LocalizeTitle(mode switch
            {
                FileDialogMode.Save => ZfText.SaveFileTitle,
                FileDialogMode.Folder => ZfText.SelectFolderTitle,
                _ => ZfText.OpenFileTitle,
            });
        }

        Size = new Size(680, 460);

        Content = BuildLayout();

        _filter.Items.AddRange<object>([.. Filters()]);
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => Reload();

        _list.ItemTemplate = CreateRow;
        _list.SelectionChanged += (_, _) => OnSelectionChanged();
        _list.DoubleClick += (_, _) => OpenOrAccept();

        _path.Accepted += (_, _) => OnPathAccepted();
        _name.Accepted += (_, _) => OpenOrAccept();

        _name.Text = options.FileName;

        Navigate(options.InitialDirectory is { Length: > 0 } start && Directory.Exists(start)
            ? start
            : DefaultDirectory());
    }

    /// <summary>Documents, or home, or where the application runs: Documents is ""
    /// on a Linux without the XDG user directories — a container, a fresh server.</summary>
    private static string DefaultDirectory()
    {
        foreach (Environment.SpecialFolder folder in (Environment.SpecialFolder[])
            [Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.UserProfile])
        {
            if (Environment.GetFolderPath(folder) is { Length: > 0 } path && Directory.Exists(path))
                return path;
        }

        return Directory.GetCurrentDirectory();
    }

    private IEnumerable<FileFilter> Filters()
    {
        foreach (FileFilter filter in _options.Filters)
            yield return filter;

        // "all files" is always needed: otherwise there is no way out
        // of the dialog if none of the filters fit
        yield return new FileFilter(Localization.Get(ZfText.AllFiles));
    }

    private FileFilter CurrentFilter =>
        _filter.SelectedItem as FileFilter ?? new FileFilter(Localization.Get(ZfText.AllFiles));

    private DockPanel BuildLayout()
    {
        _accept.Localize(_mode == FileDialogMode.Save ? ZfText.Save : ZfText.Select);
        _accept.Click += (_, _) => OpenOrAccept();

        Button cancel = new Button().Localize(ZfText.Cancel);
        cancel.Click += (_, _) => Cancel();

        Button up = new Button().Localize(ZfText.Up);
        up.Click += (_, _) => NavigateUp();

        StackPanel top = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Thickness(12, 10),
            CrossAxisAlignment = CrossAxisAlignment.Center,
            Docking = Dock.Top,
        };

        _path.FlexGrow = 1;
        _path.LeadingIcon = IconSource.FromPath(SearchIcon, strokeWidth: 2f);
        _path.Localize(nameof(TextBox.Watermark), static (box, text) => box.Watermark = text, ZfText.PathOrSearch);
        top.Children.AddRange([up, _path]);

        StackPanel bottom = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Thickness(12, 10),
            CrossAxisAlignment = CrossAxisAlignment.Center,
            Docking = Dock.Bottom,
        };

        // when picking a folder, neither the name nor the filter is needed
        if (_mode != FileDialogMode.Folder)
        {
            _name.FlexGrow = 1;
            _filter.Size = new Size(200, float.NaN);

            bottom.Children.AddRange([_name, _filter]);
        }

        bottom.Children.AddRange([cancel, _accept]);

        _list.OverflowY = Overflow.Auto;
        _list.Margin = new Thickness(12, 0);

        // the renderer clears the window to white and knows nothing about
        // the theme, so the root paints the theme background itself —
        // otherwise the names in the list, which take the theme text color,
        // end up light-on-white in the dark theme
        DockPanel root = new() { Background = App.Theme.Colors.Background };
        root.Children.AddRange([top, bottom, _list]);

        return root;
    }

    // ===== navigation =====

    private void Navigate(string directory)
    {
        _directory = directory;
        _path.Text = directory;

        // a search looked in the folder it was typed in
        _search = null;

        Reload();
    }

    /// <summary>Enter in the path field. A folder goes there, a file is picked as if
    /// chosen in its folder, and anything else is looked for in the current folder:
    /// a part of a name, or a pattern such as <c>*.txt</c>. An empty field shows the
    /// whole folder again.</summary>
    private void OnPathAccepted()
    {
        string text = (_path.Text ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            Navigate(_directory);
            return;
        }

        string? target = Resolve(text);

        if (target is not null && Directory.Exists(target))
        {
            Navigate(target);
            return;
        }

        if (target is not null && _mode != FileDialogMode.Folder && File.Exists(target) &&
            Path.GetDirectoryName(target) is { Length: > 0 } folder)
        {
            Navigate(folder);
            _name.Text = Path.GetFileName(target);
            OpenOrAccept();
            return;
        }

        _search = text;
        Reload();
    }

    /// <summary>The full path the text names: absolute, from the home folder for
    /// <c>~</c>, or else from the current folder. Null for text no path can be made of.</summary>
    private string? Resolve(string text)
    {
        if (text == "~" || text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith("~\\", StringComparison.Ordinal))
            text = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text[1..].TrimStart('/', '\\'));

        try
        {
            return Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(_directory, text));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private bool MatchesSearch(string name) =>
        _search is not { } search ||
        (search.Contains('*') || search.Contains('?')
            ? FileSystemName.MatchesSimpleExpression(search, name, ignoreCase: true)
            : name.Contains(search, StringComparison.OrdinalIgnoreCase));

    private void NavigateUp()
    {
        string? parent = Path.GetDirectoryName(_directory);

        if (parent is { Length: > 0 } && Directory.Exists(parent))
            Navigate(parent);
    }

    private void Reload()
    {
        _list.Items.Clear();
        _list.SelectedIndex = -1;

        foreach (string directory in Enumerate(Directory.EnumerateDirectories))
        {
            if (!MatchesSearch(Path.GetFileName(directory))) continue;

            _list.Items.Add(new Entry(directory, IsDirectory: true, FileKind.Folder));
        }

        if (_mode == FileDialogMode.Folder) return;

        FileFilter filter = CurrentFilter;

        foreach (string file in Enumerate(Directory.EnumerateFiles))
        {
            if (!filter.Matches(file) || !MatchesSearch(Path.GetFileName(file))) continue;

            _list.Items.Add(new Entry(file, IsDirectory: false, FileKinds.Of(file)));
        }
    }

    /// <summary>A row: the kind's icon and the name. The folders were told apart by
    /// brackets around the name; an icon tells every kind apart and leaves the name
    /// as it is.</summary>
    private static StackPanel CreateRow(object item)
    {
        var entry = (Entry)item;
        (string data, float stroke, Color color) = FileKinds.Icon(entry.Kind);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Thickness(6, 3),
            CrossAxisAlignment = CrossAxisAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new SvgIcon { PathData = data, StrokeWidth = stroke, Color = color, IconSize = 16 },
                new Label
                {
                    Text = entry.Name,
                    FlexGrow = 1,
                    HorizontalContentAlign = HorizontalContentAlignment.Left,
                },
            },
        };
    }

    private Entry? EntryAt(int index) =>
        index >= 0 && index < _list.Items.Count ? _list.Items[index] as Entry : null;

    /// <summary>Enumeration that cuts off whatever is inaccessible: system folders
    /// like "System Volume Information" throw on the very first access,
    /// and the dialog must not crash because of them.</summary>
    private List<string> Enumerate(Func<string, IEnumerable<string>> source)
    {
        List<string> items;

        try
        {
            items = [.. source(_directory)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        items.Sort(StringComparer.OrdinalIgnoreCase);

        return items;
    }

    // ===== selection =====

    private void OnSelectionChanged()
    {
        if (EntryAt(_list.SelectedIndex) is { IsDirectory: false } entry)
            _name.Text = entry.Name;
    }

    /// <summary>Double click or the button: a folder — enter it, a file — return it.</summary>
    private void OpenOrAccept()
    {
        Entry? current = EntryAt(_list.SelectedIndex);

        // picking a folder: take the selected one, or the current one if nothing is selected
        if (_mode == FileDialogMode.Folder)
        {
            Accept(new[] { current?.Path ?? _directory });
            return;
        }

        // entering a folder takes priority over everything else:
        // a double click on it must not try to return anything
        if (current is { IsDirectory: true } directory)
        {
            Navigate(directory.Path);
            return;
        }

        // multiple files are checked before touching _name: it holds only
        // the last selected one and has nothing to do with the rest
        if (_mode == FileDialogMode.Open && _options.AllowMultiple)
        {
            string[] files = [.. _list.SelectedIndices
                .Select(EntryAt)
                .OfType<Entry>()
                .Where(entry => !entry.IsDirectory)
                .Select(entry => entry.Path)];

            if (files.Length > 1)
            {
                Accept(files);
                return;
            }
        }

        if (_name.Text is not { Length: > 0 } name) return;

        string full = Path.Combine(_directory, name);

        // there is nothing to open if the file does not exist,
        // while saving to a new one is the usual case
        if (_mode == FileDialogMode.Open && !File.Exists(full)) return;

        if (_mode == FileDialogMode.Save && File.Exists(full) &&
            !MessageBox.Confirm(this, Localization.Get(ZfText.FileExists, name)))
            return;

        Accept(new[] { full });
    }
}