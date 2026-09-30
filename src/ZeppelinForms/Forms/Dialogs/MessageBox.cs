using ZeppelinForms.Core.Globalization;

namespace ZeppelinForms.Forms.Dialogs;

/// <remarks>
/// A null title means the localized default for the kind of message.
/// </remarks>
public static class MessageBox
{
    /// <summary>Synchronous display. Requires a platform with a nested loop;
    /// in the browser use ShowAsync.</summary>
    public static MessageBoxResult Show(
        Form owner,
        string message,
        string? title = null,
        MessageBoxButtons buttons = MessageBoxButtons.Ok,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        var dialog = new MessageBoxForm(message, title ?? Localization.Get(ZfText.MessageTitle), buttons, icon);

        return Unwrap(dialog.ShowDialog<MessageBoxResult>(owner));
    }

    public static async Task<MessageBoxResult> ShowAsync(
        Form owner,
        string message,
        string? title = null,
        MessageBoxButtons buttons = MessageBoxButtons.Ok,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        var dialog = new MessageBoxForm(message, title ?? Localization.Get(ZfText.MessageTitle), buttons, icon);

        return Unwrap(await dialog.ShowDialogAsync<MessageBoxResult>(owner));
    }

    public static bool Confirm(Form owner, string message, string? title = null) =>
        Show(owner, message, title ?? Localization.Get(ZfText.ConfirmTitle),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxResult.Yes;

    public static async Task<bool> ConfirmAsync(Form owner, string message, string? title = null) =>
        await ShowAsync(owner, message, title ?? Localization.Get(ZfText.ConfirmTitle),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxResult.Yes;

    public static void Error(Form owner, string message, string? title = null) =>
        Show(owner, message, title ?? Localization.Get(ZfText.ErrorTitle),
            MessageBoxButtons.Ok, MessageBoxIcon.Error);

    public static Task ErrorAsync(Form owner, string message, string? title = null) =>
        ShowAsync(owner, message, title ?? Localization.Get(ZfText.ErrorTitle),
            MessageBoxButtons.Ok, MessageBoxIcon.Error);

    // closing with the window's close button and cancelling are the same thing to the caller
    private static MessageBoxResult Unwrap(DialogResult<MessageBoxResult> result) =>
        result.IsAccepted ? result.Value : MessageBoxResult.Cancel;
}