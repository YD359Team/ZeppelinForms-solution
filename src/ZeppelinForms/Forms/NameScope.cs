using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms;

/// <summary>
/// Область имён в пределах одной формы: имена уникальны внутри формы,
/// но могут повторяться в разных окнах.
/// </summary>
public sealed class NameScope
{
    private readonly Dictionary<string, UIElement> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, UIElement> Elements => _byName;

    public void Register(UIElement element)
    {
        if (string.IsNullOrEmpty(element.Name))
        {
            element.Name = GenerateName(element);
        }
        else if (_byName.TryGetValue(element.Name, out var existing) && !ReferenceEquals(existing, element))
        {
            throw new InvalidOperationException(
                $"Имя '{element.Name}' уже занято другим элементом этой формы.");
        }

        _byName[element.Name] = element;
    }

    public void Unregister(UIElement element)
    {
        if (!string.IsNullOrEmpty(element.Name) &&
            _byName.TryGetValue(element.Name, out var existing) &&
            ReferenceEquals(existing, element))
        {
            _byName.Remove(element.Name);
        }
    }

    public UIElement? Find(string name) =>
        _byName.TryGetValue(name, out var element) ? element : null;

    public T? Find<T>(string name) where T : UIElement => Find(name) as T;

    private string GenerateName(UIElement element)
    {
        string typeName = element.GetType().Name;
        string prefix = char.ToLowerInvariant(typeName[0]) + typeName[1..];

        _counters.TryGetValue(prefix, out int counter);

        string candidate;
        do
        {
            counter++;
            candidate = prefix + counter;
        }
        while (_byName.ContainsKey(candidate));

        _counters[prefix] = counter;
        return candidate;
    }
}
