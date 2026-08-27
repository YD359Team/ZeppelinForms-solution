namespace ZeppelinForms.Forms;

public sealed record DialogResult<T>(bool IsAccepted, T? Value)
{
    public static DialogResult<T> Cancelled() => new(false, default);

    public bool TryGetValue(out T value)
    {
        value = Value!;
        return IsAccepted && Value is not null;
    }
}
