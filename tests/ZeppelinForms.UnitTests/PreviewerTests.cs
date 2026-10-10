using System.IO.Pipes;
using Xunit;
using ZeppelinForms.Design;
using ZeppelinForms.Design.Protocol;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>Previews used by <see cref="PreviewerTests"/>: the catalog finds them in
/// this assembly, as it would in a project.</summary>
public static class SamplePreviews
{
    [Preview("Primary", Group = "Samples", Width = 240, Height = 80)]
    public static UIElement Primary() => Buttons.Primary("Save");

    [Preview(Group = "Samples", Theme = "Dark", RightToLeft = true)]
    public static UIElement Plain() => new Label { Text = "plain" };

    [Preview("Throws", Group = "Samples")]
    public static UIElement Throws() => throw new InvalidOperationException("the view is broken");

    [Preview("Settings form", Group = "Samples")]
    public static Form SettingsForm() => new SampleForm();

    // not previews: parameters, a wrong return type, no attribute
    [Preview(Group = "Samples")]
    public static UIElement WithParameter(int size) => new Label();

    [Preview(Group = "Samples")]
    public static string NotAView() => "text";

    public static UIElement NoAttribute() => new Label();
}

/// <summary>A form: found without an attribute.</summary>
public class SampleForm : Form
{
    public SampleForm()
    {
        Size = new Size(320, 200);
        Content = new Button { Text = "OK", Size = new Size(100, 30) };
    }
}

[Collection("Platform")]
public class PreviewerTests
{
    /// <summary>Draws the tree like a renderer would, without pixels; counts frames.</summary>
    private sealed class FakeRenderer
    {
        public int Frames { get; private set; }

        public Image Render(Form form, int width, int height, float scale)
        {
            Frames++;

            if (form.Content is not null)
                ElementTreeRenderer.Draw(form.Content, new HeadlessGraphics());

            return new Image(width, height, new byte[width * height * 4]);
        }
    }

    private static Point Center(UIElement element)
    {
        Point at = element.GetAbsolutePosition();
        return new Point(at.X + element.ActualSize.Width / 2f, at.Y + element.ActualSize.Height / 2f);
    }

    private static IReadOnlyList<PreviewEntry> Samples() =>
        [.. PreviewCatalog.Discover(typeof(SamplePreviews).Assembly).Where(e => e.Info.Group == "Samples" || e.Info.TypeName == typeof(SampleForm).FullName)];

    private static PreviewEntry Sample(string id) =>
        Samples().Single(e => e.Id == id);

    // ===== catalog =====

    [Fact]
    public void TheCatalogFindsMethodsAndForms()
    {
        string[] ids = [.. Samples().Select(e => e.Id)];

        Assert.Contains("ZeppelinForms.UnitTests.SamplePreviews.Primary", ids);
        Assert.Contains("ZeppelinForms.UnitTests.SamplePreviews.Plain", ids);
        Assert.Contains("ZeppelinForms.UnitTests.SamplePreviews.SettingsForm", ids);
        Assert.Contains("ZeppelinForms.UnitTests.SampleForm", ids);

        Assert.DoesNotContain("ZeppelinForms.UnitTests.SamplePreviews.WithParameter", ids);
        Assert.DoesNotContain("ZeppelinForms.UnitTests.SamplePreviews.NotAView", ids);
        Assert.DoesNotContain("ZeppelinForms.UnitTests.SamplePreviews.NoAttribute", ids);

        PreviewInfo primary = Sample("ZeppelinForms.UnitTests.SamplePreviews.Primary").Info;

        Assert.Equal("Primary", primary.Name);
        Assert.Equal(240f, primary.Defaults.Width);
        Assert.Equal("Primary", primary.MemberName);
        Assert.False(primary.IsForm);

        // without a name — the method's
        Assert.Equal("Plain", Sample("ZeppelinForms.UnitTests.SamplePreviews.Plain").Info.Name);

        Assert.True(Sample("ZeppelinForms.UnitTests.SampleForm").Info.IsForm);
    }

    // ===== session =====

    [Fact]
    public void ASessionSizesDrawsAndRedrawsOnDemand()
    {
        var renderer = new FakeRenderer();
        using var session = new PreviewSession(renderer.Render, new HeadlessPlatform());

        session.Open(Sample("ZeppelinForms.UnitTests.SamplePreviews.Primary"), new PreviewSettings { Scale = 2f });

        // the preview's own size, at the IDE's scale
        FrameMessage frame = session.Render()!;
        Assert.Equal(480, frame.Width);
        Assert.Equal(160, frame.Height);
        Assert.Equal(480 * 160 * 4, frame.Pixels.Length);

        Assert.False(session.NeedsFrame);

        // hovering the button repaints it: a frame is needed again. A form centers
        // an element smaller than itself, so the button is in the middle
        Point center = Center(session.Form!.Content!);
        session.Pointer(new PointerMessage(PointerAction.Move, center.X, center.Y));
        Assert.True(session.NeedsFrame);

        // the IDE's size beats the preview's own
        session.Apply(new PreviewSettings { Width = 300, Height = 100, Scale = 1f });
        Assert.Equal(new Size(300, 100), session.Form!.ClientSize);
    }

    [Fact]
    public void FormsKeepTheirSizeAndElementsGetTheDefault()
    {
        var renderer = new FakeRenderer();
        using var session = new PreviewSession(renderer.Render, new HeadlessPlatform());

        session.Open(Sample("ZeppelinForms.UnitTests.SampleForm"), new PreviewSettings());
        Assert.Equal(new Size(320, 200), session.Form!.ClientSize);

        session.Open(Sample("ZeppelinForms.UnitTests.SamplePreviews.Plain"), new PreviewSettings { Theme = "Light" });
        Assert.Equal(PreviewSession.DefaultElementSize, session.Form!.ClientSize);
        Assert.True(session.Form.Content!.IsRightToLeft);
    }

    [Fact]
    public void KeysAndTextReachTheFocusedField()
    {
        var renderer = new FakeRenderer();
        using var session = new PreviewSession(renderer.Render, new HeadlessPlatform());

        var entry = new PreviewEntry(
            new PreviewInfo("test.box", "box", "Samples", "test", "box", isForm: false),
            () => new TextBox { Size = new Size(200, 30) });

        session.Open(entry, new PreviewSettings());

        Point center = Center(session.Form!.Content!);
        session.Pointer(new PointerMessage(PointerAction.Down, center.X, center.Y));
        session.Pointer(new PointerMessage(PointerAction.Up, center.X, center.Y));
        session.Text("hey");

        // by name, as a Rider plugin sends it
        Assert.True(session.Key(new KeyMessage(true, "Backspace")));
        Assert.True(session.Key(new KeyMessage(false, "Backspace")));

        // by the virtual-key code, as Visual Studio sends it: 0x08 is Backspace too
        Assert.True(session.Key(new KeyMessage(true, string.Empty) { Code = 0x08 }));
        Assert.True(session.Key(new KeyMessage(false, string.Empty) { Code = 0x08 }));

        Assert.False(session.Key(new KeyMessage(true, "NoSuchKey")));

        Assert.Equal("h", ((TextBox)session.Form!.Content!).Text);
    }

    // ===== the whole conversation =====

    [Fact]
    public async Task AnIdeTalksToTheServerOverAPipe()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        string name = $"zf-preview-test-{Guid.NewGuid():N}";
        var renderer = new FakeRenderer();

        using var serverPipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1);
        using var clientPipe = new NamedPipeClientStream(".", name, PipeDirection.InOut);

        Task connected = serverPipe.WaitForConnectionAsync(token);
        await clientPipe.ConnectAsync(5000, token);
        await connected.WaitAsync(TimeSpan.FromSeconds(5), token);

        var server = new PreviewServer(new DesignerChannel(serverPipe), renderer.Render, new HeadlessPlatform());

        var serverThread = new Thread(() =>
        {
            server.Load(typeof(SamplePreviews).Assembly);
            server.Run();
        });

        serverThread.Start();

        var ide = new DesignerChannel(clientPipe);
        string folder = Directory.CreateTempSubdirectory("zf-preview").FullName;

        try
        {
            CatalogMessage catalog = await NextAsync<CatalogMessage>(ide);
            Assert.Null(catalog.Error);
            Assert.Contains(catalog.Previews, p => p.Id == "ZeppelinForms.UnitTests.SamplePreviews.Primary");

            HelloMessage hello = await NextAsync<HelloMessage>(ide);
            Assert.Equal(DesignerProtocol.Version, hello.ProtocolVersion);

            ide.Send(new OpenMessage("ZeppelinForms.UnitTests.SamplePreviews.Primary", new PreviewSettings { Scale = 1.5f }));

            FrameMessage frame = await NextAsync<FrameMessage>(ide);
            Assert.Equal(360, frame.Width);
            Assert.Equal(120, frame.Height);

            // hover over the middle of the 240×80 preview, where the button is:
            // it repaints, and a new frame follows
            ide.Send(new PointerMessage(PointerAction.Move, 120, 40));
            await NextAsync<FrameMessage>(ide);

            // the user's exception comes back as a message, and the server goes on
            ide.Send(new OpenMessage("ZeppelinForms.UnitTests.SamplePreviews.Throws", new PreviewSettings()));
            PreviewErrorMessage error = await NextAsync<PreviewErrorMessage>(ide);
            Assert.Contains("the view is broken", error.Message);

            ide.Send(new OpenMessage("No.Such.Preview", new PreviewSettings()));
            Assert.Contains("No.Such.Preview", (await NextAsync<PreviewErrorMessage>(ide)).Message);

            // a style sheet is checked against the project's controls
            string sheet = Path.Combine(folder, "app.zss");
            File.WriteAllText(sheet, "Button { BackgroundColr: red; }");

            ide.Send(new CheckSheetMessage(sheet));
            SheetDiagnosticsMessage diagnostics = await NextAsync<SheetDiagnosticsMessage>(ide);

            SheetDiagnosticInfo problem = Assert.Single(diagnostics.Items);
            Assert.True(problem.IsError);
            Assert.Equal(1, problem.Line);
            Assert.Equal(10, problem.Column);

            ide.Send(new ShutdownMessage());
            Assert.True(serverThread.Join(5000));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The next message of a kind, skipping frames and logs that may come
    /// in between — the server sends frames whenever the view repaints.</summary>
    /// <remarks>Awaited with a timeout rather than waited on: a blocking wait in a test
    /// can deadlock the runner, and the xUnit analyzers refuse it.</remarks>
    private static async Task<T> NextAsync<T>(DesignerChannel channel) where T : DesignerMessage
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        Task<T> reading = Task.Run(() =>
        {
            while (true)
            {
                DesignerMessage message = channel.Receive() ?? throw new EndOfStreamException("The server closed the pipe.");

                if (message is T wanted) return wanted;
                if (message is PreviewErrorMessage unexpected && typeof(T) != typeof(PreviewErrorMessage))
                    throw new InvalidOperationException(unexpected.Details);
            }
        }, token);

        try
        {
            return await reading.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        catch (TimeoutException timeout)
        {
            throw new TimeoutException($"No {typeof(T).Name} in time.", timeout);
        }
    }

    [Fact]
    public void TheProtocolRoundTripsEveryField()
    {
        using var stream = new MemoryStream();
        var channel = new DesignerChannel(stream);

        var info = new PreviewInfo("a.b", "B", "A", "a", "b", isForm: true)
        {
            Defaults = new PreviewSettings { Width = 1, Height = 2, Scale = 3, Theme = "Dark", Culture = "ar-SA", RightToLeft = true, TextScale = 1.5f },
        };

        channel.Send(new CatalogMessage([info], null, "careful"));
        channel.Send(new PointerMessage(PointerAction.Wheel, 1.5f, 2.5f) { WheelDelta = -120, Button = DesignerMouseButton.Right, Modifiers = DesignerModifiers.Control | DesignerModifiers.Shift });
        channel.Send(new KeyMessage(false, "F5") { Code = 0x74, Modifiers = DesignerModifiers.Alt, IsRepeat = true });
        channel.Send(new FrameMessage(2, 1, 2f, [1, 2, 3, 4, 5, 6, 7, 8]));

        stream.Position = 0;

        var catalog = (CatalogMessage)channel.Receive()!;
        PreviewInfo read = Assert.Single(catalog.Previews);
        Assert.Equal("a.b", read.Id);
        Assert.True(read.IsForm);
        Assert.Equal("ar-SA", read.Defaults.Culture);
        Assert.Equal(1.5f, read.Defaults.TextScale);
        Assert.Null(catalog.Error);
        Assert.Equal("careful", catalog.Warning);

        var pointer = (PointerMessage)channel.Receive()!;
        Assert.Equal(PointerAction.Wheel, pointer.Action);
        Assert.Equal(-120, pointer.WheelDelta);
        Assert.Equal(DesignerModifiers.Control | DesignerModifiers.Shift, pointer.Modifiers);

        var key = (KeyMessage)channel.Receive()!;
        Assert.Equal("F5", key.Key);
        Assert.Equal(0x74, key.Code);
        Assert.True(key.IsRepeat);

        var frame = (FrameMessage)channel.Receive()!;
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, frame.Pixels);

        Assert.Null(channel.Receive());
    }
}