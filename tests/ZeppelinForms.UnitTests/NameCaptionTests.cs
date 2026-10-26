using System.Xml.Linq;
using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>A control given a name and no text shows its name, as one the WinForms
/// designer adds shows "label1": the name it was given, never one its form made up.</summary>
[Collection("Platform")]
public sealed class NameCaptionTests
{
    private static (Form Form, HeadlessWindow Window) Host(UIElement content, Form? form = null)
    {
        form ??= new Form { Size = new Size(400, 300) };
        form.Content = new StackPanel { Children = { content } };

        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, window);
    }

    [Fact]
    public void ANamedLabelWithoutTextShowsItsName()
    {
        var label = new Label { Name = "label1" };
        Host(label);

        Assert.Null(label.Text);
        Assert.Equal("label1", label.ShownText);
        Assert.True(label.ActualSize.Width > label.Padding.Horizontal, "the name is measured as the text");
    }

    [Fact]
    public void AnEmptyTextSetOnPurposeShowsNothing()
    {
        var label = new Label { Name = "spacer", Text = string.Empty };
        Host(label);

        Assert.Equal(string.Empty, label.ShownText);
    }

    [Fact]
    public void ATextReplacesTheName()
    {
        var check = new CheckBox { Name = "agree" };
        Host(check);

        float named = check.ActualSize.Width;

        check.Text = "I agree to the terms";
        Assert.Equal("I agree to the terms", check.ShownText);

        // back to "not set": the name again
        check.Text = null;
        Assert.Equal("agree", check.ShownText);
        Assert.True(named > 0);
    }

    [Fact]
    public void ANameTheFormMadeUpIsNotACaption()
    {
        var label = new Label();
        (Form form, _) = Host(label);

        Assert.StartsWith("label", label.Name);
        Assert.Same(label, form.FindByName(label.Name));
        Assert.Null(label.ShownText);
    }

    [Fact]
    public void AnIconStandsForAButtonsCaption()
    {
        var button = new Button { Name = "close", Icon = IconSource.FromPath("M0 0 L10 10") };

        Assert.Null(button.ShownText);

        button.Icon = null;
        Assert.Equal("close", button.ShownText);
    }

    [Fact]
    public void AToggleButtonKeepsTheButtonsRuleForAnIcon()
    {
        // a toggle button is a button: its icon stands for the caption as well
        var toggle = new ToggleButton { Name = "bold", Icon = IconSource.FromPath("M0 0 L10 10") };

        Assert.Null(toggle.ShownText);
    }

    [Fact]
    public void ASplitButtonMeasuresTheNameItShows()
    {
        var split = new SplitButton { Name = "more", StableWidth = false };
        var empty = new SplitButton { Text = string.Empty, StableWidth = false };
        Host(split);
        Host(empty);

        Assert.True(split.ActualSize.Width > empty.ActualSize.Width, "the name takes room as a text does");
    }

    [Fact]
    public void EveryCaptionedControlFallsBackToItsName()
    {
        UIElement[] controls =
        [
            new Button { Name = "a" }, new CheckBox { Name = "b" }, new RadioButton { Name = "c" },
            new ToggleSwitch { Name = "d" }, new SplitButton { Name = "e" }, new GroupBox { Name = "f" },
            new Label { Name = "g" }, new LinkLabel { Name = "h" },
            new ToggleButton { Name = "i" },
        ];

        foreach (UIElement control in controls)
            Assert.Equal(control.Name, control.ShownText);
    }

    [Fact]
    public void TheNameIsReadAsTheAccessibleName()
    {
        var button = new Button { Name = "save" };
        Host(button);

        Assert.Equal("save", button.GetAccessibilityPeer()!.Name);
    }

    [Fact]
    public void AFormWithoutATitleIsCaptionedByItsName()
    {
        var form = new Form { Name = "Form1" };
        (_, HeadlessWindow window) = Host(new Label { Text = "x" }, form);

        form.Show();
        Assert.Equal("Form1", window.Title);

        // a title set later shows at once
        form.Title = "Orders";
        Assert.Equal("Orders", window.Title);
    }
}