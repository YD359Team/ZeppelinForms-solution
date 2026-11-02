using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.21: the virtual views TalkBack reads on Android — the nodes, the actions a
/// double tap and a swipe ask for, the finger's exploration, the events. The Android
/// bridge only copies them into AccessibilityNodeInfo and AccessibilityEvent; every
/// decision is checked here.</summary>
[Collection("Platform")]
public sealed class VirtualViewTreeTests
{
    private readonly List<VirtualViewTree.Layer> _layers = [];

    private VirtualViewTree CreateTree() => new(() => _layers);

    private Form Show(UIElement content, string title = "Window", Point origin = default, Size? size = null)
    {
        var form = new Form { Size = size ?? new Size(400, 300), Content = content, Title = title };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        _layers.Add(new VirtualViewTree.Layer(form, origin, false));

        return form;
    }

    private static StackPanel Panel(params UIElement[] children)
    {
        var panel = new StackPanel();

        foreach (UIElement child in children)
            panel.Children.Add(child);

        return panel;
    }

    private static int Id(VirtualViewTree tree, UIElement element) => tree.IdOf(element.GetAccessibilityPeer()!);

    private static VirtualNode Node(VirtualViewTree tree, UIElement element) =>
        tree.Describe(Id(tree, element)) ?? throw new InvalidOperationException("Not in the tree.");

    /// <summary>The tree's events while an action runs: the platform's, as the bridge
    /// listens to them while a screen reader is on.</summary>
    private static IReadOnlyList<VirtualEvent> Listen(VirtualViewTree tree, Action action)
    {
        Action<AccessibilityPeer> focus = tree.OnFocusChanged;
        Action<AccessibilityPeer, AccessibilityProperty> property = tree.OnPropertyChanged;
        Action<AccessibilityPeer> structure = tree.OnStructureChanged;

        AccessibilityEvents.FocusChanged += focus;
        AccessibilityEvents.PropertyChanged += property;
        AccessibilityEvents.StructureChanged += structure;

        try
        {
            action();
        }
        finally
        {
            AccessibilityEvents.FocusChanged -= focus;
            AccessibilityEvents.PropertyChanged -= property;
            AccessibilityEvents.StructureChanged -= structure;
        }

        return tree.TakeEvents();
    }

    // ===== shape =====

    [Fact]
    public void TheWindowsAreTheHostsChildrenAndContainersAreLifted()
    {
        var label = new Label { Text = "Hello" };
        var button = new Button { Text = "OK" };
        Form form = Show(Panel(label, Panel(button)), "Login");
        VirtualViewTree tree = CreateTree();

        int window = Assert.Single(tree.HostChildren());
        VirtualNode windowNode = tree.Describe(window)!;

        Assert.Equal(VirtualViewTree.HostId, windowNode.Parent);
        Assert.Equal("Login", windowNode.PaneTitle);
        Assert.Null(windowNode.ContentDescription);

        // the two stack panels are no nodes: the label and the button stand in the window
        Assert.Equal([Id(tree, label), Id(tree, button)], windowNode.Children);
        Assert.Equal(window, Node(tree, button).Parent);
        Assert.Same(form.GetAccessibilityPeer(), tree.PeerOf(window));
    }

    [Fact]
    public void ANumberStaysWithItsPeer()
    {
        var button = new Button { Text = "OK" };
        Show(Panel(button));
        VirtualViewTree tree = CreateTree();

        int first = Id(tree, button);

        Assert.Equal(first, Id(tree, button));
        Assert.NotEqual(first, tree.HostChildren()[0]);
    }

    [Fact]
    public void AWindowUnderAModalDialogIsOutOfTheTree()
    {
        var below = new Button { Text = "Below" };
        Show(Panel(below));
        Form dialog = Show(Panel(new Button { Text = "OK" }), "Confirm", new Point(40, 30), new Size(200, 120));

        _layers[0] = _layers[0] with { IsHidden = true };

        VirtualViewTree tree = CreateTree();

        Assert.Equal([tree.IdOf(dialog.GetAccessibilityPeer())], tree.HostChildren());
        Assert.Null(tree.Describe(Id(tree, below)));
    }

    [Fact]
    public void BoundsAreOnTheSurfaceAndCutToTheWindow()
    {
        var button = new Button { Text = "OK", Width = 100, Height = 30 };
        Show(Panel(button), origin: new Point(10, 20));
        VirtualViewTree tree = CreateTree();

        VirtualNode node = Node(tree, button);
        Point position = button.GetAbsolutePosition();

        Assert.Equal(position.X + 10, node.Bounds.X);
        Assert.Equal(position.Y + 20, node.Bounds.Y);
        Assert.True(node.IsVisible);
    }

    [Fact]
    public void ANamelessPictureIsLeftOut()
    {
        var picture = new PictureBox();
        var button = new Button { Text = "OK" };
        Show(Panel(picture, button));
        VirtualViewTree tree = CreateTree();

        Assert.Equal([Id(tree, button)], tree.Describe(tree.HostChildren()[0])!.Children);
    }

    // ===== the mapping =====

    [Fact]
    public void AButtonIsReadByItsNameAndClicks()
    {
        var button = new Button { Text = "Save" };
        Show(Panel(button));

        VirtualNode node = Node(CreateTree(), button);

        Assert.Equal("android.widget.Button", node.ClassName);
        Assert.Equal("Save", node.ContentDescription);
        Assert.True(node.IsClickable);
        Assert.True(node.IsEnabled);
        Assert.True(node.Actions.HasFlag(VirtualNodeActions.Click));
    }

    [Fact]
    public void ADisabledButtonDoesNotClick()
    {
        var button = new Button { Text = "Save", IsEnabled = false };
        Show(Panel(button));

        VirtualNode node = Node(CreateTree(), button);

        Assert.False(node.IsEnabled);
        Assert.False(node.IsClickable);
    }

    [Fact]
    public void ALabelIsReadByItsText()
    {
        var label = new Label { Text = "Hello" };
        Show(Panel(label));

        VirtualNode node = Node(CreateTree(), label);

        Assert.Equal(VirtualViewTree.TextView, node.ClassName);
        Assert.Equal("Hello", node.Text);
        Assert.Null(node.ContentDescription);
    }

    [Fact]
    public void ACheckBoxIsCheckable()
    {
        var check = new CheckBox { Text = "Remember", IsChecked = true };
        Show(Panel(check));

        VirtualNode node = Node(CreateTree(), check);

        Assert.Equal("android.widget.CheckBox", node.ClassName);
        Assert.True(node.IsCheckable);
        Assert.True(node.IsChecked);
    }

    [Fact]
    public void AFieldHasItsTextAsTextAndItsLabelAsTheHint()
    {
        var box = new TextBox { AccessibleName = "Name", Text = "Ada" };
        Show(Panel(box));

        VirtualNode node = Node(CreateTree(), box);

        Assert.Equal(VirtualViewTree.EditText, node.ClassName);
        Assert.Equal("Ada", node.Text);
        Assert.Equal("Name", node.Hint);
        Assert.True(node.IsEditable);
        Assert.True(node.Actions.HasFlag(VirtualNodeActions.SetText));
    }

    [Fact]
    public void APasswordFieldSaysSo()
    {
        var box = new TextBox { Text = "secret", PasswordChar = '*' };
        Show(Panel(box));

        Assert.True(Node(CreateTree(), box).IsPassword);
    }

    [Fact]
    public void ASliderHasItsRangeAndIsAdjustedBySwipes()
    {
        var slider = new TrackBar { Minimum = 0, Maximum = 10, Value = 4, Step = 2 };
        Show(Panel(slider));
        VirtualViewTree tree = CreateTree();

        VirtualNode node = Node(tree, slider);

        Assert.Equal("android.widget.SeekBar", node.ClassName);
        Assert.Equal(new RangeInfo(0, 10, 4, 2), node.Range);
        Assert.True(node.Actions.HasFlag(VirtualNodeActions.ScrollForward));
        Assert.True(node.Actions.HasFlag(VirtualNodeActions.ScrollBackward));

        Assert.True(tree.Perform(node.Id, VirtualAction.ScrollForward));
        Assert.Equal(6, slider.Value);

        Assert.True(tree.Perform(node.Id, VirtualAction.SetProgress, 99));
        Assert.Equal(10, slider.Value);

        // at the end of its range a slider goes no further
        Assert.False(Node(tree, slider).Actions.HasFlag(VirtualNodeActions.ScrollForward));
    }

    [Fact]
    public void AListIsACollectionOfItsItems()
    {
        var list = new ListBox();
        list.Items.Add("One");
        list.Items.Add("Two");
        Show(list);
        VirtualViewTree tree = CreateTree();

        VirtualNode node = Node(tree, list);

        Assert.Equal("android.widget.ListView", node.ClassName);
        Assert.Equal((2, 1, false), node.Collection);

        VirtualNode second = tree.Describe(node.Children[1])!;

        Assert.Equal(1, second.ItemIndex);
        Assert.Equal(node.Id, second.Parent);
    }

    // ===== actions =====

    [Fact]
    public void ADoubleTapClicksAndIsToldAsAClick()
    {
        int clicks = 0;
        var button = new Button { Text = "Save" };
        button.Click += (_, _) => clicks++;
        Show(Panel(button));
        VirtualViewTree tree = CreateTree();

        int id = Id(tree, button);

        Assert.True(tree.Perform(id, VirtualAction.Click));
        Assert.Equal(1, clicks);
        Assert.Contains(new VirtualEvent(VirtualEventKind.Clicked, id), tree.TakeEvents());
    }

    [Fact]
    public void ADoubleTapTogglesACheckBox()
    {
        var check = new CheckBox { Text = "Remember" };
        Show(Panel(check));
        VirtualViewTree tree = CreateTree();

        Assert.True(tree.Perform(Id(tree, check), VirtualAction.Click));
        Assert.True(check.IsChecked);
        Assert.True(Node(tree, check).IsChecked);
    }

    [Fact]
    public void SetTextWritesTheField()
    {
        var box = new TextBox();
        Show(Panel(box));
        VirtualViewTree tree = CreateTree();

        Assert.True(tree.Perform(Id(tree, box), VirtualAction.SetText, text: "Ada"));
        Assert.Equal("Ada", box.Text);
    }

    [Fact]
    public void TheReadersFocusMovesFromNodeToNode()
    {
        var first = new Button { Text = "One" };
        var second = new Button { Text = "Two" };
        Show(Panel(first, second));
        VirtualViewTree tree = CreateTree();

        int a = Id(tree, first);
        int b = Id(tree, second);

        Assert.True(tree.Perform(a, VirtualAction.AccessibilityFocus));
        Assert.True(Node(tree, first).IsAccessibilityFocused);

        // a node already in focus refuses it again: TalkBack moves on
        Assert.False(tree.Perform(a, VirtualAction.AccessibilityFocus));

        tree.TakeEvents();
        Assert.True(tree.Perform(b, VirtualAction.AccessibilityFocus));

        Assert.Equal(
            [new VirtualEvent(VirtualEventKind.AccessibilityFocusCleared, a), new VirtualEvent(VirtualEventKind.AccessibilityFocused, b)],
            tree.TakeEvents());
        Assert.False(Node(tree, first).IsAccessibilityFocused);

        Assert.True(tree.Perform(b, VirtualAction.ClearAccessibilityFocus));
        Assert.Equal(VirtualViewTree.NoId, tree.AccessibilityFocus);
    }

    [Fact]
    public void AGoneNodeRefusesEveryAction()
    {
        var button = new Button { Text = "OK" };
        StackPanel panel = Panel(button);
        Show(panel);
        VirtualViewTree tree = CreateTree();

        int id = Id(tree, button);
        _layers.Clear();

        Assert.Null(tree.Describe(id));
        Assert.False(tree.Perform(id, VirtualAction.Click));
    }

    // ===== exploring by touch =====

    [Fact]
    public void TheFingerFindsTheDeepestNodeUnderIt()
    {
        var button = new Button { Text = "OK", Width = 100, Height = 30 };
        Form form = Show(Panel(new Label { Text = "Title" }, button), origin: new Point(10, 20));
        VirtualViewTree tree = CreateTree();

        Point position = button.GetAbsolutePosition();

        Assert.Equal(Id(tree, button), tree.At(new Point(position.X + 10 + 5, position.Y + 20 + 5)));

        // inside the window, on no control: the window itself
        Assert.Equal(tree.IdOf(form.GetAccessibilityPeer()), tree.At(new Point(10 + 390, 20 + 290)));

        // outside every window
        Assert.Equal(VirtualViewTree.HostId, tree.At(new Point(5, 5)));
    }

    [Fact]
    public void TheFingerFindsTheDialogOverTheWindowBelow()
    {
        Show(Panel(new Button { Text = "Below", Width = 400, Height = 300 }));
        var ok = new Button { Text = "OK", Width = 200, Height = 120 };
        Show(Panel(ok), "Confirm", new Point(40, 30), new Size(200, 120));
        _layers[0] = _layers[0] with { IsHidden = true };

        VirtualViewTree tree = CreateTree();

        Assert.Equal(Id(tree, ok), tree.At(new Point(60, 50)));
    }

    // ===== events =====

    [Fact]
    public void TypingIsToldAsATextChangeAgainstTheTextRead()
    {
        var box = new TextBox { Text = "Ad" };
        Form form = Show(Panel(box));
        VirtualViewTree tree = CreateTree();
        int id = Id(tree, box);

        tree.Describe(id);
        box.Focus();

        IReadOnlyList<VirtualEvent> events = Listen(tree, () =>
        {
            box.Select(2, 0);
            HeadlessInput.TypeText(form, "a");
        });

        VirtualEvent change = Assert.Single(events, e => e.Kind == VirtualEventKind.TextChanged);

        Assert.Equal(id, change.Id);
        Assert.Equal("Ad", change.BeforeText);
        Assert.Equal("Ada", change.Text);
        Assert.Equal((2, 0, 1), (change.From, change.Removed, change.Added));
    }

    [Theory]
    [InlineData("abc", "abXc", 2, 0, 1)]
    [InlineData("abc", "ac", 1, 1, 0)]
    [InlineData("abc", "xyz", 0, 3, 3)]
    [InlineData("", "a", 0, 0, 1)]
    [InlineData("aaa", "aaaa", 3, 0, 1)]
    public void ATextChangeIsOneEdit(string before, string after, int from, int removed, int added) =>
        Assert.Equal((from, removed, added), VirtualViewTree.TextChange(before, after));

    [Fact]
    public void TheKeyboardsFocusIsTold()
    {
        var button = new Button { Text = "OK" };
        Form form = Show(Panel(button));
        VirtualViewTree tree = CreateTree();

        IReadOnlyList<VirtualEvent> events = Listen(tree, () => HeadlessInput.PressKey(form, Key.Tab));

        Assert.Contains(new VirtualEvent(VirtualEventKind.Focused, Id(tree, button)), events);
    }

    [Fact]
    public void ChangesOfOneNodeAreToldOnce()
    {
        var check = new CheckBox { Text = "Remember" };
        Show(Panel(check));
        VirtualViewTree tree = CreateTree();

        // read first, as TalkBack reads before anything changes: a peer is made when asked for
        Node(tree, check);

        IReadOnlyList<VirtualEvent> events = Listen(tree, () =>
        {
            check.IsChecked = true;
            check.AccessibleName = "Remember me";
        });

        VirtualEvent change = Assert.Single(events, e => e.Kind == VirtualEventKind.ContentChanged);

        Assert.Equal(Id(tree, check), change.Id);
        Assert.True(change.Changes.HasFlag(VirtualChanges.StateDescription));
        Assert.True(change.Changes.HasFlag(VirtualChanges.ContentDescription));
    }

    [Fact]
    public void AChangeInsideAContainerIsToldOnTheNodeAbove()
    {
        var inner = new StackPanel();
        Form form = Show(Panel(inner));
        VirtualViewTree tree = CreateTree();

        tree.Describe(tree.HostChildren()[0]);

        IReadOnlyList<VirtualEvent> events = Listen(tree, () => inner.Children.Add(new Button { Text = "New" }));

        VirtualEvent change = Assert.Single(events);

        Assert.Equal(tree.IdOf(form.GetAccessibilityPeer()), change.Id);
        Assert.Equal(VirtualChanges.Subtree, change.Changes);
    }

    [Fact]
    public void ADialogComingIsToldAsAPaneByItsTitle()
    {
        Show(Panel(new Button { Text = "Below" }));
        VirtualViewTree tree = CreateTree();

        tree.Review();
        tree.TakeEvents();

        Form dialog = Show(Panel(new Button { Text = "OK" }), "Delete the file?", new Point(40, 30), new Size(200, 120));
        _layers[0] = _layers[0] with { IsHidden = true };

        tree.Review();
        IReadOnlyList<VirtualEvent> events = tree.TakeEvents();

        VirtualEvent pane = Assert.Single(events, e => e.Kind == VirtualEventKind.WindowStateChanged);

        Assert.Equal(tree.IdOf(dialog.GetAccessibilityPeer()), pane.Id);
        Assert.Equal("Delete the file?", pane.Text);
        Assert.Contains(events, e => e is { Kind: VirtualEventKind.ContentChanged, Id: VirtualViewTree.HostId });

        // nothing more came: nothing more is told
        tree.Review();
        Assert.Empty(tree.TakeEvents());
    }

    [Fact]
    public void ANodeReadThatMovedIsToldOnce()
    {
        var button = new Button { Text = "OK", Width = 100, Height = 30 };
        Show(Panel(button));
        VirtualViewTree tree = CreateTree();

        tree.Review();
        tree.Describe(Id(tree, button));
        tree.TakeEvents();

        button.Margin = new Thickness(0, 50, 0, 0);
        _layers[0].Form.UpdateLayout();

        tree.Review();
        Assert.Equal([new VirtualEvent(VirtualEventKind.ContentChanged, VirtualViewTree.HostId) { Changes = VirtualChanges.Subtree }], tree.TakeEvents());

        tree.Review();
        Assert.Empty(tree.TakeEvents());
    }

    [Fact]
    public void AnAnnouncementIsReadOut()
    {
        VirtualViewTree tree = CreateTree();

        tree.OnAnnouncement("Saved");

        VirtualEvent e = Assert.Single(tree.TakeEvents());
        Assert.Equal((VirtualEventKind.Announcement, "Saved"), (e.Kind, e.Text));
    }
}
