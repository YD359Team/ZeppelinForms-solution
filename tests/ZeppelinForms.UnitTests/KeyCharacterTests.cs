using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.21: the character of a key with its modifiers, as X11, Wayland, the
/// browser and Android give it — one event for both. Alt makes it an access key in the
/// user's layout, as WM_SYSCHAR does on Windows; Control makes it a shortcut; both are
/// AltGr and type.</summary>
[Collection("Platform")]
public sealed class KeyCharacterTests
{
    private static Form CreateForm(params UIElement[] children)
    {
        var panel = new StackPanel();

        foreach (UIElement child in children)
            panel.Children.Add(child);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    [Fact]
    public void AltWithALetterOfTheLayoutRunsItsAccessKey()
    {
        int clicks = 0;
        var button = new Button { Text = "&Файл", UseMnemonic = true };
        button.Click += (_, _) => clicks++;
        Form form = CreateForm(button);

        // Ф is on the key of A: the key code finds nothing, the character does
        form.OnKeyDown(Key.A, KeyModifiers.Alt, false);
        form.OnKeyCharacter('ф', KeyModifiers.Alt);

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void ALatinAccessKeyRunsOnceForItsKeyAndItsCharacter()
    {
        int clicks = 0;
        var button = new Button { Text = "&Save", UseMnemonic = true };
        button.Click += (_, _) => clicks++;
        Form form = CreateForm(button);

        form.OnKeyDown(Key.S, KeyModifiers.Alt, false);
        form.OnKeyCharacter('s', KeyModifiers.Alt);

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void AltWithALetterIsNotTyped()
    {
        var box = new TextBox();
        Form form = CreateForm(box);
        box.Focus();

        form.OnKeyCharacter('ф', KeyModifiers.Alt);

        Assert.Equal(string.Empty, box.Text);
    }

    [Fact]
    public void ControlAloneIsAShortcutAndTypesNothing()
    {
        var box = new TextBox();
        Form form = CreateForm(box);
        box.Focus();

        form.OnKeyCharacter(';', KeyModifiers.Control);

        Assert.Equal(string.Empty, box.Text);
    }

    [Fact]
    public void AltGrTypes()
    {
        var box = new TextBox();
        Form form = CreateForm(box);
        box.Focus();

        form.OnKeyCharacter('@', KeyModifiers.Control | KeyModifiers.Alt);
        form.OnKeyCharacter('x', KeyModifiers.Shift);

        Assert.Equal("@x", box.Text);
    }
}
