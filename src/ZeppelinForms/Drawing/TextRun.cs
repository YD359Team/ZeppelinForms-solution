using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

/// <summary>A piece of text with its own formatting. Unset properties are taken
/// from the control — this way only what differs is set.</summary>
public sealed record TextRun(string Text)
{
    public Font? Font { get; init; }
    public Color? Color { get; init; }
    public Color? Background { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }

    public static TextRun Bold(string text, Font? baseFont = null) =>
        new(text) { Font = (baseFont ?? Drawing.Font.Default).Bold() };

    public static TextRun Italic(string text, Font? baseFont = null) =>
        new(text) { Font = (baseFont ?? Drawing.Font.Default).Italic() };

    public static TextRun Colored(string text, Color color) =>
        new(text) { Color = color };

    public static TextRun Link(string text, Color color) =>
        new(text) { Color = color, Underline = true };

    public static implicit operator TextRun(string text) => new(text);
}