using ZeppelinForms;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Linux;
using ZeppelinForms.Theming;
using ZF_SharedLib;
using ZF_SharedLib.Showcase;

namespace ZF_Linux;

public class Program
{
    static void Main()
    {
        // the showcase's native control here: a plain X11 window, filled by the server
        NativePage.Sample = () => new X11WindowHost { Background = new Color(255, 0x2E, 0x7D, 0x32) };
        X11Platform linuxPlatform = new();
        App myApp = new(linuxPlatform)
        {
            MainForm = new ExampleMainForm(),
            StartupTheme = Themes.FluentLight,
        };
        myApp.Run();
    }
}
