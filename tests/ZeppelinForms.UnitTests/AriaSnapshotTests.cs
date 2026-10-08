using System.Text.Json;
using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The ARIA snapshot the browser mirror applies to its DOM: roles, names, text
/// content, aria-* attributes, bounds, the focus, and dialogs over the windows below.
/// The mirror itself only applies it; every decision is checked here.
/// </summary>
[Collection("Platform")]
public class AriaSnapshotTests
{
    private sealed class Ids
    {
        private readonly Dictionary<AccessibilityPeer, string> _ids = new(ReferenceEqualityComparer.Instance);

        public string Of(AccessibilityPeer peer) =>
            _ids.TryGetValue(peer, out string? id) ? id : _ids[peer] = "n" + _ids.Count;
    }

    private static Form CreateForm(UIElement content, string title = "Window")
    {
        var form = new Form { Size = new Size(400, 300), Content = content, Title = title };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    private static StackPanel Panel(params UIElement[] children)
    {
        var panel = new StackPanel();

        foreach (UIElement child in children)
            panel.Children.Add(child);

        return panel;
    }

    private static JsonElement Snapshot(Ids ids, params AriaSnapshot.Layer[] layers)
    {
        string json = AriaSnapshot.Build(layers, ids.Of, []);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement Snapshot(Form form, Ids? ids = null) =>
        Snapshot(ids ?? new Ids(), new AriaSnapshot.Layer(form, Point.Empty, false));

    /// <summary>The node of an element: by the peer's id, anywhere in the tree.</summary>
    private static JsonElement Node(JsonElement snapshot, Ids ids, UIElement element) =>
        Find(snapshot.GetProperty("windows"), ids.Of(element.GetAccessibilityPeer()!))
        ?? throw new InvalidOperationException("Not in the snapshot.");

    private static JsonElement? Find(JsonElement nodes, string id)
    {
        foreach (JsonElement node in nodes.EnumerateArray())
        {
            if (node.GetProperty("id").GetString() == id) return node;
            if (Find(node.GetProperty("children"), id) is { } inside) return inside;
        }

        return null;
    }

    private static string? Attr(JsonElement node, string name) =>
        node.GetProperty("attrs").TryGetProperty(name, out JsonElement value) ? value.GetString() : null;

    private static string? Str(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;

    // ===== shape =====

    [Fact]
    public void WindowAndFocus()
    {
        var button = new Button { Text = "OK" };
        Form form = CreateForm(Panel(new Label { Text = "x" }, button), "Login");
        var ids = new Ids();

        JsonElement before = Snapshot(form, ids);

        Assert.Equal("Login", before.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("focus").ValueKind);

        HeadlessInput.PressKey(form, Key.Tab);
        JsonElement after = Snapshot(form, ids);

        Assert.Equal(ids.Of(button.GetAccessibilityPeer()!), after.GetProperty("focus").GetString());
    }

    [Fact]
    public void DialogHidesTheWindowsBelow()
    {
        Form main = CreateForm(new Label { Text = "main" });
        Form dialog = CreateForm(new Button { Text = "OK" }, "Confirm");

        JsonElement snapshot = Snapshot(new Ids(),
            new AriaSnapshot.Layer(main, Point.Empty, false),
            new AriaSnapshot.Layer(dialog, new Point(40, 30), true));

        JsonElement[] windows = [.. snapshot.GetProperty("windows").EnumerateArray()];

        Assert.True(windows[0].GetProperty("hidden").GetBoolean());
        Assert.True(windows[1].GetProperty("dialog").GetBoolean());
        Assert.False(windows[1].GetProperty("hidden").GetBoolean());
        Assert.Equal(40, windows[1].GetProperty("x").GetDouble());
    }

    [Fact]
    public void BoundsAreRelativeToTheParentNode()
    {
        var button = new Button { Text = "OK", Margin = new Thickness(10) };
        var group = new GroupBox { Header = "Box", Child = Panel(button) };
        var ids = new Ids();
        Form form = CreateForm(Panel(new Label { Text = "above" }, group));

        JsonElement groupNode = Node(Snapshot(form, ids), ids, group);
        JsonElement buttonNode = Node(Snapshot(form, ids), ids, button);

        Point groupAt = group.GetAbsolutePosition();
        Point buttonAt = button.GetAbsolutePosition();

        Assert.Equal(groupAt.Y, groupNode.GetProperty("y").GetDouble(), 1);
        Assert.True(buttonNode.GetProperty("y").GetDouble() < buttonAt.Y);
    }

    // ===== roles, names, states =====

    [Fact]
    public void TextIsContentAndControlsAreLabelled()
    {
        var heading = new Label { Text = "Account", HeadingLevel = 2 };
        var plain = new Label { Text = "Some words" };
        var check = new CheckBox { Text = "Remember", IsThreeState = true, CheckedState = CheckedState.Intermediate };
        var ids = new Ids();
        JsonElement snapshot = Snapshot(CreateForm(Panel(heading, plain, check)), ids);

        JsonElement h = Node(snapshot, ids, heading);
        Assert.Equal("heading", Str(h, "role"));
        Assert.Equal("Account", Str(h, "text"));
        Assert.Equal("2", Attr(h, "aria-level"));

        JsonElement p = Node(snapshot, ids, plain);
        Assert.Null(Str(p, "role"));
        Assert.Equal("Some words", Str(p, "text"));
        Assert.Null(Str(p, "label"));

        JsonElement c = Node(snapshot, ids, check);
        Assert.Equal("checkbox", Str(c, "role"));
        Assert.Equal("Remember", Str(c, "label"));
        Assert.Equal("mixed", Attr(c, "aria-checked"));
    }

    [Fact]
    public void FieldHasItsLabelAsNameAndItsTextAsContent()
    {
        var box = new TextBox { Text = "Ada", Validator = _ => "Required later" };
        var label = new Label { Text = "First name", Target = box };
        var ids = new Ids();
        Form form = CreateForm(Panel(label, box));
        box.Validate();

        JsonElement node = Node(Snapshot(form, ids), ids, box);

        Assert.Equal("textbox", Str(node, "role"));
        Assert.Equal("First name", Str(node, "label"));
        Assert.Equal("Ada", Str(node, "text"));
        Assert.Equal("true", Attr(node, "aria-invalid"));
        Assert.Equal("Required later", Attr(node, "aria-description"));
    }

    [Fact]
    public void ListBoxIsListboxOfOptions()
    {
        var list = new ListBox();
        list.Items.Add("One");
        list.Items.Add("Two");
        var ids = new Ids();
        Form form = CreateForm(list);
        list.SelectedIndex = 1;

        JsonElement listNode = Node(Snapshot(form, ids), ids, list);
        JsonElement second = listNode.GetProperty("children")[1];

        Assert.Equal("listbox", Str(listNode, "role"));
        Assert.Equal("option", Str(second, "role"));
        Assert.Equal("true", Attr(second, "aria-selected"));
        Assert.Equal("2", Attr(second, "aria-posinset"));
        Assert.Equal("2", Attr(second, "aria-setsize"));
    }

    [Fact]
    public void RangesCarryTheirValues()
    {
        var slider = new TrackBar { Minimum = 0, Maximum = 10, Value = 4 };
        var toggle = new ToggleSwitch { Text = "Wi-Fi", IsOn = true };
        var ids = new Ids();
        JsonElement snapshot = Snapshot(CreateForm(Panel(slider, toggle)), ids);

        JsonElement s = Node(snapshot, ids, slider);
        Assert.Equal("slider", Str(s, "role"));
        Assert.Equal("0", Attr(s, "aria-valuemin"));
        Assert.Equal("10", Attr(s, "aria-valuemax"));
        Assert.Equal("4", Attr(s, "aria-valuenow"));

        JsonElement t = Node(snapshot, ids, toggle);
        Assert.Equal("switch", Str(t, "role"));
        Assert.Equal("true", Attr(t, "aria-checked"));
    }

    private sealed record Person(string Name);

    [Fact]
    public void GridHasRowsCellsAndSortedHeaders()
    {
        var grid = new DataGridView
        {
            Columns = [new DataGridViewColumn { Header = "Name", Value = o => ((Person)o).Name }],
        };
        grid.Items.Add(new Person("Ada"));
        var ids = new Ids();
        Form form = CreateForm(grid);
        grid.SortBy(0);

        JsonElement g = Node(Snapshot(form, ids), ids, grid);
        JsonElement header = g.GetProperty("children")[0].GetProperty("children")[0];
        JsonElement row = g.GetProperty("children")[1];

        Assert.Equal("grid", Str(g, "role"));
        Assert.Equal("columnheader", Str(header, "role"));
        Assert.Equal("ascending", Attr(header, "aria-sort"));
        Assert.Equal("row", Str(row, "role"));
        Assert.Equal("gridcell", Str(row.GetProperty("children")[0], "role"));
    }

    [Fact]
    public void NamelessPictureIsPresentation()
    {
        var picture = new PictureBox { Size = new Size(20, 20) };
        var named = new PictureBox { Size = new Size(20, 20), AccessibleName = "Logo" };
        var ids = new Ids();
        JsonElement snapshot = Snapshot(CreateForm(Panel(picture, named)), ids);

        Assert.Equal("presentation", Str(Node(snapshot, ids, picture), "role"));
        Assert.Equal("img", Str(Node(snapshot, ids, named), "role"));
    }

    [Fact]
    public void AccessKeyAndLiveSetting()
    {
        var save = new Button { Text = "&Save", UseMnemonic = true };
        var status = new Label { Text = "Ready", LiveSetting = AccessibilityLiveSetting.Polite };
        var ids = new Ids();
        JsonElement snapshot = Snapshot(CreateForm(Panel(save, status)), ids);

        Assert.Equal("Alt+S", Attr(Node(snapshot, ids, save), "aria-keyshortcuts"));
        Assert.Equal("Save", Str(Node(snapshot, ids, save), "label"));
        Assert.Equal("polite", Attr(Node(snapshot, ids, status), "aria-live"));
    }

    [Fact]
    public void EveryWrittenPeerCanBeFoundById()
    {
        var button = new Button { Text = "OK" };
        var ids = new Ids();
        var peers = new Dictionary<string, AccessibilityPeer>();
        Form form = CreateForm(Panel(button));

        AriaSnapshot.Build([new AriaSnapshot.Layer(form, Point.Empty, false)], ids.Of, peers);

        Assert.Same(button.GetAccessibilityPeer(), peers[ids.Of(button.GetAccessibilityPeer()!)]);
    }
}