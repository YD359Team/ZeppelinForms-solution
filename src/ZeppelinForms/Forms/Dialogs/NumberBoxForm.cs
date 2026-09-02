using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Dialogs;

internal sealed class NumberBoxForm : Form
{
    private readonly NumericUpDown _input;

    public NumberBoxForm(string prompt, string title, decimal initial, decimal minimum, decimal maximum, int decimalPlaces)
    {
        Title = title;
        Size = new Size(340, 170);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanMaximize = false;
        CanMinimize = false;

        _input = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            DecimalPlaces = decimalPlaces,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _input.Value = initial;

        var ok = Buttons.Primary("ОК");
        ok.Size = new Size(96, 32);
        ok.Click += (_, _) => Accept(_input.Value);

        var cancel = Buttons.Secondary("Отмена");
        cancel.Size = new Size(96, 32);
        cancel.Click += (_, _) => Cancel();

        Content = new DockPanel
        {
            Padding = new Thickness(16),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Docking = Dock.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
                new StackPanel
                {
                    Docking = Dock.Fill,
                    Spacing = 8,
                    Children =
                    {
                        new Label
                        {
                            Text = prompt,
                            TextColor = Colors.Black,
                            HorizontalContentAlign = HorizontalContentAlignment.Left,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                        },
                        _input,
                    },
                },
            },
        };
    }
}