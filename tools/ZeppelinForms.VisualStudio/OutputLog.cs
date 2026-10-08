using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace ZeppelinForms.VisualStudio;

/// <summary>The "ZeppelinForms" pane of the Output window: what the host says, how
/// it starts and stops. Writing is safe from any thread.</summary>
internal sealed class OutputLog(DTE2 dte)
{
    private OutputWindowPane? _pane;

    public void Write(string line)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            _pane ??= FindOrCreatePane();
            _pane.OutputString($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }).FileAndForget("zeppelinforms/log");
    }

    private OutputWindowPane FindOrCreatePane()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        OutputWindowPanes panes = dte.ToolWindows.OutputWindow.OutputWindowPanes;

        foreach (OutputWindowPane pane in panes)
            if (pane.Name == "ZeppelinForms")
                return pane;

        return panes.Add("ZeppelinForms");
    }
}