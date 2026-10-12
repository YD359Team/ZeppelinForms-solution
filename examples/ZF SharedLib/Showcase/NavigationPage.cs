using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>Moving between screens: tabs, a browser's tab strip, swiped pages, a menu
/// bar and the split view this showcase itself is made of.</summary>
internal static class NavigationPage
{
    public static UIElement Create() =>
        Demo.Page("Navigation", "Tabs, pages, menus and panes: the ways to move between screens.",
            Demo.Section("Tab control", Tabs(), "Ctrl+Tab and Ctrl+Shift+Tab go through the tabs; the disabled one is skipped"),
            Demo.Section("Tab strip", Strip(), "The plus adds a tab, the cross or a middle click closes one, dragging reorders them"),
            Demo.Section("Pages", Pages(), "Swipe or drag the page sideways, or click the dots"),
            Demo.Section("Menu bar", Menu(), "Alt and the underlined letter open a menu from the keyboard"),
            Demo.Section("Split view", Split(), "The control this whole showcase is: items with icons, headers, a footer, a badge"));

    private static UIElement Tabs()
    {
        var tabs = new TabControl
        {
            Size = new(float.NaN, 200),
            Tabs =
            [
                new TabItem { Header = "General", Content = Body("The first tab: what a tab control shows when it opens") },
                new TabItem { Header = "Appearance", Content = Body("Each tab keeps its own content; switching only shows another") },
                new TabItem { Header = "Advanced", Content = Body("TabStripPlacement puts the headers on any side") },
                new TabItem { Header = "Disabled", IsEnabled = false, Content = Body("Never shown") },
            ],
        };

        var placement = new ComboBox { Items = { "Top", "Bottom", "Left", "Right" }, SelectedIndex = 0 };
        placement.SelectionChanged += (_, _) => tabs.TabStripPlacement = (TabStripPlacement)placement.SelectedIndex;

        var root = Stretch();
        root.Children.AddRange([Demo.Row(Demo.Labeled("Headers on the", placement)), tabs]);
        return root;
    }

    private static UIElement Strip()
    {
        int opened = 3;
        Label content = Body("Document 1");

        var strip = new TabStrip
        {
            ShowAddButton = true,
            CanReorder = true,
            Items =
            {
                new TabStripItem("Document 1") { Icon = Icons.Text },
                new TabStripItem("Document 2") { Icon = Icons.Text },
                new TabStripItem("Pinned") { Icon = Icons.Heart, IsClosable = false, ToolTip = "Not closable" },
            },
            SelectedIndex = 0,
        };

        strip.SelectionChanged += (_, _) => content.Text = strip.SelectedItem?.Header ?? "No tabs open";

        strip.AddTabRequested += (_, _) =>
        {
            var tab = new TabStripItem($"Document {++opened}") { Icon = Icons.Text };
            strip.Items.Add(tab);
            strip.SelectedItem = tab;
        };

        // the strip asks; the application decides and removes the tab itself
        strip.TabCloseRequested += (_, e) => strip.Items.Remove(e.Item);

        var root = Stretch();
        root.Children.AddRange([strip, content]);
        return root;
    }

    private static UIElement Pages()
    {
        var pages = new PageControl
        {
            Size = new(float.NaN, 180),
            SwipeMode = PageSwipeMode.Sequential,
        };

        (string Name, string Title, Color Color)[] slides =
        [
            ("welcome", "Welcome", new Color(255, 0x0D, 0x6E, 0xFD)),
            ("features", "Features", new Color(255, 0x66, 0x10, 0xF2)),
            ("start", "Get started", new Color(255, 0x19, 0x87, 0x54)),
        ];

        foreach (var slide in slides)
            pages.AddPage(slide.Name, () => Slide(slide.Title, slide.Color), slide.Title);

        PageIndicator dots = pages.CreateIndicator(PageIndicatorStyle.Dots);
        dots.HorizontalAlignment = HorizontalAlignment.Center;

        int current = 0;

        void Go(int step)
        {
            current = (current + step + slides.Length) % slides.Length;
            pages.Navigate(slides[current].Name, step > 0 ? PageTransition.SlideLeft : PageTransition.SlideRight);
        }

        // a swipe or a dot moves the page too: follow it
        pages.Navigated += (_, page) => current = Math.Max(0, Array.FindIndex(slides, s => s.Title == page.Title));

        var back = new Button { Text = "Previous" };
        back.Click += (_, _) => Go(-1);

        var next = new PrimaryButton { Text = "Next" };
        next.Click += (_, _) => Go(+1);

        var style = new ComboBox { Items = { "Dots", "Dashes", "Numbers" }, SelectedIndex = 0 };
        style.SelectionChanged += (_, _) => dots.Style = (PageIndicatorStyle)style.SelectedIndex;

        var root = Stretch();
        root.Children.AddRange([pages, dots, Demo.Row(back, next, Demo.Labeled("Indicator", style))]);
        return root;
    }

    private static UIElement Menu()
    {
        Label ran = Demo.Note("Choose a command");

        MenuItem Command(string text)
        {
            var item = new MenuItem { Text = text };
            item.Click += (_, _) => ran.Text = $"Ran: {text.Replace("&", string.Empty, StringComparison.Ordinal)}";
            return item;
        }

        var file = new MenuItem { Text = "&File" };
        file.Items.AddRange([Command("&New"), Command("&Open…"), Command("&Save"), MenuItem.Separator, Command("E&xit")]);

        var edit = new MenuItem { Text = "&Edit" };
        edit.Items.AddRange([Command("&Undo"), Command("&Redo"), MenuItem.Separator, Command("Cu&t"), Command("&Copy"), Command("&Paste")]);

        var recent = new MenuItem { Text = "&Recent" };
        recent.Items.AddRange([Command("report.md"), Command("budget.xlsx")]);

        var view = new MenuItem { Text = "&View" };
        view.Items.AddRange([Command("&Zoom in"), Command("Zoom &out"), MenuItem.Separator, recent]);

        var bar = new MenuBar { Items = [file, edit, view] };

        var root = Stretch();
        root.Children.AddRange([bar, ran]);
        return root;
    }

    private static UIElement Split()
    {
        Label chosen = Body("Inbox");

        var view = new SplitView
        {
            DisplayMode = SplitViewDisplayMode.CompactInline,
            IsPaneOpen = true,
            OpenPaneLength = 200,
            PaneTitle = "Mail",
            Size = new(float.NaN, 280),
            Content = chosen,
        };

        view.Items.Add(new SplitViewItem("Inbox", Icons.Home) { Badge = "3" });
        view.Items.Add(new SplitViewItem("Sent", Icons.Send));
        view.Items.Add(SplitViewItem.Header("Folders"));
        view.Items.Add(new SplitViewItem("Favorites", Icons.Heart));
        view.Items.Add(new SplitViewItem("Trash", Icons.Trash));
        view.FooterItems.Add(new SplitViewItem("Settings", Icons.Gear));

        // no factories here: one content for all items, changed on selection
        view.SelectionChanged += (_, _) => chosen.Text = view.SelectedItem?.Text ?? string.Empty;
        view.SelectedItem = view.Items[0];

        var mode = new ComboBox { Items = { "Overlay", "Inline", "CompactOverlay", "CompactInline" }, SelectedIndex = 3 };
        mode.SelectionChanged += (_, _) => view.DisplayMode = (SplitViewDisplayMode)mode.SelectedIndex;

        var end = new ToggleSwitch { Text = "Pane at the end" };
        end.Toggled += (_, _) => view.PanePlacement = end.IsOn ? SplitViewPanePlacement.End : SplitViewPanePlacement.Start;

        var root = Stretch();
        root.Children.AddRange([Demo.Row(Demo.Labeled("Display mode", mode), end), view]);
        return root;
    }

    private static Label Body(string text) => new()
    {
        Text = text,
        Padding = new(16),
        HorizontalContentAlign = HorizontalContentAlignment.Center,
        VerticalContentAlign = VerticalContentAlignment.Center,
    };

    private static Label Slide(string text, Color color) => new()
    {
        Text = text,
        TextStyle = TextStyle.Title,
        Background = color,
        TextColor = Colors.White,
        CornerRadius = new(8),
        HorizontalContentAlign = HorizontalContentAlignment.Center,
        VerticalContentAlign = VerticalContentAlignment.Center,
    };

    private static StackPanel Stretch() => new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 10,
        CrossAxisAlignment = CrossAxisAlignment.Stretch,
    };
}
