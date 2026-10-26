using Xunit;
using ZeppelinForms.Animation;
using ZeppelinForms.Forms;

namespace ZeppelinForms.UnitTests;

/// <summary>The frame clock's step over the running animations: one that a neighbour's
/// end removes isn't advanced again, one a neighbour's end adds waits for the next frame,
/// and many animations cost a frame what they cost one by one.</summary>
[Collection("Platform")]
public sealed class FrameClockTests
{
    /// <summary>An animation of a form — always visible to the clock — that counts its
    /// steps, ends after a number of them and then runs what it was given.</summary>
    private sealed class Counting(Form target, string key, int steps, Action? completed = null) : IAnimation
    {
        public object Target => target;

        public string Key => key;

        public int Advanced { get; private set; }

        public bool Cancelled { get; private set; }

        public bool Advance(TimeSpan elapsed)
        {
            Advanced++;

            if (Advanced < steps) return true;

            completed?.Invoke();
            return false;
        }

        public void Cancel(bool applyFinalValue) => Cancelled = true;
    }

    [Fact]
    public void ANeighboursEndRemovesAndAddsWithinTheFrame()
    {
        var form = new Form();
        FrameClock clock = form.Clock;

        var removed = new Counting(form, "second", 10);
        Counting? added = null;

        // the first one ends on its first step: it removes the second, adds a third
        var first = new Counting(form, "first", 1, () =>
        {
            clock.Remove(form, "second");

            added = new Counting(form, "third", 10);
            clock.Add(added);
        });

        clock.Add(first);
        clock.Add(removed);

        clock.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal(1, first.Advanced);

        // removed before its turn in the frame: not advanced, cancelled
        Assert.Equal(0, removed.Advanced);
        Assert.True(removed.Cancelled);

        // added during the frame: its first step is the next frame's
        Counting third = Assert.IsType<Counting>(added);
        Assert.Equal(0, third.Advanced);

        clock.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal(1, first.Advanced);
        Assert.Equal(0, removed.Advanced);
        Assert.Equal(1, third.Advanced);
    }

    [Fact]
    public void ManyAnimationsAreEachAdvancedOncePerFrame()
    {
        var form = new Form();
        FrameClock clock = form.Clock;

        var animations = Enumerable.Range(0, 500).Select(i => new Counting(form, "k" + i, 3)).ToList();

        foreach (Counting animation in animations)
            clock.Add(animation);

        for (int frame = 0; frame < 3; frame++)
            clock.Advance(TimeSpan.FromMilliseconds(16));

        Assert.All(animations, a => Assert.Equal(3, a.Advanced));

        // all of them ended: a fourth frame advances nothing
        clock.Advance(TimeSpan.FromMilliseconds(16));
        Assert.All(animations, a => Assert.Equal(3, a.Advanced));
    }

    [Fact]
    public void ADisplacedAnimationIsCancelledAndNotAdvanced()
    {
        var form = new Form();
        FrameClock clock = form.Clock;

        var old = new Counting(form, "opacity", 10);
        var replacement = new Counting(form, "opacity", 10);

        clock.Add(old);
        clock.Add(replacement);
        clock.Advance(TimeSpan.FromMilliseconds(16));

        Assert.True(old.Cancelled);
        Assert.Equal(0, old.Advanced);
        Assert.Equal(1, replacement.Advanced);
    }
}