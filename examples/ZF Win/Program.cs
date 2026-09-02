using ZeppelinForms;
using ZF_SharedLib;
using ZeppelinForms.Windows;

namespace ZF_Win;

public class Program
{
    static void Main()
    {
        WindowsPlatform windowsPlatform = new();
        App myApp = new(windowsPlatform)
        {
            MainForm = new ExampleMainForm()
        };
        myApp.Run();
    }
}
