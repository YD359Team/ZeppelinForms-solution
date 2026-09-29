using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Skia;

namespace ZeppelinForms.Benchmarks;

public static class Scenarios
{
    public static IReadOnlyList<Benchmark> All() =>
    [
        Layout(),
        LayoutCached(),
        ScrollVirtualized(),
        RenderBusinessForm(),
        RenderTextHeavy(),
        MeasureText(),
        ImageRetention(),
        FontFallbackRetention(),
        TextChurn(),
    ];

    /// <summary>
    /// A full layout pass without the measure cache. Measure and Arrange are public,
    /// while Form.PerformLayout is internal, so Content is called directly.
    /// </summary>
    /// <remarks>
    /// The cache is turned off explicitly: with it a repeated Measure with the same
    /// constraint is almost free, and the scenario would measure a cache hit rather
    /// than layout. The honest cost of a pass is still needed: it is exactly what
    /// the first frame pays, and any change that touches the whole tree.
    /// </remarks>
    private static Benchmark Layout() => new()
    {
        Name = "layout.business-form",
        Description = "Measure + Arrange of the whole business form tree, without the measure cache",
        Iterations = 300,
        Setup = () =>
        {
            UIElement.MeasureCacheEnabled = false;

            return Scenes.BusinessForm();
        },
        Body = state =>
        {
            var scene = (Scene)state;
            var size = new Size(scene.Width, scene.Height);

            scene.Form.Content!.Measure(size);
            scene.Form.Content!.Arrange(new Rectangle(Point.Empty, size));
        },
        Report = _ =>
        {
            UIElement.MeasureCacheEnabled = true;

            return "measure cache off for the duration of the scenario";
        },
    };

    /// <summary>
    /// The same pass, but with the measure cache — that is, a repeated layout in
    /// which nothing changed. This is exactly what happens when one property of
    /// one control changed within a frame.
    /// </summary>
    private static Benchmark LayoutCached() => new()
    {
        Name = "layout.business-form-cached",
        Description = "A repeated Measure + Arrange hitting the measure cache",
        Iterations = 300,
        Setup = () => Scenes.BusinessForm(),
        Body = state =>
        {
            var scene = (Scene)state;
            var size = new Size(scene.Width, scene.Height);

            scene.Form.Content!.Measure(size);
            scene.Form.Content!.Arrange(new Rectangle(Point.Empty, size));
        },
    };

    /// <summary>
    /// A whole scrolling frame of a virtualized list: the offset, rebuilding the
    /// visible range, layout and drawing.
    /// </summary>
    /// <remarks>
    /// Five thousand rows in the source, and the cost of a frame must not depend
    /// on their number: only what is visible changes. Scrolling goes by one row
    /// height per iteration — as when scrolling with a finger, and crossing row
    /// boundaries, which is where the panel rebuilds its containers.
    /// </remarks>
    private static Benchmark ScrollVirtualized() => new()
    {
        Name = "scroll.virtualized-list",
        Description = "A scrolling frame of a 5000-row list: range, layout, drawing",
        Iterations = 200,
        Setup = () => Scenes.VirtualizedList(),
        Body = state =>
        {
            var scene = (Scene)state;
            var list = (VirtualizingStackPanel)scene.Form.Content!;

            // one row height per frame, in a circle: otherwise the list would hit
            // the end, and from then on the scenario would measure standing still
            float next = list.ScrollY + list.ItemHeight;
            list.ScrollTo(0, next > 4000 ? 0 : next);

            scene.Form.UpdateLayout();

            SkiaRenderer.Render(scene.Form, scene.Canvas);
            scene.Canvas.Flush();
        },
    };

    /// <summary>
    /// A whole frame. Guards what phase 1 fixed: SkiaGraphics used to create
    /// a new SKPaint for every primitive and split every line into runs several
    /// times per frame. The brush pool and the parsed-line cache keep both near
    /// zero, and the report shows they still do — two brushes, a bounded number
    /// of lines.
    /// </summary>
    private static Benchmark RenderBusinessForm() => new()
    {
        Name = "render.business-form",
        Description = "SkiaRenderer.Render of the whole form into an offscreen surface",
        Iterations = 200,
        Setup = () => Scenes.BusinessForm(),
        Body = state =>
        {
            var scene = (Scene)state;
            SkiaRenderer.Render(scene.Form, scene.Canvas);
            scene.Canvas.Flush();
        },
        Report = _ =>
            $"paints {SkiaDiagnostics.PaintsCreated}, " +
            $"lines {SkiaDiagnostics.LineEntries}",
    };

    /// <summary>The same frame, but dominated by text.</summary>
    private static Benchmark RenderTextHeavy() => new()
    {
        Name = "render.text-heavy",
        Description = "A frame of 300 captions — a load on text drawing",
        Iterations = 150,
        Setup = () => Scenes.TextHeavy(),
        Body = state =>
        {
            var scene = (Scene)state;
            SkiaRenderer.Render(scene.Form, scene.Canvas);
            scene.Canvas.Flush();
        },
    };

    /// <summary>
    /// Pure text measurement, without drawing. Every call after the first hits
    /// the parsed-line cache; before it existed, MeasureText walked the runes and
    /// called ContainsGlyph for every character each time. A jump in this number
    /// means the cache stopped hitting.
    /// </summary>
    private static Benchmark MeasureText() => new()
    {
        Name = "text.measure-repeated",
        Description = "1000 repeated measurements of the same lines",
        Iterations = 100,
        Setup = () =>
        {
            Scenes.EnsureServices();

            string[] samples = new string[50];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = $"Позиция {i}: наименование товара и единица измерения";

            return new MeasureState(new SkiaTextMeasurer(), samples, Font.Default);
        },
        Body = state =>
        {
            var measure = (MeasureState)state;

            for (int repeat = 0; repeat < 20; repeat++)
                foreach (string sample in measure.Samples)
                    measure.Measurer.MeasureText(sample, measure.Font);
        },
    };

    private sealed record MeasureState(
        SkiaTextMeasurer Measurer,
        string[] Samples,
        Font Font);

    /// <summary>
    /// An image retention detector. Every iteration creates a new Image and draws
    /// it. SkiaGraphics.GetOrCreate makes a native copy of the pixels in an SKImage
    /// (FromPixelCopy) and keeps it in a ConditionalWeakTable, which must let the
    /// entry go when the Image becomes unreachable. If RetainedBytes or
    /// WorkingSetDelta grow linearly — it doesn't.
    /// </summary>
    private static Benchmark ImageRetention() => new()
    {
        Name = "memory.image-retention",
        Description = "Creating and drawing one-off 256x256 images",
        Iterations = 200,
        WarmupIterations = 10,
        Setup = () =>
        {
            Scenes.EnsureServices();

            SKSurface surface = Scenes.CreateSurface(512, 512);
            return new ImageState(surface, new SkiaGraphics(surface.Canvas));
        },
        Body = state =>
        {
            var images = (ImageState)state;

            const int side = 256;
            var image = new Image(side, side, new byte[side * side * 4]);

            images.Graphics.DrawImage(new Rectangle(0, 0, side, side), image);
            images.Surface.Canvas.Flush();
        },
        Report = _ => $"uploads {SkiaDiagnostics.ImagesUploaded}",
    };

    private sealed record ImageState(SKSurface Surface, SkiaGraphics Graphics);

    /// <summary>
    /// Growth of the font fallback cache. The Fallbacks key is (family, weight,
    /// style, codepoint), that is, an entry for every unique character outside the
    /// primary font. The scenario feeds the measurer rare code points and watches
    /// how much memory stays occupied after garbage collection.
    /// </summary>
    /// <remarks>
    /// Touches two caches at once: 32 new code points and one new line per
    /// iteration, so entries accumulate both in Fallbacks and in Lines. That's why
    /// both counters go into the report: their contributions can't be separated
    /// by retained memory alone.
    /// </remarks>
    private static Benchmark FontFallbackRetention() => new()
    {
        Name = "memory.font-fallback-growth",
        Description = "Measuring text with rare code points (Fallbacks growth)",
        Iterations = 100,
        WarmupIterations = 5,
        Setup = () =>
        {
            Scenes.EnsureServices();
            return new FallbackState(new SkiaTextMeasurer(), 0);
        },
        Body = state =>
        {
            var fallback = (FallbackState)state;

            // the CJK range: the characters are almost certainly absent from
            // the base Latin font and go to a fallback
            int start = 0x4E00 + fallback.Next * 32;
            fallback.Next++;

            var builder = new System.Text.StringBuilder(32);
            for (int i = 0; i < 32; i++)
                builder.Append(char.ConvertFromUtf32(start + i));

            fallback.Measurer.MeasureText(builder.ToString(), Font.Default);
        },

        // A bounded cache keeps the number of entries around the limit regardless
        // of how many characters have gone through it. A growing number is a sign
        // that the ceiling doesn't work.
        Report = _ =>
            $"fallbacks {SkiaDiagnostics.FallbackEntries}, lines {SkiaDiagnostics.LineEntries}",
    };

    /// <summary>
    /// Text and font size changing on every iteration. Checks the caches' ceilings:
    /// both the line and the font size are new each time, so without a limit Lines
    /// and Fonts would grow linearly — this is how clocks, counters and live list
    /// filtering behave.
    /// </summary>
    /// <remarks>
    /// Time is secondary here: every iteration misses both caches by construction,
    /// and what is measured is the cost of a miss, not the application's work.
    /// The subject of the measurement is the counters and retention.
    /// </remarks>
    private static Benchmark TextChurn() => new()
    {
        Name = "memory.text-churn",
        Description = "A unique line and font size on every iteration (Lines and Fonts ceilings)",
        Iterations = 600,
        WarmupIterations = 10,
        Setup = () =>
        {
            Scenes.EnsureServices();
            return new ChurnState(new SkiaTextMeasurer(), 0);
        },
        Body = state =>
        {
            var churn = (ChurnState)state;
            int n = churn.Next++;

            // the font size is fractional and new every time: Font is a record,
            // Size is part of its equality, so each value gets its own SKFont
            Font font = Font.Default.WithSize(14f + n * 0.01f);

            churn.Measurer.MeasureText($"Обновление {n}: значение счётчика", font);
        },

        Report = _ =>
            $"lines {SkiaDiagnostics.LineEntries}, " +
            $"fonts {SkiaDiagnostics.FontEntries}, " +
            $"sized {SkiaDiagnostics.SizedFontEntries}",
    };

    private sealed class ChurnState(SkiaTextMeasurer measurer, int next)
    {
        public SkiaTextMeasurer Measurer { get; } = measurer;
        public int Next { get; set; } = next;
    }

    private sealed class FallbackState(SkiaTextMeasurer measurer, int next)
    {
        public SkiaTextMeasurer Measurer { get; } = measurer;
        public int Next { get; set; } = next;
    }
}