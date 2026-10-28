using ZeppelinForms.Core.Globalization;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Localized texts: properties bound to keys and texted anew when the language
/// changes.</summary>
public abstract partial class UIElement
{
    /// <summary>Texts of this element given as keys rather than strings.
    /// Null for the vast majority of elements — no list is created until needed.</summary>
    private List<LocalizedBinding>? _localized;

    /// <summary>The Localization.Version the texts were last resolved for.</summary>
    private int _localizationVersion;

    internal void SetLocalized(LocalizedBinding binding)
    {
        _localized ??= [];

        int index = _localized.FindIndex(existing => existing.Property == binding.Property);

        if (index >= 0) _localized[index] = binding;
        else _localized.Add(binding);

        binding.Apply(this);
        _localizationVersion = Localization.Version;
    }

    internal void RemoveLocalized(string property) =>
        _localized?.RemoveAll(existing => existing.Property == property);

    /// <summary>Resolve every key again and let the control rebuild the texts
    /// it composes itself — the language changed.</summary>
    internal void ApplyLocalization()
    {
        if (_localized is not null)
        {
            // a copy: a setter is user code and may localize another property
            foreach (LocalizedBinding binding in _localized.ToArray())
                binding.Apply(this);
        }

        _localizationVersion = Localization.Version;

        OnLocalizationChanged();
    }

    /// <summary>The language changed. For controls that compose texts themselves
    /// from keys and state — "3 files" — which a key on a property can't express.
    /// Called when a form walks its tree, and on attaching after a missed change.</summary>
    protected virtual void OnLocalizationChanged() { }
}
