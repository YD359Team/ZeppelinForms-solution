using ZeppelinForms.Core.Collections;
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

    private readonly Label _path = new();
    private readonly ListBox _list = new();
    private readonly TextBox _name = new();
    private readonly ComboBox _filter = new();
    private readonly Button _accept = new();

    // maps list rows to real paths: the ListBox holds
    // only the display names
    private readonly List<Entry> _entries = [];

    private string _directory = string.Empty;

    private readonly record struct Entry(string Path, bool IsDirectory);

    public FileDialogForm(FileDialogOptions options, FileDialogMode mode)
    {
        _options = options;
        _mode = mode;

        _list.SelectionMode = options.AllowMultiple && mode == FileDialogMode.Open
              ? SelectionMode.Extended
              : SelectionMode.Single;

        Title = options.Title ?? mode switch
        {
            FileDialogMode.Save => "Сохранить файл",
            FileDialogMode.Folder => "Выбрать папку",
            _ => "Открыть файл",
        };

        Size = new Size(680, 460);

        Content = BuildLayout();

        _filter.Items.AddRange<object>([.. Filters()]);
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => Reload();

        _list.SelectionChanged += (_, _) => OnSelectionChanged();
        _list.DoubleClick += (_, _) => Activate();

        _name.Text = options.FileName;

        Navigate(options.InitialDirectory is { Length: > 0 } start && Directory.Exists(start)
            ? start
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    }

    private IEnumerable<FileFilter> Filters()
    {
        foreach (FileFilter filter in _options.Filters)
            yield return filter;

        // "all files" is always needed: otherwise there is no way out
        // of the dialog if none of the filters fit
        yield return new FileFilter("Все файлы");
    }

    private FileFilter CurrentFilter =>
        _filter.SelectedItem as FileFilter ?? new FileFilter("Все файлы");

    private UIElement BuildLayout()
    {
        _accept.Text = _mode == FileDialogMode.Save ? "Сохранить" : "Выбрать";
        _accept.Click += (_, _) => Activate();

        Button cancel = new() { Text = "Отмена" };
        cancel.Click += (_, _) => Cancel();

        Button up = new() { Text = "Вверх" };
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
        // otherwise the path label, which takes the theme text color,
        // ends up light-on-white in the dark theme
        DockPanel root = new() { Background = App.Theme.Colors.Background };
        root.Children.AddRange([top, bottom, _list]);

        return root;
    }

    // ===== navigation =====

    private void Navigate(string directory)
    {
        _directory = directory;
        _path.Text = directory;

        Reload();
    }

    private void NavigateUp()
    {
        string? parent = Path.GetDirectoryName(_directory);

        if (parent is { Length: > 0 } && Directory.Exists(parent))
            Navigate(parent);
    }

    private void Reload()
    {
        _entries.Clear();
        _list.Items.Clear();
        _list.SelectedIndex = -1;

        foreach (string directory in Enumerate(Directory.EnumerateDirectories))
        {
            _entries.Add(new Entry(directory, IsDirectory: true));
            _list.Items.Add("[" + Path.GetFileName(directory) + "]");
        }

        if (_mode == FileDialogMode.Folder) return;

        FileFilter filter = CurrentFilter;

        foreach (string file in Enumerate(Directory.EnumerateFiles))
        {
            if (!filter.Matches(file)) continue;

            _entries.Add(new Entry(file, IsDirectory: false));
            _list.Items.Add(Path.GetFileName(file));
        }
    }

    /// <summary>Enumeration that cuts off whatever is inaccessible: system folders
    /// like "System Volume Information" throw on the very first access,
    /// and the dialog must not crash because of them.</summary>
    private IEnumerable<string> Enumerate(Func<string, IEnumerable<string>> source)
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
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _entries.Count) return;

        Entry entry = _entries[_list.SelectedIndex];

        if (!entry.IsDirectory)
            _name.Text = Path.GetFileName(entry.Path);
    }

    /// <summary>Double click or the button: a folder — enter it, a file — return it.</summary>
    private void Activate()
    {
        Entry? current = _list.SelectedIndex >= 0 && _list.SelectedIndex < _entries.Count
            ? _entries[_list.SelectedIndex]
            : null;

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
                .Where(i => i < _entries.Count && !_entries[i].IsDirectory)
                .Select(i => _entries[i].Path)];

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
            !MessageBox.Confirm(this, $"Файл «{name}» уже есть. Заменить?"))
            return;

        Accept(new[] { full });
    }
}