using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Dialogs;

internal sealed class MessageBoxForm : Form
{
    /// <summary>The widest the message may make the box: beyond it the text wraps.</summary>
    private const float MaxWidth = 480f;

    /// <summary>A one-word message still gets a box that looks like one.</summary>
    private const float MinWidth = 320f;

    public MessageBoxForm(string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanMaximize = false;
        CanMinimize = false;

        // the box is as large as its message and its buttons. It used to be sized by
        // the text alone: three buttons didn't fit next to a short question, and on
        // Windows, where Size includes the title bar, the height came out ~40 px short
        SizeToContent = SizeToContent.WidthAndHeight;
        MinimumSize = new Size(MinWidth, float.NaN);
        MaximumSize = new Size(MaxWidth, float.NaN);

        // no color of its own: the theme's text color, readable in the dark theme too.
        // It used to be black, and vanished on a dark background
        var text = new Label
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            HorizontalContentAlign = HorizontalContentAlignment.Left,
            VerticalContentAlign = VerticalContentAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // a dock panel rather than a horizontal stack: the stack offered the text an
        // endless width, and a long message never wrapped
        var body = new DockPanel();

        if (icon != MessageBoxIcon.None)
        {
            SvgIcon glyph = CreateIcon(icon);
            glyph.Docking = Dock.Left;
            glyph.Margin = new Thickness(0, 0, 12, 0);

            body.Children.Add(glyph);
        }

        body.Children.Add(text);

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Docking = Dock.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        foreach (Button button in CreateButtons(buttons))
            buttonBar.Children.Add(button);

        Content = new DockPanel
        {
            Padding = new Thickness(16),
            Children = { buttonBar, body },
        };
    }

    private IEnumerable<Button> CreateButtons(MessageBoxButtons buttons)
    {
        switch (buttons)
        {
            case MessageBoxButtons.Ok:
                yield return MakeButton(ZfText.Ok, MessageBoxResult.Ok, primary: true);
                break;

            case MessageBoxButtons.OkCancel:
                yield return MakeButton(ZfText.Cancel, MessageBoxResult.Cancel, primary: false);
                yield return MakeButton(ZfText.Ok, MessageBoxResult.Ok, primary: true);
                break;

            case MessageBoxButtons.YesNo:
                yield return MakeButton(ZfText.No, MessageBoxResult.No, primary: false);
                yield return MakeButton(ZfText.Yes, MessageBoxResult.Yes, primary: true);
                break;

            case MessageBoxButtons.YesNoCancel:
                yield return MakeButton(ZfText.Cancel, MessageBoxResult.Cancel, primary: false);
                yield return MakeButton(ZfText.No, MessageBoxResult.No, primary: false);
                yield return MakeButton(ZfText.Yes, MessageBoxResult.Yes, primary: true);
                break;
        }
    }

    private Button MakeButton(TextKey caption, MessageBoxResult result, bool primary)
    {
        Button button = primary ? Buttons.Primary(caption) : Buttons.Secondary(caption);

        button.Size = new Size(96, 32);
        button.Click += (_, _) => Accept(result);

        return button;
    }

    private static SvgIcon CreateIcon(MessageBoxIcon icon)
    {
        (string path, Color color) = icon switch
        {
            MessageBoxIcon.Information => ("M12 16v-5M12 8h.01", new Color(255, 0x0D, 0x6E, 0xFD)),
            MessageBoxIcon.Warning => ("M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z",
                new Color(255, 0xFF, 0xC1, 0x07)),
            MessageBoxIcon.Error => ("M15 9l-6 6M9 9l6 6", new Color(255, 0xDC, 0x35, 0x45)),
            _ => ("M9.1 9a3 3 0 1 1 5.8 1c0 2-3 3-3 3M12 17h.01", new Color(255, 0x6C, 0x75, 0x7D)),
        };

        return new SvgIcon
        {
            PathData = path,
            StrokeWidth = 2,
            Color = color,
            IconSize = 28,
            VerticalAlignment = VerticalAlignment.Top,
        };
    }
}