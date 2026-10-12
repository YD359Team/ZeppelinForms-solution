using System.Diagnostics.CodeAnalysis;
namespace ZeppelinForms.Forms;

public sealed record DialogResult<T>(bool IsAccepted, T? Value)
{
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
        Justification = "Called where T is known: a dialog returning DialogResult<T> cancels with DialogResult<T>.Cancelled().")]
    public static DialogResult<T> Cancelled() => new(false, default);

    public bool TryGetValue(out T value)
    {
        value = Value!;
        return IsAccepted && Value is not null;
    }
}
