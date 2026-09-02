namespace ZeppelinForms.Core.Text;

/// <summary>Одна операция правки. Хранит и что вставили, и что удалили,
/// чтобы отмена не требовала пересчёта.</summary>
public sealed class TextEdit(int position, string removed, string inserted, int caretBefore, int anchorBefore, int caretAfter)
{
    public int Position { get; } = position;
    public string RemovedText { get; } = removed;
    public string InsertedText { get; private set; } = inserted;

    public int CaretBefore { get; } = caretBefore;
    public int AnchorBefore { get; } = anchorBefore;
    public int CaretAfter { get; private set; } = caretAfter;

    public DateTime Timestamp { get; private set; } = DateTime.UtcNow;

    public bool TryMerge(TextEdit next, TimeSpan window)
    {
        if (next.Timestamp - Timestamp > window) return false;
        if (RemovedText.Length > 0 || next.RemovedText.Length > 0) return false;
        if (next.Position != Position + InsertedText.Length) return false;
        if (next.InsertedText is "\n" or " ") return false;

        InsertedText += next.InsertedText;
        CaretAfter = next.CaretAfter;
        Timestamp = next.Timestamp;
        return true;
    }
}
