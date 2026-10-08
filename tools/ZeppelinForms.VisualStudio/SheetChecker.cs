using System.IO;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using ZeppelinForms.Design.Protocol;

namespace ZeppelinForms.VisualStudio;

/// <summary>
/// The problems of .zss style sheets in the Error List: checked by the previewer's
/// host — it knows the project's own controls — whenever a sheet is opened or saved.
/// </summary>
/// <remarks>
/// The host is started for the check if the preview window hasn't started one, and
/// stopped again a while after the last check: a sheet saved every few seconds should
/// not pay a process start each time, and an idle solution should not keep one.
/// </remarks>
internal sealed class SheetChecker : IDisposable
{
    private static readonly TimeSpan IdleStop = TimeSpan.FromMinutes(2);

    private readonly AsyncPackage _package;
    private readonly DesignerService _designer;
    private readonly ErrorListProvider _errors;

    // see DesignerService: DTE events must be held on to
    private readonly DocumentEvents _documentEvents;

    /// <summary>Sheets sent to the host and not answered yet: the host may still be starting.</summary>
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);

    private bool _holdsHost;
    private System.Threading.Timer? _idle;

    public SheetChecker(AsyncPackage package, DTE2 dte, DesignerService designer)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _package = package;
        _designer = designer;
        _errors = new ErrorListProvider(package) { ProviderName = "ZeppelinForms style sheets" };

        _documentEvents = dte.Events.DocumentEvents;
        _documentEvents.DocumentSaved += OnDocument;
        _documentEvents.DocumentOpened += OnDocument;

        designer.Received += OnReceived;
        designer.HostStarted += OnHostStarted;
    }

    private void OnDocument(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string path = document.FullName;

        if (!path.EndsWith(".zss", StringComparison.OrdinalIgnoreCase)) return;

        Check(path);
    }

    public void Check(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _pending.Add(path);

        if (!_holdsHost)
        {
            _holdsHost = true;

            // the preview may already hold one: then this starts nothing
            _designer.Acquire();
        }

        _designer.Send(new CheckSheetMessage(path));

        ScheduleIdleStop();
    }

    /// <summary>A host started after the checks were sent: send them again.</summary>
    private void OnHostStarted()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (string path in _pending)
            _designer.Send(new CheckSheetMessage(path));
    }

    private void OnReceived(DesignerMessage message)
    {
        if (message is not SheetDiagnosticsMessage diagnostics) return;

        _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

            _pending.Remove(diagnostics.Path);
            Show(diagnostics);
        }).FileAndForget("zeppelinforms/sheets/show");
    }

    /// <summary>Replace the tasks of this sheet and its imports with the new ones.</summary>
    private void Show(SheetDiagnosticsMessage diagnostics)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { diagnostics.Path };
        foreach (SheetDiagnosticInfo item in diagnostics.Items)
            files.Add(item.Source);

        _errors.SuspendRefresh();

        try
        {
            foreach (ErrorTask stale in _errors.Tasks.OfType<ErrorTask>().Where(t => files.Contains(t.Document)).ToList())
                _errors.Tasks.Remove(stale);

            foreach (SheetDiagnosticInfo item in diagnostics.Items)
            {
                var task = new ErrorTask
                {
                    Category = TaskCategory.BuildCompile,
                    ErrorCategory = item.IsError ? TaskErrorCategory.Error : TaskErrorCategory.Warning,
                    Text = "ZSS: " + item.Message,
                    Document = item.Source,
                    Line = item.Line - 1,
                    Column = item.Column - 1,
                };

                task.Navigate += (_, _) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();

                    // ErrorTask.Line is zero-based, Navigate wants it one-based
                    task.Line++;
                    _errors.Navigate(task, new Guid(EnvDTE.Constants.vsViewKindCode));
                    task.Line--;
                };

                _errors.Tasks.Add(task);
            }
        }
        finally
        {
            _errors.ResumeRefresh();
        }

        if (diagnostics.Items.Count > 0)
            _errors.Show();
    }

    private void ScheduleIdleStop()
    {
        _idle ??= new System.Threading.Timer(_ =>
        {
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (!_holdsHost || _pending.Count > 0) return;

                _holdsHost = false;
                _designer.Release();
            }).FileAndForget("zeppelinforms/sheets/idle");
        });

        _idle.Change(IdleStop, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _documentEvents.DocumentSaved -= OnDocument;
        _documentEvents.DocumentOpened -= OnDocument;

        _designer.Received -= OnReceived;
        _designer.HostStarted -= OnHostStarted;

        _idle?.Dispose();
        _errors.Dispose();
    }
}