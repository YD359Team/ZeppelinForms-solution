using ZeppelinForms.Drawing;

namespace ZeppelinForms.Browser;

/// <summary>
/// Буфер обмена браузера асинхронный, а IClipboard — нет. Синхронное чтение
/// поэтому отдаёт последнее известное значение, а не свежее: до Promise
/// без блокировки потока не добраться, а блокировать в браузере нельзя.
/// </summary>
public sealed class BrowserClipboard : IClipboard
{
    public static void Register() => Clipboard.Current = new BrowserClipboard();

    private static string? s_cached;

    /// <summary>Значение с момента последнего GetTextAsync или SetText.
    /// null — за время работы приложения буфер ещё не читали.</summary>
    public string? GetText() => s_cached;

    public void SetText(string text)
    {
        s_cached = text;
        Interop.WriteClipboard(text);
    }

    /// <summary>Настоящее чтение. Требует, чтобы вызов случился внутри
    /// обработки жеста пользователя — иначе браузер откажет в разрешении.</summary>
    public static async Task<string?> GetTextAsync()
    {
        try
        {
            s_cached = await Interop.ReadClipboardAsync();
        }
        catch
        {
            // отказ в разрешении или отсутствие navigator.clipboard:
            // остаёмся на прошлом значении, это не повод падать
        }

        return s_cached;
    }
}