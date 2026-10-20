using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>The color picker's spectrum and its eyedropper: the HSV model, the
/// editor following a value set from elsewhere, and picking a color from the form.</summary>
[Collection("Platform")]
public sealed class ColorPickerSpectrumAndEyedropperTests : IDisposable
{
    /// <summary>The renderer is global: the next test gets the headless one back.</summary>
    public void Dispose() => HeadlessElementRenderer.Register();

    /// <summary>Every capture is one color, opaque.</summary>
    private sealed class SolidRenderer(Color color) : IElementRenderer
    {
        public Image Render(UIElement element, int width, int height)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            byte[] pixels = new byte[w * h * 4];

            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = color.R;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.B;
                pixels[i + 3] = 255;
            }

            return new Image(w, h, pixels);
        }
    }

    private static readonly Color Green = new(0, 200, 0);

    private static (Form Form, ColorPicker Picker) Show(ColorPicker picker)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical };
        column.Children.Add(picker);

        // room for the editor below the picker: a flyout without room covers it
        var form = new Form { Size = new Size(400, 600), Content = column };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, picker);
    }

    private static IEnumerable<UIElement> Descendants(UIElement root)
    {
        yield return root;

        IEnumerable<UIElement> children = root switch
        {
            PanelControl panel => panel.Children,
            WrapControl { Child: { } child } => [child],
            _ => [],
        };

        foreach (UIElement child in children)
            foreach (UIElement descendant in Descendants(child))
                yield return descendant;
    }

    // ===== HSV =====

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(255, 255, 0)]
    [InlineData(18, 52, 86)]
    [InlineData(200, 120, 40)]
    [InlineData(128, 128, 128)]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    public void AColorSurvivesTheWayThroughHSV(byte r, byte g, byte b)
    {
        var color = new Color(r, g, b);

        (float h, float s, float v) = HsvColor.FromColor(color);

        Assert.Equal(color, HsvColor.ToColor(h, s, v, 255));
    }

    [Fact]
    public void TheHueOf360IsRedAgain()
    {
        Assert.Equal(new Color(255, 0, 0), HsvColor.ToColor(360f, 1f, 1f, 255));
    }

    // ===== the editor follows the value =====

    [Fact]
    public void TheOpenEditorsSlidersFollowAValueSetFromCode()
    {
        var (form, picker) = Show(new ColorPicker { ShowSpectrum = true, ShowEyedropper = true });

        Point at = picker.GetAbsolutePosition();
        HeadlessInput.Click(form, at.X + picker.ActualSize.Width / 2f, at.Y + picker.ActualSize.Height / 2f);
        form.UpdateLayout();

        Assert.True(picker.IsDropDownOpen);

        picker.Value = new Color(255, 10, 20, 30);

        float[] sliders = [.. form.Overlays.SelectMany(Descendants).OfType<TrackBar>().Select(t => t.Value)];

        Assert.Equal(new[] { 10f, 20f, 30f }, sliders);
    }

    // ===== the eyedropper =====

    [Fact]
    public void AClickTakesTheColorUnderItAndKeepsTheAlpha()
    {
        var (form, picker) = Show(new ColorPicker { ShowEyedropper = true });
        picker.Value = new Color(128, 0, 0, 0);

        ElementRenderer.Current = new SolidRenderer(Green);
        picker.StartPicking();

        Assert.True(picker.IsPicking);
        Assert.Contains(form.Overlays, o => o is ColorPicker.EyedropperOverlay);

        HeadlessInput.Click(form, 200, 150);

        Assert.False(picker.IsPicking);
        Assert.DoesNotContain(form.Overlays, o => o is ColorPicker.EyedropperOverlay);
        Assert.Equal(new Color(128, Green.R, Green.G, Green.B), picker.Value);
    }

    [Fact]
    public void EscapeCancelsThePicking()
    {
        var (form, picker) = Show(new ColorPicker { ShowEyedropper = true });
        Color before = picker.Value;

        ElementRenderer.Current = new SolidRenderer(Green);
        picker.StartPicking();

        form.OnKeyDown(Key.Escape, KeyModifiers.None, false);

        Assert.False(picker.IsPicking);
        Assert.Equal(before, picker.Value);
    }

    [Fact]
    public void TheRightButtonCancelsThePicking()
    {
        var (form, picker) = Show(new ColorPicker { ShowEyedropper = true });
        Color before = picker.Value;

        ElementRenderer.Current = new SolidRenderer(Green);
        picker.StartPicking();

        HeadlessInput.RightClick(form, 200, 150);

        Assert.False(picker.IsPicking);
        Assert.Equal(before, picker.Value);
    }

    [Fact]
    public void EnterTakesThePointTheArrowsMovedTo()
    {
        var (form, picker) = Show(new ColorPicker { ShowEyedropper = true });

        ElementRenderer.Current = new SolidRenderer(Green);
        picker.StartPicking();

        // no cursor yet: the first arrow starts the point in the middle of the form
        form.OnKeyDown(Key.Right, KeyModifiers.None, false);
        form.OnKeyDown(Key.Enter, KeyModifiers.None, false);

        Assert.False(picker.IsPicking);
        Assert.Equal(Green, picker.Value);
    }

    [Fact]
    public void ATransparentPixelShowsTheFormsBackground()
    {
        // one opaque red pixel, one half-transparent white (premultiplied), one empty
        byte[] pixels =
        [
            255, 0, 0, 255,
            128, 128, 128, 128,
            0, 0, 0, 0,
        ];

        var content = new StackPanel();
        var blue = new Color(0, 0, 255);
        var source = new ColorPicker.EyedropperSource(new Image(3, 1, pixels), content, blue);

        Assert.Equal(new Color(255, 0, 0), source.ColorAt(new Point(0.5f, 0.5f)));
        Assert.Equal(blue, source.ColorAt(new Point(2.5f, 0.5f)));
        Assert.Equal(blue, source.ColorAt(new Point(-5f, 0.5f)));

        // half white over blue
        Color mixed = source.ColorAt(new Point(1.5f, 0.5f));
        Assert.Equal(255, mixed.A);
        Assert.InRange(mixed.R, 126, 130);
        Assert.InRange(mixed.B, 253, 255);
    }
}