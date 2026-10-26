using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Gles;
using ZeppelinForms.Gles.Geometry;

namespace ZeppelinForms.UnitTests;

/// <summary>The CPU half of the GLES backend: SVG paths, the stroker and the coverage
/// rasterizer — what decides the shape of every icon, chart line and pie it draws.</summary>
public class GlesGeometryTests
{
    private static byte[] Rasterize(Polylines contours, int width, int height, FillRule rule = FillRule.NonZero)
    {
        byte[] coverage = new byte[width * height];
        new Rasterizer().Rasterize(contours, 0, 0, width, height, rule, coverage);
        return coverage;
    }

    private static Polylines Rect(float x, float y, float w, float h, bool clockwise = true)
    {
        var lines = new Polylines();
        lines.BeginContour();

        if (clockwise)
        {
            lines.Add(new Point(x, y));
            lines.Add(new Point(x + w, y));
            lines.Add(new Point(x + w, y + h));
            lines.Add(new Point(x, y + h));
        }
        else
        {
            lines.Add(new Point(x, y));
            lines.Add(new Point(x, y + h));
            lines.Add(new Point(x + w, y + h));
            lines.Add(new Point(x + w, y));
        }

        lines.EndContour(closed: true);
        return lines;
    }

    private static float Area(byte[] coverage) => coverage.Sum(c => c / 255f);

    [Fact]
    public void ARectangleOnWholePixelsCoversExactlyThem()
    {
        byte[] coverage = Rasterize(Rect(2, 3, 4, 5), 10, 10);

        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                bool inside = x is >= 2 and < 6 && y is >= 3 and < 8;
                Assert.Equal(inside ? (byte)255 : (byte)0, coverage[y * 10 + x]);
            }
        }
    }

    [Fact]
    public void AHalfPixelEdgeIsHalfCovered()
    {
        byte[] coverage = Rasterize(Rect(2.5f, 2, 3, 3), 10, 10);

        Assert.Equal(128f, coverage[3 * 10 + 2], 1f);
        Assert.Equal(255, coverage[3 * 10 + 3]);
        Assert.Equal(128f, coverage[3 * 10 + 5], 1f);
    }

    [Fact]
    public void TheCoveredAreaIsTheShapesArea()
    {
        // a triangle at an odd slope: the antialiased area sums to its exact area
        var lines = new Polylines();
        lines.BeginContour();
        lines.Add(new Point(1.3f, 1.7f));
        lines.Add(new Point(17.9f, 4.1f));
        lines.Add(new Point(6.2f, 15.5f));
        lines.EndContour(closed: true);

        float exact = MathF.Abs(
            (17.9f - 1.3f) * (15.5f - 1.7f) - (6.2f - 1.3f) * (4.1f - 1.7f)) / 2f;

        Assert.Equal(exact, Area(Rasterize(lines, 20, 20)), 0.3f);
    }

    [Fact]
    public void EitherWindingFills()
    {
        Assert.Equal(25f, Area(Rasterize(Rect(1, 1, 5, 5, clockwise: false), 10, 10)), 0.01f);
    }

    [Fact]
    public void ARegionCutsTheShapeButKeepsItsWinding()
    {
        // a rectangle reaching far left and right of the region still covers it all
        var lines = Rect(-50, 2, 200, 3);
        byte[] coverage = new byte[10 * 10];

        new Rasterizer().Rasterize(lines, 0, 0, 10, 10, FillRule.NonZero, coverage);

        Assert.Equal(30f, Area(coverage), 0.01f);
    }

    [Fact]
    public void AnOffsetRegionSeesTheShapeWhereItIs()
    {
        var lines = Rect(100, 200, 4, 4);
        byte[] coverage = new byte[8 * 8];

        new Rasterizer().Rasterize(lines, 98, 198, 8, 8, FillRule.NonZero, coverage);

        Assert.Equal(255, coverage[2 * 8 + 2]);
        Assert.Equal(0, coverage[0]);
        Assert.Equal(16f, Area(coverage), 0.01f);
    }

    [Fact]
    public void EvenOddLeavesTheOverlapEmpty()
    {
        var lines = Rect(1, 1, 8, 8);
        lines.BeginContour();
        lines.Add(new Point(3, 3));
        lines.Add(new Point(7, 3));
        lines.Add(new Point(7, 7));
        lines.Add(new Point(3, 7));
        lines.EndContour(closed: true);

        Assert.Equal(64f, Area(Rasterize(lines, 10, 10, FillRule.NonZero)), 0.01f);
        Assert.Equal(48f, Area(Rasterize(lines, 10, 10, FillRule.EvenOdd)), 0.01f);
    }

    [Fact]
    public void AStrokedLineCoversItsLengthTimesItsWidth()
    {
        var line = new Polylines();
        line.BeginContour();
        line.Add(new Point(5, 10));
        line.Add(new Point(45, 10));
        line.EndContour(closed: false);

        var outline = new Polylines();
        new Stroker().Stroke(line, 4, LineCap.Butt, LineJoin.Miter, 4, 0.1f, outline);

        Assert.Equal(160f, Area(Rasterize(outline, 50, 20)), 0.5f);
    }

    [Fact]
    public void RoundCapsAddHalfACircleAtEachEnd()
    {
        var line = new Polylines();
        line.BeginContour();
        line.Add(new Point(10, 10));
        line.Add(new Point(30, 10));
        line.EndContour(closed: false);

        var outline = new Polylines();
        new Stroker().Stroke(line, 6, LineCap.Round, LineJoin.Round, 4, 0.05f, outline);

        float expected = 20 * 6 + MathF.PI * 9;
        Assert.Equal(expected, Area(Rasterize(outline, 40, 20)), 1.5f);
    }

    [Fact]
    public void AClosedStrokeLeavesItsInsideEmpty()
    {
        var square = Rect(10, 10, 20, 20);
        var outline = new Polylines();
        new Stroker().Stroke(square, 2, LineCap.Butt, LineJoin.Miter, 4, 0.1f, outline);

        byte[] coverage = Rasterize(outline, 40, 40);

        Assert.Equal(0, coverage[20 * 40 + 20]);
        Assert.Equal(255, coverage[10 * 40 + 20]);

        // mitered corners: the band is a 22-square less an 18-square
        Assert.Equal(22f * 22 - 18 * 18, Area(coverage), 1f);
    }

    [Fact]
    public void ASharpTurnStaysFilledInside()
    {
        // a V: the inner side folds over itself, and must not cut a hole at the vertex
        var v = new Polylines();
        v.BeginContour();
        v.Add(new Point(5, 5));
        v.Add(new Point(20, 30));
        v.Add(new Point(35, 5));
        v.EndContour(closed: false);

        var outline = new Polylines();
        new Stroker().Stroke(v, 6, LineCap.Butt, LineJoin.Round, 4, 0.1f, outline);

        byte[] coverage = Rasterize(outline, 40, 40);

        Assert.Equal(255, coverage[28 * 40 + 20]);
        Assert.Equal(255, coverage[25 * 40 + 20]);
    }

    [Fact]
    public void SvgCommandsMakeTheShapeTheyDescribe()
    {
        VectorPath path = SvgPathParser.Parse("M2 2 h10 v6 H2 z");
        var lines = new Polylines();
        path.Flatten(Affine.Identity, 0.1f, lines);

        Assert.Single(lines.Contours);
        Assert.Equal(60f, Area(Rasterize(lines, 20, 20)), 0.01f);
    }

    [Fact]
    public void CompactSvgNumbersAreSplitAsBrowsersSplitThem()
    {
        // "1.5.5" is 1.5 and .5; "-1-2" is -1 and -2; arc flags run into the numbers
        VectorPath a = SvgPathParser.Parse("M1.5.5l3-2");
        Assert.Equal(new Rectangle(1.5f, -1.5f, 3, 2), a.ControlBounds);

        VectorPath arc = SvgPathParser.Parse("M0 10a10 10 0 0120 0");
        Assert.False(arc.IsEmpty);
        Assert.Equal(20f, arc.ControlBounds.Width, 0.01f);
    }

    [Fact]
    public void AnArcIsOnTheCircle()
    {
        VectorPath path = SvgPathParser.Parse("M0 10 A10 10 0 1 1 20 10 A10 10 0 1 1 0 10 z");
        var lines = new Polylines();
        path.Flatten(Affine.Identity, 0.01f, lines);

        foreach (Point p in lines.Points)
        {
            float r = MathF.Sqrt((p.X - 10) * (p.X - 10) + (p.Y - 10) * (p.Y - 10));
            Assert.Equal(10f, r, 0.02f);
        }

        Assert.Equal(MathF.PI * 100, Area(Rasterize(lines, 24, 24)), 1f);
    }

    [Fact]
    public void ShorthandCurvesMirrorTheLastControlPoint()
    {
        VectorPath s = SvgPathParser.Parse("M0 0 C0 10 10 10 10 0 S20 -10 20 0");
        VectorPath c = SvgPathParser.Parse("M0 0 C0 10 10 10 10 0 C10 -10 20 -10 20 0");

        Assert.Equal(c.ControlBounds, s.ControlBounds);
    }

    [Fact]
    public void ABareMoveSetsTheBoundsButDrawsNothing()
    {
        // an icon's grid: two moves to its corners, then the shape
        VectorPath path = SvgPathParser.Parse("M0 0M24 24M2 2h10v6H2z");
        Assert.Equal(new Rectangle(0, 0, 24, 24), path.ControlBounds);

        var lines = new Polylines();
        path.Flatten(Affine.Identity, 0.1f, lines);
        Assert.Single(lines.Contours);

        // stroked with round caps, the moves left no dots in the corners
        var outline = new Polylines();
        new Stroker().Stroke(lines, 2, LineCap.Round, LineJoin.Round, 4, 0.1f, outline);
        byte[] coverage = Rasterize(outline, 26, 26);

        Assert.Equal(0, coverage[0]);
        Assert.Equal(0, coverage[24 * 26 + 24]);

        // a move closed at once is still a dot, as Skia caps a line of no length
        var dot = new Polylines();
        SvgPathParser.Parse("M5 5z").Flatten(Affine.Identity, 0.1f, dot);
        Assert.Single(dot.Contours);
    }

    [Fact]
    public void BrokenDataKeepsWhatCameBefore()
    {
        VectorPath path = SvgPathParser.Parse("M0 0 L10 0 L10 10 X 5 5");
        Assert.Equal(new Rectangle(0, 0, 10, 10), path.ControlBounds);
    }

    [Fact]
    public void ATransformAppliesTheInnerOneFirst()
    {
        Affine m = Affine.Translation(10, 0).Then(Affine.Scaling(2, 2));
        Assert.Equal(new Point(12, 2), m.Map(new Point(1, 1)));

        Affine r = Affine.Rotation(90);
        Point p = r.Map(new Point(1, 0));
        Assert.Equal(0f, p.X, 1e-5f);
        Assert.Equal(1f, p.Y, 1e-5f);
    }

    [Fact]
    public void AnInverseMapsBack()
    {
        Affine m = Affine.Translation(30, -7).Then(Affine.Rotation(25)).Then(Affine.Skewing(0.3f, 0)).Then(Affine.Scaling(1.5f, 2));

        Assert.True(m.TryInvert(out Affine inverse));

        Point back = inverse.Map(m.Map(new Point(11, -4)));
        Assert.Equal(11f, back.X, 1e-3f);
        Assert.Equal(-4f, back.Y, 1e-3f);

        Assert.False(Affine.Scaling(0, 1).TryInvert(out _));
    }

    [Theory]
    [InlineData("OpenGL ES 3.2 Mesa 25.2.8", true, true)]
    [InlineData("OpenGL ES 3.0 (ANGLE 2.1.0)", true, true)]
    [InlineData("OpenGL ES 2.0 Mesa", true, false)]
    [InlineData("4.6 (Core Profile) Mesa 25.2.8", false, true)]
    [InlineData("3.3.0 NVIDIA 550.54", false, true)]
    [InlineData("3.1 Mesa", false, false)]
    [InlineData("", true, false)]
    public void TheContextMustBeRecentEnough(string version, bool isGles, bool supported)
    {
        Assert.Equal(supported, GlesGraphicsBackend.SupportsVersion(version, isGles));
    }
}