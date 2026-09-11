namespace ZeppelinForms.Forms.Dialogs;

/// <summary>
/// Системный выбор файлов. Там, где он есть, FileDialog обращается к нему
/// вместо собственного обозревателя: в браузере локальной файловой системы
/// у приложения нет, и обходить её папки бессмысленно.
/// </summary>
public interface IFilePicker
{
    /// <summary>Выбранные пути. Пустой массив — отменили.</summary>
    Task<string[]> OpenAsync(FileDialogOptions options);

    /// <summary>Путь для сохранения или null, если отменили.</summary>
    Task<string?> SaveAsync(FileDialogOptions options);

    /// <summary>Выбранная папка или null. Поддерживается не везде —
    /// в браузере выбора папки как такового нет.</summary>
    Task<string?> SelectFolderAsync(FileDialogOptions options);
}

public static class FilePicker
{
    /// <summary>null — обходимся встроенной формой-обозревателем.
    /// Ставится платформой при запуске.</summary>
    public static IFilePicker? Current { get; set; }
}