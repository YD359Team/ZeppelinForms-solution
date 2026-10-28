using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Dialogs;

namespace ZeppelinForms.Headless;

/// <summary>The system's file dialogs without a screen: answered by the test.</summary>
public sealed partial class HeadlessPlatform : INativeFileDialogs
{
    /// <summary>Plays the system's file dialog: given what the dialog was asked, it answers
    /// the paths chosen — empty for a cancel. Null — no system dialog, and the framework's
    /// own is shown, as on a platform without one.</summary>
    public Func<FileDialogMode, FileDialogOptions, string[]>? NativeFileDialog { get; set; }

    string[]? INativeFileDialogs.Show(Form owner, FileDialogOptions options, FileDialogMode mode) =>
        NativeFileDialog?.Invoke(mode, options);

    Task<string[]?> INativeFileDialogs.ShowAsync(Form owner, FileDialogOptions options, FileDialogMode mode) =>
        Task.FromResult(NativeFileDialog?.Invoke(mode, options));
}
