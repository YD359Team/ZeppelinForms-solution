using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Shapes;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tree;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The semantic model of 0.13.0: what each control is to a screen reader — role,
/// name, states, value, children, actions — read through its peer, and the events
/// a bridge listens to. No platform is involved: this is what the bridges translate.
/// </summary>
[Collection("Platform")]
public class AccessibilityTreeTests
{
    private static Form CreateForm(UIElement content, string? title = null)
    {
        var form = new Form { Size = new Size(400, 300), Content = content, Title = title };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    private static AccessibilityPeer PeerOf(UIElement element) => element.GetAccessibilityPeer()!;

    // ===== the model itself =====

    [Fact]
    public void PeerIsCreatedOnlyWhenAskedFor()
    {
        var button = new Button { Text = "OK" };
        CreateForm(button);

        Assert.Null(button.ExistingAccessibilityPeer);

        AccessibilityPeer peer = PeerOf(button);

        Assert.Same(peer, button.ExistingAccessibilityPeer);
        Assert.Same(peer, PeerOf(button));
    }

    [Fact]
    public void FormIsTheRoot()
    {
        var button = new Button { Text = "OK" };
        Form form = CreateForm(button, "Settings");

        AccessibilityPeer root = form.GetAccessibilityPeer();

        Assert.Equal(AccessibilityRole.Window, root.Role);
        Assert.Equal("Settings", root.Name);
        Assert.Null(root.Parent);
        Assert.Same(root, PeerOf(button).Parent);
    }

    [Fact]
    public void TreeDumpLiftsLayoutContainers()
    {
        var panel = new StackPanel();
        panel.Children.Add(new Label { Text = "Account", HeadingLevel = 2 });
        panel.Children.Add(new CheckBox { Text = "Remember me", IsChecked = true });
        panel.Children.Add(new Button { Text = "Sign in" });

        Form form = CreateForm(panel, "Login");

        string dump = AccessibilityTree.Dump(form.GetAccessibilityPeer());

        Assert.Equal(
            "Window \"Login\"\n" +
            "  Heading \"Account\"\n" +
            "  CheckBox \"Remember me\" {focusable, checked}\n" +
            "  Button \"Sign in\" {focusable}\n",
            dump);
    }

    [Fact]
    public void DecorationAndHiddenElementsAreLeftOut()
    {
        var panel = new StackPanel();
        panel.Children.Add(new RectangleShape());
        panel.Children.Add(new Label { Text = "hidden", IsAccessibilityHidden = true });
        panel.Children.Add(new Label { Text = "invisible", IsVisible = false });
        panel.Children.Add(new Label { Text = "shown" });

        Form form = CreateForm(panel);

        IReadOnlyList<AccessibilityPeer> children = PeerOf(panel).Children;

        Assert.Equal("shown", Assert.Single(children).Name);
    }

    // ===== names =====

    [Fact]
    public void NameComesByPrecedence()
    {
        var caption = new Button { Text = "Save" };
        var iconOnly = new Button { ToolTip = "Delete" };
        var named = new Button { Text = "X", AccessibleName = "Close", ToolTip = "Closes the window" };

        var panel = new StackPanel();
        panel.Children.Add(caption);
        panel.Children.Add(iconOnly);
        panel.Children.Add(named);
        CreateForm(panel);

        Assert.Equal("Save", PeerOf(caption).Name);

        // the tooltip names what has no text; said once, it is not repeated
        Assert.Equal("Delete", PeerOf(iconOnly).Name);
        Assert.Null(PeerOf(iconOnly).Description);

        Assert.Equal("Close", PeerOf(named).Name);
        Assert.Equal("Closes the window", PeerOf(named).Description);
    }

    [Fact]
    public void LabelNamesItsTarget()
    {
        var box = new TextBox { Text = "Ada" };
        var label = new Label { Text = "First name", Target = box };

        var panel = new StackPanel();
        panel.Children.Add(label);
        panel.Children.Add(box);
        CreateForm(panel);

        AccessibilityPeer peer = PeerOf(box);

        Assert.Same(label, box.LabeledBy);
        Assert.Equal(AccessibilityRole.TextBox, peer.Role);
        Assert.Equal("First name", peer.Name);

        // the text is the value, not the name
        Assert.Equal("Ada", peer.Value);
    }

    [Fact]
    public void PasswordIsNeverGivenOut()
    {
        var box = new TextBox { Text = "secret", PasswordChar = '*' };
        CreateForm(box);

        AccessibilityPeer peer = PeerOf(box);

        Assert.Null(peer.Value);
        Assert.True(peer.States.HasFlag(AccessibilityStates.Protected));
    }

    [Fact]
    public void ValidationErrorIsInvalidWithItsMessage()
    {
        var box = new TextBox { Validator = _ => "Required" };
        CreateForm(box);
        box.Validate();

        AccessibilityPeer peer = PeerOf(box);

        Assert.True(peer.States.HasFlag(AccessibilityStates.Invalid));
        Assert.Equal("Required", peer.Description);
    }

    // ===== states and actions =====

    [Fact]
    public void CheckBoxReportsItsThirdState()
    {
        var box = new CheckBox { Text = "All", IsThreeState = true };
        CreateForm(box);

        AccessibilityPeer peer = PeerOf(box);

        Assert.True(peer.Toggle());
        Assert.True(peer.States.HasFlag(AccessibilityStates.Checked));

        Assert.True(peer.Toggle());
        Assert.True(peer.States.HasFlag(AccessibilityStates.Mixed));
        Assert.Equal(CheckedState.Intermediate, box.CheckedState);
    }

    [Fact]
    public void DisabledControlRefusesActions()
    {
        var box = new CheckBox { Text = "x", IsEnabled = false };
        CreateForm(box);

        AccessibilityPeer peer = PeerOf(box);

        Assert.True(peer.States.HasFlag(AccessibilityStates.Disabled));
        Assert.False(peer.Toggle());
        Assert.False(box.IsChecked);
    }

    [Fact]
    public void RadioButtonsKnowTheirGroup()
    {
        var a = new RadioButton { Text = "A" };
        var b = new RadioButton { Text = "B" };
        var c = new RadioButton { Text = "C" };

        var panel = new StackPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        panel.Children.Add(c);
        CreateForm(panel);

        Assert.Equal(new SetPosition(2, 3), PeerOf(b).Position);

        Assert.True(PeerOf(b).Select());
        Assert.True(b.IsChecked);
        Assert.True(PeerOf(b).States.HasFlag(AccessibilityStates.Checked));
    }

    [Fact]
    public void SliderAndProgressGiveRanges()
    {
        var slider = new TrackBar { Minimum = 0, Maximum = 10, Value = 4 };
        var progress = new ProgressBar { Minimum = 0, Maximum = 200, Value = 50 };

        var panel = new StackPanel();
        panel.Children.Add(slider);
        panel.Children.Add(progress);
        CreateForm(panel);

        Assert.Equal(4, PeerOf(slider).Range!.Value.Value);
        Assert.True(PeerOf(slider).SetRangeValue(7));
        Assert.Equal(7f, slider.Value);

        Assert.Equal(AccessibilityRole.ProgressBar, PeerOf(progress).Role);
        Assert.Equal(50, PeerOf(progress).Range!.Value.Value);
    }

    [Fact]
    public void ComboBoxExpandsThroughItsOwnClick()
    {
        var combo = new ComboBox { Items = ["Red", "Green"] };
        combo.SelectedIndex = 1;
        Form form = CreateForm(combo);

        AccessibilityPeer peer = PeerOf(combo);

        Assert.Equal("Green", peer.Value);
        Assert.True(peer.States.HasFlag(AccessibilityStates.Collapsed));

        Assert.True(peer.Expand());

        Assert.True(peer.States.HasFlag(AccessibilityStates.Expanded));

        // the drop-down is an overlay of the form: there it is in the tree
        Assert.True(form.GetAccessibilityPeer().Children.Count > 1);

        Assert.True(peer.Collapse());
        Assert.True(peer.States.HasFlag(AccessibilityStates.Collapsed));
    }

    // ===== collections =====

    [Fact]
    public void ListRowsAreListItems()
    {
        var list = new ListBox();
        list.Items.Add("One");
        list.Items.Add("Two");
        list.Items.Add("Three");
        CreateForm(list);

        AccessibilityPeer peer = PeerOf(list);
        IReadOnlyList<AccessibilityPeer> items = peer.Children;

        Assert.Equal(AccessibilityRole.List, peer.Role);
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal(AccessibilityRole.ListItem, item.Role));
        Assert.Equal("Two", items[1].Name);
        Assert.Equal(new SetPosition(2, 3), items[1].Position);

        Assert.True(items[2].Select());

        Assert.Equal(2, list.SelectedIndex);
        Assert.True(items[2].States.HasFlag(AccessibilityStates.Selected));
    }

    [Fact]
    public void ElementMovedIntoListBecomesListItem()
    {
        var label = new Label { Text = "Row" };
        var panel = new StackPanel();
        panel.Children.Add(label);
        CreateForm(panel);

        Assert.Equal(AccessibilityRole.Text, PeerOf(label).Role);

        var list = new ListBox { ItemTemplate = _ => label };
        panel.Children.Remove(label);
        panel.Children.Add(list);
        list.Items.Add("anything");

        Assert.Equal(AccessibilityRole.ListItem, PeerOf(label).Role);
    }

    [Fact]
    public void TreeComesFromTheModel()
    {
        var root = new TreeNode("Documents");
        root.Children.Add(new TreeNode("Letters"));
        root.Children.Add(new TreeNode("Reports"));

        var tree = new TreeView();
        tree.Nodes.Add(root);
        tree.Nodes.Add(new TreeNode("Pictures"));
        CreateForm(tree);

        AccessibilityPeer documents = PeerOf(tree).Children[0];

        Assert.Equal(AccessibilityRole.TreeItem, documents.Role);
        Assert.True(documents.States.HasFlag(AccessibilityStates.Collapsed));
        Assert.Empty(documents.Children);

        Assert.True(documents.Expand());

        Assert.Equal(2, documents.Children.Count);
        Assert.Equal("Reports", documents.Children[1].Name);
        Assert.Equal(2, documents.Children[1].Level);
        Assert.Same(documents, documents.Children[1].Parent);

        // the same node, the same peer: bridges know peers by reference
        Assert.Same(documents, PeerOf(tree).Children[0]);
    }

    [Fact]
    public void TabsAreSelectable()
    {
        var tabs = new TabControl
        {
            Tabs =
            [
                new TabItem { Header = "General", Content = new Label { Text = "general page" } },
                new TabItem { Header = "Advanced", Content = new Label { Text = "advanced page" } },
            ],
        };
        CreateForm(tabs);

        IReadOnlyList<AccessibilityPeer> children = PeerOf(tabs).Children;

        Assert.Equal(AccessibilityRole.Tab, children[1].Role);
        Assert.Equal("Advanced", children[1].Name);

        Assert.True(children[1].Select());

        Assert.Equal(1, tabs.SelectedIndex);
        Assert.True(children[1].States.HasFlag(AccessibilityStates.Selected));
    }

    [Fact]
    public void MenuItemsRunTheirActions()
    {
        int clicks = 0;
        var save = new MenuItem { Text = "Save" };
        save.Click += (_, _) => clicks++;

        var menu = new MenuList { Items = [save, MenuItem.Separator, new MenuItem { Text = "Quit", IsEnabled = false }] };
        CreateForm(menu);

        IReadOnlyList<AccessibilityPeer> items = PeerOf(menu).Children;

        Assert.Equal(AccessibilityRole.Separator, items[1].Role);

        // separators are not counted: Quit is the second of two
        Assert.Equal(new SetPosition(2, 2), items[2].Position);
        Assert.True(items[2].States.HasFlag(AccessibilityStates.Disabled));

        Assert.True(items[0].Invoke());
        Assert.False(items[2].Invoke());
        Assert.Equal(1, clicks);
    }

    private sealed record Person(string Name, int Age);

    [Fact]
    public void GridRowsAndCellsReadTheData()
    {
        var grid = new DataGridView
        {
            Columns =
            [
                new DataGridViewColumn { Header = "Name", Value = o => ((Person)o).Name },
                new DataGridViewColumn { Header = "Age", Value = o => ((Person)o).Age },
            ],
        };
        grid.Items.Add(new Person("Ada", 36));
        grid.Items.Add(new Person("Alan", 41));
        CreateForm(grid);

        IReadOnlyList<AccessibilityPeer> rows = PeerOf(grid).Children;

        Assert.Equal(3, rows.Count);
        Assert.Equal(AccessibilityRole.ColumnHeader, rows[0].Children[1].Role);
        Assert.Equal("Age", rows[0].Children[1].Name);

        Assert.Equal("Alan, 41", rows[2].Name);
        Assert.Equal("41", rows[2].Children[1].Value);

        Assert.True(rows[2].Select());
        Assert.Equal(1, grid.SelectedIndex);

        // sorting by Age descending through the header puts Alan first
        Assert.True(rows[0].Children[1].Invoke());
        Assert.True(PeerOf(grid).Children[0].Children[1].Invoke());
        Assert.Equal("Alan, 41", PeerOf(grid).Children[1].Name);
    }

    [Fact]
    public void CollapsedSpoilerHidesItsContent()
    {
        var spoiler = new Spoiler(new Label { Text = "details" }) { Header = "More" };
        CreateForm(spoiler);

        AccessibilityPeer peer = PeerOf(spoiler);

        Assert.Equal("More", peer.Name);
        Assert.Single(peer.Children);

        Assert.True(peer.Collapse());

        Assert.Empty(peer.Children);
        Assert.True(peer.States.HasFlag(AccessibilityStates.Collapsed));
    }

    // ===== events =====

    [Fact]
    public void FocusIsReported()
    {
        var button = new Button { Text = "OK" };
        Form form = CreateForm(button);

        var focused = new List<AccessibilityPeer>();
        Action<AccessibilityPeer> listen = focused.Add;
        AccessibilityEvents.FocusChanged += listen;

        try
        {
            HeadlessInput.PressKey(form, Key.Tab);
        }
        finally
        {
            AccessibilityEvents.FocusChanged -= listen;
        }

        Assert.Same(PeerOf(button), Assert.Single(focused));
    }

    [Fact]
    public void FocusInsideListIsTheCurrentRow()
    {
        var list = new ListBox();
        list.Items.Add("One");
        list.Items.Add("Two");
        Form form = CreateForm(list);
        list.SelectedIndex = 1;

        var focused = new List<AccessibilityPeer>();
        Action<AccessibilityPeer> listen = focused.Add;
        AccessibilityEvents.FocusChanged += listen;

        try
        {
            HeadlessInput.PressKey(form, Key.Tab);
        }
        finally
        {
            AccessibilityEvents.FocusChanged -= listen;
        }

        Assert.Equal("Two", focused[^1].Name);
        Assert.Equal(AccessibilityRole.ListItem, focused[^1].Role);
    }

    [Fact]
    public void AnnouncementGoesToListeners()
    {
        Form form = CreateForm(new Label { Text = "x" });

        var heard = new List<string>();
        Action<Form, string, AccessibilityLiveSetting> listen = (_, text, _) => heard.Add(text);
        AccessibilityEvents.Announcement += listen;

        try
        {
            form.Announce("Saved");
            form.Announce("   ");
            form.Announce("muted", AccessibilityLiveSetting.Off);
        }
        finally
        {
            AccessibilityEvents.Announcement -= listen;
        }

        Assert.Equal(["Saved"], heard);
    }

    [Fact]
    public void ChangeIsReportedOnlyWherePeerExists()
    {
        var box = new CheckBox { Text = "x" };
        CreateForm(box);

        var changes = new List<AccessibilityProperty>();
        Action<AccessibilityPeer, AccessibilityProperty> listen = (_, property) => changes.Add(property);
        AccessibilityEvents.PropertyChanged += listen;

        try
        {
            // nobody asked for the peer yet: nothing exists to report
            box.IsChecked = true;
            Assert.Empty(changes);

            _ = PeerOf(box);
            box.IsChecked = false;
        }
        finally
        {
            AccessibilityEvents.PropertyChanged -= listen;
        }

        Assert.Equal([AccessibilityProperty.States], changes);
    }
}