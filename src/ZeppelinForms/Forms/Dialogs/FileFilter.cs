namespace ZeppelinForms.Forms.Dialogs;

/// <summary>Набор расширений под одним описанием: «Изображения», ["png", "jpg"].</summary>
public sealed record FileFilter(string Description, params string[] Extensions)
{
    /// <summary>Подходит ли имя файла. Пустой список расширений — подходит всё.</summary>
    public bool Matches(string fileName)
    {
        if (Extensions.Length == 0) return true;

        string extension = Path.GetExtension(fileName).TrimStart('.');

        foreach (string candidate in Extensions)
            if (string.Equals(extension, candidate.TrimStart('*', '.'), StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    public override string ToString() => Extensions.Length == 0
        ? Description
        : $"{Description} ({string.Join(", ", Extensions.Select(e => "*." + e.TrimStart('*', '.')))})";
}
