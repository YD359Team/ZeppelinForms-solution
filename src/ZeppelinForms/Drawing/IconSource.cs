using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

/// <summary>
/// An icon a control draws next to its text: SVG path data, tinted with the text's
/// color, or a picture, drawn as it is.
/// </summary>
/// <example>
/// <code>
/// new Button { Text = "Save", Icon = IconSource.FromPath("M5 3h11l3 3v13H5z") };
/// new Button { Text = "Open", Icon = Image.LoadAsset("icons/open.png") };
/// new TextBox { LeadingIcon = IconSource.FromPath(Icons.Search) };
/// </code>
/// </example>
public abstract record IconSource
{
    /// <summary>Draw into <paramref name="rect"/>. <paramref name="color"/> is the color
    /// of the text beside the icon: a path takes it, a picture keeps its own colors.</summary>
    public abstract void Draw(Graphics g, Rectangle rect, Color color);

    /// <summary>The d attribute of a single SVG &lt;path&gt;, scaled into the icon's square.</summary>
    /// <param name="strokeWidth">0 — filled; greater — stroked with this width.</param>
    public static IconSource FromPath(string pathData, float strokeWidth = 0f) => new PathIconSource(pathData, strokeWidth);

    public static IconSource FromImage(Image image) => new ImageIconSource(image);

    public static implicit operator IconSource(Image image) => FromImage(image);
}

/// <summary>An icon from SVG path data, in the color of the text beside it.</summary>
public sealed record PathIconSource(string Data, float StrokeWidth = 0f) : IconSource
{
    public override void Draw(Graphics g, Rectangle rect, Color color)
    {
        if (string.IsNullOrWhiteSpace(Data)) return;

        g.DrawSvgPath(Data, rect, color, StrokeWidth);
    }
}

/// <summary>An icon from a picture, fitted into the icon's square with its proportions.</summary>
public sealed record ImageIconSource(Image Image) : IconSource
{
    public override void Draw(Graphics g, Rectangle rect, Color color) =>
        g.DrawImage(rect, Image, layout: ImageLayout.Zoom);
}

/// <summary>Where a control's icon stands relative to its text.</summary>
public enum IconPlacement : byte
{
    /// <summary>Before the text: on the left, or on the right in a right-to-left layout.</summary>
    Start,

    /// <summary>After the text.</summary>
    End,

    /// <summary>Above the text.</summary>
    Top,

    /// <summary>Below the text.</summary>
    Bottom,
}