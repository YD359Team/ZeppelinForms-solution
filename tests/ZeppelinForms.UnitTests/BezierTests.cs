using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>Bézier curves on a backend without curves of its own: the base
/// <c>Graphics</c> flattens them into one polyline.</summary>
public class BezierTests
{
    /// <summary>Records the polylines drawn.</summary>
    private sealed class Recorder : HeadlessGraphics
    {
        public List<Point[]> Polylines { get; } = [];

        public override void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width) =>
            Polylines.Add(points.ToArray());
    }

    private static readonly Point P0 = new(0, 0);
    private static readonly Point P1 = new(0, 100);
    private static readonly Point P2 = new(100, 100);
    private static readonly Point P3 = new(100, 0);

    [Fact]
    public void ACurveIsOnePolylineFromItsStartToItsEnd()
    {
        var g = new Recorder();

        g.DrawBezier(P0, P1, P2, P3, Colors.Black, 1);

        Point[] line = Assert.Single(g.Polylines);
        Assert.Equal(P0, line[0]);
        Assert.Equal(P3, line[^1]);
        Assert.True(line.Length > 8, $"a curve this bent needs more than {line.Length - 1} segments");
    }

    [Fact]
    public void TheFlattenedCurveStaysCloseToTheCurve()
    {
        var g = new Recorder();

        g.DrawBezier(P0, P1, P2, P3, Colors.Black, 1);

        // the middle of this symmetric arch is at (50, 75)
        Point[] line = g.Polylines[0];
        Point top = line.OrderBy(p => Math.Abs(p.X - 50)).First();

        Assert.InRange(top.Y, 74.5f, 75.5f);
    }

    [Fact]
    public void AStraightCurveTakesASingleSegment()
    {
        var g = new Recorder();

        // control points on the line: no bend, nothing to split
        g.DrawBezier(new Point(0, 0), new Point(10, 0), new Point(20, 0), new Point(30, 0), Colors.Black, 1);

        Assert.Equal(2, g.Polylines[0].Length);
    }

    [Fact]
    public void AChainIsOneStrokeThroughEveryJoint()
    {
        var g = new Recorder();
        Point joint = P3;
        Point end = new(200, 0);

        g.DrawBeziers([P0, P1, P2, joint, new Point(100, -100), new Point(200, -100), end], Colors.Black, 1);

        Point[] line = Assert.Single(g.Polylines);
        Assert.Equal(P0, line[0]);
        Assert.Contains(joint, line);
        Assert.Equal(end, line[^1]);
    }

    [Fact]
    public void AnEmptyChainDrawsNothing()
    {
        var g = new Recorder();

        g.DrawBeziers([], Colors.Black, 1);

        Assert.Empty(g.Polylines);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void ACountOtherThanOnePlusThreeNIsAnError(int count)
    {
        var g = new Recorder();
        Point[] points = new Point[count];

        Assert.Throws<ArgumentException>("points", () => g.DrawBeziers(points, Colors.Black, 1));
    }

    [Fact]
    public void AControlPointAtInfinityCostsABoundedNumberOfPoints()
    {
        var g = new Recorder();

        g.DrawBezier(P0, new Point(float.MaxValue, 0), P2, P3, Colors.Black, 1);

        Assert.InRange(g.Polylines[0].Length, 2, 513);
    }
}