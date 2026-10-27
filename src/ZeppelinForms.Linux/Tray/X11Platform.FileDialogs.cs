using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Linux.Tray;

namespace ZeppelinForms.Linux;

/// <summary>The desktop's file dialogs through the portal, see <see cref="LinuxFileChooser"/>:
/// owned by the form's window — "x11:XID" — and modal to it.</summary>
public sealed partial class X11Platform : INativeFileDialogs
{
    async Task<string[]?> INativeFileDialogs.ShowAsync(Form owner, FileDialogOptions options, FileDialogMode mode)
    {
        string parent = owner.PlatformWindow is X11Window { Handle: not 0 } window
            ? $"x11:{(ulong)window.Handle:x}"
            : string.Empty;

        return await LinuxFileDialogs.ShowAsync(_tray.FileChooser, owner, parent, options, mode);
    }

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
