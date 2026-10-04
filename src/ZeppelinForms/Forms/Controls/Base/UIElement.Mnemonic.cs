using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Access keys in the element's own caption: Alt and the marked letter
/// press a button, toggle a check box, move the focus to a label's target.</summary>
public abstract partial class UIElement
{
    /// <summary>Read "&amp;" in the caption as the access key mark: "&amp;Save" shows
    /// "Save" and answers Alt+S; "&amp;&amp;" is an ampersand itself.</summary>
    /// <remarks>Off by default — on, an ordinary "Tom &amp; Jerry" would lose its
    /// ampersand. Menu items always read the mark: there it is expected.</remarks>
    public bool UseMnemonic
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the shown caption gets shorter or longer by the marks
            Invalidate();
        }
    }

    /// <summary>The caption the access key is read from: the element's own text,
    /// or the header of a group.</summary>
    internal string? MnemonicSource => this switch
    {
        ITextElement text => text.Text,
        GroupBox group => group.Header,
        _ => null,
    };

    /// <summary>The access key, upper-cased; null — none, or marks are off.</summary>
    internal char? AccessKey => UseMnemonic ? Mnemonic.Key(MnemonicSource) : null;

    /// <summary>Underline the access key of a caption just drawn — while the form
    /// shows access keys: Alt is held, or the menu is worked from the keyboard.
    /// Hidden the rest of the time, as Windows does by default.</summary>
    protected void DrawAccessKeyUnderline(
        Graphics g, string? caption, Rectangle area, Color color,
        HorizontalContentAlignment horizontal, VerticalContentAlignment vertical)
    {
        if (!UseMnemonic || caption is null) return;
        if (FindOwner() is not { ShowsAccessKeys: true }) return;

        (string shown, int index) = Mnemonic.Parse(caption);

        if (index < 0) return;

        Mnemonic.DrawUnderline(g, TransformCase(shown), index, area, color, EffectiveFont, horizontal, vertical);
    }
}