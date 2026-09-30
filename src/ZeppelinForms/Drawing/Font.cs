using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Drawing;

/// <summary>
/// Family — a comma-separated list of families, as in CSS:
/// "Consolas, Courier New, monospace". The first one found in the system is used.
/// Generic names: sans-serif, serif, monospace.
/// </summary>
public sealed record Font(
    string Family,
    float Size,
    FontWeight Weight = FontWeight.Normal,
    FontStyle Style = FontStyle.Normal)
{
    /// <summary>The path to the font file. If set, it is used instead of the system lookup.</summary>
    public string? FilePath { get; init; }

    public Font WithFile(string path) => this with { FilePath = path };

    public static Font Default { get; set; } = new("Segoe UI, sans-serif", 14);

    public static Font Monospace { get; } = new("Consolas, Courier New, monospace", 14);

    public Font WithSize(float size) => this with { Size = size };
    public Font WithWeight(FontWeight weight) => this with { Weight = weight };
    public Font Bold() => this with { Weight = FontWeight.Bold };
    public Font SemiBold() => this with { Weight = FontWeight.SemiBold };
    public Font Light() => this with { Weight = FontWeight.Light };
    public Font Italic() => this with { Style = FontStyle.Italic };

    public static implicit operator Font(string fontFamiliy) => new(fontFamiliy, 14f);
}

/// <summary>Font weight. The values are the numeric weights of CSS and OpenType,
/// so a renderer passes them to the font manager as they are.</summary>
/// <remarks>
/// Fluent needs more than Normal and Bold: its type ramp is built on SemiBold,
/// and Light is used for large display text. A family without the requested
/// face is matched to the nearest one by the font manager; a font given by
/// <see cref="Font.FilePath"/> carries a single face, so there SemiBold and
/// Bold are synthesized, and Light is drawn as the file's own weight.
/// </remarks>
public enum FontWeight
{
    Light = 300,
    Normal = 400,
    SemiBold = 600,
    Bold = 700,
}

public enum FontStyle { Normal, Italic }

public static class FontEx
{
    public static Font WithSize(this Font font, float fontSize)
    {
        return new Font(font.Family, fontSize, font.Weight, font.Style);
    }
}