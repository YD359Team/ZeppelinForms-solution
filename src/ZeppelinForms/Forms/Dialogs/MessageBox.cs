namespace ZeppelinForms.Forms.Dialogs;

public static class MessageBox
{
    public static MessageBoxResult Show(
        Form owner,
        string message,
        string title = "Сообщение",
        MessageBoxButtons buttons = MessageBoxButtons.Ok,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        var dialog = new MessageBoxForm(message, title, buttons, icon);

        DialogResult<MessageBoxResult> result = dialog.ShowDialog<MessageBoxResult>(owner);

        return result.IsAccepted ? result.Value : MessageBoxResult.Cancel;
    }

    public static bool Confirm(Form owner, string message, string title = "Подтверждение") =>
        Show(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxResult.Yes;

    public static void Error(Form owner, string message, string title = "Ошибка") =>
        Show(owner, message, title, MessageBoxButtons.Ok, MessageBoxIcon.Error);
}
