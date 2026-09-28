using Android.Content;

namespace ZeppelinForms.Android;

/// <summary>The Android system clipboard.</summary>
/// <remarks>
/// The manager is not remembered: its lifetime is the lifetime of the context,
/// and the activity may be recreated. The query is cheap, while a dangling
/// reference to a killed activity is a leak of its whole view tree.
///
/// For the same reason the context itself is the application's, not the
/// activity's: this object lives in the static Clipboard.Current, and holding
/// the activity there kept every recreated activity alive for good.
/// </remarks>
public sealed class AndroidClipboard(Context context) : IClipboard
{
    private readonly Context _context = context.ApplicationContext ?? context;

    public static void Register(Context context) => Clipboard.Current = new AndroidClipboard(context);

    private ClipboardManager? Manager =>
        _context.GetSystemService(Context.ClipboardService) as ClipboardManager;

    public string? GetText()
    {
        if (Manager?.PrimaryClip is not { ItemCount: > 0 } clip) return null;

        // CoerceToText rather than Text: the clipboard may hold a link, HTML
        // or an Intent, and the system can give the text for all of these itself
        return clip.GetItemAt(0)?.CoerceToText(_context);
    }

    public void SetText(string text)
    {
        if (Manager is not { } manager) return;

        // the label is visible in the system clipboard hints, so it's
        // a meaningful word rather than an empty string
        manager.PrimaryClip = ClipData.NewPlainText("text", text);
    }
}