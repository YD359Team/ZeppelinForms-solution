using System.Globalization;

namespace ZeppelinForms.Core.Text;

/// <summary>Границы видимых символов: эмодзи, модификаторы тона, комбинирующие знаки.</summary>
public static class TextElements
{
    public static int Next(string text, int index)
    {
        if (index >= text.Length) return text.Length;

        return index + StringInfo.GetNextTextElementLength(text.AsSpan(index));
    }

    public static int Previous(string text, int index)
    {
        if (index <= 0) return 0;

        // кластеры длиннее 32 char практически не встречаются,
        // поэтому ищем не с начала строки, а с небольшим запасом
        int from = Math.Max(0, index - 32);
        int position = from;

        while (position < index)
        {
            int next = Next(text, position);
            if (next >= index) return position;
            position = next;
        }

        return from;
    }

    public static int Count(string text) => new StringInfo(text).LengthInTextElements;

    public static IEnumerable<int> Boundaries(string text)
    {
        int position = 0;

        while (position <= text.Length)
        {
            yield return position;
            if (position >= text.Length) yield break;
            position = Next(text, position);
        }
    }
}
