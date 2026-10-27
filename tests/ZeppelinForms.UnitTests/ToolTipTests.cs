using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

/// <summary>The tooltip of 0.19: a themed control of its own, wrapped at a width, of any
/// content, and shown for the keyboard focus too.</summary>
[Collection("Platform")]
public class ToolTipTests
{
    private static (Form Form, HeadlessPlatform Platform) CreateForm(UIElement content, int delay = 1)
    {
        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(500, 300), Content = content, ToolTipDelay = delay };

        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, platform);
    }

    private static StackPanel Panel(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(20) };

        foreach (UIElement child in children)
            panel.Children.Add(child);

        return panel;
    }

    private static Point CenterOf(UIElement element)
    {
        Point at = element.GetAbsolutePosition();
        return new Point(at.X + element.ActualSize.Width / 2f, at.Y + element.ActualSize.Height / 2f);
    }

    /// <summary>The delay runs on a timer that comes back through the UI queue: pump the
    /// queue, as the UI thread would, until a tooltip is shown.</summary>
    private static ToolTip? WaitForToolTip(Form form, HeadlessPlatform platform)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (Shown(form) is null && DateTime.UtcNow < deadline)
        {
            platform.PumpAll();
            Thread.Sleep(5);
        }

        return Shown(form);
    }

    private static ToolTip? Shown(Form form) => form.Overlays.OfType<ToolTip>().LastOrDefault();

    private static ToolTip Hover(Form form, HeadlessPlatform platform, UIElement element)
    {
        form.OnPointerMove(CenterOf(element));

        ToolTip? tip = WaitForToolTip(form, platform);
        Assert.NotNull(tip);

        form.UpdateLayout();
        return tip!;
    }

    [Fact]
    public void TheThemeGivesTheToolTipItsLook()
    {
        var button = new Button { Text = "X", ToolTip = "Delete" };
        var (form, platform) = CreateForm(Panel(button));

        ToolTip tip = Hover(form, platform, button);
        Theme theme = App.Theme;

        Assert.Equal("Delete", tip.Text);
        Assert.Equal(theme.Colors.Surface, tip.Background);
        Assert.Equal(theme.Colors.Border, tip.BorderColor);
        Assert.Equal(theme.Metrics.OverlayCornerRadius, tip.CornerRadius);
        Assert.NotNull(tip.BoxShadow);
        Assert.False(tip.IsHitTestVisible);

        // the text is the theme's caption, in the theme's text color
        var text = Assert.IsAssignableFrom<Label>(tip.Child);
        Assert.Equal(TextStyle.Caption, text.TextStyle);
        Assert.Equal(theme.Colors.Text, text.TextColor);
    }

    [Fact]
    public void ALongTextWrapsAtTheMaximumWidth()
    {
        string text = string.Join(" ", Enumerable.Repeat("a tooltip that goes on", 12));
        var button = new Button { Text = "X", ToolTip = text };
        var (form, platform) = CreateForm(Panel(button));

        ToolTip tip = Hover(form, platform, button);
        var label = Assert.IsAssignableFrom<Label>(tip.Child);

        Assert.InRange(tip.DesiredSize.Width, 1f, tip.MaxContentWidth + tip.Padding.Horizontal + 0.5f);

        // more than one row: the height of the wrapped text, not of one line
        float oneRow = label.EffectiveFont.Size * 1.5f;
        Assert.True(label.DesiredSize.Height > oneRow, $"{label.DesiredSize.Height} is one row");
    }

    [Fact]
    public void AStyleSheetReachesTheToolTip()
    {
        var button = new Button { Text = "X", ToolTip = "Delete" };
        var (form, platform) = CreateForm(Panel(button));

        form.Styles.Add(StyleSheet.Parse("ToolTip { Background: #ff0000; MaxContentWidth: 200; }", "tip.zss"));

        ToolTip tip = Hover(form, platform, button);

        Assert.Equal(new Color(255, 255, 0, 0), tip.Background);
        Assert.Equal(200f, tip.MaxContentWidth);
    }

    [Fact]
    public void ContentIsLentToTheToolTipAndGivenBack()
    {
        var picture = new Label { Text = "a picture" };
        var button = new Button { Text = "X", ToolTipContent = picture };
        var other = new Button { Text = "Y" };
        var (form, platform) = CreateForm(Panel(button, other));

        ToolTip tip = Hover(form, platform, button);
        Assert.Same(picture, tip.Child);

        // the pointer leaves: the content is free again
        form.OnPointerMove(CenterOf(other));
        Assert.Null(Shown(form));
        Assert.Null(picture.Parent);

        // and serves the next showing
        Assert.Same(picture, Hover(form, platform, button).Child);
    }

    [Fact]
    public void AnElementsOwnDelayWins()
    {
        var button = new Button { Text = "X", ToolTip = "Delete", ToolTipDelay = 1 };
        var (form, platform) = CreateForm(Panel(button), delay: 60_000);

        form.OnPointerMove(CenterOf(button));

        Assert.NotNull(WaitForToolTip(form, platform));
    }

    [Fact]
    public void TheKeyboardFocusShowsItsToolTipUnderTheElement()
    {
        var first = new Button { Text = "First" };
        var second = new Button { Text = "X", ToolTip = "Delete" };
        var (form, platform) = CreateForm(Panel(first, second));

        first.Focus();
        HeadlessInput.PressKey(form, Key.Tab);
        Assert.Same(second, form.FocusedElement);

        ToolTip? tip = WaitForToolTip(form, platform);
        Assert.NotNull(tip);
        Assert.Equal("Delete", tip!.Text);

        // under the element, not at a pointer that never came
        Point at = second.GetAbsolutePosition();
        Assert.True(tip.Position.Y >= at.Y + second.ActualSize.Height, $"{tip.Position.Y} is not under {at.Y + second.ActualSize.Height}");

        // focus moves on: the tooltip goes with it
        HeadlessInput.PressKey(form, Key.Tab);
        Assert.Null(Shown(form));
    }

    [Fact]
    public void AKeyClosesTheToolTip()
    {
        var button = new Button { Text = "X", ToolTip = "Delete" };
        var (form, platform) = CreateForm(Panel(button));

        Hover(form, platform, button);

        HeadlessInput.PressKey(form, Key.Escape);

        Assert.Null(Shown(form));
    }

    [Fact]
    public void AModifierAloneLeavesTheToolTip()
    {
        var button = new Button { Text = "X", ToolTip = "Delete" };
        var (form, platform) = CreateForm(Panel(button));

        Hover(form, platform, button);

        form.OnKeyDown(Key.LeftControl, KeyModifiers.Control, false);
        form.OnKeyUp(Key.LeftControl, KeyModifiers.None);
        Keyboard.Reset();

        Assert.NotNull(Shown(form));
    }

    [Fact]
    public void TheToolTipGoesWithItsElement()
    {
        var button = new Button { Text = "X", ToolTip = "Delete" };
        var panel = Panel(button);
        var (form, platform) = CreateForm(panel);

        Hover(form, platform, button);

        panel.Children.Remove(button);

        Assert.Null(Shown(form));
    }

    [Fact]
    public void TheToolTipFadesIn()
    {
        Assert.SkipUnless(UIElement.TransitionsActive, "transitions are off here");

        var button = new Button { Text = "X", ToolTip = "Delete" };
        var (form, platform) = CreateForm(Panel(button));

        ToolTip tip = Hover(form, platform, button);

        Assert.NotNull(tip.EnterTransition);
        Assert.True(tip.IsInTransition);
    }
}
