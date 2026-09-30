using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// Focus shown only for keyboard work, the shared focus ring, and the control
/// geometry and Fluent visuals of 0.13.0. What a control draws is read through
/// a recording Graphics: the strokes and fills it asked for, with their colors.
/// Distinct colors are set on purpose, so that a stroke found is the one looked for.
/// </summary>
[Collection("Platform")]
public class FocusVisualTests
{
    private sealed class RecordingGraphics : HeadlessGraphics
    {
        public List<(Rectangle Rect, Color Color, float Width)> Strokes { get; } = [];
        public List<(Rectangle Rect, Color Color)> Fills { get; } = [];
        public List<Color> Polylines { get; } = [];

        public override void DrawRoundRectangle(Rectangle rect, CornerRadius radius, Color color, float width) =>
            Strokes.Add((rect, color, width));

        public override void FillRoundRectangle(Rectangle rect, CornerRadius radius, Color color) =>
            Fills.Add((rect, color));

        public override void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width) =>
            Polylines.Add(color);

        public bool Stroked(Color color) => Strokes.Exists(s => s.Color == color);
        public bool Filled(Color color) => Fills.Exists(f => f.Color == color);
    }

    private static RecordingGraphics Record(UnitControl control)
    {
        var g = new RecordingGraphics();
        control.Draw(g);
        return g;
    }

    private static Form CreateForm(UIElement content, float width = 200, float height = 60)
    {
        var form = new Form { Size = new Size(width, height), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    // ===== focus visibility =====

    [Fact]
    public void ClickFocusesWithoutShowingFocus()
    {
        var button = new Button { Text = "x", FocusRingColor = Colors.Red };
        Form form = CreateForm(button);

        HeadlessInput.Click(form, 100, 30);

        Assert.True(button.IsFocused);
        Assert.False(button.IsFocusVisible);
        Assert.False(Record(button).Stroked(Colors.Red));
    }

    [Fact]
    public void KeyAfterClickShowsFocus()
    {
        var button = new Button { Text = "x", FocusRingColor = Colors.Red };
        Form form = CreateForm(button);

        HeadlessInput.Click(form, 100, 30);
        HeadlessInput.PressKey(form, Key.Space);

        Assert.True(button.IsFocusVisible);
        Assert.True(Record(button).Stroked(Colors.Red));
    }

    [Fact]
    public void TabShowsFocus()
    {
        var button = new Button { Text = "x" };
        Form form = CreateForm(button);

        HeadlessInput.PressKey(form, Key.Tab);

        Assert.True(button.IsFocused);
        Assert.True(button.IsFocusVisible);
    }

    [Fact]
    public void ModifierAloneKeepsFocusHidden()
    {
        var button = new Button { Text = "x" };
        Form form = CreateForm(button);

        HeadlessInput.Click(form, 100, 30);
        HeadlessInput.PressKey(form, Key.LeftControl);

        Assert.False(button.IsFocusVisible);
    }

    [Fact]
    public void PointerHidesFocusAgain()
    {
        var button = new Button { Text = "x" };
        Form form = CreateForm(button);

        HeadlessInput.PressKey(form, Key.Tab);
        HeadlessInput.Click(form, 100, 30);

        Assert.True(button.IsFocused);
        Assert.False(button.IsFocusVisible);
    }

    [Fact]
    public void TextFieldShowsFocusBorderAfterClick()
    {
        var box = new TextBox { FocusBorderColor = Colors.Red };
        Form form = CreateForm(box);

        HeadlessInput.Click(form, 100, 30);

        Assert.True(box.IsFocused);
        Assert.False(box.IsFocusVisible);

        // where typing goes must be seen, whatever brought focus there
        Assert.True(Record(box).Stroked(Colors.Red));
    }

    [Fact]
    public void OtherControlShowsFocusBorderOnlyWhenVisible()
    {
        var check = new CheckBox { Text = "x", BorderWidth = 1f, FocusBorderColor = Colors.Red };
        Form form = CreateForm(check);

        HeadlessInput.Click(form, 100, 30);
        Assert.False(Record(check).Stroked(Colors.Red));

        HeadlessInput.PressKey(form, Key.Space);
        Assert.True(Record(check).Stroked(Colors.Red));
    }

    // ===== focus ring =====

    [Fact]
    public void InnerStrokeRunsInsideOuter()
    {
        var button = new Button
        {
            Text = "x",
            FocusRingColor = Colors.Red,
            FocusRingInnerColor = Colors.Blue,
            FocusRingThickness = 2f,
            FocusRingInnerThickness = 1f,
            FocusRingInset = 1f,
        };

        Form form = CreateForm(button);
        HeadlessInput.PressKey(form, Key.Tab);

        RecordingGraphics g = Record(button);

        (Rectangle outer, _, float outerWidth) = g.Strokes.Find(s => s.Color == Colors.Red);
        (Rectangle inner, _, float innerWidth) = g.Strokes.Find(s => s.Color == Colors.Blue);

        Assert.Equal(2f, outerWidth);
        Assert.Equal(1f, innerWidth);

        // center lines half of both thicknesses apart: the strokes touch
        Assert.Equal(outer.X + 1.5f, inner.X);
        Assert.Equal(outer.Width - 3f, inner.Width);

        // the inset puts the outer stroke flush with the bounds
        Assert.Equal(1f, outer.X);
    }

    [Fact]
    public void CheckBoxRingFallsBackToCheckColor()
    {
        var check = new CheckBox { Text = "x", CheckColor = Colors.Green };
        Form form = CreateForm(check);

        HeadlessInput.PressKey(form, Key.Tab);

        Assert.True(check.IsFocusVisible);

        // the box is stroked with its border color unchecked; the only
        // green stroke is the ring at the check box's own 1.5 px
        Assert.Contains(Record(check).Strokes, s => s.Color == Colors.Green && s.Width == 1.5f);
    }

    [Fact]
    public void RingAroundIndicatorIsInDirtyBounds()
    {
        var check = new CheckBox { Text = "x" };
        var button = new Button { Text = "x" };

        CreateForm(check);
        CreateForm(button);

        // both stand at the origin; the check box's ring sticks out past the
        // left edge, beyond the margin every element gets for antialiasing
        Assert.True(check.DirtyBounds.X < button.DirtyBounds.X);
    }

    // ===== Fluent visuals =====

    [Fact]
    public void ElevationEdgeIsFlatWhenPressed()
    {
        var button = new Button { Text = "x", ElevationBorderColor = Colors.Magenta };
        Form form = CreateForm(button);

        Assert.True(Record(button).Stroked(Colors.Magenta));

        form.OnPointerDown(new Point(100, 30));
        Assert.False(Record(button).Stroked(Colors.Magenta));

        form.OnPointerUp(new Point(100, 30));
        Assert.True(Record(button).Stroked(Colors.Magenta));
    }

    [Fact]
    public void FocusUnderlineFollowsFocus()
    {
        var box = new TextBox { UnderlineColor = Colors.Blue, FocusUnderlineColor = Colors.Red };
        Form form = CreateForm(box);

        RecordingGraphics rest = Record(box);
        Assert.True(rest.Filled(Colors.Blue));
        Assert.False(rest.Filled(Colors.Red));

        HeadlessInput.Click(form, 100, 30);

        RecordingGraphics focused = Record(box);
        Assert.True(focused.Filled(Colors.Red));
        Assert.False(focused.Filled(Colors.Blue));
    }

    [Fact]
    public void ValidationErrorKeepsBorderAndUnderline()
    {
        var box = new TextBox
        {
            ErrorColor = Colors.Red,
            UnderlineColor = Colors.Blue,
            Validator = _ => "error",
        };

        CreateForm(box);
        box.Validate();

        RecordingGraphics g = Record(box);

        // the border says it on its own, the underline repeats it rather than argue
        Assert.True(g.Stroked(Colors.Red));
        Assert.True(g.Filled(Colors.Red));
        Assert.False(g.Filled(Colors.Blue));
    }

    [Fact]
    public void CheckGlyphTakesItsColor()
    {
        var check = new CheckBox { IsChecked = true, CheckGlyphColor = Colors.Black };
        CreateForm(check);

        Assert.Contains(Colors.Black, Record(check).Polylines);
    }

    // ===== geometry =====

    [Fact]
    public void BoxSizeChangesMeasure()
    {
        var check = new CheckBox
        {
            BoxSize = 30f,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        CreateForm(check);

        Assert.Equal(new Size(30f, 30f), check.DesiredSize);
    }

    [Fact]
    public void CircleSizeChangesMeasure()
    {
        var radio = new RadioButton
        {
            CircleSize = 24f,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        CreateForm(radio);

        Assert.Equal(new Size(24f, 24f), radio.DesiredSize);
    }

    [Fact]
    public void TrackSizeChangesMeasure()
    {
        var toggle = new ToggleSwitch
        {
            TrackSize = new Size(50f, 26f),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        Form form = CreateForm(toggle);
        Assert.Equal(new Size(50f, 26f), toggle.DesiredSize);

        // AffectsLayout: a change after the first layout is measured again
        toggle.TrackSize = new Size(44f, 22f);
        form.UpdateLayout();

        Assert.Equal(new Size(44f, 22f), toggle.DesiredSize);
    }
}