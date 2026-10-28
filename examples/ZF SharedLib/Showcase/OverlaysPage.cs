using ZeppelinForms;
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
            Demo.Section("Tooltips", Tooltips(), "UIElement.ToolTip or ToolTipContent: on hover, and for the keyboard focus — Tab onto a button. Styled as ToolTip in a sheet"),
            Demo.Section("Toasts", Toasts(form), "form.ShowToast(message, duration, position): a note that goes away by itself"),
            Demo.Section("Flyout", Flyout(form), "form.ShowFlyout(anchor, content, placement): closes on a click outside or Esc"),
            Demo.Section("Context menu", ContextMenu(), "UIElement.ContextMenu: a right click, the menu key, or a finger or a pen held on it; looked up the tree. An item with Items of its own — Share — opens a submenu"),
            Demo.Section("Message box", Messages(form), "await MessageBox.ShowAsync works everywhere, the browser included"),
            Demo.Section("Input box and own dialogs", Dialogs(form), "Any form is a dialog: ShowDialogAsync<T> waits for Accept(value) or Cancel()"),
            Demo.Section("Files", Files(form), "FileDialog: the system's dialog — Explorer's, the desktop portal's, the browser's — or the framework's own; PickSaveFileAsync writes through a stream everywhere"),
            Demo.Section("Hiding the window", Hiding(form), "form.Hide() keeps the window and its state; Show() brings it back — the ground of a tray icon"),
            Demo.Section("Tray icon", Tray(form), "TrayIcon: an icon in the notification area or on the panel, with a tooltip, a click and the system's menu"),
            Demo.Section("Taskbar button", Taskbar(form), "form.Taskbar: progress and a badge on the taskbar or the dock; form.RequestAttention() for a window in the background"),
            Demo.Section("Notifications", NotificationsDemo(form), "Notifications.Show: a toast on Windows, the desktop's notification on Linux, a web notification in the browser; TrayIcon.ShowNotification from the tray icon"));

    private static UIElement Tooltips() => Demo.Row(
        new Button { Text = "Hover me", ToolTip = "A tooltip: a short hint about the control" },
        new Button { Icon = Icons.Trash, ToolTip = "Delete — an icon button names itself in its tooltip" },
        new Label { Text = "Labels have them too", ToolTip = "Any element can have one" },
        new Button
        {
            Text = "A long one",
            ToolTip = "A long tooltip doesn't run across the window: past its MaxContentWidth, 320 by default, " +
                      "the text wraps into rows, as tooltips do in WinUI.",
        },
        new Button
        {
            Text = "Rich content",

            // the text is still there: a screen reader reads it as the description
            ToolTip = "Ctrl+S saves the document",
            ToolTipContent = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2,
                Children =
                {
                    new Label { Text = "Save", TextStyle = TextStyle.BodyStrong },
                    new Label { Text = "Ctrl+S", TextStyle = TextStyle.Caption, Classes = { "demo-note" } },
                },
            },
        },
        new Button { Text = "At once", ToolTip = "ToolTipDelay = 0: no waiting for this one", ToolTipDelay = 0 });

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
                Text = "Right-click, or hold a finger here",
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

        // the system's dialog by default; the framework's own on request
        var own = new CheckBox { Text = "The framework's own dialog" };
        own.CheckedChanged += (_, _) => options.UseNative = !own.IsChecked;

        var open = new PrimaryButton { Text = "Open a file" };
        open.Click += async (_, _) =>
            chosen.Text = await FileDialog.OpenFileAsync(form, options) is { } file ? file : "Cancelled";

        var many = new Button { Text = "Open several" };
        many.Click += async (_, _) =>
        {
            string[] files = await FileDialog.OpenFilesAsync(form, options);
            chosen.Text = files.Length == 0 ? "Cancelled" : $"{files.Length} file(s): {string.Join(", ", files.Select(Path.GetFileName))}";
        };

        // a picked file rather than a path: the same code writes it on the desktop and in
        // the browser — through its save dialog, or as a download
        var save = new Button { Text = "Save as" };
        save.Click += async (_, _) =>
        {
            var saveOptions = new FileDialogOptions
            {
                FileName = "notes.txt",
                Filters = { new FileFilter("Text", "txt") },
                UseNative = options.UseNative,
            };

            if (await FileDialog.PickSaveFileAsync(form, saveOptions) is not { } file)
            {
                chosen.Text = "Cancelled";
                return;
            }

            await using (Stream output = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(output))
                await writer.WriteLineAsync($"Saved by the ZeppelinForms showcase at {DateTime.Now:t}");

            chosen.Text = $"Saved {file}";
        };

        var folder = new Button { Text = "Pick a folder" };
        folder.Click += async (_, _) =>
            chosen.Text = await FileDialog.SelectFolderAsync(form) is { } path ? path : "Cancelled";

        return Demo.Column(Demo.Row(open, many, save, folder), own, chosen);
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

    /// <summary>The taskbar button: a pretend task with its progress, a badge, and a request
    /// for attention a few seconds later — time to switch to another window.</summary>
    private static UIElement Taskbar(Form form)
    {
        Label note = Demo.Note($"{App.DisplayName} {App.Version}, id {App.Id}");

        var run = new PrimaryButton { Text = "Run a task" };
        run.Click += async (_, _) =>
        {
            run.IsEnabled = false;

            for (int step = 0; step <= 50; step++)
            {
                form.Taskbar.ProgressValue = step / 50.0;
                note.Text = $"Working: {step * 2} %";
                await Task.Delay(100);
            }

            form.Taskbar.ProgressState = TaskbarProgressState.None;
            note.Text = "Done — and the button asks for attention, if the window is in the background";
            form.RequestAttention();

            run.IsEnabled = true;
        };

        var paused = new Button { Text = "Paused" };
        paused.Click += (_, _) =>
        {
            form.Taskbar.ProgressValue = Math.Max(form.Taskbar.ProgressValue, 0.6);
            form.Taskbar.ProgressState = TaskbarProgressState.Paused;
        };

        var failed = new Button { Text = "Failed" };
        failed.Click += (_, _) =>
        {
            form.Taskbar.ProgressValue = Math.Max(form.Taskbar.ProgressValue, 0.6);
            form.Taskbar.ProgressState = TaskbarProgressState.Error;
        };

        var badge = new Button { Text = "Badge + 1" };
        badge.Click += (_, _) => form.Taskbar.Badge = (form.Taskbar.Badge ?? 0) + 1;

        var clear = new Button { Text = "Clear" };
        clear.Click += (_, _) => form.Taskbar.Clear();

        var attention = new Button { Text = "Attention in 3 s" };
        attention.Click += async (_, _) =>
        {
            note.Text = "Switch to another window: in three seconds this one asks for attention";
            await Task.Delay(3000);
            form.RequestAttention();
        };

        return Demo.Column(Demo.Row(run, paused, failed, badge, clear, attention), note);
    }

    /// <summary>The system's notifications: one of each kind, and one from the tray icon;
    /// a click brings the form forward.</summary>
    private static UIElement NotificationsDemo(Form form)
    {
        Label note = Demo.Note("A click on a notification comes back here");

        void Watch(Notification? notification)
        {
            if (notification is null)
            {
                note.Text = "No notifications here — or they are turned off";
                return;
            }

            note.Text = $"Shown: {notification.Title}";

            notification.Clicked += (_, _) =>
            {
                note.Text = $"Clicked: {notification.Title}";
                form.Show();
                form.Activate();
            };

            notification.Closed += (_, _) =>
            {
                if (note.Text == $"Shown: {notification.Title}") note.Text = $"Closed: {notification.Title}";
            };
        }

        var info = new PrimaryButton { Text = "Information" };
        info.Click += (_, _) => Watch(Notifications.Show("Export finished", "orders.csv — 1 248 rows"));

        var warning = new Button { Text = "Warning" };
        warning.Click += (_, _) => Watch(Notifications.Show("Disk almost full", "2 GB left on C:", NotificationKind.Warning));

        var error = new Button { Text = "Error" };
        error.Click += (_, _) => Watch(Notifications.Show("Sync failed", "The server didn't answer", NotificationKind.Error));

        var fromTray = new Button { Text = "From the tray icon" };
        fromTray.Click += (_, _) =>
            Watch(s_tray is { IsShown: true } tray
                ? tray.ShowNotification("From the tray", "TrayIcon.ShowNotification — the icon's balloon on Windows")
                : Notifications.Show("No tray icon yet", "Show it in the Tray icon section first"));

        return Demo.Column(Demo.Row(info, warning, error, fromTray), note);
    }

    // one icon for the application, whatever times the page is made again
    private static TrayIcon? s_tray;
    private static bool s_closeToTray;

    /// <summary>The icon in the tray, and "close to the tray": the main form's Closing
    /// cancelled and the form hidden; a click on the icon or its menu brings it back.</summary>
    private static UIElement Tray(Form form)
    {
        Label note = Demo.Note(s_tray?.IsShown == true
            ? "The icon is in the tray: click it, or open its menu"
            : "Windows shows it in the notification area, Linux on a panel with StatusNotifierItem");

        if (s_tray is null)
        {
            s_tray = new TrayIcon { ToolTip = "ZeppelinForms Showcase" };

            var show = new MenuItem { Text = "Show the window" };
            show.Click += (_, _) => form.Show();

            var closeToTray = new MenuItem { Text = "Close to the tray", IsCheckable = true };
            closeToTray.Click += (_, _) => s_closeToTray = closeToTray.IsChecked;

            var quit = new MenuItem { Text = "Quit" };
            quit.Click += (_, _) =>
            {
                s_closeToTray = false;
                form.Show();
                form.Close();
            };

            s_tray.Menu.AddRange([show, closeToTray, MenuItem.Separator, quit]);

            // the menu shows the state as it is when it opens
            s_tray.MenuOpening += (_, _) => closeToTray.IsChecked = s_closeToTray;

            s_tray.Click += (_, _) => form.Show();

            // the close button hides the window while the icon is there to bring it back
            form.Closing += (_, e) =>
            {
                if (!s_closeToTray || s_tray?.IsShown != true || e.Reason != CloseReason.UserClosing) return;

                e.Cancel = true;
                form.Hide();
            };
        }

        var visible = new CheckBox { Text = "Icon in the tray", IsChecked = s_tray.Visible };
        visible.CheckedChanged += (_, _) =>
        {
            s_tray.Visible = visible.IsChecked;

            note.Text = !visible.IsChecked ? "The icon is off the tray"
                : s_tray.IsShown ? "The icon is in the tray: click it, or open its menu"
                : "This platform has no tray";
        };

        var close = new CheckBox { Text = "Close to the tray", IsChecked = s_closeToTray };
        close.CheckedChanged += (_, _) => s_closeToTray = close.IsChecked;

        var tip = new TextBox { Text = s_tray.ToolTip ?? string.Empty, Size = new(260, float.NaN) };
        tip.TextChanged += (_, _) => s_tray.ToolTip = tip.Text;

        return Demo.Column(Demo.Row(visible, close), Demo.Row(new Label { Text = "Tooltip" }, tip), note);
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
