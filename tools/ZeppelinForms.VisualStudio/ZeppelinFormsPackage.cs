using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace ZeppelinForms.VisualStudio;

/// <summary>
/// The extension's entry point: the command that opens the preview, and the services
/// that live as long as Visual Studio — the connection to the previewer's host and
/// the style sheet checker.
/// </summary>
/// <remarks>
/// Loaded in the background once a solution is open: the Error List must hear about
/// a saved .zss even when nobody has opened the preview.
/// </remarks>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(ZeppelinFormsPackage.PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
// docked as a tab next to the Properties window, where a designer is expected
[ProvideToolWindow(typeof(PreviewToolWindow), Style = VsDockStyle.Tabbed, Window = ZeppelinFormsPackage.PropertiesWindowGuid)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class ZeppelinFormsPackage : AsyncPackage
{
    public const string PackageGuidString = "de640047-399c-45dc-8ad4-139c0a2265e5";

    /// <summary>EnvDTE.Constants.vsWindowKindProperties.</summary>
    public const string PropertiesWindowGuid = "eefa5220-e298-11d0-8f78-00a0c9110057";

    public static readonly Guid CommandSet = new("61e83f1d-2200-4895-9866-eff903281f91");

    public const int ShowPreviewCommandId = 0x0100;

    /// <summary>The loaded package: the tool window reaches the services through it.</summary>
    public static ZeppelinFormsPackage? Instance { get; private set; }

    /// <summary>The previewer's host for the active project.</summary>
    internal DesignerService Designer { get; private set; } = null!;

    internal SheetChecker Sheets { get; private set; } = null!;

    internal OutputLog Log { get; private set; } = null!;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = (DTE2)(await GetServiceAsync(typeof(EnvDTE.DTE)))!;

        Log = new OutputLog(dte);
        Designer = new DesignerService(this, dte, Log);
        Sheets = new SheetChecker(this, dte, Designer);

        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            commands.AddCommand(new MenuCommand(ShowPreview, new CommandID(CommandSet, ShowPreviewCommandId)));

        Instance = this;
    }

    private void ShowPreview(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(async () =>
        {
            ToolWindowPane window = await ShowToolWindowAsync(typeof(PreviewToolWindow), 0, create: true, DisposalToken);

            if (window?.Frame is null)
                throw new NotSupportedException("The preview window could not be created.");
        }).FileAndForget("zeppelinforms/preview/show");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Designer?.Dispose();
            Sheets?.Dispose();
        }

        base.Dispose(disposing);
    }
}