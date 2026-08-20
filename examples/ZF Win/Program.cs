using ZeppelinForms;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;
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
                Content = new Grid
                {
                    Docking = Dock.Fill,
                    ColumnDefinitions = [new(0.5f, true), new(0.5f, true)],
                    RowDefinitions = [new(1f, true)],
                    Children =
                    [
                       new Label { Text = "Label 1", Size = new(50, 200) },
                       new StackPanel
                        {
                            Padding = new(2),
                            Children =
                            [
                                new Label { Text = "Label 1", Size = new(50, 200) },
                                new Label { Text = "Label 2", Size = new(50, 200) },
                                new Label { Text = "Label 3", Size = new(50, 200) },
                                new Button { Text = "Button 1", Size = new(50, 200), ButtonStyle = ButtonStyle.Primary },
                            ]
                        },
                    ]
                }
            }
        };
        myApp.Run();
    }
}
