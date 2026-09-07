namespace ZeppelinForms.Input.DragDrop;

/// <summary>
/// Что тащат в окно из системы. Сейчас источники дают файлы и текст;
/// когда понадобятся картинки или свои форматы, тип расширяется, а
/// обработчики продолжат смотреть на нужные им свойства.
/// </summary>
public sealed class DragDropData
{
    /// <summary>Пути перетаскиваемых файлов и папок. Пусто — тащат не файлы.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    public string? Text { get; init; }

    public bool HasFiles => Files.Count > 0;
    public bool HasText => !string.IsNullOrEmpty(Text);
}
