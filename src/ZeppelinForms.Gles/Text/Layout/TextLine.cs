using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Gles.Text.Fonts;

namespace ZeppelinForms.Gles.Text.Layout;

/// <summary>
/// A piece of a line in one typeface and one direction, as glyphs: what is measured and
/// what is drawn are the same arrays, so a caret never drifts from the letter it is at.
/// </summary>
/// <remarks>Positions are in pixels of the line's font size, from the piece's left edge,
/// y down from the baseline. The characters' boxes are indexed from the piece's first
/// character, in logical order.</remarks>
internal sealed class GlyphRun
{
    public required int Start { get; init; }

    public required int Length { get; init; }

    public required Typeface Typeface { get; init; }

    public required float Size { get; init; }

    public bool IsRightToLeft { get; init; }

    /// <summary>Shaped by HarfBuzz rather than a glyph per character.</summary>
    public bool IsShaped { get; init; }

    public required ushort[] Glyphs { get; init; }

    public required float[] X { get; init; }

    public required float[] Y { get; init; }

    public required float Width { get; init; }

    public required float[] CharacterLefts { get; init; }

    public required float[] CharacterRights { get; init; }
}

/// <summary>A line laid out in its font: its pieces in visual order, left to right, its
/// advance width and the box of its ink.</summary>
internal sealed class TextLine
{
    public static readonly TextLine Empty = new()
    {
        Text = string.Empty,
        Runs = [],
        Width = 0,
        InkBounds = Rectangle.Empty,
    };

    public required string Text { get; init; }

    public required TextLine[] Runs { get; init; }

    public required float Width { get; init; }

    /// <summary>The ink's box, x from the line's start, y from the baseline, down.</summary>
    public required Rectangle InkBounds { get; init; }

    /// <summary>The height of the ink: what Skia's backend measures a line's height by.</summary>
    public float InkHeight => InkBounds.Height;

    /// <summary>The boxes of the characters, built the first time a caret asks.</summary>
    public TextLineLayout? Layout { get; set; }
}