using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using ZeppelinForms.Design.Protocol;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;

namespace ZeppelinForms.Design;

/// <summary>
/// The previewer's host loop: answers an IDE over a <see cref="DesignerChannel"/>.
/// </summary>
/// <remarks>
/// <para>
/// The thread that calls <see cref="Run"/> is the previewed view's UI thread. Messages
/// are read on a thread of their own and queued to it, together with whatever the
/// view posts to its synchronization context — await continuations, a style sheet's
/// reload. Between messages the loop ticks animations and sends a frame when the
/// view asked for a repaint, at most one per <see cref="FrameInterval"/>.
/// </para>
/// <para>
/// Errors of the previewed code never end the loop: they go to the IDE as
/// <see cref="PreviewErrorMessage"/>, and the next message is served as usual.
/// </para>
/// </remarks>
public sealed class PreviewServer
{
    /// <summary>The shortest time between two frames: 60 per second, as a display.</summary>
    public static TimeSpan FrameInterval { get; } = TimeSpan.FromMilliseconds(16);

    /// <summary>How long the loop sleeps with nothing to do: often enough for delayed
    /// actions of the view, rarely enough to cost nothing while the IDE sits idle.</summary>
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(50);

    private readonly DesignerChannel _channel;
    private readonly HeadlessPlatform _platform;
    private readonly PreviewSession _session;
    private readonly BlockingCollection<object> _queue = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private IReadOnlyList<PreviewEntry> _entries = [];
    // in the past, so that the first frame is not held back; not MinValue, which
    // overflows the moment it is subtracted from
    private TimeSpan _lastFrame = TimeSpan.FromSeconds(-1);
    private TimeSpan _lastTick = TimeSpan.FromSeconds(-1);
    private bool _running;

    public PreviewServer(DesignerChannel channel, PreviewSession.FrameRenderer render, HeadlessPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _channel = channel;
        _platform = platform;
        _session = new PreviewSession(render, platform);
    }

    /// <summary>The previews of the loaded assembly.</summary>
    public IReadOnlyList<PreviewEntry> Entries => _entries;

    /// <summary>Serve until the IDE sends <see cref="ShutdownMessage"/> or closes the stream.</summary>
    public void Run()
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new LoopContext(_queue));

        var reader = new Thread(ReadMessages) { IsBackground = true, Name = "ZeppelinForms previewer: reader" };
        reader.Start();

        _running = true;

        try
        {
            Send(new HelloMessage(DesignerProtocol.Version, FrameworkVersion));

            while (_running)
            {
                if (_queue.TryTake(out object? item, WaitTime()))
                {
                    if (item is EndOfStream) break;

                    Handle(item);
                }

                Pump();
            }
        }
        finally
        {
            _session.Dispose();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static string FrameworkVersion =>
        typeof(UIElement).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(UIElement).Assembly.GetName().Version?.ToString()
        ?? string.Empty;

    // ===== the loop =====

    private void ReadMessages()
    {
        try
        {
            while (_channel.Receive() is { } message)
                _queue.Add(message);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidDataException)
        {
            // the IDE went away or the stream broke: the loop ends the same way
        }

        _queue.Add(EndOfStream.Instance);
    }

    /// <summary>Until the next frame is due while there is work for frames; idle otherwise.</summary>
    private TimeSpan WaitTime()
    {
        if (!_session.IsAnimating && !_session.NeedsFrame) return IdleWait;

        TimeSpan due = Max(_lastFrame, _lastTick) + FrameInterval - _clock.Elapsed;

        return due > TimeSpan.Zero ? due : TimeSpan.Zero;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>What a platform does between messages: run posted work, deliver
    /// animation frames, draw.</summary>
    private void Pump()
    {
        Guard(() =>
        {
            _platform.PumpAll();

            TimeSpan now = _clock.Elapsed;

            if (_session.IsAnimating && now - _lastTick >= FrameInterval)
            {
                _lastTick = now;
                _session.Tick();
            }

            if (_session.NeedsFrame && now - _lastFrame >= FrameInterval)
                SendFrame();
        });
    }

    private void SendFrame()
    {
        FrameMessage? frame;

        try
        {
            frame = _session.Render();
        }
        catch
        {
            // a view that throws while drawing would otherwise be redrawn — and
            // reported — sixty times a second until something else changes
            _session.SkipFrame();
            throw;
        }

        if (frame is not null)
        {
            _lastFrame = _clock.Elapsed;
            Send(frame);
        }
    }

    // ===== messages =====

    private void Handle(object item)
    {
        switch (item)
        {
            case Action posted:
                Guard(posted);
                break;

            case HelloMessage hello when hello.ProtocolVersion != DesignerProtocol.Version:
                Send(new LogMessage(
                    $"The IDE speaks protocol {hello.ProtocolVersion}, the previewer {DesignerProtocol.Version}: " +
                    "update the extension and the framework together."));
                break;

            case LoadMessage load:
                Load(load.AssemblyPath);
                break;

            case OpenMessage open:
                Open(open);
                break;

            case SettingsMessage settings:
                Guard(() => _session.Apply(settings.Settings));
                break;

            case PointerMessage pointer:
                Guard(() => _session.Pointer(pointer));
                break;

            case KeyMessage key:
                Guard(() => _session.Key(key));
                break;

            case TextMessage text:
                Guard(() => _session.Text(text.Text));
                break;

            case CheckSheetMessage check:
                CheckSheet(check.Path);
                break;

            case ShutdownMessage:
                _running = false;
                break;
        }
    }

    /// <summary>Load the project, run its setup and send the list of previews.</summary>
    public void Load(string assemblyPath)
    {
        Assembly assembly;
        string? warning;

        try
        {
            assembly = PreviewAssemblies.Load(assemblyPath, out warning);
        }
        catch (Exception e)
        {
            Send(new CatalogMessage([], $"The project could not be loaded: {e.Message}", null));
            return;
        }

        Load(assembly, warning);
    }

    /// <summary>Use an assembly already loaded — for tests, and for a host that
    /// links the project in.</summary>
    public void Load(Assembly assembly, string? warning = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (MethodInfo setup in PreviewCatalog.SetupMethods(assembly))
        {
            try
            {
                setup.Invoke(null, null);
            }
            catch (TargetInvocationException e) when (e.InnerException is { } inner)
            {
                warning = Join(warning, $"{setup.DeclaringType?.Name}.{setup.Name} threw: {inner.Message}");
            }
        }

        try
        {
            _entries = PreviewCatalog.Discover(assembly);
        }
        catch (Exception e)
        {
            Send(new CatalogMessage([], $"The previews could not be listed: {e.Message}", warning));
            return;
        }

        Send(new CatalogMessage([.. _entries.Select(e => e.Info)], null, warning));
    }

    private static string Join(string? first, string second) =>
        first is null ? second : first + Environment.NewLine + second;

    private void Open(OpenMessage open)
    {
        PreviewEntry? entry = _entries.FirstOrDefault(e => string.Equals(e.Id, open.PreviewId, StringComparison.Ordinal));

        if (entry is null)
        {
            Send(new PreviewErrorMessage($"There is no preview '{open.PreviewId}' in the project.", string.Empty));
            return;
        }

        if (Guard(() => _session.Open(entry, open.Settings)))
            SendFrame();
    }

    private void CheckSheet(string path)
    {
        StyleSheet sheet = StyleSheet.Load(path);

        Send(new SheetDiagnosticsMessage(path, [.. sheet.Diagnostics.Select(d => new SheetDiagnosticInfo(
            d.Severity == StyleDiagnosticSeverity.Error, d.Message, d.Source, d.Line, d.Column))]));
    }

    /// <summary>Run the previewed code; its exception goes to the IDE. False — it threw.</summary>
    private bool Guard(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e)
        {
            Exception shown = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
            Send(new PreviewErrorMessage($"{shown.GetType().Name}: {shown.Message}", shown.ToString()));
            return false;
        }
    }

    private void Send(DesignerMessage message)
    {
        try
        {
            _channel.Send(message);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // the IDE is gone; the reader thread ends the loop
            _running = false;
        }
    }

    private sealed class EndOfStream
    {
        public static readonly EndOfStream Instance = new();
    }

    /// <summary>The view's synchronization context: posts go into the loop's queue
    /// and run on its thread, between messages.</summary>
    private sealed class LoopContext(BlockingCollection<object> queue) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // the loop may be over: a late continuation has nowhere to run
            if (!queue.IsAddingCompleted) queue.Add(new Action(() => d(state)));
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("The previewer's UI thread doesn't wait for itself; use Post.");

        public override SynchronizationContext CreateCopy() => this;
    }
}