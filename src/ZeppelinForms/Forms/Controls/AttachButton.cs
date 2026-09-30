using ZeppelinForms.Core.Collections;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Delegates file picking to a dialog and shows the result next to the button,
/// like input type=file on websites. The children are created in the constructor —
/// there is no need to add your own to Children.
/// </summary>
public partial class AttachButton : StackPanel
{
    private readonly Button _browse = new();
    private readonly Label _status = new();
    private readonly Button _clear = new();

    private string[] _files = [];

    /// <summary>A dialog is open: a second press must not open another one on top.</summary>
    private bool _browsing;

    /// <summary>The caption on the button. Describes the action, not the state.
    /// Null — the localized default.</summary>
    /// <remarks>No default value in the generator: it is computed once at registration
    /// and would freeze in the language the application started with.</remarks>
    [Styled(Category = "Attach", AffectsLayout = true)]
    public partial string? BrowseText { get; set; }

    /// <summary>Text when nothing is selected. Null — the localized default.</summary>
    [Styled(Category = "Attach", AffectsLayout = true)]
    public partial string? EmptyText { get; set; }

    /// <summary>Show a clear cross when something is selected.
    /// The native control has none, and that is its well-known flaw.</summary>
    [Styled(Category = "Attach")]
    public partial bool AllowClear { get; set; }

    private static bool AllowClearDefault => true;

    public bool AllowMultiple { get; set; }

    /// <summary>Pick a folder rather than a file.</summary>
    public bool SelectFolder { get; set; }

    public string? InitialDirectory { get; set; }

    public List<FileFilter> Filters { get; init; } = [];

    /// <summary>The selected paths. An empty array — nothing is selected.</summary>
    public IReadOnlyList<string> Files => _files;

    public string? FileName => _files.Length > 0 ? _files[0] : null;

    public event EventHandler? FilesChanged;

    public AttachButton()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        CrossAxisAlignment = CrossAxisAlignment.Center;

        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Left);

        _browse.Click += (_, _) => Browse();

        _clear.Text = "✕";
        _clear.IsVisible = false;
        _clear.Localize(nameof(ToolTip), static (button, text) => button.ToolTip = text, ZfText.AttachClear);
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

    /// <remarks>
    /// The asynchronous path works on every platform. The synchronous FileDialog
    /// methods throw where the platform has a system file picker, so in the browser
    /// the button used to fail on the very first press.
    /// </remarks>
    private async void Browse()
    {
        if (_browsing) return;
        if (FindOwner() is not { } owner) return;

        var options = new FileDialogOptions
        {
            InitialDirectory = InitialDirectory,
            AllowMultiple = AllowMultiple,
            Filters = { },
        };

        options.Filters.AddRange(Filters);

        string[] picked;

        _browsing = true;

        try
        {
            if (SelectFolder)
                picked = await FileDialog.SelectFolderAsync(owner, options) is { } folder ? [folder] : [];
            else if (AllowMultiple)
                picked = await FileDialog.OpenFilesAsync(owner, options);
            else
                picked = await FileDialog.OpenFileAsync(owner, options) is { } file ? [file] : [];
        }
        finally
        {
            _browsing = false;
        }

        // cancelling the dialog must not reset the previous selection
        if (picked.Length == 0) return;

        _files = picked;

        UpdateStatus();
        FilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStatus()
    {
        _browse.Text = BrowseText ?? Localization.Get(ZfText.AttachBrowse);

        if (_files.Length == 0)
        {
            _status.Text = EmptyText ?? Localization.Get(ZfText.AttachEmpty);
            _status.ToolTip = null;
            _clear.IsVisible = false;

            return;
        }

        // the name, not the path: a path stretches the layout and gets cut off anyway.
        // The count goes through the language's plural rules: a wrong form next
        // to a number is more glaring than it seems
        _status.Text = _files.Length == 1
            ? Path.GetFileName(_files[0].TrimEnd(Path.DirectorySeparatorChar))
            : Localization.Get(ZfText.FilesSelected, _files.Length);

        _status.ToolTip = string.Join(Environment.NewLine, _files);
        _clear.IsVisible = AllowClear;
    }

    /// <summary>The status line is composed from keys and the file count,
    /// so it is rebuilt here rather than through a key on a property.</summary>
    protected override void OnLocalizationChanged() => UpdateStatus();

    protected override void OnStyledPropertyChanged(StyledProperty property)
    {
        if (property == BrowseTextProperty ||
            property == EmptyTextProperty ||
            property == AllowClearProperty)
            UpdateStatus();

        base.OnStyledPropertyChanged(property);
    }
}