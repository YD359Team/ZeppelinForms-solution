using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using ZeppelinForms.Design;
using ZeppelinForms.Design.Protocol;
using ZeppelinForms.Headless;
using ZeppelinForms.Skia;

namespace ZeppelinForms.Designer;

/// <summary>
/// The previewer's host process. An IDE extension listens and starts this process with
/// where to connect: <c>dotnet ZeppelinForms.Designer.dll --pipe &lt;name&gt;</c> — a
/// named pipe, what Visual Studio and VS Code on Windows create — or
/// <c>--socket &lt;path&gt;</c>, a Unix domain socket, what Rider creates everywhere and
/// VS Code on Linux and macOS.
/// The conversation itself is <see cref="DesignerProtocol"/>; the process exits when
/// the IDE says so or closes the connection.
/// </summary>
/// <remarks>
/// <para>
/// Why a socket besides the pipe: on Linux and macOS .NET's named pipe is a socket file
/// in the temporary folder under a name of .NET's own choice, which the JVM and Node
/// would have to guess. A socket at a path the IDE chose needs no guessing; Windows has
/// had them since Windows 10 1803.
/// </para>
/// <para>
/// <c>--assembly &lt;path&gt;</c> loads a project at once, without waiting for the
/// IDE's load message — handy for trying the host by hand with a test client.
/// </para>
/// </remarks>
public static class Program
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public static int Main(string[] args)
    {
        string? pipe = Argument(args, "--pipe");
        string? socket = Argument(args, "--socket");
        string? assembly = Argument(args, "--assembly");

        if ((pipe is null) == (socket is null))
        {
            Console.Error.WriteLine("Usage: ZeppelinForms.Designer (--pipe <name> | --socket <path>) [--assembly <path>]");
            return 2;
        }

        // the services a platform would register: real text metrics and image
        // decoding, so the preview is laid out exactly as the application will be
        SkiaTextMeasurer.Register();
        SkiaImageDecoder.Register();
        SkiaOffscreenRenderer.Register();

        var platform = new HeadlessPlatform(registerServices: false);
        var renderer = new SkiaOffscreenRenderer();

        using Stream? stream = pipe is not null ? ConnectPipe(pipe) : ConnectSocket(socket!);

        if (stream is null) return 3;

        var server = new PreviewServer(
            new DesignerChannel(stream),
            (form, width, height, scale) => renderer.RenderForm(form, width, height, scale),
            platform);

        if (assembly is not null)
            server.Load(assembly);

        server.Run();
        return 0;
    }

    private static NamedPipeClientStream? ConnectPipe(string name)
    {
        // Asynchronous: the server reads on one thread and writes on another. On
        // Windows the I/O of a pipe opened without it is serialized — a read waiting
        // for the IDE held back every frame and answer until the IDE sent something
        var stream = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            stream.Connect(ConnectTimeout);
            return stream;
        }
        catch (TimeoutException)
        {
            stream.Dispose();
            Console.Error.WriteLine($"No IDE is listening on the pipe '{name}'.");
            return null;
        }
    }

    private static NetworkStream? ConnectSocket(string path)
    {
        // the IDE listens before it starts the process, but a slow file system may
        // show the socket's file a moment later: tried again until the timeout
        var waited = Stopwatch.StartNew();

        while (true)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(path));

                // a socket's reads and writes are independent: the reader thread waiting
                // for the IDE doesn't hold back frames, as a synchronous pipe's did
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) when (waited.Elapsed < ConnectTimeout)
            {
                socket.Dispose();
                Thread.Sleep(100);
            }
            catch (SocketException e)
            {
                socket.Dispose();
                Console.Error.WriteLine($"No IDE is listening on the socket '{path}': {e.Message}");
                return null;
            }
        }
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}