using System.Diagnostics;
using Xunit;
using ZeppelinForms.Forms;
using ZeppelinForms.Windows;

namespace ZeppelinForms.UnitTests;

public class WindowsPlatformTests
{
    [Fact]
    public void RunApp_ShowsWindowAndClosesGracefully()
    {
        Exception? backgroundException = null;
        var shown = new ManualResetEventSlim(false);
        var form = new FormForTests();
        form.Shown += (_, _) => shown.Set();

        var uiThread = new Thread(() =>
        {
            try
            {
                var app = new App(new WindowsPlatform()) { MainForm = form };
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

        Assert.True(shown.Wait(TimeSpan.FromSeconds(5)), "Окно не появилось за отведённое время.");

        form.Invoke(form.Close);

        Assert.True(uiThread.Join(TimeSpan.FromSeconds(5)), "Приложение не завершилось после Close().");
        Assert.Null(backgroundException);
    }
}

internal class FormForTests : Form
{
    public FormForTests()
    {
        Title = nameof(FormForTests);
        Size = new(600, 450);
    }
}
