namespace ZeppelinForms.Forms.Dialogs;

public static class MessageBox
{
    /// <summary>Синхронный показ. Требует платформы с вложенным циклом;
    /// в браузере используйте ShowAsync.</summary>
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

    // закрытие крестиком и отмена — для вызывающего кода одно и то же
    private static MessageBoxResult Unwrap(DialogResult<MessageBoxResult> result) =>
        result.IsAccepted ? result.Value : MessageBoxResult.Cancel;
}