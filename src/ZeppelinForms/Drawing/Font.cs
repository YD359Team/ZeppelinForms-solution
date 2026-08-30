using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Drawing;

/// <summary>
/// Family — список семейств через запятую, как в CSS:
/// "Consolas, Courier New, monospace". Берётся первое найденное в системе.
/// Обобщённые имена: sans-serif, serif, monospace.
/// </summary>
public sealed record Font(
    string Family,
    float Size,
    FontWeight Weight = FontWeight.Normal,
    FontStyle Style = FontStyle.Normal)
{
    /// <summary>Путь к файлу шрифта. Задан — используется он, а не системный поиск.</summary>
    public string? FilePath { get; init; }

    public Font WithFile(string path) => this with { FilePath = path };

    public static Font Default { get; set; } = new("Segoe UI, sans-serif", 14);

    public static Font Monospace { get; } = new("Consolas, Courier New, monospace", 14);

    public Font WithSize(float size) => this with { Size = size };
    public Font Bold() => this with { Weight = FontWeight.Bold };
    public Font Italic() => this with { Style = FontStyle.Italic };

    public static implicit operator Font(string fontFamiliy) => new(fontFamiliy, 14f);
}

public enum FontWeight { Normal, Bold }
public enum FontStyle { Normal, Italic }
