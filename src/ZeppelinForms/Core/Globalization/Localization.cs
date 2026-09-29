using System.Globalization;
using ZeppelinForms.Core.Text;

namespace ZeppelinForms.Core.Globalization;

/// <summary>The language of the framework and of the application's own keys.</summary>
/// <remarks>
/// The culture here drives only the framework: texts and the culture-dependent
/// formatting of controls. The thread's CurrentCulture and CurrentUICulture are
/// deliberately not touched — that is a process-wide side effect (number formats
/// in logs, in serialization), and an application that wants it sets them itself.
///
/// Lookup goes along the culture chain: ru-RU → ru → the key's English text.
/// Within one culture the table registered last wins, so an application
/// overrides the built-in texts simply by registering its own table.
/// </remarks>
public static class Localization
{
    private static readonly Lock Sync = new();

    private static readonly Dictionary<string, List<StringTable>> Tables =
        new(StringComparer.OrdinalIgnoreCase);

    private static CultureInfo? _culture;
    private static string[] _chain = [];
    private static bool _mirror = true;
    private static FlowDirection _layoutDirection = FlowDirection.LeftToRight;

    /// <summary>The language changed, or a table for the current one arrived.
    /// Forms walk their trees on it; an application's own texts outside
    /// the controls should re-read too.</summary>
    public static event EventHandler? Changed;

    /// <summary>Grows with every change. An element compares it with the version
    /// its texts were resolved for, to catch up after being out of every tree.</summary>
    public static int Version { get; private set; }

    /// <summary>The current culture. By default — the thread's CurrentUICulture
    /// at the first access.</summary>
    public static CultureInfo Culture
    {
        get
        {
            lock (Sync)
                return _culture ?? Initialize(CultureInfo.CurrentUICulture);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            lock (Sync)
            {
                if (_culture is not null && _culture.Name == value.Name) return;

                Initialize(value);
            }

            RaiseChanged();
        }
    }

    /// <summary>Mirror the layout for right-to-left cultures: Arabic, Hebrew, Persian.</summary>
    /// <remarks>
    /// Only the layout is mirrored. Text itself is not shaped or reordered yet —
    /// that needs a shaping engine the framework doesn't have. An element or
    /// a form with its own FlowDirection keeps it either way.
    /// </remarks>
    public static bool MirrorLayoutForRightToLeft
    {
        get => _mirror;
        set
        {
            lock (Sync)
            {
                if (_mirror == value) return;

                _mirror = value;
                _layoutDirection = DirectionOf(_culture ?? Initialize(CultureInfo.CurrentUICulture));
            }

            RaiseChanged();
        }
    }

    /// <summary>The layout direction the culture gives to elements that set none.
    /// Read by layout for every element, so it is a cached field.</summary>
    public static FlowDirection LayoutDirection
    {
        get
        {
            if (_culture is null) _ = Culture;

            return _layoutDirection;
        }
    }

    /// <summary>Add a table. A table for the language already on screen
    /// updates the texts right away.</summary>
    public static void Register(StringTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        bool affectsCurrent;

        lock (Sync)
        {
            if (!Tables.TryGetValue(table.CultureName, out List<StringTable>? list))
                Tables[table.CultureName] = list = [];

            list.Add(table);

            affectsCurrent = _culture is not null &&
                Array.Exists(_chain, name => string.Equals(name, table.CultureName, StringComparison.OrdinalIgnoreCase));
        }

        if (affectsCurrent) RaiseChanged();
    }

    public static string Get(TextKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (Sync)
        {
            EnsureInitialized();

            foreach (string name in _chain)
            {
                if (!Tables.TryGetValue(name, out List<StringTable>? list)) continue;

                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].TryGet(key, out string text))
                        return text;
            }
        }

        return key.DefaultText;
    }

    /// <summary>The text with {0}-style placeholders filled in, formatted
    /// by the current culture.</summary>
    public static string Get(TextKey key, params object?[] args) =>
        args.Length == 0 ? Get(key) : string.Format(Culture, Get(key), args);

    /// <summary>The form of a plural text for the count. The count is {0};
    /// further arguments are {1}, {2} and so on.</summary>
    public static string Get(PluralKey key, long count, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(key);

        CultureInfo culture = Culture;
        string? text = null;

        lock (Sync)
        {
            PluralCategory category = PluralRules.Select(culture, count);

            foreach (string name in _chain)
            {
                if (!Tables.TryGetValue(name, out List<StringTable>? list)) continue;

                for (int i = list.Count - 1; i >= 0 && text is null; i--)
                    if (list[i].TryGet(key, category, out string found))
                        text = found;

                if (text is not null) break;
            }
        }

        // no table for the language: the English forms go by the English rule,
        // not by the current language's — "2 files", not the Russian "few" form
        text ??= key.DefaultFor(count);

        object?[] formatArgs = new object?[args.Length + 1];
        formatArgs[0] = count;
        args.CopyTo(formatArgs, 1);

        return string.Format(culture, text, formatArgs);
    }

    private static void EnsureInitialized()
    {
        if (_culture is null) Initialize(CultureInfo.CurrentUICulture);
    }

    /// <summary>Set the culture and everything derived from it. Called under the lock.</summary>
    private static CultureInfo Initialize(CultureInfo culture)
    {
        _culture = culture;

        var chain = new List<string>();

        // up to the invariant culture, which has an empty name: that link is
        // the key's own English text, not a table
        for (CultureInfo current = culture; !string.IsNullOrEmpty(current.Name); current = current.Parent)
            chain.Add(current.Name);

        _chain = [.. chain];
        _layoutDirection = DirectionOf(culture);

        return culture;
    }

    private static FlowDirection DirectionOf(CultureInfo culture) =>
        _mirror && culture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

    private static void RaiseChanged()
    {
        Version++;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}