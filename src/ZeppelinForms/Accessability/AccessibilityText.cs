using System.Text;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Accessibility;

/// <summary>The text an element shows, as a name: its own text, or the text of
/// what is inside it.</summary>
internal static class AccessibilityText
{
    /// <summary>The element's own text, if it has one.</summary>
    public static string? Own(UIElement element) => element switch
    {
        ITextElement text => text.Text,
        RichLabel rich => string.Concat(rich.Inlines.Select(run => run.Text)),
        HintLabel hint => hint.Text,
        _ => null,
    };

    /// <summary>The own text, or else the texts inside, joined: the name of a list
    /// item whose template is an icon and two labels is the two labels.</summary>
    public static string Of(UIElement element)
    {
        if (Own(element) is { Length: > 0 } own) return own;

        var builder = new StringBuilder();
        Collect(element, builder);

        return builder.ToString();
    }

    private static void Collect(UIElement element, StringBuilder builder)
    {
        if (!element.IsVisible || element.IsAccessibilityHidden) return;

        string? text = element.AccessibleName is { Length: > 0 } explicitName
            ? explicitName
            : Own(element);

        if (!string.IsNullOrEmpty(text))
        {
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(text);
            return;
        }

        foreach (UIElement child in UIElementPeer.ChildElements(element))
            Collect(child, builder);
    }
}