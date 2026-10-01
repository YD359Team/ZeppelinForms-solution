using ZeppelinForms;
using ZF_SharedLib;
using ZeppelinForms.Linux;
using ZeppelinForms.Theming;

namespace ZF_Linux;

public class Program
{
    static void Main()
    {
        X11Platform linuxPlatform = new();
        App myApp = new(linuxPlatform)
        {
            MainForm = new ExampleMainForm(),
            StartupTheme = Themes.FluentLight,
        };
        myApp.Run();
    }
}
