using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Linux.Tray;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>The desktop's file dialogs through the portal, see <see cref="LinuxFileChooser"/>.</summary>
/// <remarks>
/// A Wayland window is named to the portal by a handle of xdg-foreign, which the
/// compositor may not offer; without one the dialog is a window of its own — centered,
/// not attached to the form — and the form waits for it disabled.
/// </remarks>
public sealed partial class WaylandPlatform : INativeFileDialogs
{
    Task<string[]?> INativeFileDialogs.ShowAsync(Form owner, FileDialogOptions options, FileDialogMode mode) =>
        LinuxFileDialogs.ShowAsync(_tray.FileChooser, owner, string.Empty, options, mode);

    /// <summary>The synchronous dialog: the portal's too, with the loop spun until it
    /// answers — the forms keep painting under it, as under the framework's own.</summary>
    string[]? INativeFileDialogs.Show(Form owner, FileDialogOptions options, FileDialogMode mode)
    {
        Task<string[]?> dialog = ((INativeFileDialogs)this).ShowAsync(owner, options, mode);

        // closing the last window ends the wait: nothing is left to answer to
        while (!dialog.IsCompleted && _windows.Count > 0)
            PumpOnce();

        return dialog.IsCompleted ? dialog.GetAwaiter().GetResult() : [];
    }
}
