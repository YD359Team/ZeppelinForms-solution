using ZeppelinForms;
using ZF_SharedLib;
using ZeppelinForms.Linux;

namespace ZF_Linux;

public class Program
{
    static void Main()
    {
        X11Platform linuxPlatform = new();
        App myApp = new(linuxPlatform)
        {
            MainForm = new ExampleMainForm()
        };
        myApp.Run();
    }
}
