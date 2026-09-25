namespace ZeppelinForms.Forms.Dialogs;

/// <summary>A set of extensions under one description: "Images", ["png", "jpg"].</summary>
public sealed record FileFilter(string Description, params string[] Extensions)
{
    /// <summary>Whether a file name matches. An empty extension list matches everything.</summary>
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