using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

/// <summary>0.16: a held Fluent button says so with its text, and a border is drawn
/// inside its box — one side of a frame no longer looks thinner than the others.</summary>
[Collection("Platform")]
public class ButtonPressAndBorderTests
{
    private sealed class RecordingGraphics : HeadlessGraphics
    {
        public List<Color> TextColors { get; } = [];
        public List<(Rectangle Rect, CornerRadius Radius, float Width)> Strokes { get; } = [];

        public override void DrawText(string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center) =>
            TextColors.Add(color);

        public override void DrawRoundRectangle(Rectangle rect, CornerRadius radius, Color color, float width) =>
            Strokes.Add((rect, radius, width));
    }

    private static void WithTheme(Theme theme, Action test)
    {
        Theme before = App.RequestedTheme;

        try
        {
            App.Theme = theme;
            test();
        }
        finally
        {
            App.Theme = before;
        }
    }

    private static (Form Form, Button Button) Create()
    {
        var button = new Button
        {
            Text = "Save",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var form = new Form { Size = new Size(200, 80), Content = button };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, button);
    }

    private static Point Middle(UIElement element)
    {
        Point at = element.GetAbsolutePosition();
        return new Point(at.X + element.ActualSize.Width / 2f, at.Y + element.ActualSize.Height / 2f);
    }

    private static Color TextColorOf(Button button)
    {
        var g = new RecordingGraphics();
        button.Draw(g);
        return g.TextColors.Single();
    }

    [Fact]
    public void AHeldFluentButtonDimsItsText()
    {
        WithTheme(Themes.FluentLight, () =>
        {
            var (form, button) = Create();
            Point middle = Middle(button);

            form.OnPointerMove(middle);
            Color hovered = TextColorOf(button);

            form.OnPointerDown(middle, MouseButton.Left);
            Color pressed = TextColorOf(button);

            form.OnPointerUp(middle, MouseButton.Left);

            Assert.Equal(Themes.FluentLight.Colors.Text, hovered);
            Assert.Equal(Themes.FluentLight.Colors.TextSecondary, pressed);
            Assert.Equal(hovered, TextColorOf(button));
        });
    }

    [Fact]
    public void ATextColorChosenInCodeStaysWhenHeld()
    {
        WithTheme(Themes.FluentLight, () =>
        {
            var (form, button) = Create();
            button.TextColor = Colors.Red;

            form.OnPointerDown(Middle(button), MouseButton.Left);

            Assert.Equal(Colors.Red, TextColorOf(button));
        });
    }

    [Fact]
    public void TheBorderIsDrawnInsideTheBox()
    {
        var border = new Border
        {
            BorderWidth = 2,
            BorderColor = Colors.Blue,
            CornerRadius = new CornerRadius(8),
            Size = new Size(100, 40),
        };

        var form = new Form { Size = new Size(200, 80), Content = border };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        var g = new RecordingGraphics();
        border.DrawOverlay(g);

        // the middle of a 2 px line runs 1 px inside: the line ends at the box's edge,
        // where a parent cutting its children at its own edge doesn't reach it
        (Rectangle rect, CornerRadius radius, float width) = g.Strokes.Single();

        Assert.Equal(new Rectangle(new Point(1, 1), new Size(98, 38)), rect);
        Assert.Equal(new CornerRadius(7), radius);
        Assert.Equal(2f, width);
    }
}
