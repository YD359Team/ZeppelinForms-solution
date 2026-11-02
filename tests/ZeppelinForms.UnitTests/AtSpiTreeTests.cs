using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Linux.Accessibility;
using ZeppelinForms.Linux.DBus;

namespace ZeppelinForms.UnitTests;

/// <summary>0.21: the forms on the AT-SPI bus of Linux, without a bus — the calls a screen
/// reader makes, answered from the peers, and the events it is sent, recorded.</summary>
[Collection("Platform")]
public sealed class AtSpiTreeTests
{
    private const string BusName = ":1.7";

    private sealed class Windows : IAtSpiWindows
    {
        public float Scale(Form form) => 2f;

        public bool TryGetScreenOrigin(Form form, out int x, out int y)
        {
            x = 100;
            y = 50;
            return true;
        }
    }

    private sealed record Signal(string Path, string Interface, string Member, IReadOnlyList<object?> Values)
    {
        public string Detail => (string)Values[0]!;

        public int Detail1 => (int)Values[1]!;

        public object? Data => ((DBusVariant)Values[3]!).Value;
    }

    private sealed class Fixture
    {
        public List<Signal> Signals { get; } = [];

        public Form Form { get; }

        public AtSpiTree Tree { get; }

        public Fixture(params UIElement[] children)
        {
            var panel = new StackPanel();

            foreach (UIElement child in children)
                panel.Children.Add(child);

            Form = new Form { Title = "Orders", Size = new Size(400, 300), Content = panel };
            new HeadlessPlatform().CreateWindow(Form);
            Form.Show();
            Form.UpdateLayout();

            Tree = new AtSpiTree(BusName,
                (path, @interface, member, _, values) => Signals.Add(new Signal(path, @interface, member, values)),
                new Windows());
        }

        public string PathOf(UIElement element) => Tree.PathOf(element.GetAccessibilityPeer()!);

        public IReadOnlyList<object?> Call(string path, string @interface, string member, string signature = "", params object?[] body)
        {
            var call = new DBusMessage(DBusMessageType.MethodCall, 1, null, path, @interface, member, null, ":1.9", signature, body);

            DBusReply reply = Tree.Call(call);

            // what the tree answers must go over the wire as its signature says
            byte[] bytes = DBusWire.MethodReturn(2, call, reply.Signature, reply.Values);
            Assert.Equal(bytes.Length, DBusWire.MessageLength(bytes));

            return DBusWire.Parse(bytes).Body;
        }

        public object? Single(string path, string @interface, string member, string signature = "", params object?[] body) =>
            Call(path, @interface, member, signature, body)[0];

        public object? Property(string path, string @interface, string name) =>
            ((DBusVariant)Single(path, AtSpi.PropertiesInterface, "Get", "ss", @interface, name)!).Value;
    }

    [Fact]
    public void TheRootIsTheApplicationAndItsChildrenTheForms()
    {
        var fixture = new Fixture(new Label { Text = "Hello" });

        Assert.Equal(AtSpi.RoleApplication, fixture.Single(AtSpi.RootPath, AtSpi.AccessibleInterface, "GetRole"));

        var children = (List<object?>)fixture.Single(AtSpi.RootPath, AtSpi.AccessibleInterface, "GetChildren")!;
        var form = (object?[])Assert.Single(children)!;

        Assert.Equal(BusName, form[0]);
        Assert.Equal(AtSpi.RoleFrame, fixture.Single((string)form[1]!, AtSpi.AccessibleInterface, "GetRole"));
        Assert.Equal("Orders", fixture.Property((string)form[1]!, AtSpi.AccessibleInterface, "Name"));

        // the form's parent is the application
        var parent = (object?[])fixture.Property((string)form[1]!, AtSpi.AccessibleInterface, "Parent")!;
        Assert.Equal(AtSpi.RootPath, parent[1]);
    }

    [Fact]
    public void AButtonIsAPushButtonThatClicksWithItsAccessKey()
    {
        int clicks = 0;
        var button = new Button { Text = "&Save", UseMnemonic = true };
        button.Click += (_, _) => clicks++;
        var fixture = new Fixture(button);
        string path = fixture.PathOf(button);

        Assert.Equal(AtSpi.RolePushButton, fixture.Single(path, AtSpi.AccessibleInterface, "GetRole"));
        Assert.Equal("Save", fixture.Property(path, AtSpi.AccessibleInterface, "Name"));

        var actions = (List<object?>)fixture.Single(path, AtSpi.ActionInterface, "GetActions")!;
        var click = (object?[])Assert.Single(actions)!;
        Assert.Equal("click", click[0]);
        Assert.Equal("<Alt>s;;", click[2]);

        Assert.True((bool)fixture.Single(path, AtSpi.ActionInterface, "DoAction", "i", 0)!);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void TheStateSetCarriesTheChecks()
    {
        var check = new CheckBox { Text = "Remember", IsChecked = true };
        var fixture = new Fixture(check);
        string path = fixture.PathOf(check);

        var words = (List<object?>)fixture.Single(path, AtSpi.AccessibleInterface, "GetState")!;
        ulong states = (uint)words[0]! | ((ulong)(uint)words[1]! << 32);

        Assert.NotEqual(0UL, states & AtSpi.Bit(AtSpi.StateChecked));
        Assert.NotEqual(0UL, states & AtSpi.Bit(AtSpi.StateCheckable));
        Assert.NotEqual(0UL, states & AtSpi.Bit(AtSpi.StateEnabled));
    }

    [Fact]
    public void AStateChangeIsToldAgainstWhatTheClientSaw()
    {
        var check = new CheckBox { Text = "Remember" };
        var fixture = new Fixture(check);
        string path = fixture.PathOf(check);

        fixture.Call(path, AtSpi.AccessibleInterface, "GetState");
        check.IsChecked = true;

        Signal changed = Assert.Single(fixture.Signals, s => s.Member == "StateChanged" && s.Detail == "checked");
        Assert.Equal(path, changed.Path);
        Assert.Equal(1, changed.Detail1);
    }

    [Fact]
    public void ScreenCoordinatesAreTheWindowsCornerAndItsScale()
    {
        var label = new Label { Text = "Hello" };
        var fixture = new Fixture(label);
        string path = fixture.PathOf(label);
        Rectangle bounds = label.GetAccessibilityPeer()!.Bounds;

        var extents = (object?[])fixture.Single(path, AtSpi.ComponentInterface, "GetExtents", "u", AtSpi.CoordScreen)!;

        Assert.Equal((int)MathF.Round(100 + bounds.X * 2), extents[0]);
        Assert.Equal((int)MathF.Round(50 + bounds.Y * 2), extents[1]);
        Assert.Equal((int)MathF.Round(bounds.Width * 2), extents[2]);
    }

    [Fact]
    public void TypingIsToldAsTextInsertedAndTheCaretMoved()
    {
        var box = new TextBox { AccessibleName = "Name" };
        var fixture = new Fixture(box);
        string path = fixture.PathOf(box);

        // a client read the field: the bridge listens from now on
        Assert.Equal(string.Empty, fixture.Single(path, AtSpi.TextInterface, "GetText", "ii", 0, -1));

        box.Focus();
        fixture.Form.OnTextInput('h');
        fixture.Form.OnTextInput('i');

        List<Signal> inserted = [.. fixture.Signals.Where(s => s.Member == "TextChanged" && s.Detail == "insert")];
        Assert.Equal(new object?[] { "h", "i" }, inserted.Select(s => s.Data));
        Assert.Equal(new[] { 0, 1 }, inserted.Select(s => s.Detail1));

        Signal caret = fixture.Signals.Last(s => s.Member == "TextCaretMoved");
        Assert.Equal(2, caret.Detail1);

        Assert.Equal("hi", fixture.Single(path, AtSpi.TextInterface, "GetText", "ii", 0, -1));
        Assert.Equal(2, fixture.Property(path, AtSpi.TextInterface, "CaretOffset"));
    }

    [Fact]
    public void APasswordGivesNeitherItsTextNorItsCaret()
    {
        var box = new TextBox { Text = "secret", PasswordChar = '*' };
        var fixture = new Fixture(box);
        string path = fixture.PathOf(box);

        Assert.Equal(AtSpi.RolePasswordText, fixture.Single(path, AtSpi.AccessibleInterface, "GetRole"));
        Assert.Equal(string.Empty, fixture.Single(path, AtSpi.TextInterface, "GetText", "ii", 0, -1));
        Assert.Equal(-1, fixture.Property(path, AtSpi.TextInterface, "CaretOffset"));
    }

    [Fact]
    public void NothingIsSentUntilAClientAsks()
    {
        var label = new Label { Text = "Hello" };
        var fixture = new Fixture(label);

        label.Text = "Changed";

        Assert.Empty(fixture.Signals);
    }

    [Fact]
    public void AnAnnouncementIsToldOnTheForm()
    {
        var fixture = new Fixture(new Label { Text = "Hello" });
        fixture.Call(AtSpi.RootPath, AtSpi.AccessibleInterface, "GetChildren");

        fixture.Form.Announce("Saved", AccessibilityLiveSetting.Assertive);

        Signal announcement = Assert.Single(fixture.Signals, s => s.Member == "Announcement");
        Assert.Equal(fixture.Tree.PathOf(fixture.Form.GetAccessibilityPeer()), announcement.Path);
        Assert.Equal(AtSpi.LiveAssertive, announcement.Detail1);
        Assert.Equal("Saved", announcement.Data);
    }

    [Fact]
    public void AnObjectThatIsGoneIsUnknown()
    {
        var fixture = new Fixture();

        var error = Assert.Throws<DBusErrorException>(() =>
            fixture.Call(AtSpi.AccessiblePrefix + "/999", AtSpi.AccessibleInterface, "GetRole"));

        Assert.Equal("org.freedesktop.DBus.Error.UnknownObject", error.Name);
    }

    // ===== text by pieces =====

    [Fact]
    public void AWordIsTakenWithTheSpacesAfterIt() =>
        Assert.Equal(("two ", 4, 8), AtSpiText.At("one two three", 5, AtSpi.GranularityWord));

    [Fact]
    public void ALineIsTakenWithItsBreak()
    {
        Assert.Equal(("second\n", 6, 13), AtSpiText.At("first\nsecond\nthird", 6, AtSpi.GranularityLine));
        Assert.Equal(("third", 13, 18), AtSpiText.At("first\nsecond\nthird", 18, AtSpi.GranularityLine));
    }

    [Fact]
    public void TheDifferenceIsWhatWasDeletedAndWhatWasInserted()
    {
        Assert.Equal((3, "", "d"), AtSpiText.Difference("abc", "abcd"));
        Assert.Equal((1, "b", "x"), AtSpiText.Difference("abc", "axc"));
        Assert.Null(AtSpiText.Difference("same", "same"));
    }
}
