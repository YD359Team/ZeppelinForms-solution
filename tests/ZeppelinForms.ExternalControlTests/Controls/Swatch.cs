using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Skia;
using ZeppelinForms.Theming;

namespace ZeppelinForms.ExternalControlTests.Controls;

/// <summary>A color swatch, written as a control library writes one: a styled
/// property of its own, a theme rule every theme picks up, a pseudo-class for style
/// sheets, and drawing on the Skia canvas itself.</summary>
public partial class Swatch : DecoratedControl
{
    /// <summary>The swatch is the chosen one; a style sheet names it <c>:chosen</c>.</summary>
    public static readonly PseudoClass Chosen = PseudoClass.Register("chosen");

    // the library's rule, in every theme the application has or makes: the swatch
    // takes the theme's accent, as a built-in control would
    static Swatch() =>
        Theme.RegisterDefault<Swatch>(static (swatch, theme) => swatch.Fill = theme.Colors.Accent);

    [Styled(Category = "Appearance")]
    public partial Color Fill { get; set; }

    public bool IsChosen
    {
        get => HasPseudoClass(Chosen);
        set => SetPseudoClass(Chosen, value);
    }

    /// <summary>Whether the last drawing went to the Skia canvas rather than
    /// through <see cref="Graphics"/>.</summary>
    public bool DrewOnCanvas { get; private set; }

    protected override void DrawContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;

        // the way a PDF page or a chart is drawn: on the canvas, in the element's
        // own coordinates — the canvas already stands at its place
        if (g is SkiaGraphics skia)
        {
            using var paint = new SKPaint
            {
                Color = new SKColor(Fill.R, Fill.G, Fill.B, Fill.A),
                IsAntialias = false,
            };

            skia.Canvas.DrawRect(SKRect.Create(bounds.X, bounds.Y, bounds.Width, bounds.Height), paint);
            DrewOnCanvas = true;
        }
        else
        {
            g.FillRectangle(bounds, Fill);
            DrewOnCanvas = false;
        }
    }
}