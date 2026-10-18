using Xunit;
using ZeppelinForms.Dispatchers;
using ZeppelinForms.Forms;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: <see cref="DispatcherTimer"/> — ticks on the UI thread, never piled
/// up, never after Stop or after the owner form closed.</summary>
/// <remarks>Most tests drive the timer by hand through <c>Elapse</c>, as its pool timer
/// would, and run the UI queue with <see cref="HeadlessPlatform.PumpAll"/>: no test
/// waits for the clock but the one that checks the clock itself.</remarks>
[Collection("Platform")]
public class DispatcherTimerTests
{
    private static (Form Form, HeadlessPlatform Platform) Open()
    {
        var platform = new HeadlessPlatform();
        var form = new Form();

        platform.CreateWindow(form);
        return (form, platform);
    }

    [Fact]
    public void ATickComesThroughTheUiQueue()
    {
        (Form form, HeadlessPlatform platform) = Open();
        using var timer = new DispatcherTimer(form) { Interval = TimeSpan.FromHours(1) };

        int ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Start();

        timer.Elapse();
        Assert.Equal(0, ticks);   // queued, not run inline

        platform.PumpAll();
        Assert.Equal(1, ticks);
    }

    [Fact]
    public void ElapsesWhileATickIsQueuedAreDropped()
    {
        (Form form, HeadlessPlatform platform) = Open();
        using var timer = new DispatcherTimer(form) { Interval = TimeSpan.FromHours(1) };

        int ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Start();

        // a busy UI thread: three intervals pass before the queue runs
        timer.Elapse();
        timer.Elapse();
        timer.Elapse();
        platform.PumpAll();

        Assert.Equal(1, ticks);

        // and the next interval ticks again
        timer.Elapse();
        platform.PumpAll();

        Assert.Equal(2, ticks);
    }

    [Fact]
    public void ATickQueuedBeforeStopDoesNotCome()
    {
        (Form form, HeadlessPlatform platform) = Open();
        using var timer = new DispatcherTimer(form) { Interval = TimeSpan.FromHours(1) };

        int ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Start();

        timer.Elapse();
        timer.Stop();
        platform.PumpAll();

        Assert.Equal(0, ticks);
        Assert.False(timer.IsEnabled);
    }

    [Fact]
    public void ATickQueuedBeforeARestartDoesNotCountForTheNewRun()
    {
        (Form form, HeadlessPlatform platform) = Open();
        using var timer = new DispatcherTimer(form) { Interval = TimeSpan.FromHours(1) };

        int ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Start();

        timer.Elapse();
        timer.Start();        // restarted: the interval begins anew
        platform.PumpAll();

        Assert.Equal(0, ticks);
        Assert.True(timer.IsEnabled);
    }

    [Fact]
    public void ClosingTheOwnerStopsTheTimer()
    {
        (Form form, HeadlessPlatform platform) = Open();
        var timer = new DispatcherTimer(form) { Interval = TimeSpan.FromHours(1) };

        int ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Start();

        timer.Elapse();
        form.Close();
        platform.PumpAll();

        Assert.False(timer.IsEnabled);
        Assert.Equal(0, ticks);
    }

    [Fact]
    public void ADisposedTimerCannotBeStarted()
    {
        var timer = new DispatcherTimer();
        timer.Dispose();

        Assert.Throws<ObjectDisposedException>(timer.Start);
        Assert.False(timer.IsEnabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AnIntervalUnderAMillisecondThrows(int milliseconds)
    {
        using var timer = new DispatcherTimer();

        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Interval = TimeSpan.FromMilliseconds(milliseconds));
    }

    [Fact]
    public void TheClockTicksByItself()
    {
        (Form form, HeadlessPlatform platform) = Open();
        using var timer = new DispatcherTimer(form) { Interval = TimeSpan.FromMilliseconds(10) };

        int ticks = 0;
        int thread = Environment.CurrentManagedThreadId;
        bool onUiThread = true;

        timer.Tick += (_, _) =>
        {
            ticks++;
            onUiThread &= Environment.CurrentManagedThreadId == thread;
        };

        timer.Start();

        // the pool timer queues the ticks; this thread is the UI thread and runs them
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (ticks < 3 && DateTime.UtcNow < deadline)
        {
            platform.PumpAll();
            Thread.Sleep(5);
        }

        timer.Stop();

        Assert.True(ticks >= 3, $"ticks: {ticks}");
        Assert.True(onUiThread);
    }
}