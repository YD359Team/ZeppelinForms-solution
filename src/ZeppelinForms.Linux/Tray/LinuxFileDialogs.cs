using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Dialogs;

namespace ZeppelinForms.Linux.Tray;

/// <summary>What X11 and Wayland do alike around the portal's dialog.</summary>
internal static class LinuxFileDialogs
{
    /// <summary>The owner is disabled while the dialog is open, as under the framework's
    /// own: the portal's modality holds only where the compositor knows the parent.</summary>
    public static async Task<string[]?> ShowAsync(
        LinuxFileChooser chooser, Form owner, string parent, FileDialogOptions options, FileDialogMode mode)
    {
        Task<string[]?> dialog = chooser.ShowAsync(parent, options, mode);

        // no portal: answered at once, and the framework's dialog does the rest
        if (dialog.IsCompleted) return await dialog;

        IPlatformWindow? window = owner.PlatformWindow;
        window?.SetEnabled(false);

        try
        {
            return await dialog;
        }
        finally
        {
            if (window is not null)
            {
                window.SetEnabled(true);
                window.Activate();
            }
        }
    }
}
