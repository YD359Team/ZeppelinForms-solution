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
// The version must change with every change of ZeppelinForms.vsct: Visual Studio
// merges the command table once and reads it again only for a new number. It stayed 1
// when the key binding was added, and the binding worked only where the cache happened
// to be rebuilt
[ProvideMenuResource("Menus.ctmenu", 2)]
// In the documents' area, as a tab beside the code. It used to be docked next to the
// Properties window, where a designer is expected — but that pane is narrow, and a form
// laid out at its width looked nothing like the form in the application. The user's
// own placement, once made, is kept by Visual Studio
[ProvideToolWindow(typeof(PreviewToolWindow), Style = VsDockStyle.MDI)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class ZeppelinFormsPackage : AsyncPackage
{
    public const string PackageGuidString = "de640047-399c-45dc-8ad4-139c0a2265e5";

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

        // the interface, not OleMenuCommandService: its base, MenuCommandService, lives
        // in System.Design, and AddCommand is all that is needed here
        if (await GetServiceAsync(typeof(IMenuCommandService)) is IMenuCommandService commands)
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