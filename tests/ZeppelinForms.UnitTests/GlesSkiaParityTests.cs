using System.Runtime.InteropServices;
using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Gles;
using ZeppelinForms.Headless;
using ZeppelinForms.Linux;
using ZeppelinForms.Skia;
using ZeppelinForms.UnitTests.Snapshots;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The GLES backend against Skia: the same form drawn by both, the pictures compared. Two
/// renderers that draw a button differently are a bug of one of them — and the GLES one,
/// a preview, is the one nobody looks at while Skia draws every window.
/// </summary>
/// <remarks>
/// <para>
/// GLES draws on a GPU, so on Linux the test makes an OpenGL ES 3 context of Mesa's without
/// a window or a display (<c>EGL_MESA_platform_surfaceless</c>): llvmpipe draws it on the
/// CPU, in CI as on a desktop. A machine without Mesa's EGL skips the tests; CI sets
/// <c>ZF_REQUIRE_GLES=true</c> to make that a failure.
/// </para>
/// <para>
/// The two can't match to the pixel: their antialiasing differs on the edges, and text is
/// rasterized by FreeType in Skia and by the backend's own rasterizer — the same letters at
/// the same places, but each glyph's pixels its own. Shapes and pictures are compared pixel
/// by pixel: few may differ visibly, the edges only. Text, and the controls that carry it,
/// are compared in blocks of 8 × 8 pixels: a word in another place, of another size or
/// color, or missing, still shows; the rasterizers' differences inside a glyph don't. The
/// pictures of a failure go to Snapshots/Failed, which CI uploads.
/// </para>
/// <para>
/// Known differences, left out on purpose: a stretched picture is sampled by the nearest
/// pixel in Skia (<c>SKSamplingOptions.Default</c>) and smoothly in GLES; GLES's text is a
/// little heavier.
/// </para>
/// </remarks>
[Collection("Platform")]
public sealed class GlesSkiaParityTests : IClassFixture<SnapshotFixture>, IClassFixture<GlesSkiaParityTests.HeadlessGlContext>
{
    /// <summary>A channel differing by more than this is a visible difference.</summary>
    private const int VisibleDifference = 40;

    /// <summary>The same for the mean of a block: text heavier in one renderer moves a
    /// block's mean by up to about 36.</summary>
    private const int VisibleBlockDifference = 48;

    /// <summary>The side of the blocks text is compared in.</summary>
    private const int TextBlock = 8;

    /// <summary>A font both renderers read from the same file: the system's would be
    /// whatever the machine has, and not the same one for both.</summary>
    private static readonly Font Inter =
        new Font("Inter", 14).WithFile(Path.Combine(AppContext.BaseDirectory, "Fonts", "Inter-Regular.ttf"));

    private readonly HeadlessGlContext _gl;

    public GlesSkiaParityTests(SnapshotFixture _, HeadlessGlContext gl) => _gl = gl;

    // ===== the scenes =====

    /// <summary>An element that draws what it is given: the primitives without a control.
    /// It covers the form, so the coordinates are the form's.</summary>
    private sealed class Scene : DecoratedControl
    {
        private readonly Action<Graphics> _draw;

        public Scene(Action<Graphics> draw)
        {
            _draw = draw;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
        }

        protected override void DrawContent(Graphics g) => _draw(g);
    }

    private static readonly Color Ink = new(255, 0x1F, 0x2A, 0x44);
    private static readonly Color Accent = new(255, 0x25, 0x63, 0xEB);
    private static readonly Color Warm = new(255, 0xF5, 0x9E, 0x0B);
    private static readonly Color Red = new(255, 0xDC, 0x26, 0x26);
    private static readonly Color Green = new(200, 0x16, 0xA3, 0x4A);

    private static Form Form(UIElement content, int width, int height)
    {
        // the form's font, not Font.Default: the tests of other classes run beside these
        var form = new Form { Size = new Size(width, height), Content = content, Background = Colors.White, Font = Inter };
        new HeadlessPlatform(registerServices: false).CreateWindow(form);
        form.UpdateLayout();
        return form;
    }

    [Fact]
    public void FillsAndStrokesMatch()
    {
        Form form = Form(new Scene(g =>
        {
            g.FillRectangle(new Rectangle(10, 10, 80, 50), Accent);
            g.DrawRectangle(new Rectangle(100.5f, 10.5f, 80, 50), Ink, 1f);
            g.FillRoundRectangle(new Rectangle(190, 10, 90, 50), new CornerRadius(12), Warm);
            g.DrawRoundRectangle(new Rectangle(290, 10, 90, 50), new CornerRadius(8, 20, 8, 20), Red, 3f);

            g.FillEllipse(new Rectangle(10, 80, 80, 80), Green);
            g.DrawEllipse(new Rectangle(100, 80, 80, 80), Accent, 4f);
            g.FillPie(new Rectangle(190, 80, 80, 80), -90f, 250f, Red);
            g.DrawArc(new Rectangle(290, 80, 80, 80), 30f, 240f, Ink, 6f);

            g.DrawLine(new Point(10, 180), new Point(380, 200), Ink, 2f);
            g.DrawPolyline([new Point(10, 230), new Point(60, 210), new Point(110, 250), new Point(160, 215)], Accent, 3f);
            g.FillPolygon([new Point(200, 210), new Point(260, 210), new Point(280, 250), new Point(180, 250)], Warm);
            g.DrawBezier(new Point(290, 250), new Point(310, 190), new Point(360, 270), new Point(380, 210), Red, 2.5f);
        }), 400, 270);

        AssertParity(form, "shapes", maxVisibleRatio: 0.01f);
    }

    [Fact]
    public void PathsGradientsAndTransformsMatch()
    {
        Form form = Form(new Scene(g =>
        {
            // an icon's path, filled and stroked
            g.DrawSvgPath("M12 3 L14.8 8.8 L21 9.6 L16.5 14 L17.6 20.3 L12 17.3 L6.4 20.3 L7.5 14 L3 9.6 L9.2 8.8 Z",
                new Rectangle(10, 10, 72, 72), Warm);
            g.DrawSvgPath("M3 11 L12 3 L21 11 M5 9 V21 H19 V9 M10 21 V15 H14 V21",
                new Rectangle(100, 10, 72, 72), Ink, 2f);

            g.FillGradient(new Rectangle(190, 10, 190, 72), new CornerRadius(10),
                [new GradientStop(Accent, 0f), new GradientStop(Warm, 1f)], 90f);
            g.FillGradient(new Rectangle(10, 100, 170, 60), new CornerRadius(0),
                [new GradientStop(Red, 0f), new GradientStop(Green, 0.5f), new GradientStop(Accent, 1f)], 30f);

            // a rotated, scaled square, and a clip
            g.Save();
            g.Translate(280, 130);
            g.Rotate(30f);
            g.Scale(1.2f, 0.8f);
            g.FillRectangle(new Rectangle(-30, -30, 60, 60), Accent);
            g.Restore();

            g.Save();
            g.ClipRoundRect(new Rectangle(10, 180, 170, 70), new CornerRadius(30));
            g.FillRectangle(new Rectangle(0, 170, 200, 100), Ink);
            g.FillEllipse(new Rectangle(120, 160, 90, 90), Warm);
            g.Restore();

            // half transparent over the rest
            g.SaveLayer(0.5f);
            g.FillRectangle(new Rectangle(200, 180, 180, 70), Red);
            g.Restore();
        }), 400, 270);

        AssertParity(form, "paths", maxVisibleRatio: 0.015f);
    }

    [Fact]
    public void PicturesMatch()
    {
        // a picture whose every pixel is its own: red grows to the right, green down. A
        // mirrored or upside-down picture shows, and so do swapped channels. At its own size:
        // a stretched one is sampled differently (see the remarks)
        const int side = 16;
        byte[] pixels = new byte[side * side * 4];

        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                int i = (y * side + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = ((byte)(x * 16), (byte)(y * 16), (byte)160, (byte)255);
            }
        }

        var picture = new Image(side, side, pixels);

        Form form = Form(new Scene(g =>
        {
            g.DrawImage(new Rectangle(10, 10, side, side), picture);
            g.DrawImage(new Rectangle(36, 10, side, side), picture, ImageFlip.Horizontal);
            g.DrawImage(new Rectangle(62, 10, side, side), picture, ImageFlip.Vertical);

            g.SaveLayer(0.5f);
            g.DrawImage(new Rectangle(88, 10, side, side), picture);
            g.Restore();
        }), 120, 40);

        AssertParity(form, "pictures", maxVisibleRatio: 0.002f);
    }

    [Fact]
    public void TextMatches()
    {
        Font big = Inter.WithSize(24);

        Form form = Form(new Scene(g =>
        {
            g.DrawText("Parity of the renderers", new Point(10, 24), Ink, Inter);
            g.DrawText("Remember me, Notifications", new Point(10, 50), Accent, Inter);
            g.DrawText("Typed text 0123456789", new Point(10, 90), Ink, big);
            g.DrawText("Centered", new Rectangle(10, 100, 300, 30), Red, Inter);
        }), 320, 140);

        AssertParity(form, "text", maxVisibleRatio: 0.01f, block: TextBlock, checkInk: true);
    }

    [Fact]
    public void ControlsMatch()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            Padding = new Thickness(12),
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };

        panel.Children.Add(new Label { Text = "Parity of the renderers" });
        panel.Children.Add(Buttons.Primary("Save"));
        panel.Children.Add(new Button { Text = "Cancel" });
        panel.Children.Add(new CheckBox { Text = "Remember me", IsChecked = true });
        panel.Children.Add(new ToggleSwitch { Text = "Notifications", IsOn = true });
        panel.Children.Add(new TrackBar { Value = 40 });
        panel.Children.Add(new ProgressBar { Value = 65 });
        panel.Children.Add(new TextBox { Text = "Typed text" });

        Form form = Form(panel, 300, 380);

        // the text: rasterized by two different rasterizers
        AssertParity(form, "controls", maxVisibleRatio: 0.01f, block: TextBlock, checkInk: true);
    }

    // ===== the comparison =====

    /// <param name="block">1 — pixel by pixel; more — the means of blocks of that side.</param>
    /// <param name="checkInk">Also compare how much is drawn on the white: a text drawn twice,
    /// or not at all, in a block that holds little else.</param>
    private void AssertParity(Form form, string name, float maxVisibleRatio, int block = 1, bool checkInk = false)
    {
        int width = (int)MathF.Ceiling(form.ClientSize.Width);
        int height = (int)MathF.Ceiling(form.ClientSize.Height);

        Image skia = new SkiaOffscreenRenderer().RenderForm(form, width, height);
        Image gles = _gl.Render(width, height, g => FormRenderer.Render(form, g));

        Assert.Equal(skia.Width, gles.Width);
        Assert.Equal(skia.Height, gles.Height);

        int threshold = block == 1 ? VisibleDifference : VisibleBlockDifference;
        (int visible, int total, double mean) = Compare(skia, gles, block, threshold);
        float ratio = visible / (float)total;

        string? problem = null;

        if (ratio > maxVisibleRatio)
        {
            problem = $"{visible} of {total} {(block == 1 ? "pixels" : $"blocks of {block}×{block}")} ({ratio:P2}, " +
                      $"at most {maxVisibleRatio:P2}) differ by more than {threshold}; the mean difference is {mean:0.00}";
        }
        else if (checkInk)
        {
            double inkRatio = InkOnPage(gles) / Math.Max(1.0, InkOnPage(skia));

            // GLES's text is heavier by about a quarter: half or twice as much is another text
            if (inkRatio is < 0.6 or > 1.6)
                problem = $"GLES puts {inkRatio:0.00} times as much ink on the page as Skia";
        }

        if (problem is null) return;

        SaveFailure(name, skia, gles);
        Assert.Fail($"GLES draws '{name}' unlike Skia: {problem}. The pictures are in Snapshots/Failed/parity-{name}.*.png.");
    }

    /// <summary>The pixels — or blocks — differing visibly, how many there are, and the
    /// mean difference of a channel.</summary>
    private static (int Visible, int Total, double Mean) Compare(Image a, Image b, int block, int threshold)
    {
        int columns = a.Width / block;
        int rows = a.Height / block;
        int visible = 0;
        double sum = 0;

        Span<int> channels = stackalloc int[4];

        for (int by = 0; by < rows; by++)
        {
            for (int bx = 0; bx < columns; bx++)
            {
                channels.Clear();

                for (int y = by * block; y < (by + 1) * block; y++)
                {
                    for (int x = bx * block; x < (bx + 1) * block; x++)
                    {
                        int i = (y * a.Width + x) * 4;

                        for (int c = 0; c < 4; c++)
                            channels[c] += a.Pixels[i + c] - b.Pixels[i + c];
                    }
                }

                int worst = 0;

                for (int c = 0; c < 4; c++)
                {
                    int difference = Math.Abs(channels[c]) / (block * block);
                    sum += difference;
                    worst = Math.Max(worst, difference);
                }

                if (worst > threshold) visible++;
            }
        }

        int total = Math.Max(1, columns * rows);
        return (visible, total, sum / (total * 4.0));
    }

    /// <summary>How far the picture is from white, summed: the ink on the page.</summary>
    private static double InkOnPage(Image image)
    {
        double ink = 0;

        for (int i = 0; i < image.Pixels.Length; i += 4)
            ink += 765 - image.Pixels[i] - image.Pixels[i + 1] - image.Pixels[i + 2];

        return ink;
    }

    private static void SaveFailure(string name, Image skia, Image gles)
    {
        string directory = Path.Combine(
            Path.GetDirectoryName(typeof(GlesSkiaParityTests).Assembly.Location)!,
            "..", "..", "..", "Snapshots", "Failed");

        // bin/Release/net10.0 → the project's folder, as the snapshots' failures go
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        byte[] diff = new byte[skia.Pixels.Length];

        for (int i = 0; i < diff.Length; i += 4)
        {
            int worst = 0;
            for (int c = 0; c < 4; c++) worst = Math.Max(worst, Math.Abs(skia.Pixels[i + c] - gles.Pixels[i + c]));

            // red where the difference is visible, grey where it is only an edge's
            (diff[i], diff[i + 1], diff[i + 2], diff[i + 3]) = worst > VisibleDifference
                ? ((byte)255, (byte)0, (byte)0, (byte)255)
                : ((byte)(255 - worst * 3), (byte)(255 - worst * 3), (byte)(255 - worst * 3), (byte)255);
        }

        SkiaOffscreenRenderer.SavePng(skia, Path.Combine(directory, $"parity-{name}.skia.png"));
        SkiaOffscreenRenderer.SavePng(gles, Path.Combine(directory, $"parity-{name}.gles.png"));
        SkiaOffscreenRenderer.SavePng(new Image(skia.Width, skia.Height, diff), Path.Combine(directory, $"parity-{name}.diff.png"));
    }

    // ===== a GL context without a window =====

    /// <summary>
    /// An OpenGL ES 3 context of Mesa's on the surfaceless platform: no X server, no
    /// window, no pbuffer — the GLES backend draws a picture into a framebuffer of its own.
    /// One for the class, made current on the thread that draws.
    /// </summary>
    public sealed class HeadlessGlContext : IDisposable
    {
        private const uint EGL_PLATFORM_SURFACELESS_MESA = 0x31DD;
        private const int EGL_PBUFFER_BIT = 0x0001;

        private readonly nint _display;
        private readonly nint _context;
        private readonly IGlSurface? _surface;

        /// <summary>Why there is no context: null when there is one.</summary>
        public string? Unavailable { get; }

        public HeadlessGlContext()
        {
            if (!OperatingSystem.IsLinux())
            {
                Unavailable = "the headless GL context is Mesa's, on Linux";
                return;
            }

            try
            {
                if (!Egl.ClientExtensions().Contains("EGL_MESA_platform_surfaceless", StringComparison.Ordinal))
                {
                    Unavailable = "EGL has no surfaceless platform (EGL_MESA_platform_surfaceless)";
                    return;
                }

                _display = GetSurfacelessDisplay();

                if (_display == 0 || Egl.eglInitialize(_display, out _, out _) == 0)
                {
                    Unavailable = $"EGL's surfaceless display didn't initialize ({Egl.LastError()})";
                    return;
                }

                if (Egl.eglBindAPI(Egl.EGL_OPENGL_ES_API) == 0)
                {
                    Unavailable = $"EGL has no OpenGL ES ({Egl.LastError()})";
                    return;
                }

                int[] attribs =
                [
                    Egl.EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
                    Egl.EGL_RENDERABLE_TYPE, Egl.EGL_OPENGL_ES3_BIT,
                    Egl.EGL_RED_SIZE, 8,
                    Egl.EGL_GREEN_SIZE, 8,
                    Egl.EGL_BLUE_SIZE, 8,
                    Egl.EGL_ALPHA_SIZE, 8,
                    Egl.EGL_NONE,
                ];

                var configs = new nint[1];

                if (Egl.eglChooseConfig(_display, attribs, configs, 1, out int count) == 0 || count == 0)
                {
                    Unavailable = "EGL has no OpenGL ES 3 config";
                    return;
                }

                _context = Egl.eglCreateContext(_display, configs[0], 0, [Egl.EGL_CONTEXT_CLIENT_VERSION, 3, Egl.EGL_NONE]);

                if (_context == 0 || !MakeCurrent())
                {
                    Unavailable = $"EGL made no OpenGL ES 3 context current ({Egl.LastError()})";
                    return;
                }

                // the window's framebuffer is never drawn: the pictures have their own
                _surface = GlesGraphicsBackend.Instance.CreateGlSurface(new GlTarget
                {
                    Width = 1,
                    Height = 1,
                    IsGles = true,
                    GetProcAddress = EglDisplay.GetProcAddress,
                    MakeCurrent = () => MakeCurrent(),
                });

                Release();
            }
            catch (DllNotFoundException)
            {
                Unavailable = "there is no libEGL";
            }
        }

        /// <summary>eglGetPlatformDisplayEXT: an extension's function, found by name.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint GetPlatformDisplay(uint platform, nint nativeDisplay, nint attributes);

        // a delegate rather than a function pointer: the test project compiles no unsafe code
        private static nint GetSurfacelessDisplay()
        {
            nint address = Egl.eglGetProcAddress("eglGetPlatformDisplayEXT");
            if (address == 0) return 0;

            var get = Marshal.GetDelegateForFunctionPointer<GetPlatformDisplay>(address);
            return get(EGL_PLATFORM_SURFACELESS_MESA, 0, 0);
        }

        private bool MakeCurrent() => Egl.eglMakeCurrent(_display, 0, 0, _context) != 0;

        private void Release() => Egl.eglMakeCurrent(_display, 0, 0, 0);

        /// <summary>A picture drawn by the GLES backend: RGBA, premultiplied, the top row first.</summary>
        public Image Render(int width, int height, Action<Graphics> draw)
        {
            if (Unavailable is not null)
            {
                if (Environment.GetEnvironmentVariable("ZF_REQUIRE_GLES") == "true")
                    Assert.Fail($"No GL context for the GLES backend: {Unavailable}.");

                Assert.Skip($"No GL context for the GLES backend: {Unavailable}.");
            }

            // the tests may run on another thread than the one that made the context
            try
            {
                return ((GlesSurface)_surface!).RenderOffscreen(width, height, draw);
            }
            finally
            {
                Release();
            }
        }

        public void Dispose()
        {
            if (_context == 0) return;

            MakeCurrent();
            _surface?.Dispose();
            Release();

            Egl.eglDestroyContext(_display, _context);
            Egl.eglTerminate(_display);
        }
    }
}
