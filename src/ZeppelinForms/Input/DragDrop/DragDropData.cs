namespace ZeppelinForms.Input.DragDrop;

/// <summary>
/// What is being dragged into the window from the system. For now the sources
/// give files and text; when pictures or custom formats are needed, the type
/// is extended, and handlers keep looking at the properties they need.
/// </summary>
public sealed class DragDropData
{
    /// <summary>Paths of the dragged files and folders. Empty — not files are being dragged.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    public string? Text { get; init; }

    public bool HasFiles => Files.Count > 0;
    public bool HasText => !string.IsNullOrEmpty(Text);
}