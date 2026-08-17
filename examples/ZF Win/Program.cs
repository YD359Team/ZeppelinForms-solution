using ZeppelinForms;
using ZeppelinForms.Forms;
using ZeppelinForms.Windows;

namespace ZF_Win;

public class Program
{
    static void Main()
    {
        WindowsPlatform windowsPlatform = new WindowsPlatform();
        App myApp = new App(windowsPlatform)
        {
            MainForm = new Form
            {
                Title = "Form 1 - Hello World",
                Size = new(1024, 768),
                Position = new(0, 0),
            }
        };
        myApp.Run();
    }
}
