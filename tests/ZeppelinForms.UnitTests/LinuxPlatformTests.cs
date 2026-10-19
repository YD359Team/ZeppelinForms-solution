using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Linux;
using ZeppelinForms.Windows;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class LinuxPlatformTests
{
    [Fact]
    public void RunApp_ShowsWindowAndClosesGracefully()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Test for Linux only");
        Assert.SkipWhen(
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
            "No X server is available (DISPLAY is not set)");

        // the window sets the X11 display provider for the whole process; the tests
        // after this one get the one they had, not round trips to a display whose
        // event loop is gone
        IDisplayProvider displays = Displays.Current;

        Exception? backgroundException = null;
        var shown = new ManualResetEventSlim(false);
        var form = new FormForTests();
        form.Shown += (_, _) => shown.Set();

        var uiThread = new Thread(() =>
        {
            try
            {
                var app = new App(new X11Platform()) { MainForm = form };
                app.Run();
            }
            catch (Exception ex)
            {
                backgroundException = ex;
                shown.Set();
            }
        })
        {
            IsBackground = true,
        };

        uiThread.Start();

        try
        {
            Assert.True(shown.Wait(TimeSpan.FromSeconds(5), CancellationToken.None), "The window didn't appear within the allotted time.");
            Assert.Null(backgroundException);

            // Invoke waits for the UI thread with no limit: a loop that died or hung
            // after Shown held the whole test run, not this test. The wait is bounded,
            // and the loop's own exception is the message
            var closed = new ManualResetEventSlim(false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                // an exception on a pool thread ends the process: it is kept instead
                try
                {
                    form.Invoke(form.Close);
                }
                catch (Exception ex)
                {
                    backgroundException ??= ex;
                }

                closed.Set();
            });

            Assert.True(closed.Wait(TimeSpan.FromSeconds(5), CancellationToken.None),
                $"Close() didn't run on the UI thread. {backgroundException}");

            Assert.True(uiThread.Join(TimeSpan.FromSeconds(5)), $"The application didn't exit after Close(). {backgroundException}");
            Assert.Null(backgroundException);
        }
        finally
        {
            Displays.Current = displays;
        }
    }
}