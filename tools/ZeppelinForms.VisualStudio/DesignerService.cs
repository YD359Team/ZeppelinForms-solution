using System.IO;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using ZeppelinForms.Design.Protocol;

namespace ZeppelinForms.VisualStudio;

/// <summary>
/// The previewer's host for the project being edited: started when it is needed,
/// started anew after each build of that project and when another project's document
/// becomes active.
/// </summary>
/// <remarks>
/// <para>
/// A new process per build rather than a reload in place: see the host's
/// PreviewAssemblies — the framework's registries would keep every old version of
/// the project's controls. The tool window reopens what it showed, so a rebuild looks
/// like a refresh.
/// </para>
/// <para>
/// Messages from the host are passed on as they come, on the pipe's thread;
/// subscribers move to the UI thread themselves. Frames come often, and only the
/// subscriber knows which of them it can skip.
/// </para>
/// </remarks>
internal sealed class DesignerService : IDisposable
{
    private readonly AsyncPackage _package;
    private readonly DTE2 _dte;
    private readonly OutputLog _log;

    // DTE events are COM objects: without a reference they are collected,
    // and the handlers silently stop being called
    private readonly BuildEvents _buildEvents;
    private readonly WindowEvents _windowEvents;

    private DesignerConnection? _connection;
    private string? _assemblyPath;
    private DateTime _assemblyStamp;
    private int _users;
    private int _generation;

    public DesignerService(AsyncPackage package, DTE2 dte, OutputLog log)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _package = package;
        _dte = dte;
        _log = log;

        _buildEvents = dte.Events.BuildEvents;
        _buildEvents.OnBuildDone += OnBuildDone;

        _windowEvents = dte.Events.WindowEvents;
        _windowEvents.WindowActivated += OnWindowActivated;
    }

    /// <summary>A host started and loaded the project: what was shown should be shown again.</summary>
    public event Action? HostStarted;

    /// <summary>The host could not be started or has gone; the text says why.</summary>
    public event Action<string>? HostFailed;

    /// <summary>A document became active: the preview window shows its previews.
    /// Raised on the UI thread with the document's path.</summary>
    public event Action<string>? DocumentActivated;

    /// <summary>Any message of the host, on the pipe's thread.</summary>
    public event Action<DesignerMessage>? Received;

    /// <summary>The project's assembly the host has loaded; null — none.</summary>
    public string? AssemblyPath => _assemblyPath;

    /// <summary>The source file of the active document, for choosing its previews.</summary>
    public string? ActiveDocumentPath
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                return _dte.ActiveDocument?.FullName;
            }
            catch (Exception e) when (e is COMException or ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>Somebody needs the host — the preview window, the sheet checker. The
    /// first one starts it; the last <see cref="Release"/> stops it.</summary>
    public void Acquire()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_users++ == 0) Refresh(force: false);
    }

    public void Release()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (--_users > 0) return;

        _users = 0;
        Stop();
    }

    public void Send(DesignerMessage message) => _connection?.Send(message);

    /// <summary>Start a host for the active project if there is none for it, or if the
    /// project was built since. <paramref name="force"/> — start a new one regardless.</summary>
    public void Refresh(bool force)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_users == 0) return;

        string? path = ProjectOutput.ForActiveProject(_dte);
        DateTime stamp = path is not null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;

        if (!force && _connection is { IsAlive: true } &&
            string.Equals(path, _assemblyPath, StringComparison.OrdinalIgnoreCase) && stamp == _assemblyStamp)
            return;

        _assemblyPath = path;
        _assemblyStamp = stamp;

        Restart(path);
    }

    private void Restart(string? assemblyPath)
    {
        Stop();

        int generation = ++_generation;

        // starting waits for the process to connect: off the UI thread
        _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await TaskScheduler.Default;

            DesignerConnection connection;

            try
            {
                connection = DesignerConnection.Start(_log.Write);
            }
            catch (Exception e)
            {
                _log.Write(e.Message);
                HostFailed?.Invoke(e.Message);
                return;
            }

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

            // a newer start has begun meanwhile: this one is not wanted any more
            if (generation != _generation || _users == 0)
            {
                connection.Dispose();
                return;
            }

            _connection = connection;

            connection.Received += message =>
            {
                if (message is LogMessage log) _log.Write(log.Text);
                Received?.Invoke(message);
            };

            connection.Closed += () =>
            {
                if (generation == _generation)
                    HostFailed?.Invoke("The previewer's host has stopped; see the ZeppelinForms output pane.");
            };

            connection.Send(new HelloMessage(DesignerProtocol.Version, string.Empty));

            bool loading = assemblyPath is not null && File.Exists(assemblyPath);

            if (loading)
            {
                _log.Write($"Loading {assemblyPath}");
                connection.Send(new LoadMessage(assemblyPath!));
            }

            // a host without a project still checks style sheets: subscribers resend
            // what they were waiting for
            HostStarted?.Invoke();

            // after HostStarted, whose subscribers report "loading…": the reason there
            // is nothing to load must be the last word
            if (!loading)
            {
                HostFailed?.Invoke(assemblyPath is null
                    ? "Open a document of a ZeppelinForms project to see its previews."
                    : $"Build the project to see its previews: {Path.GetFileName(assemblyPath)} is not there yet.");
            }
        }).FileAndForget("zeppelinforms/preview/start");
    }

    private void Stop()
    {
        DesignerConnection? old = _connection;
        _connection = null;

        if (old is null) return;

        // shutting down waits for the process a moment: off the UI thread.
        // Qualified: Microsoft.VisualStudio.Shell has a Task of its own, the task list's
        _ = System.Threading.Tasks.Task.Run(old.Dispose);
    }

    // ===== Visual Studio events =====

    private void OnBuildDone(vsBuildScope scope, vsBuildAction action)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // the same project, a new build: the stamp tells
        if (action != vsBuildAction.vsBuildActionClean)
            Refresh(force: false);
    }

    private void OnWindowActivated(EnvDTE.Window gotFocus, EnvDTE.Window lostFocus)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? path;

        try
        {
            // only documents choose the project: tool windows don't belong to one
            path = gotFocus?.Document?.FullName;
        }
        catch (COMException)
        {
            return;
        }

        if (path is null) return;

        Refresh(force: false);
        DocumentActivated?.Invoke(path);
    }

    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _buildEvents.OnBuildDone -= OnBuildDone;
        _windowEvents.WindowActivated -= OnWindowActivated;

        _users = 0;
        _connection?.Dispose();
        _connection = null;
    }
}