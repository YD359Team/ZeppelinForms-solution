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
                Content = new Panel
                {
                    Size = new(1024, 768),
                    Background = new Color(240, 240, 240),
                    Children =
                    {
                        new Label
                        {
                            Position = new(20, 20),
                            Size = new(300, 24),
                            Text = "Hello, ZeppelinForms!",
                        }, 
                        new Button
                        {
                            Position = new(120, 120),
                            Size = new(300, 24),
                            Text = "Click me!",
                        }
                    },
                },
            }
        };
        myApp.Run();
    }
}
