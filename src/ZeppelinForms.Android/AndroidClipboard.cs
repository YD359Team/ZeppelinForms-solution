using Android.Content;

namespace ZeppelinForms.Android;

/// <summary>Системный буфер обмена Android.</summary>
/// <remarks>
/// Менеджер не запоминается: его время жизни — это время жизни контекста,
/// а активность может пережить пересоздание. Запрос дёшев, а висящая
/// ссылка на убитую активность — это утечка всего её дерева представлений.
/// </remarks>
public sealed class AndroidClipboard(Context context) : IClipboard
{
    public static void Register(Context context) => Clipboard.Current = new AndroidClipboard(context);

    private ClipboardManager? Manager =>
        context.GetSystemService(Context.ClipboardService) as ClipboardManager;

    public string? GetText()
    {
        if (Manager?.PrimaryClip is not { ItemCount: > 0 } clip) return null;

        // CoerceToText, а не Text: в буфере могут лежать ссылка, HTML
        // или Intent, и для всех этих видов система умеет отдать текст сама
        return clip.GetItemAt(0)?.CoerceToText(context);
    }

    public void SetText(string text)
    {
        if (Manager is not { } manager) return;

        // ярлык виден в системных подсказках буфера обмена, поэтому
        // не пустая строка, а осмысленное слово
        manager.PrimaryClip = ClipData.NewPlainText("text", text);
    }
}