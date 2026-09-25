using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Dialogs;

internal sealed class InputBoxForm : Form
{
    private readonly TextBox _input;

    public InputBoxForm(string prompt, string title, string initialValue, char? passwordChar)
    {
        Title = title;
        Size = new Size(380, 170);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanMaximize = false;
        CanMinimize = false;

        _input = new TextBox
        {
            Text = initialValue,
            PasswordChar = passwordChar,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // Enter in the field is the same as pressing OK — that is how system dialogs behave
        _input.Accepted += (_, _) => Accept(_input.Text ?? string.Empty);

        var ok = Buttons.Primary("ОК");
        ok.Size = new Size(96, 32);
        ok.Click += (_, _) => Accept(_input.Text ?? string.Empty);

        var cancel = Buttons.Secondary("Отмена");
        cancel.Size = new Size(96, 32);
        cancel.Click += (_, _) => Cancel();

        Content = new DockPanel
        {
            // the renderer clears the window to white and knows nothing about
            // the theme, so the root paints the theme background itself —
            // otherwise the prompt, which takes the theme text color,
            // ends up light-on-white in the dark theme
            Background = App.Theme.Colors.Background,
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