using Xunit;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.UnitTests;

/// <summary>A gradient's stops are copied for the graphics when they change, not on every
/// frame: an array goes as it is, any other list is copied once and again only after an
/// edit.</summary>
public sealed class GradientStopCacheTests
{
    private static readonly GradientStop Red = new(Colors.Red, 0f);
    private static readonly GradientStop Blue = new(Colors.Blue, 1f);

    [Fact]
    public void AnArrayGoesAsItIs()
    {
        var cache = new GradientStopCache();
        GradientStop[] stops = [Red, Blue];

        Assert.Same(stops, cache.For(stops));
    }

    [Fact]
    public void AListIsCopiedOnceUntilItChanges()
    {
        var cache = new GradientStopCache();
        var stops = new List<GradientStop> { Red, Blue };

        GradientStop[] first = cache.For(stops);
        Assert.Equal([Red, Blue], first);

        // drawn again unchanged: the same copy
        Assert.Same(first, cache.For(stops));

        // edited in place, as GradientBorder.Stops may be: a new copy with the edit
        stops[1] = new GradientStop(Colors.Green, 1f);
        GradientStop[] edited = cache.For(stops);

        Assert.NotSame(first, edited);
        Assert.Equal(Colors.Green, edited[1].Color);

        stops.Add(new GradientStop(Colors.White, 0.5f));
        Assert.Equal(3, cache.For(stops).Length);
    }
}