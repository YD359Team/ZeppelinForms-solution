using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>A phone application's frame: an app bar and a navigation bar around a stack of
/// pages, rows swiped aside for their actions, a sheet from the bottom, a snackbar.</summary>
internal static class MobilePage
{
    public static UIElement Create() =>
        Demo.Page("Mobile", "The frame of a phone application, the same on a phone, in a browser and in a window.",
            Demo.Section("Scaffold", Phone(),
                "Tap a message to open it. The arrow in the bar, Alt+Left, a swipe from the left edge, the browser's back button and Android's back go back. " +
                "Swipe a message: to the left to delete it, to the right to star it — or right-click it. Tap Inbox again to go back to its top"),
            Demo.Section("Bottom sheet", Sheet(), "Drag it down, tap beside it, or press Escape or back to close it"),
            Demo.Section("Snackbar", Snack(), "One at a time; with an action it waits longer"));

    // ===== the phone =====

    private static readonly string[] Senders = ["Anna", "Boris", "Clara", "Dmitry", "Eva", "Fyodor", "Galina", "Hugo"];

    private static UIElement Phone()
    {
        var inbox = new NavigationHost();
        inbox.RootPage = new Page(Inbox(inbox), "Inbox");
        inbox.RegisterRoute("message", parameters => MessagePage(parameters.GetValueOrDefault("from", "Someone")));

        var appBar = new AppBar
        {
            OverflowItems =
            [
                new MenuItem { Text = "Mark all as read" },
                new MenuItem { Text = "Settings" },
            ],
        };

        var write = new Button
        {
            Icon = Icons.Send,
            Text = "Write",
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 14),
        };

        write.Classes.Add("accent");
        write.Click += (_, _) => _ = inbox.PushAsync(new Page(Compose(), "New message"));

        var scaffold = new Scaffold
        {
            AppBar = appBar,
            FloatingActionButton = write,
            NavigationBar = new NavigationBar
            {
                Items =
                {
                    new NavigationBarItem("Inbox", Icons.List) { Content = inbox, Badge = "3" },
                    new NavigationBarItem("Starred", Icons.Heart, () => new NavigationHost { RootPage = new Page(Centered("Starred messages come here"), "Starred") }),
                    new NavigationBarItem("Settings", Icons.Gear, () => new NavigationHost { RootPage = new Page(Settings(), "Settings") }) { Badge = "" },
                },
            },
        };

        // a phone's screen in a card: 360 × 640, the size the frame is made for
        return new Border
        {
            Size = new Size(360, 640),
            BorderWidth = 1,
            CornerRadius = new CornerRadius(12),
            Child = scaffold,
        };
    }

    private static UIElement Inbox(NavigationHost host)
    {
        var list = new StackPanel
        {
            Orientation = Orientation.Vertical,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
            OverflowY = Overflow.Auto,
        };

        foreach (string sender in Senders)
            list.Children.Add(Row(host, list, sender));

        return list;
    }

    private static SwipeView Row(NavigationHost host, StackPanel list, string sender)
    {
        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Padding = new Thickness(16, 10) };
        text.Children.Add(new Label { Text = sender, TextStyle = TextStyle.BodyStrong });
        text.Children.Add(new Label { Text = $"A message from {sender} about the release", TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });

        var row = new SwipeView(text) { RightMode = SwipeMode.Execute };

        row.LeftItems.Add(new SwipeItem("Star", Icons.Heart, new Color(255, 0xF5, 0x9E, 0x0B)));
        row.RightItems.Add(new SwipeItem("Delete", Icons.Trash, new Color(255, 0xDC, 0x35, 0x45), async (_, _) =>
        {
            int index = list.Children.IndexOf(row);
            list.Children.Remove(row);

            if (await (list.FindForm() ?? host.FindForm())!.ShowSnackbarAsync($"Message from {sender} deleted", "Undo"))
                list.Children.Insert(Math.Min(index, list.Children.Count), Row(host, list, sender));
        }));

        // the content's click, not the row's: a tap on a row swiped aside closes it, and
        // its content takes no clicks until then
        text.Click += (_, _) => _ = host.GoToAsync($"message?from={Uri.EscapeDataString(sender)}");

        return row;
    }

    private static Page MessagePage(string sender)
    {
        var body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Padding = new Thickness(16) };

        body.Children.Add(new Label { Text = $"From {sender}", TextStyle = TextStyle.BodyStrong });
        body.Children.Add(new Label
        {
            Text = "The release is close: the navigation stack, the bars and the sheets are in. " +
                   "Go back with the arrow, with Alt+Left or with a swipe from the left edge.",
            TextWrapping = TextWrapping.Wrap,
        });

        var reply = new Button { Text = "Reply", HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(reply);

        var page = new Page(body, sender);
        reply.Click += (_, _) => _ = page.NavigationHost?.PushAsync(new Page(Compose(), $"Re: {sender}"));

        page.Actions.Add(new AppBarButton(Icons.Heart, "Star"));
        page.Actions.Add(new AppBarButton(Icons.Trash, "Delete", (_, _) => _ = page.NavigationHost?.PopAsync()));

        return page;
    }

    private static UIElement Compose()
    {
        var form = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Padding = new Thickness(16), CrossAxisAlignment = CrossAxisAlignment.Stretch };

        form.Children.Add(new TextBox { Watermark = "To" });
        form.Children.Add(new TextBox { Watermark = "Message", IsMultiline = true, Size = new Size(float.NaN, 160) });

        return form;
    }

    private static UIElement Settings()
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Padding = new Thickness(16) };

        column.Children.Add(new ToggleSwitch { Text = "Notifications", IsOn = true });
        column.Children.Add(new ToggleSwitch { Text = "Load pictures" });
        column.Children.Add(new ToggleSwitch { Text = "Dark theme" });

        return column;
    }

    private static Label Centered(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // ===== the sheet and the snackbar =====

    private static UIElement Sheet()
    {
        var open = new Button { Text = "Share…" };
        Label result = Demo.Note("Nothing shared yet");

        open.Click += async (_, _) =>
        {
            var options = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, CrossAxisAlignment = CrossAxisAlignment.Stretch };
            options.Children.Add(new Label { Text = "Share with", TextStyle = TextStyle.Subtitle });

            var sheet = new BottomSheet(options);

            foreach (string target in new[] { "Messages", "Mail", "Copy link" })
            {
                var choice = new Button { Text = target, HorizontalContentAlign = HorizontalContentAlignment.Left };
                choice.Click += (_, _) =>
                {
                    result.Text = $"Shared with {target}";
                    sheet.Close();
                };

                options.Children.Add(choice);
            }

            if (open.FindForm() is { } form) await sheet.ShowAsync(form);
        };

        return Demo.Row(open, result);
    }

    private static UIElement Snack()
    {
        var plain = new Button { Text = "Saved" };
        var undo = new Button { Text = "Archive, with Undo" };
        Label result = Demo.Note("");

        plain.Click += (_, _) => _ = plain.FindForm()?.ShowSnackbarAsync("Changes saved");

        undo.Click += async (_, _) =>
        {
            if (undo.FindForm() is not { } form) return;

            bool undone = await form.ShowSnackbarAsync("Conversation archived", "Undo");
            result.Text = undone ? "Undone" : "Archived";
        };

        return Demo.Row(plain, undo, result);
    }
}
