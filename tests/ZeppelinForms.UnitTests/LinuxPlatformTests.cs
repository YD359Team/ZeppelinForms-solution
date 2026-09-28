using Xunit;
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

        Assert.True(shown.Wait(TimeSpan.FromSeconds(5), CancellationToken.None), "The window didn't appear within the allotted time.");

        form.Invoke(form.Close);

        Assert.True(uiThread.Join(TimeSpan.FromSeconds(5)), "The application didn't exit after Close().");
        Assert.Null(backgroundException);
    }
}
