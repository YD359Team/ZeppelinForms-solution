using System.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms;

/// <summary>Access keys written into a caption as in WinForms: "&amp;File" is
/// File with F as its key, "&amp;&amp;" is an ampersand itself.</summary>
internal static class Mnemonic
{
    /// <summary>The caption as shown, and the index of the access key in it;
    /// −1 — none. The first marked letter wins; a lone trailing "&amp;" stays.</summary>
    public static (string Text, int Index) Parse(string text)
    {
        if (!text.Contains('&')) return (text, -1);

        var builder = new StringBuilder(text.Length);
        int index = -1;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '&' && i + 1 < text.Length)
            {
                i++;

                if (text[i] != '&' && index < 0)
                    index = builder.Length;
            }

            builder.Append(text[i]);
        }

        return (builder.ToString(), index);
    }

    public static string Strip(string text) => Parse(text).Text;

    /// <summary>The access key of a caption, upper-cased; null — none.</summary>
    public static char? Key(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        (string shown, int index) = Parse(text);

        return index < 0 ? null : char.ToUpperInvariant(shown[index]);
    }

    /// <summary>The character a key stands for in an access key: a Latin letter
    /// or a digit. The key codes of both match their ASCII characters.</summary>
    public static char? CharOf(Key key) =>
        key is >= Input.Keyboard.Key.A and <= Input.Keyboard.Key.Z or >= Input.Keyboard.Key.D0 and <= Input.Keyboard.Key.D9
            ? (char)key
            : null;

    /// <summary>Underline the access key of a caption drawn with DrawText in
    /// <paramref name="area"/> with the same alignment.</summary>
    public static void DrawUnderline(
        Graphics g, string shown, int index, Rectangle area, Color color, Font font,
        HorizontalContentAlignment horizontal, VerticalContentAlignment vertical)
    {
        if (index < 0 || index >= shown.Length) return;

        ITextMeasurer measurer = TextMeasurer.Current;
        Size full = measurer.MeasureText(shown, font);

        float x = horizontal switch
        {
            HorizontalContentAlignment.Center => area.X + (area.Width - full.Width) / 2f,
            HorizontalContentAlignment.Right => area.Right - full.Width,
            _ => area.X,
        };

        float top = vertical switch
        {
            VerticalContentAlignment.Center => area.Y + (area.Height - full.Height) / 2f,
            VerticalContentAlignment.Bottom => area.Bottom - full.Height,
            _ => area.Y,
        };

        float before = index == 0 ? 0f : measurer.MeasureText(shown[..index], font).Width;
        float width = measurer.MeasureText(shown.Substring(index, 1), font).Width;

        // just under the baseline: the line height includes the descent, and the
        // underline belongs between the letter and the descenders
        float y = top + full.Height * 0.85f;

        g.DrawLine(new Point(x + before, y), new Point(x + before + width, y), color, 1f);
    }
}