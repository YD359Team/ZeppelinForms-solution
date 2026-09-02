namespace ZeppelinForms.Forms.Dialogs;

public static class InputBox
{
    /// <summary>Запрос строки. Возвращает null, если отменили.</summary>
    public static string? Show(
        Form owner,
        string prompt,
        string title = "Ввод",
        string initialValue = "",
        char? passwordChar = null)
    {
        var dialog = new InputBoxForm(prompt, title, initialValue, passwordChar);

        DialogResult<string> result = dialog.ShowDialog<string>(owner);

        return result.IsAccepted ? result.Value : null;
    }

    /// <summary>Запрос числа. Возвращает null, если отменили или ввели не число.</summary>
    public static decimal? ShowNumber(
        Form owner,
        string prompt,
        string title = "Ввод числа",
        decimal initialValue = 0,
        decimal minimum = decimal.MinValue,
        decimal maximum = decimal.MaxValue,
        int decimalPlaces = 0)
    {
        var dialog = new NumberBoxForm(prompt, title, initialValue, minimum, maximum, decimalPlaces);

        DialogResult<decimal> result = dialog.ShowDialog<decimal>(owner);

        return result.IsAccepted ? result.Value : null;
    }
}
