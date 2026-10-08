using System.Collections.ObjectModel;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// An ordered set of styles in one scope: the application (<see cref="App.Styles"/>),
/// a form (<see cref="Form.Styles"/>) or an element and its descendants
/// (<see cref="Controls.Base.UIElement.Styles"/>).
/// </summary>
/// <remarks>
/// The collection is live: adding, removing or changing a style restyles what the
/// scope covers. Of two styles of equal specificity the one later in the collection
/// wins, and a nearer scope beats a farther one — an element's own styles beat the
/// form's, the form's beat the application's.
/// </remarks>
public sealed class Styles : Collection<Style>
{
    private readonly Action _changed;

    /// <summary>Raised after any change of the set or of a style in it.</summary>
    public event EventHandler? Changed;

    internal Styles(Action changed)
    {
        _changed = changed;
    }

    /// <summary>Add several styles at once, restyling once.</summary>
    public void AddRange(IEnumerable<Style> styles)
    {
        ArgumentNullException.ThrowIfNull(styles);

        _batch++;

        try
        {
            foreach (Style style in styles)
                Add(style);
        }
        finally
        {
            _batch--;
        }

        Notify();
    }

    /// <summary>Replace the whole set at once, restyling once: what a reloaded
    /// style sheet does.</summary>
    public void Replace(IEnumerable<Style> styles)
    {
        ArgumentNullException.ThrowIfNull(styles);

        _batch++;

        try
        {
            Clear();

            foreach (Style style in styles)
                Add(style);
        }
        finally
        {
            _batch--;
        }

        Notify();
    }

    private int _batch;

    /// <summary>Add a style sheet's styles, keeping the link to them: the link
    /// reloads the sheet in place or removes it.</summary>
    public StyleSheetLink Add(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        AddRange(sheet.Styles);

        return new StyleSheetLink(this, sheet);
    }

    /// <summary>Load a <c>.zss</c> file and add its styles.</summary>
    /// <param name="watch">Reload the sheet when it or one of its imports is saved —
    /// styles edited live, in a running application. Meant for development: a
    /// published application has nobody editing its sheets.</param>
    public StyleSheetLink Load(string path, bool watch = false)
    {
        StyleSheetLink link = Add(StyleSheet.Load(path));

        if (watch) link.Watch();

        return link;
    }

    /// <summary>Put new styles in place of old ones, restyling once: a reloaded
    /// sheet keeps its place among the other styles, and with it its strength
    /// at equal specificity.</summary>
    internal void ReplaceRange(IReadOnlyList<Style> old, IReadOnlyList<Style> replacement)
    {
        int at = Count;

        if (old.Count > 0)
        {
            int first = IndexOf(old[0]);

            if (first >= 0) at = first;
        }

        _batch++;

        try
        {
            foreach (Style style in old)
            {
                int index = IndexOf(style);

                if (index < 0) continue;

                RemoveAt(index);

                if (index < at) at--;
            }

            at = Math.Min(at, Count);

            foreach (Style style in replacement)
                Insert(at++, style);
        }
        finally
        {
            _batch--;
        }

        Notify();
    }

    protected override void InsertItem(int index, Style item)
    {
        ArgumentNullException.ThrowIfNull(item);

        StyleUsage.Register(item.Selector);
        item.Changed += OnStyleChanged;

        base.InsertItem(index, item);
        Notify();
    }

    protected override void SetItem(int index, Style item)
    {
        ArgumentNullException.ThrowIfNull(item);

        StyleUsage.Register(item.Selector);

        this[index].Changed -= OnStyleChanged;
        item.Changed += OnStyleChanged;

        base.SetItem(index, item);
        Notify();
    }

    protected override void RemoveItem(int index)
    {
        this[index].Changed -= OnStyleChanged;

        base.RemoveItem(index);
        Notify();
    }

    protected override void ClearItems()
    {
        foreach (Style style in this)
            style.Changed -= OnStyleChanged;

        base.ClearItems();
        Notify();
    }

    private void OnStyleChanged(object? sender, EventArgs e) => Notify();

    private void Notify()
    {
        if (_batch > 0) return;

        StyleUsage.BumpVersion();

        _changed();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}