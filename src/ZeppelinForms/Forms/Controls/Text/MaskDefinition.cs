namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>
/// Разбор шаблона маски. Символы-заполнители задают, что можно ввести,
/// остальное — литералы, которые пользователь не редактирует.
/// </summary>
public sealed class MaskDefinition
{
    private readonly char[] _pattern;
    private readonly bool[] _isPlaceholder;

    public string Pattern { get; }
    public char PromptChar { get; }

    public int Length => _pattern.Length;

    /// <summary>
    /// 0 — цифра обязательна, 9 — цифра необязательна, L — буква,
    /// A — буква или цифра, * — любой символ. Литерал экранируется \.
    /// </summary>
    public MaskDefinition(string pattern, char promptChar = '_')
    {
        Pattern = pattern;
        PromptChar = promptChar;

        List<char> chars = [];
        List<bool> placeholders = [];

        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '\\' && i + 1 < pattern.Length)
            {
                chars.Add(pattern[++i]);
                placeholders.Add(false);
                continue;
            }

            bool isPlaceholder = pattern[i] is '0' or '9' or 'L' or 'A' or '*';

            chars.Add(pattern[i]);
            placeholders.Add(isPlaceholder);
        }

        _pattern = [.. chars];
        _isPlaceholder = [.. placeholders];
    }

    public bool IsPlaceholder(int index) =>
        index >= 0 && index < _isPlaceholder.Length && _isPlaceholder[index];

    public char LiteralAt(int index) => _pattern[index];

    public bool IsRequired(int index) => IsPlaceholder(index) && _pattern[index] is '0' or 'L' or 'A';

    public bool Accepts(int index, char c)
    {
        if (!IsPlaceholder(index)) return false;

        return _pattern[index] switch
        {
            '0' or '9' => char.IsAsciiDigit(c),
            'L' => char.IsLetter(c),
            'A' => char.IsLetterOrDigit(c),
            '*' => !char.IsControl(c),
            _ => false,
        };
    }

    /// <summary>Пустая строка по маске: литералы на местах, заполнители — приглашения.</summary>
    public char[] CreateBuffer()
    {
        char[] buffer = new char[_pattern.Length];

        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = _isPlaceholder[i] ? PromptChar : _pattern[i];

        return buffer;
    }

    public int NextPlaceholder(int from)
    {
        for (int i = Math.Max(0, from); i < _pattern.Length; i++)
            if (_isPlaceholder[i]) return i;

        return _pattern.Length;
    }

    public int PreviousPlaceholder(int from)
    {
        for (int i = Math.Min(from, _pattern.Length) - 1; i >= 0; i--)
            if (_isPlaceholder[i]) return i;

        return -1;
    }

    public static MaskDefinition Phone => new("+7 (000) 000-00-00");
    public static MaskDefinition Date => new("00.00.0000");
    public static MaskDefinition Time => new("00:00");
    public static MaskDefinition Inn => new("000000000000");
}
