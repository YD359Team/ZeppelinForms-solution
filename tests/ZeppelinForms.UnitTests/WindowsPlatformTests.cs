using System.Diagnostics;
using Xunit;
using ZeppelinForms.Forms;
using ZeppelinForms.Windows;

namespace ZeppelinForms.UnitTests;

public class WindowsPlatformTests
{
    [Fact]
    public void RunApp()
    {
        WindowsPlatform windowsPlatform = new();
        App myApp = new(windowsPlatform)
        {
            MainForm = new FormForTests()
        };
        Timer t = new Timer(new(x => Process.GetProcessesByName(nameof(FormForTests)).First().Kill()), null, 3000, 0);
        myApp.Run();
        Assert.True(myApp is not null);
    }
}

internal class FormForTests : Form 
{
    public FormForTests()
    {
        this.Title = nameof(FormForTests);
        this.Size = new(600, 450);
    }
}
