using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Dialogs;

internal sealed class MessageBoxForm : Form
{
    private const float MaxTextWidth = 420f;

    public MessageBoxForm(string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanMaximize = false;
        CanMinimize = false;

        var text = new Label
        {
            Text = message,
            TextColor = Colors.Black,
            HorizontalContentAlign = HorizontalContentAlignment.Left,
            VerticalContentAlign = VerticalContentAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // размер окна считаем по тексту: диалог не должен быть шире нужного
        Size textSize = TextMeasurer.Current.MeasureText(message, Font.Default);
        float width = Math.Clamp(textSize.Width + 48, 260, MaxTextWidth);

        var body = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Docking = Dock.Fill,
        };

        if (icon != MessageBoxIcon.None)
            body.Children.Add(CreateIcon(icon));

        body.Children.Add(text);

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Docking = Dock.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        foreach (Button button in CreateButtons(buttons))
            buttonBar.Children.Add(button);

        Content = new DockPanel
        {
            Padding = new Thickness(16),
            Children = { buttonBar, body },
        };

        Size = new Size(width, Math.Max(140, textSize.Height + 110));
    }

    private IEnumerable<Button> CreateButtons(MessageBoxButtons buttons)
    {
        switch (buttons)
        {
            case MessageBoxButtons.Ok:
                yield return MakeButton("ОК", MessageBoxResult.Ok, primary: true);
                break;

            case MessageBoxButtons.OkCancel:
                yield return MakeButton("Отмена", MessageBoxResult.Cancel, primary: false);
                yield return MakeButton("ОК", MessageBoxResult.Ok, primary: true);
                break;

            case MessageBoxButtons.YesNo:
                yield return MakeButton("Нет", MessageBoxResult.No, primary: false);
                yield return MakeButton("Да", MessageBoxResult.Yes, primary: true);
                break;

            case MessageBoxButtons.YesNoCancel:
                yield return MakeButton("Отмена", MessageBoxResult.Cancel, primary: false);
                yield return MakeButton("Нет", MessageBoxResult.No, primary: false);
                yield return MakeButton("Да", MessageBoxResult.Yes, primary: true);
                break;
        }
    }

    private Button MakeButton(string caption, MessageBoxResult result, bool primary)
    {
        Button button = primary ? Buttons.Primary(caption) : Buttons.Secondary(caption);

        button.Size = new Size(96, 32);
        button.Click += (_, _) => Accept(result);

        return button;
    }

    private static UIElement CreateIcon(MessageBoxIcon icon)
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
