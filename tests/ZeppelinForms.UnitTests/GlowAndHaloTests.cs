using Xunit;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: light — radial and sweep gradients, a neon border standing or running
/// round an element (<see cref="Glow"/>), and a halo behind it (<see cref="Halo"/>).</summary>
[Collection("Platform")]
public class GlowAndHaloTests
{
    private sealed class RecordingGraphics : HeadlessGraphics
    {
        public List<(Rectangle Rect, Color Color)> Ellipses { get; } = [];
        public List<(float Start, float Sweep, Color Color)> Pies { get; } = [];
        public List<(Point From, Point To, Color Color)> Lines { get; } = [];
        public List<(Rectangle Rect, Color Color, float Width)> RoundStrokes { get; } = [];
        public List<float> Blurs { get; } = [];
        public List<BlendMode> Blends { get; } = [];
        public List<(Point Center, float Radius, GradientStop[] Stops)> Radials { get; } = [];
        public List<string> Texts { get; } = [];
        public bool RecordRadials { get; init; }

        public override void FillEllipse(Rectangle rect, Color color) => Ellipses.Add((rect, color));

        public override void FillPie(Rectangle rect, float startAngle, float sweepAngle, Color color) =>
            Pies.Add((startAngle, sweepAngle, color));

        public override void DrawLine(Point from, Point to, Color color, float width) => Lines.Add((from, to, color));

        public override void DrawRoundRectangle(Rectangle rect, CornerRadius radius, Color color, float width) =>
            RoundStrokes.Add((rect, color, width));

        public override void SaveBlurLayer(float radius)
        {
            Blurs.Add(radius);
            base.SaveBlurLayer(radius);
        }

        public override void SaveLayer(float opacity, BlendMode blendMode)
        {
            Blends.Add(blendMode);
            base.SaveLayer(opacity, blendMode);
        }

        public override void FillRadialGradient(
            Rectangle bounds, CornerRadius radius, GradientStop[] stops, Point center, float gradientRadius)
        {
            if (RecordRadials) Radials.Add((center, gradientRadius, stops));
            else base.FillRadialGradient(bounds, radius, stops, center, gradientRadius);
        }

        public override void DrawText(string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center) => Texts.Add(text);
    }

    private static readonly Color Red = new(255, 255, 0, 0);
    private static readonly Color Blue = new(255, 0, 0, 255);
    private static readonly GradientStop[] RedToBlue = [new(Red, 0f), new(Blue, 1f)];

    private static Form Show(Border content)
    {
        var form = new Form { Size = new Size(300, 200), Content = content };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
        return form;
    }

    private static Border Box(Glow? glow = null, Halo? halo = null)
    {
        var box = new Border { Size = new Size(100, 60), CornerRadius = new CornerRadius(8) };

        // only what is given: a null written is a local value, and a style sheet's
        // light would not reach the element under it
        if (glow is not null) box.Glow = glow;
        if (halo is not null) box.Halo = halo;

        return box;
    }

    private static RecordingGraphics Render(Border box, bool recordRadials = false)
    {
        var g = new RecordingGraphics { RecordRadials = recordRadials };
        ElementTreeRenderer.Draw(box, g);
        return g;
    }

    private static void WithMotion(MotionPreference preference, Action body)
    {
        MotionPreference before = Motion.Preference;

        try
        {
            Motion.Preference = preference;
            body();
        }
        finally
        {
            Motion.Preference = before;
        }
    }

    // ===== gradients =====

    [Fact]
    public void AGradientIsReadBetweenItsStops()
    {
        GradientStop[] stops = [new(Red, 0.25f), new(Blue, 0.75f)];

        Assert.Equal(Red, GradientMath.Sample(stops, 0f));
        Assert.Equal(Blue, GradientMath.Sample(stops, 1f));
        Assert.Equal(Color.Lerp(Red, Blue, 0.5f), GradientMath.Sample(stops, 0.5f));
    }

    [Fact]
    public void WithoutABackendOfItsOwnARadialGradientIsRings()
    {
        var g = new RecordingGraphics();

        g.FillRadialGradient(new Rectangle(0, 0, 100, 100), CornerRadius.Zero, RedToBlue, new Point(50, 50), 50);

        // the smallest ring is the center's, red; the largest the edge's, blue
        (Rectangle smallest, Color center) = g.Ellipses.MinBy(e => e.Rect.Width);
        (Rectangle largest, Color edge) = g.Ellipses.MaxBy(e => e.Rect.Width);

        Assert.Equal(100f, largest.Width, 0.01f);
        Assert.True(center.R > center.B && edge.B > edge.R);
        Assert.Equal(50f, smallest.X + smallest.Width / 2f, 0.01f);
    }

    [Fact]
    public void WithoutABackendOfItsOwnASweepGradientIsWedgesFromTheStart()
    {
        var g = new RecordingGraphics();

        g.FillSweepGradient(new Rectangle(0, 0, 100, 100), CornerRadius.Zero, RedToBlue, new Point(50, 50), 90f);

        Assert.Equal(90f, g.Pies[0].Start, 0.01f);
        Assert.True(g.Pies[0].Color.R > g.Pies[0].Color.B);
        Assert.True(g.Pies[^1].Color.B > g.Pies[^1].Color.R);
        Assert.Equal(360f, g.Pies.Sum(p => p.Sweep) - 0.5f * g.Pies.Count, 0.1f);
    }

    // ===== the border's path =====

    [Fact]
    public void TheBorderIsWalkedClockwiseByLength()
    {
        var square = new BorderPath(new Rectangle(0, 0, 100, 50), CornerRadius.Zero);

        Assert.Equal(300f, square.Length, 0.01f);
        Assert.Equal(new Point(0, 0), square.At(0));
        Assert.Equal(new Point(100, 0), square.At(100));
        Assert.Equal(new Point(100, 50), square.At(150));
        Assert.Equal(new Point(0, 50), square.At(250));
        Assert.Equal(square.At(10), square.At(310));

        var rounded = new BorderPath(new Rectangle(0, 0, 100, 50), new CornerRadius(10));

        Assert.Equal(2 * (80 + 30) + 2 * MathF.PI * 10, rounded.Length, 0.01f);

        // the middle of the top-right corner's arc is on its circle
        Point arc = rounded.At(80 + MathF.PI * 10 / 4);
        float distance = MathF.Sqrt((arc.X - 90) * (arc.X - 90) + (arc.Y - 10) * (arc.Y - 10));
        Assert.Equal(10f, distance, 0.01f);
    }

    // ===== glow =====

    [Fact]
    public void AStandingGlowLightsTheBorderAndReachesPastIt()
    {
        var box = Box(new Glow(Red, Blur: 12f, Width: 2f));
        Show(box);

        RecordingGraphics g = Render(box);

        Assert.Contains(12f, g.Blurs);
        Assert.Contains(g.RoundStrokes, stroke => stroke.Color == Red && stroke.Width == 2f);
        Assert.Empty(g.Lines);

        // the light is in the element's dirty region
        Assert.True(box.LocalDirtyBounds.X <= -new Glow(Red, 12f, 2f).Reach);
    }

    [Fact]
    public void ACometRunsRoundTheBorder()
    {
        WithMotion(MotionPreference.Full, () =>
        {
            var box = Box(new Glow(Red, Blur: 0f, Width: 2f, Motion: GlowMotion.Comet) { Period = TimeSpan.FromSeconds(1) });
            Form form = Show(box);

            RecordingGraphics first = Render(box);
            Assert.NotEmpty(first.Lines);

            Point headBefore = first.Lines[^1].To;

            form.Clock.Advance(TimeSpan.FromMilliseconds(250));

            Point headAfter = Render(box).Lines[^1].To;

            Assert.NotEqual(headBefore, headAfter);
            Assert.True(box.GlowPhase > 0f);

            // the tail fades: its first piece is dimmer than the head's
            Assert.True(first.Lines[0].Color.A < first.Lines[^1].Color.A);
        });
    }

    [Fact]
    public void WithReducedMotionTheCometStands()
    {
        WithMotion(MotionPreference.Reduced, () =>
        {
            var box = Box(new Glow(Red, Blur: 8f, Width: 2f, Motion: GlowMotion.Comet));
            Show(box);

            RecordingGraphics g = Render(box);

            Assert.False(box.GlowRuns);
            Assert.Empty(g.Lines);
            Assert.Contains(g.RoundStrokes, stroke => stroke.Color == Red);
        });
    }

    [Fact]
    public void ACometTakenAwayStops()
    {
        WithMotion(MotionPreference.Full, () =>
        {
            var box = Box(new Glow(Red, Motion: GlowMotion.Comet));
            Form form = Show(box);
            Render(box);

            box.Glow = null;
            form.Clock.Advance(TimeSpan.FromMilliseconds(100));
            float phase = box.GlowPhase;

            form.Clock.Advance(TimeSpan.FromMilliseconds(400));

            Assert.Equal(phase, box.GlowPhase);
            Assert.Empty(Render(box).Lines);
        });
    }

    // ===== halo =====

    [Fact]
    public void ARadialHaloIsALampBehindTheElement()
    {
        var box = Box(halo: new Halo(Red, 30f, HaloShape.Radial));
        Show(box);

        (Point center, float radius, GradientStop[] stops) = Assert.Single(Render(box, recordRadials: true).Radials);

        Assert.Equal(new Point(50, 30), center);
        Assert.Equal(MathF.Sqrt(100 * 100 + 60 * 60) / 2f + 30f, radius, 0.01f);
        Assert.Equal(Red, stops[0].Color);
        Assert.Equal(0, stops[^1].Color.A);
    }

    [Fact]
    public void AHaloOfTheContentsShapeIsItsBodyInTheHalosColor()
    {
        var box = Box(halo: new Halo(Red, 16f));
        box.Child = new Label { Text = "lit" };
        Show(box);

        RecordingGraphics g = Render(box);

        // twice in the halo — a wide light and a near one —, once as it is
        Assert.Equal(3, g.Texts.Count(text => text == "lit"));
        Assert.Equal(2, g.Blends.Count(blend => blend == BlendMode.SourceIn));
        Assert.Contains(16f, g.Blurs);
    }

    // ===== style sheets =====

    [Fact]
    public void AStyleSheetWritesLight()
    {
        var box = Box();
        box.Classes.Add("lit");
        Form form = Show(box);

        StyleSheet sheet = StyleSheet.Parse(".lit { Glow: #FF0000 10 3 comet; Halo: #0000FF 40 radial; }", "light.zss");
        Assert.Empty(sheet.Diagnostics);
        form.Styles.Add(sheet);
        form.UpdateLayout();

        Assert.Equal(new Glow(Red, 10f, 3f, GlowMotion.Comet), box.Glow);
        Assert.Equal(new Halo(Blue, 40f, HaloShape.Radial), box.Halo);
    }
}
