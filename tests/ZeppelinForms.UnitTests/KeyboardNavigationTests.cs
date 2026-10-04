using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The keyboard of 0.13.0 beyond the focused element: access keys, the menu bar
/// worked from the keyboard, radio groups as one Tab stop, Ctrl+Tab in tabs,
/// Escape and the default button.
/// </summary>
[Collection("Platform")]
public class KeyboardNavigationTests
{
    private static Form CreateForm(UIElement content)
    {
        var form = new Form { Size = new Size(500, 300), Content = content };

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

    // ===== access keys =====

    [Fact]
    public void MarkIsTakenOutOfCaptionAndName()
    {
        var marked = new Button { Text = "&Save", UseMnemonic = true };
        var plain = new Button { Text = "Tom & Jerry" };
        CreateForm(Panel(marked, plain));

        Assert.Equal("Save", marked.GetAccessibilityPeer()!.Name);
        Assert.Equal("Alt+S", marked.GetAccessibilityPeer()!.AccessKey);

        // off by default: an ordinary ampersand stays
        Assert.Equal("Tom & Jerry", plain.GetAccessibilityPeer()!.Name);
        Assert.Null(plain.GetAccessibilityPeer()!.AccessKey);
    }

    [Fact]
    public void AltLetterPressesButton()
    {
        int clicks = 0;
        var button = new Button { Text = "&Save", UseMnemonic = true };
        button.Click += (_, _) => clicks++;
        Form form = CreateForm(Panel(new TextBox(), button));

        HeadlessInput.PressKey(form, Key.S, KeyModifiers.Alt);

        Assert.Equal(1, clicks);
        Assert.True(button.IsFocused);
    }

    [Fact]
    public void AltLetterTogglesCheckBox()
    {
        var box = new CheckBox { Text = "&Remember", UseMnemonic = true };
        Form form = CreateForm(box);

        HeadlessInput.PressKey(form, Key.R, KeyModifiers.Alt);

        Assert.True(box.IsChecked);
    }

    [Fact]
    public void LabelKeyFocusesItsTarget()
    {
        var box = new TextBox();
        var label = new Label { Text = "&Name:", UseMnemonic = true, Target = box };
        Form form = CreateForm(Panel(label, box));

        HeadlessInput.PressKey(form, Key.N, KeyModifiers.Alt);

        Assert.True(box.IsFocused);
    }

    [Fact]
    public void SameKeyCyclesFocusWithoutRunning()
    {
        int clicks = 0;
        var apply = new Button { Text = "&Apply", UseMnemonic = true };
        var about = new Button { Text = "&About", UseMnemonic = true };
        apply.Click += (_, _) => clicks++;
        about.Click += (_, _) => clicks++;
        Form form = CreateForm(Panel(apply, about));

        HeadlessInput.PressKey(form, Key.A, KeyModifiers.Alt);
        Assert.True(apply.IsFocused);

        HeadlessInput.PressKey(form, Key.A, KeyModifiers.Alt);
        Assert.True(about.IsFocused);

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void KeyInUsersLayoutComesAsCharacter()
    {
        int clicks = 0;
        var button = new Button { Text = "&Файл", UseMnemonic = true };
        button.Click += (_, _) => clicks++;
        Form form = CreateForm(button);

        form.OnAccessKeyChar('ф');

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void KeyAndItsCharacterRunOnce()
    {
        int clicks = 0;
        var button = new Button { Text = "&Save", UseMnemonic = true };
        button.Click += (_, _) => clicks++;
        Form form = CreateForm(button);

        // Windows sends WM_SYSKEYDOWN and then WM_SYSCHAR for the same press
        form.OnKeyDown(Key.S, KeyModifiers.Alt, false);
        form.OnAccessKeyChar('s');

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void UnderlinesShowWhileAltIsHeld()
    {
        Form form = CreateForm(new Button { Text = "&Save", UseMnemonic = true });

        form.OnKeyDown(Key.Alt, KeyModifiers.Alt, false);
        Assert.True(form.ShowsAccessKeys);

        form.OnKeyUp(Key.Alt, KeyModifiers.None);
        Assert.False(form.ShowsAccessKeys);
    }

    // ===== menus =====

    private static (MenuBar Bar, Func<int> NewClicks, Func<int> OpenClicks) CreateMenu()
    {
        int newClicks = 0, openClicks = 0;

        var newItem = new MenuItem { Text = "&New" };
        var openItem = new MenuItem { Text = "&Open" };
        newItem.Click += (_, _) => newClicks++;
        openItem.Click += (_, _) => openClicks++;

        var file = new MenuItem { Text = "&File" };
        file.Items.Add(newItem);
        file.Items.Add(openItem);

        var edit = new MenuItem { Text = "&Edit" };
        edit.Items.Add(new MenuItem { Text = "&Undo" });

        var bar = new MenuBar { Items = [file, edit] };

        return (bar, () => newClicks, () => openClicks);
    }

    private static MenuList? OpenMenu(Form form) => form.Overlays.OfType<MenuList>().LastOrDefault();

    [Fact]
    public void AltTapEntersMenuModeAndArrowsRunItem()
    {
        (MenuBar bar, _, Func<int> openClicks) = CreateMenu();
        Form form = CreateForm(Panel(bar, new TextBox()));

        HeadlessInput.PressKey(form, Key.Alt);
        Assert.True(form.ShowsAccessKeys);

        HeadlessInput.PressKey(form, Key.Down);
        MenuList? menu = OpenMenu(form);

        Assert.NotNull(menu);
        Assert.Equal(0, bar.OpenIndex);
        Assert.Equal(0, menu.HighlightedIndex);

        HeadlessInput.PressKey(form, Key.Down);
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Equal(1, openClicks());
        Assert.Null(OpenMenu(form));
        Assert.False(form.ShowsAccessKeys);
    }

    [Fact]
    public void F10ThenRightMovesAlongTheBar()
    {
        (MenuBar bar, _, _) = CreateMenu();
        Form form = CreateForm(bar);

        HeadlessInput.PressKey(form, Key.F10);
        HeadlessInput.PressKey(form, Key.Right);

        // moving along a closed bar only moves the highlight
        Assert.Equal(-1, bar.OpenIndex);

        HeadlessInput.PressKey(form, Key.Down);
        Assert.Equal(1, bar.OpenIndex);

        // with a submenu open, the neighbour's opens in its place
        HeadlessInput.PressKey(form, Key.Left);
        Assert.Equal(0, bar.OpenIndex);
    }

    [Fact]
    public void AltLetterOpensMenuAndLetterRunsItem()
    {
        (MenuBar bar, Func<int> newClicks, _) = CreateMenu();
        Form form = CreateForm(bar);

        HeadlessInput.PressKey(form, Key.F, KeyModifiers.Alt);
        Assert.Equal(0, bar.OpenIndex);

        HeadlessInput.PressKey(form, Key.N);

        Assert.Equal(1, newClicks());
        Assert.Null(OpenMenu(form));
    }

    [Fact]
    public void EscapeStepsBackOutOfMenus()
    {
        (MenuBar bar, _, _) = CreateMenu();
        Form form = CreateForm(bar);

        HeadlessInput.PressKey(form, Key.F, KeyModifiers.Alt);
        Assert.NotNull(OpenMenu(form));

        // first the submenu, then the bar
        HeadlessInput.PressKey(form, Key.Escape);
        Assert.Null(OpenMenu(form));
        Assert.True(form.ShowsAccessKeys);

        HeadlessInput.PressKey(form, Key.Escape);
        Assert.False(form.ShowsAccessKeys);
    }

    // ===== radio groups =====

    [Fact]
    public void RadioGroupIsOneTabStop()
    {
        var a = new RadioButton { Text = "A" };
        var b = new RadioButton { Text = "B" };
        var after = new Button { Text = "Next" };
        Form form = CreateForm(Panel(a, b, after));

        HeadlessInput.PressKey(form, Key.Tab);
        Assert.True(a.IsFocused);

        HeadlessInput.PressKey(form, Key.Tab);
        Assert.True(after.IsFocused);
    }

    [Fact]
    public void ArrowsMoveCheckWithinGroup()
    {
        var a = new RadioButton { Text = "A" };
        var b = new RadioButton { Text = "B" };
        var c = new RadioButton { Text = "C" };
        b.SetChecked(true);
        Form form = CreateForm(Panel(a, b, c));

        // Tab enters the group at the checked button
        HeadlessInput.PressKey(form, Key.Tab);
        Assert.True(b.IsFocused);

        HeadlessInput.PressKey(form, Key.Down);
        Assert.True(c.IsChecked && c.IsFocused);
        Assert.False(b.IsChecked);

        // and wraps around
        HeadlessInput.PressKey(form, Key.Down);
        Assert.True(a.IsChecked && a.IsFocused);
    }

    // ===== tabs =====

    [Fact]
    public void CtrlTabSwitchesTabsFromInside()
    {
        var inside = new Button { Text = "inside" };
        var tabs = new TabControl
        {
            Tabs =
            [
                new TabItem { Header = "One", Content = inside },
                new TabItem { Header = "Two", Content = new Label { Text = "two" } },
                new TabItem { Header = "Three", Content = new Label { Text = "three" } },
            ],
        };
        Form form = CreateForm(tabs);

        form.FocusForAccessibility(inside);

        HeadlessInput.PressKey(form, Key.Tab, KeyModifiers.Control);
        Assert.Equal(1, tabs.SelectedIndex);

        HeadlessInput.PressKey(form, Key.Tab, KeyModifiers.Control | KeyModifiers.Shift);
        HeadlessInput.PressKey(form, Key.PageUp, KeyModifiers.Control);

        // backwards from the first wraps to the last
        Assert.Equal(2, tabs.SelectedIndex);
    }

    [Fact]
    public void ArrowInsidePageDoesNotSwitchTab()
    {
        var inside = new Button { Text = "inside" };
        var tabs = new TabControl
        {
            Tabs =
            [
                new TabItem { Header = "One", Content = inside },
                new TabItem { Header = "Two", Content = new Label { Text = "two" } },
            ],
        };
        Form form = CreateForm(tabs);

        form.FocusForAccessibility(inside);
        HeadlessInput.PressKey(form, Key.Right);

        Assert.Equal(0, tabs.SelectedIndex);
    }

    // ===== Escape and Enter =====

    [Fact]
    public void EscapeClosesTopFlyout()
    {
        var anchor = new Button { Text = "anchor" };
        Form form = CreateForm(anchor);

        form.ShowFlyout(anchor, new Label { Text = "flyout" });
        int before = form.Overlays.Count;

        HeadlessInput.PressKey(form, Key.Escape);

        Assert.Equal(before - 1, form.Overlays.Count);
    }

    [Fact]
    public void EnterPressesDefaultButton()
    {
        int clicks = 0;
        var box = new TextBox();
        var ok = new Button { Text = "OK" };
        ok.Click += (_, _) => clicks++;
        Form form = CreateForm(Panel(box, ok));
        form.DefaultButton = ok;

        form.FocusForAccessibility(box);
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void FieldThatTakesEnterKeepsIt()
    {
        int clicks = 0, accepted = 0;
        var box = new TextBox();
        box.Accepted += (_, _) => accepted++;
        var ok = new Button { Text = "OK" };
        ok.Click += (_, _) => clicks++;
        Form form = CreateForm(Panel(box, ok));
        form.DefaultButton = ok;

        form.FocusForAccessibility(box);
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Equal(1, accepted);
        Assert.Equal(0, clicks);
    }
}