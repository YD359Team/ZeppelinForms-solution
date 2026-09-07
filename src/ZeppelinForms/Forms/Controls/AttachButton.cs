using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Core.Collections;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Делегирует выбор файла диалогу и показывает результат рядом с кнопкой,
/// как input type=file на сайтах. Потомки создаются в конструкторе —
/// добавлять свои в Children не нужно.
/// </summary>
public partial class AttachButton : StackPanel
{
    private readonly Button _browse = new();
    private readonly Label _status = new();
    private readonly Button _clear = new();

    private string[] _files = [];

    /// <summary>Подпись на кнопке. Описывает действие, а не состояние.</summary>
    [Styled(Category = "Attach", AffectsLayout = true)]
    public partial string BrowseText { get; set; }

    private static string BrowseTextDefault => "Выбрать файл…";

    /// <summary>Текст, когда ничего не выбрано.</summary>
    [Styled(Category = "Attach", AffectsLayout = true)]
    public partial string EmptyText { get; set; }

    private static string EmptyTextDefault => "Файл не выбран";

    /// <summary>Показывать крестик сброса, когда что-то выбрано.
    /// В родном контроле такого нет, и это его известная беда.</summary>
    [Styled(Category = "Attach")]
    public partial bool AllowClear { get; set; }

    private static bool AllowClearDefault => true;

    public bool AllowMultiple { get; set; }

    /// <summary>Выбирать папку, а не файл.</summary>
    public bool SelectFolder { get; set; }

    public string? InitialDirectory { get; set; }

    public List<FileFilter> Filters { get; init; } = [];

    /// <summary>Выбранные пути. Пустой массив — ничего не выбрано.</summary>
    public IReadOnlyList<string> Files => _files;

    public string? FileName => _files.Length > 0 ? _files[0] : null;

    public event EventHandler? FilesChanged;

    public AttachButton()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        CrossAxisAlignment = CrossAxisAlignment.Center;

        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Left);

        _browse.Text = BrowseText;
        _browse.Click += (_, _) => Browse();

        _clear.Text = "✕";
        _clear.IsVisible = false;
        _clear.ToolTip = "Сбросить выбор";
        _clear.Click += (_, _) => Clear();

        Children.AddRange([_browse, _status, _clear]);

        UpdateStatus();
    }

    public void Clear()
    {
        if (_files.Length == 0) return;

        _files = [];

        UpdateStatus();
        FilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Browse()
    {
        if (FindOwner() is not { } owner) return;

        var options = new FileDialogOptions
        {
            InitialDirectory = InitialDirectory,
            AllowMultiple = AllowMultiple,
            Filters = { },
        };

        options.Filters.AddRange(Filters);

        string[] picked = SelectFolder
            ? FileDialog.SelectFolder(owner, options) is { } folder ? [folder] : []
            : AllowMultiple
                ? FileDialog.OpenFiles(owner, options)
                : FileDialog.OpenFile(owner, options) is { } file ? [file] : [];

        // отмена диалога не должна сбрасывать прежний выбор
        if (picked.Length == 0) return;

        _files = picked;

        UpdateStatus();
        FilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStatus()
    {
        _browse.Text = BrowseText;

        if (_files.Length == 0)
        {
            _status.Text = EmptyText;
            _status.ToolTip = null;
            _clear.IsVisible = false;

            return;
        }

        // имя, а не путь: путь растягивает раскладку и всё равно обрежется
        _status.Text = _files.Length == 1
            ? Path.GetFileName(_files[0].TrimEnd(Path.DirectorySeparatorChar))
            : $"{_files.Length} {Plural(_files.Length, "файл", "файла", "файлов")}";

        _status.ToolTip = string.Join(Environment.NewLine, _files);
        _clear.IsVisible = AllowClear;
    }

    /// <summary>Форма существительного при числе. Без этого «3 файлов»
    /// и «1 файла» бросаются в глаза сильнее, чем кажется.</summary>
    private static string Plural(int count, string one, string few, string many)
    {
        int tail = count % 100;

        if (tail is >= 11 and <= 14) return many;

        return (count % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }

    protected override void OnStyledPropertyChanged(StyledProperty property)
    {
        if (property == BrowseTextProperty ||
            property == EmptyTextProperty ||
            property == AllowClearProperty)
            UpdateStatus();
    }
}
