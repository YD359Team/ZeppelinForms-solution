using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Commands;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.16: key gestures, commands on buttons and menu items, the form's
/// shortcuts, check marks and the Opening of a menu.</summary>
[Collection("Platform")]
public class CommandsAndMenusTests
{
    private static Form Show(UIElement content)
    {
        var form = new Form { Size = new Size(400, 300), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    private static T TopLeft<T>(T element, float width, float height) where T : UIElement
    {
        element.Size = new Size(width, height);
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.VerticalAlignment = VerticalAlignment.Top;

        return element;
    }

    // ===== gestures =====

    [Fact]
    public void AGestureReadsAsMenusShowIt()
    {
        Assert.Equal("Ctrl+Shift+S", new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift).ToString());
        Assert.Equal("Ctrl+Alt+Del", new KeyGesture(Key.Delete, KeyModifiers.Control | KeyModifiers.Alt).ToString());
        Assert.Equal("Ctrl+=", new KeyGesture(Key.OemPlus, KeyModifiers.Control).ToString());
        Assert.Equal("F5", new KeyGesture(Key.F5).ToString());
        Assert.Equal("Ctrl+1", new KeyGesture(Key.D1, KeyModifiers.Control).ToString());
    }

    [Theory]
    [InlineData("Ctrl+S", Key.S, KeyModifiers.Control)]
    [InlineData("ctrl+shift+s", Key.S, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Control+Z", Key.Z, KeyModifiers.Control)]
    [InlineData("Ctrl++", Key.OemPlus, KeyModifiers.Control)]
    [InlineData("Ctrl+Num +", Key.Add, KeyModifiers.Control)]
    [InlineData("Alt+Enter", Key.Enter, KeyModifiers.Alt)]
    [InlineData("Ctrl+1", Key.D1, KeyModifiers.Control)]
    [InlineData("F5", Key.F5, KeyModifiers.None)]
    [InlineData("Del", Key.Delete, KeyModifiers.None)]
    [InlineData("Escape", Key.Escape, KeyModifiers.None)]
    public void AGestureIsParsed(string text, Key key, KeyModifiers modifiers) =>
        Assert.Equal(new KeyGesture(key, modifiers), KeyGesture.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Foo")]
    [InlineData("Hyper+S")]
    public void NonsenseIsNoGesture(string text) => Assert.False(KeyGesture.TryParse(text, out _));

    [Fact]
    public void TheModifiersMustMatchExactly()
    {
        var undo = new KeyGesture(Key.Z, KeyModifiers.Control);

        Assert.True(undo.Matches(Key.Z, KeyModifiers.Control));
        Assert.False(undo.Matches(Key.Z, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.False(undo.Matches(Key.Z, KeyModifiers.None));
    }

    // ===== the form's shortcuts =====

    [Fact]
    public void AMenuBarItemRunsItsGestureWithTheMenuClosed()
    {
        int saved = 0;
        var save = new MenuItem { Text = "&Save", KeyGesture = new KeyGesture(Key.S, KeyModifiers.Control) };
        save.Click += (_, _) => saved++;

        Form form = Show(new StackPanel
        {
            Children = { new MenuBar { Items = { new MenuItem { Text = "&File", Items = { save } } } } },
        });

        HeadlessInput.PressKey(form, Key.S, KeyModifiers.Control);

        Assert.Equal(1, saved);
    }

    [Fact]
    public void OpeningRunsBeforeTheShortcutOfAnItemUnderIt()
    {
        int saved = 0, opened = 0;
        var save = new MenuItem { Text = "&Save", KeyGesture = new KeyGesture(Key.S, KeyModifiers.Control) };
        save.Click += (_, _) => saved++;

        var file = new MenuItem { Text = "&File", Items = { save } };

        // the state is set only as the menu opens: the first time there is nothing to save
        file.Opening += (_, _) => save.IsEnabled = ++opened > 1;

        Form form = Show(new StackPanel { Children = { new MenuBar { Items = { file } } } });

        HeadlessInput.PressKey(form, Key.S, KeyModifiers.Control);
        Assert.Equal((1, 0), (opened, saved));

        HeadlessInput.PressKey(form, Key.S, KeyModifiers.Control);
        Assert.Equal((2, 1), (opened, saved));

        // a key no item answers doesn't open anything
        HeadlessInput.PressKey(form, Key.Q, KeyModifiers.Control);
        Assert.Equal(2, opened);
    }

    [Fact]
    public void TheFocusedTextBoxKeepsItsKeys()
    {
        int copied = 0;
        var box = new TextBox();
        Form form = Show(new StackPanel { Children = { box } });

        form.KeyBindings.Add(new KeyBinding(new KeyGesture(Key.C, KeyModifiers.Control), new Command(() => copied++)));

        box.Focus();
        HeadlessInput.TypeText(form, "abc");
        box.Select(0, 3);
        HeadlessInput.PressKey(form, Key.C, KeyModifiers.Control);

        Assert.Equal(0, copied);
    }

    [Fact]
    public void AKeyBindingRunsWhileItsCommandCan()
    {
        bool can = false;
        int ran = 0;
        var refresh = new Command(() => ran++, () => can) { Gesture = new KeyGesture(Key.F5) };

        Form form = Show(new Button { Text = "x" });
        form.KeyBindings.Add(new KeyBinding(refresh));

        HeadlessInput.PressKey(form, Key.F5);
        Assert.Equal(0, ran);

        can = true;
        HeadlessInput.PressKey(form, Key.F5);
        HeadlessInput.PressKey(form, Key.F5, KeyModifiers.Shift);

        Assert.Equal(1, ran);
    }

    [Fact]
    public void ABindingNeedsAGesture() =>
        Assert.Throws<ArgumentException>(() => new KeyBinding(new Command(() => { })));

    // ===== commands on buttons =====

    [Fact]
    public void AButtonFollowsItsCommand()
    {
        bool can = false;
        int ran = 0;
        var go = new Command(() => ran++, () => can);

        Button button = TopLeft(new Button { Text = "Go", Command = go }, 100, 30);
        Form form = Show(button);

        Assert.False(button.IsEnabled);

        can = true;
        go.NotifyCanExecuteChanged();
        Assert.True(button.IsEnabled);

        HeadlessInput.Click(form, 50, 15);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void IsEnabledFromCodeWinsOverTheCommand()
    {
        bool can = true;
        var go = new Command(() => { }, () => can);

        Button button = TopLeft(new Button { Text = "Go", Command = go }, 100, 30);
        Show(button);

        button.IsEnabled = true;
        can = false;
        go.NotifyCanExecuteChanged();

        Assert.True(button.IsEnabled);
    }

    [Fact]
    public void WithoutItsCommandAButtonIsEnabledAgain()
    {
        var button = new Button { Command = new Command(() => { }, () => false) };
        Assert.False(button.IsEnabled);

        button.Command = null;
        Assert.True(button.IsEnabled);
    }

    [Fact]
    public void ADetachedButtonLetsGoOfItsCommand()
    {
        bool can = true;
        var go = new Command(() => { }, () => can);

        Button button = TopLeft(new Button { Text = "Go", Command = go }, 100, 30);
        var panel = new StackPanel { Children = { button } };
        Show(panel);

        panel.Children.Remove(button);

        // out of the form, the button no longer listens — and isn't kept alive by it
        can = false;
        go.NotifyCanExecuteChanged();
        Assert.True(button.IsEnabled);

        // back in a form, it catches up
        panel.Children.Add(button);
        Assert.False(button.IsEnabled);
    }

    // ===== check marks =====

    [Fact]
    public void AToggleCommandKeepsTheMenuAndTheButtonTogether()
    {
        var grid = new ToggleCommand { Gesture = new KeyGesture(Key.G, KeyModifiers.Control) };
        var item = new MenuItem { Text = "&Grid", Command = grid };
        ToggleButton button = TopLeft(new ToggleButton { Text = "Grid", Command = grid }, 80, 30);

        Form form = Show(new StackPanel
        {
            Children = { new MenuBar { Items = { new MenuItem { Text = "&View", Items = { item } } } }, button },
        });

        HeadlessInput.PressKey(form, Key.G, KeyModifiers.Control);
        Assert.True(grid.IsChecked);
        Assert.True(button.IsChecked);
        Assert.True(item.IsEffectivelyChecked);

        button.IsChecked = false;
        Assert.False(grid.IsChecked);

        // a click flips it once: by the command, not by the button and the command both
        Point at = button.GetAbsolutePosition();
        HeadlessInput.Click(form, at.X + 10, at.Y + 10);
        Assert.True(grid.IsChecked);
        Assert.True(button.IsChecked);

        grid.IsChecked = false;
        Assert.False(button.IsChecked);
    }

    [Fact]
    public void ACheckableItemFlipsAsItRuns()
    {
        var rulers = new MenuItem { Text = "&Rulers", IsCheckable = true, KeyGesture = new KeyGesture(Key.R, KeyModifiers.Control) };

        Form form = Show(new StackPanel
        {
            Children = { new MenuBar { Items = { new MenuItem { Text = "&View", Items = { rulers } } } } },
        });

        HeadlessInput.PressKey(form, Key.R, KeyModifiers.Control);
        Assert.True(rulers.IsChecked);

        HeadlessInput.PressKey(form, Key.R, KeyModifiers.Control);
        Assert.False(rulers.IsChecked);
    }

    [Fact]
    public void AnItemIsDisabledWhileItsCommandCant()
    {
        var item = new MenuItem { Text = "Paste", Command = new Command(() => { }, () => false) };

        Assert.False(item.IsEffectivelyEnabled);
        Assert.False(item.Invoke());
    }

    // ===== the menu's look =====

    [Fact]
    public void ShortcutsWidenTheMenuByAColumn()
    {
        var plain = new MenuList { Items = { new MenuItem { Text = "Save" } } };
        var withShortcut = new MenuList
        {
            Items = { new MenuItem { Text = "Save", KeyGesture = new KeyGesture(Key.S, KeyModifiers.Control) } },
        };

        plain.Measure(new Size(1000, 1000));
        withShortcut.Measure(new Size(1000, 1000));

        Assert.True(withShortcut.DesiredSize.Width > plain.DesiredSize.Width + 20);
    }

    [Fact]
    public void TheShortcutTextComesFromTheCommand()
    {
        var item = new MenuItem { Command = new Command(() => { }) { Gesture = KeyGesture.Parse("Ctrl+Shift+N") } };
        Assert.Equal("Ctrl+Shift+N", item.GestureText);

        item.InputGestureText = "Ctrl+Wheel";
        Assert.Equal("Ctrl+Wheel", item.GestureText);
    }
}
