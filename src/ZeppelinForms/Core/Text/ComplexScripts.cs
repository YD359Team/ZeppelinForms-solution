namespace ZeppelinForms.Core.Text;

/// <summary>Whether a text needs a shaper to be drawn right — joined letters, reordered
/// vowel signs, marks placed on their letters, composed emoji — or its characters can be
/// drawn one glyph each, as they always were.</summary>
/// <remarks>
/// Latin, Greek, Cyrillic, CJK and the symbols keep the fast path: a glyph per
/// character at its advance, exactly as before shaping arrived, so their widths and
/// the snapshots of every caption stay what they were. The rest goes to the shaper:
/// combining marks, the right-to-left scripts, the scripts of India and South-East
/// Asia, Hangul jamo, joiners and variation selectors, and everything outside the
/// Basic Multilingual Plane, emoji sequences among it.
/// </remarks>
internal static class ComplexScripts
{
    public static bool NeedsShaping(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
            if (c >= '̀' && IsComplex(c))
                return true;

        return false;
    }

    private static bool IsComplex(char c) => c switch
    {
        <= 'ͯ' => true,                     // combining diacritical marks
        >= '҃' and <= '҉' => true,     // Cyrillic combining marks
        >= '֐' and <= 'ࣿ' => true,     // Hebrew, Arabic, Syriac, Thaana, N'Ko…
        >= 'ऀ' and <= '෿' => true,     // the scripts of India and Sri Lanka
        >= '฀' and <= '࿿' => true,     // Thai, Lao, Tibetan
        >= 'က' and <= '႟' => true,     // Myanmar
        >= 'ᄀ' and <= 'ᇿ' => true,     // Hangul jamo
        >= 'ᜀ' and <= '᢯' => true,     // Philippine scripts, Khmer, Mongolian
        >= 'ᤀ' and <= '᳿' => true,     // Limbu… Balinese… Vedic extensions
        >= '᷀' and <= '᷿' => true,     // combining marks supplement
        >= '‌' and <= '‏' => true,     // joiners and direction marks
        >= '‪' and <= '‮' => true,     // embeddings and overrides
        >= '⁦' and <= '⁩' => true,     // isolates
        >= '⃐' and <= '⃿' => true,     // combining marks for symbols
        >= 'ꠀ' and <= '꯿' => true,     // Syloti Nagri… Meetei Mayek
        >= 'ힰ' and <= '퟿' => true,     // Hangul jamo extended
        >= '\uD800' and <= '\uDFFF' => true,     // beyond the BMP: emoji, historic scripts
        >= 'יִ' and <= '﷿' => true,     // Hebrew and Arabic presentation forms
        >= '︀' and <= '️' => true,     // variation selectors
        >= '︠' and <= '︯' => true,     // combining half marks
        >= 'ﹰ' and <= '﻿' => true,     // Arabic presentation forms B
        _ => false,
    };
}
