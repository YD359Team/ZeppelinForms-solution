using ZeppelinForms;
using ZF_SharedLib;
using ZeppelinForms.Windows;
using ZeppelinForms.Theming;

namespace ZF_Win;

public class Program
{
    [STAThread]
    static void Main()
    {
        WindowsPlatform windowsPlatform = new();
        App myApp = new(windowsPlatform)
        {
            MainForm = new ExampleMainForm(),
            StartupTheme = Themes.FluentLight,
        };
        myApp.Run();
    }
}
