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
        var btn = Buttons.Primary("Button 1");
        btn.BackgroundColor = Colors.Black;
        Form mainForm = new()
        {
            Title = "Form 1 - Hello World",
            Size = new(1024, 768),
            Position = new(0, 0),
        };
        Grid grid = new()
        {
            Docking = Dock.Fill,
            ColumnDefinitions = [new(0.5f, true), new(0.5f, true)],
            RowDefinitions = [new(1f, true)],
        };
        Label labelLeft = new Label { Text = "Label left", Size = new(50, 200) };
        StackPanel stackPanel = new()
        {
            Column = 1,
            Padding = new(2),
        };
        Label labelRight1 = new Label { Text = "Label 1", Size = new(50, 200) };
        Label labelRight2 = new Label { Text = "Label 2", Size = new(50, 200) };
        Label labelRight3 = new Label { Text = "Label 3", Size = new(50, 200) };

        mainForm.Content = grid;
        grid.Children.Add(labelLeft);
        grid.Children.Add(stackPanel);
        stackPanel.Children.Add(labelRight1);
        stackPanel.Children.Add(labelRight2);
        stackPanel.Children.Add(labelRight3);
        stackPanel.Children.Add(btn);

        App myApp = new(windowsPlatform)
        {
            MainForm = mainForm
        };
        myApp.Run();
    }
}
