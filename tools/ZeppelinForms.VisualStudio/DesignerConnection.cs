using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using ZeppelinForms.Design.Protocol;

namespace ZeppelinForms.VisualStudio;

/// <summary>
/// One previewer host process and the pipe to it. The extension creates the pipe and
/// starts <c>dotnet ZeppelinForms.Designer.dll --pipe &lt;name&gt;</c>; the host
/// connects back. Messages from the host arrive on a reader thread.
/// </summary>
internal sealed class DesignerConnection : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    private readonly NamedPipeServerStream _pipe;
    private readonly Process _process;
    private readonly DesignerChannel _channel;
    private readonly Action<string> _log;
    private int _disposed;

    private DesignerConnection(NamedPipeServerStream pipe, Process process, Action<string> log)
    {
        _pipe = pipe;
        _process = process;
        _log = log;
        _channel = new DesignerChannel(pipe);
    }

    /// <summary>A message from the host; raised on the reader thread.</summary>
    public event Action<DesignerMessage>? Received;

    /// <summary>The host exited or the pipe broke; raised once, on the reader thread.</summary>
    public event Action? Closed;

    public bool IsAlive => _disposed == 0 && _pipe.IsConnected && !_process.HasExited;

    /// <summary>The host shipped with the extension, in its Designer folder.</summary>
    public static string HostPath =>
        Path.Combine(Path.GetDirectoryName(typeof(DesignerConnection).Assembly.Location)!, "Designer", "ZeppelinForms.Designer.dll");

    /// <summary>Start a host and wait for it to connect. Blocks: call it off the UI thread.</summary>
    public static DesignerConnection Start(Action<string> log)
    {
        string host = HostPath;

        if (!File.Exists(host))
            throw new FileNotFoundException("The previewer's host is missing from the extension.", host);

        string name = "zeppelinforms-preview-" + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var start = new ProcessStartInfo("dotnet", $"\"{host}\" --pipe {name}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(host),
        };

        Process process;

        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            pipe.Dispose();
            throw new InvalidOperationException(
                "The previewer needs the .NET 10 runtime, and 'dotnet' is not on the PATH: " + e.Message, e);
        }

        process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) log("host: " + line); };
        process.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) log("host: " + line); };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        Task connecting = pipe.WaitForConnectionAsync();

        if (!connecting.Wait(ConnectTimeout))
        {
            TryKill(process);
            pipe.Dispose();

            throw new TimeoutException(process.HasExited
                ? $"The previewer's host exited with code {process.ExitCode} before connecting; see the ZeppelinForms output pane."
                : "The previewer's host did not connect in time.");
        }

        var connection = new DesignerConnection(pipe, process, log);
        connection.StartReading();

        log($"Previewer host started, process {process.Id}.");
        return connection;
    }

    private void StartReading()
    {
        var reader = new Thread(() =>
        {
            try
            {
                while (_channel.Receive() is { } message)
                    Received?.Invoke(message);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidDataException or EndOfStreamException)
            {
                if (_disposed == 0) _log("The pipe to the previewer broke: " + e.Message);
            }

            Closed?.Invoke();
        })
        {
            IsBackground = true,
            Name = "ZeppelinForms previewer pipe",
        };

        reader.Start();
    }

    /// <summary>Send a message; a dead host is not an error here — <see cref="Closed"/>
    /// reports it once.</summary>
    public void Send(DesignerMessage message)
    {
        if (_disposed != 0) return;

        try
        {
            _channel.Send(message);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try
        {
            _channel.Send(new ShutdownMessage());
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }

        if (!_process.WaitForExit(1500))
            TryKill(_process);

        _pipe.Dispose();
        _process.Dispose();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}