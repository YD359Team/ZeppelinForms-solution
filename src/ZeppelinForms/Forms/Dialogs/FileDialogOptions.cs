namespace ZeppelinForms.Forms.Dialogs;

public sealed class FileDialogOptions
{
    public string? Title { get; set; }

    /// <summary>С какой папки открыться. Пусто — папка документов.</summary>
    public string? InitialDirectory { get; set; }

    /// <summary>Предложенное имя. Осмысленно только при сохранении.</summary>
    public string? FileName { get; set; }

    public List<FileFilter> Filters { get; init; } = [];

    public bool AllowMultiple { get; set; }
}
