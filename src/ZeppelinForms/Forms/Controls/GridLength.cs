using System.Globalization;

namespace ZeppelinForms.Forms.Controls;

public readonly record struct GridLength(float Value, GridUnit Unit)
{
    public bool IsStar => Unit == GridUnit.Star;
    public bool IsAuto => Unit == GridUnit.Auto;

    public static GridLength Fixed(float px) => new(px, GridUnit.Fixed);
    public static GridLength Star(float weight = 1) => new(weight, GridUnit.Star);
    public static GridLength Auto => new(0, GridUnit.Auto);

    private static GridLength ParseSize(ReadOnlySpan<char> chars)
    {
        if (chars.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return Auto;

        if (chars[^1] == '*')
        {
            ReadOnlySpan<char> weight = chars[..^1].Trim();

            if (weight.IsEmpty)
                return Star();

            if (float.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out float w))
                return Star(w);

            throw new FormatException($"Не удалось разобрать вес звезды: '{chars}'.");
        }

        if (float.TryParse(chars, NumberStyles.Float, CultureInfo.InvariantCulture, out float px))
            return Fixed(px);

        throw new FormatException($"Не удалось разобрать размер трека: '{chars}'.");
    }

    /// <summary>Разбирает описание треков: "100", "*", "2*", "auto", "auto,*,2.5*".</summary>
    public static List<GridLength> Parse(string definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        List<GridLength> sizes = [];

        foreach (Range range in definition.AsSpan().Split(','))
        {
            ReadOnlySpan<char> part = definition.AsSpan()[range].Trim();
            if (part.IsEmpty)
                continue;

            sizes.Add(ParseSize(part));
        }

        if (sizes.Count == 0)
            throw new FormatException($"Пустое описание треков: '{definition}'.");

        return sizes;
    }
}
