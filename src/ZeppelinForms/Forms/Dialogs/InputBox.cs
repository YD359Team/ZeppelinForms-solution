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

        return Unwrap(dialog.ShowDialog<string>(owner));
    }

    public static async Task<string?> ShowAsync(
        Form owner,
        string prompt,
        string title = "Ввод",
        string initialValue = "",
        char? passwordChar = null)
    {
        var dialog = new InputBoxForm(prompt, title, initialValue, passwordChar);

        return Unwrap(await dialog.ShowDialogAsync<string>(owner));
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

        return UnwrapNumber(dialog.ShowDialog<decimal>(owner));
    }

    public static async Task<decimal?> ShowNumberAsync(
        Form owner,
        string prompt,
        string title = "Ввод числа",
        decimal initialValue = 0,
        decimal minimum = decimal.MinValue,
        decimal maximum = decimal.MaxValue,
        int decimalPlaces = 0)
    {
        var dialog = new NumberBoxForm(prompt, title, initialValue, minimum, maximum, decimalPlaces);

        return UnwrapNumber(await dialog.ShowDialogAsync<decimal>(owner));
    }

    private static string? Unwrap(DialogResult<string> result) =>
        result.IsAccepted ? result.Value : null;

    private static decimal? UnwrapNumber(DialogResult<decimal> result) =>
        result.IsAccepted ? result.Value : null;
}