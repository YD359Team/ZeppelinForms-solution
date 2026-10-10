using System.IO.Pipes;
using ZeppelinForms.Design;
using ZeppelinForms.Design.Protocol;
using ZeppelinForms.Headless;
using ZeppelinForms.Skia;

namespace ZeppelinForms.Designer;

/// <summary>
/// The previewer's host process. An IDE extension creates a named pipe and starts
/// this process with its name:
/// <c>dotnet ZeppelinForms.Designer.dll --pipe &lt;name&gt;</c>.
/// The conversation itself is <see cref="DesignerProtocol"/>; the process exits when
/// the IDE says so or closes the pipe.
/// </summary>
/// <remarks>
/// <c>--assembly &lt;path&gt;</c> loads a project at once, without waiting for the
/// IDE's load message — handy for trying the host by hand with a test client.
/// </remarks>
public static class Program
{
    public static int Main(string[] args)
    {
        string? pipe = Argument(args, "--pipe");
        string? assembly = Argument(args, "--assembly");

        if (pipe is null)
        {
            Console.Error.WriteLine("Usage: ZeppelinForms.Designer --pipe <name> [--assembly <path>]");
            return 2;
        }

        // the services a platform would register: real text metrics and image
        // decoding, so the preview is laid out exactly as the application will be
        SkiaTextMeasurer.Register();
        SkiaImageDecoder.Register();
        SkiaOffscreenRenderer.Register();

        var platform = new HeadlessPlatform(registerServices: false);
        var renderer = new SkiaOffscreenRenderer();

        // Asynchronous: the server reads on one thread and writes on another. On
        // Windows the I/O of a pipe opened without it is serialized — a read waiting
        // for the IDE held back every frame and answer until the IDE sent something
        using var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            stream.Connect(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine($"No IDE is listening on the pipe '{pipe}'.");
            return 3;
        }

        var server = new PreviewServer(
            new DesignerChannel(stream),
            (form, width, height, scale) => renderer.RenderForm(form, width, height, scale),
            platform);

        if (assembly is not null)
            server.Load(assembly);

        server.Run();
        return 0;
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}