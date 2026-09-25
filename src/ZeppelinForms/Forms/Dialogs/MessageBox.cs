namespace ZeppelinForms.Forms.Dialogs;

public static class MessageBox
{
    /// <summary>Synchronous display. Requires a platform with a nested loop;
    /// in the browser use ShowAsync.</summary>
    public static MessageBoxResult Show(
        Form owner,
        string message,
        string title = "Сообщение",
        MessageBoxButtons buttons = MessageBoxButtons.Ok,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        var dialog = new MessageBoxForm(message, title, buttons, icon);

        return Unwrap(dialog.ShowDialog<MessageBoxResult>(owner));
    }

    public static async Task<MessageBoxResult> ShowAsync(
        Form owner,
        string message,
        string title = "Сообщение",
        MessageBoxButtons buttons = MessageBoxButtons.Ok,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        var dialog = new MessageBoxForm(message, title, buttons, icon);

        return Unwrap(await dialog.ShowDialogAsync<MessageBoxResult>(owner));
    }

    public static bool Confirm(Form owner, string message, string title = "Подтверждение") =>
        Show(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxResult.Yes;

    public static async Task<bool> ConfirmAsync(Form owner, string message, string title = "Подтверждение") =>
        await ShowAsync(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxResult.Yes;

    public static void Error(Form owner, string message, string title = "Ошибка") =>
        Show(owner, message, title, MessageBoxButtons.Ok, MessageBoxIcon.Error);

    public static Task ErrorAsync(Form owner, string message, string title = "Ошибка") =>
        ShowAsync(owner, message, title, MessageBoxButtons.Ok, MessageBoxIcon.Error);

    // closing with the window's close button and cancelling are the same thing to the caller
    private static MessageBoxResult Unwrap(DialogResult<MessageBoxResult> result) =>
        result.IsAccepted ? result.Value : MessageBoxResult.Cancel;
}