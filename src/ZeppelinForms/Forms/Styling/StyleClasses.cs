using System.Collections;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// The style classes of an element — the names a selector asks about with a dot:
/// <c>button.Classes.Add("primary")</c> for <c>Button.primary</c>.
/// </summary>
/// <remarks>
/// A set in insertion order: adding a class twice keeps one. Names are compared
/// case-sensitively, as in CSS, and must be identifiers — letters, digits,
/// <c>-</c> and <c>_</c>, not starting with a digit. Every change restyles the
/// element, and its descendants when some selector names the class above its subject.
/// </remarks>
public sealed class StyleClasses : ICollection<string>, IReadOnlyCollection<string>
{
    private readonly List<string> _items = [];
    private readonly Action<string> _changed;

    internal StyleClasses(Action<string> changed)
    {
        _changed = changed;
    }

    public int Count => _items.Count;

    bool ICollection<string>.IsReadOnly => false;

    public bool Contains(string item) => _items.Contains(item, StringComparer.Ordinal);

    /// <summary>Add a class. False — the element has it already.</summary>
    public bool Add(string item)
    {
        Validate(item);

        if (Contains(item)) return false;

        _items.Add(item);
        _changed(item);

        return true;
    }

    void ICollection<string>.Add(string item) => Add(item);

    /// <summary>Add several classes: <c>Classes.Add("primary", "large")</c>.</summary>
    public void Add(params ReadOnlySpan<string> items)
    {
        foreach (string item in items)
            Add(item);
    }

    public bool Remove(string item)
    {
        int index = _items.FindIndex(existing => string.Equals(existing, item, StringComparison.Ordinal));

        if (index < 0) return false;

        _items.RemoveAt(index);
        _changed(item);

        return true;
    }

    /// <summary>Add the class when <paramref name="on"/>, remove it otherwise.</summary>
    public void Set(string item, bool on)
    {
        if (on) Add(item);
        else Remove(item);
    }

    /// <summary>Add the class if it is missing, remove it if it is there.
    /// True — the element has it now.</summary>
    public bool Toggle(string item)
    {
        if (Remove(item)) return false;

        Add(item);
        return true;
    }

    public void Clear()
    {
        if (_items.Count == 0) return;

        string[] removed = [.. _items];
        _items.Clear();

        foreach (string item in removed)
            _changed(item);
    }

    public void CopyTo(string[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => string.Join(' ', _items);

    private static void Validate(string item)
    {
        ArgumentException.ThrowIfNullOrEmpty(item);

        if (char.IsDigit(item[0]))
            throw new ArgumentException($"A class name can't start with a digit: '{item}'.", nameof(item));

        foreach (char c in item)
            if (!char.IsLetterOrDigit(c) && c is not '-' and not '_')
                throw new ArgumentException(
                    $"A class name is an identifier — letters, digits, '-' and '_': '{item}'.", nameof(item));
    }
}