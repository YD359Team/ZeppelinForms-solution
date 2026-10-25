using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Layout;

namespace ZF_SharedLib.Showcase;

/// <summary>What opens over the window: tooltips, toasts, flyouts, context menus,
/// message boxes, file dialogs and dialogs of one's own — and the window going away
/// for a while.</summary>
internal static class OverlaysPage
{
    public static UIElement Create(Form form) =>
        Demo.Page("Dialogs and overlays", "Messages, questions and menus over the window, and windows over windows.",
            Demo.Section("Tooltips", Tooltips(), "UIElement.ToolTip: on hover with a mouse, on a long press with a finger"),
            Demo.Section("Toasts", Toasts(form), "form.ShowToast(message, duration, position): a note that goes away by itself"),
            Demo.Section("Flyout", Flyout(form), "form.ShowFlyout(anchor, content, placement): closes on a click outside or Esc"),
            Demo.Section("Context menu", ContextMenu(), "UIElement.ContextMenu: right click, the menu key or a long press; looked up the tree"),
            Demo.Section("Message box", Messages(form), "await MessageBox.ShowAsync works everywhere, the browser included"),
            Demo.Section("Input box and own dialogs", Dialogs(form), "Any form is a dialog: ShowDialogAsync<T> waits for Accept(value) or Cancel()"),
            Demo.Section("Files", Files(form), "FileDialog.OpenFileAsync, OpenFilesAsync, SaveFileAsync, SelectFolderAsync: the framework's own dialog"),
            Demo.Section("Hiding the window", Hiding(form), "form.Hide() keeps the window and its state; Show() brings it back — the ground of a tray icon"));

    private static UIElement Tooltips() => Demo.Row(
        new Button { Text = "Hover me", ToolTip = "A tooltip: a short hint about the control" },
        new Button { Icon = Icons.Trash, ToolTip = "Delete — an icon button names itself in its tooltip" },
        new Label { Text = "Labels have them too", ToolTip = "Any element can have one" });

    private static UIElement Toasts(Form form)
    {
        var message = new TextBox { Text = "Saved", Size = new(220, float.NaN) };

        Button At(string text, ToastPosition position)
        {
            var button = new Button { Text = text };
            button.Click += (_, _) => form.ShowToast(string.IsNullOrWhiteSpace(message.Text) ? "Hello" : message.Text, 2500, position);
            return button;
        }

        return Demo.Column(
            Demo.Labeled("Message", message),
            Demo.Row(
                At("Bottom right", ToastPosition.BottomRight),
                At("Bottom center", ToastPosition.BottomCenter),
                At("Top right", ToastPosition.TopRight),
                At("Top center", ToastPosition.TopCenter)));
    }

    private static UIElement Flyout(Form form)
    {
        var placement = new ComboBox { Items = { "Bottom", "Top", "Left", "Right" }, SelectedIndex = 0 };
        var open = new PrimaryButton { Text = "Open a flyout" };

        open.Click += (_, _) =>
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Padding = new(12),
            };

            // the surface: a plain panel shown bare has no background of its own
            var content = new FlyoutPresenter(panel);

            var close = new Button { Text = "Close" };
            close.Click += (_, _) => form.CloseFlyout(content);

            panel.Children.AddRange([
                new Label { Text = "A flyout", TextStyle = TextStyle.BodyStrong },
                new Label { Text = "Anything can be inside:\nhere a slider and a button." },
                new TrackBar { Value = 40, Size = new(180, float.NaN) },
                close,
            ]);

            form.ShowFlyout(open, content, (FlyoutPlacement)placement.SelectedIndex);
        };

        return Demo.Row(Demo.Labeled("Placement", placement), open);
    }

    private static UIElement ContextMenu()
    {
        Label chosen = Demo.Note("Nothing chosen");

        MenuItem Command(string text)
        {
            var item = new MenuItem { Text = text };
            item.Click += (_, _) => chosen.Text = $"Chosen: {text}";
            return item;
        }

        var share = new MenuItem { Text = "Share" };
        share.Items.AddRange([Command("E-mail"), Command("Link")]);

        var area = new Border
        {
            Size = new(float.NaN, 100),
            CornerRadius = new(8),
            BorderWidth = 1,
            BorderColor = new Color(120, 128, 128, 128),
            Background = new Color(20, 128, 128, 128),
            Child = new Label
            {
                Text = "Right-click or long-press here",
                HorizontalContentAlign = HorizontalContentAlignment.Center,
                VerticalContentAlign = VerticalContentAlignment.Center,
            },
            ContextMenu = [Command("Copy"), Command("Paste"), MenuItem.Separator, share, new MenuItem { Text = "Disabled", IsEnabled = false }],
        };

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        root.Children.AddRange([area, chosen]);
        return root;
    }

    private static UIElement Messages(Form form)
    {
        Label answer = Demo.Note("No answer yet");

        var buttons = new ComboBox { SelectedIndex = -1 };
        buttons.Items.AddRange(Enum.GetNames<MessageBoxButtons>());
        buttons.SelectedIndex = (int)MessageBoxButtons.YesNoCancel;

        var icon = new ComboBox { SelectedIndex = -1 };
        icon.Items.AddRange(Enum.GetNames<MessageBoxIcon>());
        icon.SelectedIndex = (int)MessageBoxIcon.Question;

        var show = new PrimaryButton { Text = "Ask" };
        show.Click += async (_, _) =>
        {
            MessageBoxResult result = await MessageBox.ShowAsync(
                form,
                "Save the changes before closing?",
                "Showcase",
                (MessageBoxButtons)buttons.SelectedIndex,
                (MessageBoxIcon)icon.SelectedIndex);

            answer.Text = $"Answer: {result}";
        };

        var error = new DangerButton { Text = "An error" };
        error.Click += async (_, _) => await MessageBox.ErrorAsync(form, "Something went wrong. Nothing was lost.");

        return Demo.Column(Demo.Row(Demo.Labeled("Buttons", buttons), Demo.Labeled("Icon", icon)), Demo.Row(show, error), answer);
    }

    private static UIElement Dialogs(Form form)
    {
        Label answer = Demo.Note("Nothing entered yet");

        var name = new Button { Text = "Ask a name" };
        name.Click += async (_, _) =>
        {
            string? value = await InputBox.ShowAsync(form, "What is your name?", "Showcase", "Zeppelin");
            answer.Text = value is null ? "Cancelled" : $"Hello, {value}!";
        };

        var secret = new Button { Text = "Ask a password" };
        secret.Click += async (_, _) =>
        {
            string? value = await InputBox.ShowAsync(form, "A password, please", "Showcase", passwordChar: '•');
            answer.Text = value is null ? "Cancelled" : $"{value.Length} character(s), not shown";
        };

        var own = new PrimaryButton { Text = "Pick a color" };
        own.Click += async (_, _) =>
        {
            DialogResult<Color> result = await new ColorDialog().ShowDialogAsync<Color>(form);
            answer.Text = result.IsAccepted ? $"Picked #{result.Value.R:X2}{result.Value.G:X2}{result.Value.B:X2}" : "Cancelled";
        };

        return Demo.Column(Demo.Row(name, secret, own), answer);
    }

    private static UIElement Files(Form form)
    {
        Label chosen = Demo.Note("Nothing chosen yet");

        var options = new FileDialogOptions
        {
            Filters = { new FileFilter("Images", "png", "jpg", "jpeg", "gif"), new FileFilter("Text", "txt", "md", "json") },
        };

        var open = new PrimaryButton { Text = "Open a file" };
        open.Click += async (_, _) =>
            chosen.Text = await FileDialog.OpenFileAsync(form, options) is { } file ? file : "Cancelled";

        var many = new Button { Text = "Open several" };
        many.Click += async (_, _) =>
        {
            string[] files = await FileDialog.OpenFilesAsync(form, options);
            chosen.Text = files.Length == 0 ? "Cancelled" : $"{files.Length} file(s): {string.Join(", ", files.Select(Path.GetFileName))}";
        };

        var save = new Button { Text = "Save as" };
        save.Click += async (_, _) =>
            chosen.Text = await FileDialog.SaveFileAsync(form, new FileDialogOptions { FileName = "notes.txt" }) is { } file
                ? $"Would save to {file}"
                : "Cancelled";

        var folder = new Button { Text = "Pick a folder" };
        folder.Click += async (_, _) =>
            chosen.Text = await FileDialog.SelectFolderAsync(form) is { } path ? path : "Cancelled";

        return Demo.Column(Demo.Row(open, many, save, folder), chosen);
    }

    private static UIElement Hiding(Form form)
    {
        Label note = Demo.Note("The window comes back by itself after three seconds");

        var hide = new Button { Text = "Hide for 3 seconds" };
        hide.Click += async (_, _) =>
        {
            try
            {
                form.Hide();
            }
            catch (PlatformNotSupportedException)
            {
                // the browser and Android: the form is the page or the screen
                note.Text = "This platform can't hide its window";
                return;
            }

            await Task.Delay(3000);
            form.Show();
        };

        return Demo.Column(hide, note);
    }

    /// <summary>A dialog of one's own: a form that closes with <see cref="Form.Accept"/>
    /// and the value, or with <see cref="Form.Cancel"/>.</summary>
    private sealed class ColorDialog : Form
    {
        public ColorDialog()
        {
            Title = "Pick a color";
            Size = new(360, 420);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var picker = new ColorPicker { Value = new(255, 0x0D, 0x6E, 0xFD) };

            var ok = new PrimaryButton { Text = "OK" };
            ok.Click += (_, _) => Accept(picker.Value);

            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (_, _) => Cancel();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                MainAxisAlignment = MainAxisAlignment.End,
                Children = { cancel, ok },
            };

            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 16,
                Padding = new(16),
                CrossAxisAlignment = CrossAxisAlignment.Stretch,
                Children = { picker, buttons },
            };
        }
    }
}
