using ZeppelinForms.Drawing;

namespace ZeppelinForms.Browser;

/// <summary>
/// The browser clipboard is asynchronous, while IClipboard is not. So synchronous
/// reading gives the last known value rather than a fresh one: a Promise can't be
/// reached without blocking the thread, and blocking is not allowed in a browser.
/// </summary>
public sealed class BrowserClipboard : IClipboard
{
    public static void Register() => Clipboard.Current = new BrowserClipboard();

    private static string? s_cached;

    /// <summary>The value since the last GetTextAsync or SetText.
    /// null — the clipboard hasn't been read yet during the application's run.</summary>
    public string? GetText() => s_cached;

    public void SetText(string text)
    {
        s_cached = text;
        Interop.WriteClipboard(text);
    }

    /// <summary>A real read. Requires the call to happen inside handling of
    /// a user gesture — otherwise the browser denies the permission.</summary>
    public static async Task<string?> GetTextAsync()
    {
        try
        {
            s_cached = await Interop.ReadClipboardAsync();
        }
        catch
        {
            // permission denied or no navigator.clipboard:
            // we stay on the previous value, this is no reason to crash
        }

        return s_cached;
    }
}