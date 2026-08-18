using ZeppelinForms;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Windows;

namespace ZF_Win;

public class Program
{
    static void Main()
    {
        WindowsPlatform windowsPlatform = new();
        App myApp = new(windowsPlatform)
        {
            MainForm = new()
            {
                Title = "Form 1 - Hello World",
                Size = new(1024, 768),
                Position = new(0, 0),
                Content = new StackPanel
                {
                    Docking = ZeppelinForms.Forms.Enums.Dock.Fill,
                    Children = 
                    [
                        new Label { Text = "Label 1", Size = new(100, 200) },    
                        new Label { Text = "Label 2", Size = new(100, 200) },    
                        new Label { Text = "Label 3", Size = new(100, 200) },
                    ]
                },
            }
        };
        myApp.Run();
    }
}
