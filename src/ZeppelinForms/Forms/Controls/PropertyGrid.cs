using System.Diagnostics.CodeAnalysis;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using INotifyPropertyChanged = System.ComponentModel.INotifyPropertyChanged;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;

namespace ZeppelinForms.Forms.Controls;

/// <summary>The properties of one object or of several, by category, each with an
/// editor of its type: a model's bones, a control in the inspector.</summary>
/// <remarks>
/// <para>
/// The rows come from <see cref="PropertyCatalog"/>, or from
/// <see cref="PropertiesProvider"/> for objects that describe themselves. Several
/// selected objects show the properties they all have; where their values differ,
/// the editor shows none, and a value typed goes to all of them.
/// </para>
/// <para>
/// An editor is chosen by the descriptor's own <see cref="PropertyDescriptor.Editor"/>,
/// then by <see cref="Editors"/>, then by the editors every grid has
/// (<see cref="RegisterDefaultEditor"/>), then by the built-in ones: a check box, a
/// text field, a number field in the descriptor's range and precision, a list for an
/// enum, a list of check boxes for a flags enum, a color picker, and fields per part
/// for vectors, quaternions, points, sizes, thicknesses and corner radii.
/// </para>
/// <para>
/// Every edit ends in one <see cref="PropertyValueChanged"/> with the values it
/// replaced — one step for undo, however many times the wheel turned on the way.
/// </para>
/// </remarks>
public partial class PropertyGrid : DecoratedPanel
{
    internal const float RowHeight = 28f;
    internal const float CategoryHeight = 26f;

    private const float TitleHeight = 22f;
    private const float FilterHeight = 28f;
    private const float ToolbarGap = 6f;

    /// <summary>The editors every grid has, by type — what a library of models adds for
    /// its vectors, before any grid is made.</summary>
    private static readonly Dictionary<Type, Func<PropertyEditContext, UIElement>> s_defaultEditors = [];
    private static readonly System.Threading.Lock s_defaultEditorsSync = new();

    private readonly Label _title;
    private readonly TextBox _filter;
    private readonly StackPanel _list;

    /// <summary>The categories folded by the user: kept across selections, as a property
    /// browser does — whoever folded "Accessibility" doesn't want it back on the next click.</summary>
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    private IReadOnlyList<object> _targets = [];

    /// <summary>A write of the grid's own is in progress: the targets' change
    /// notifications are its echo, not a change from outside.</summary>
    private bool _applying;

    /// <summary>The edit in progress: its context and what the objects had before it.</summary>
    private (PropertyEditContext Context, object?[] OldValues)? _edit;

    /// <summary>A change from outside is waiting for the rows to show it.</summary>
    private bool _refreshPending;

    /// <summary>The object shown; with several selected — the first of them. Setting it
    /// shows that object alone; null shows nothing.</summary>
    public object? SelectedObject
    {
        get => _targets.Count == 0 ? null : _targets[0];
        set => SelectedObjects = value is null ? [] : [value];
    }

    /// <summary>The objects edited together: the rows are the properties all of them
    /// have, and a value written goes to each.</summary>
    public IReadOnlyList<object> SelectedObjects
    {
        get => _targets;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Count == _targets.Count && value.Zip(_targets).All(pair => ReferenceEquals(pair.First, pair.Second)))
                return;

            // what was being typed belongs to the objects it was typed for
            CommitEdit();

            Unsubscribe();
            _targets = [.. value];
            Subscribe();

            Rebuild();
        }
    }

    [Obsolete("Not used since 0.16: the names take NameColor, which the theme sets.")]
    public Color RowTextColor { get; set; } = Colors.Black;

    /// <summary>The stripe under every other row. A styled property, so the theme
    /// sets it: the fixed light gray stayed light in the dark theme, under
    /// the theme's light text.</summary>
    [Styled(Category = "Appearance")]
    public partial Color AlternateRowColor { get; set; }
    private static Color AlternateRowColorDefault => new(255, 248, 248, 248);

    /// <summary>The band of a category's header.</summary>
    [Styled(Category = "Appearance")]
    public partial Color CategoryColor { get; set; }
    private static Color CategoryColorDefault => new(255, 240, 241, 243);

    [Styled(Category = "Appearance")]
    public partial Color CategoryTextColor { get; set; }
    private static Color CategoryTextColorDefault => new(255, 60, 60, 60);

    /// <summary>The lines between the rows and between the columns.</summary>
    [Styled(Category = "Appearance")]
    public partial Color SeparatorColor { get; set; }
    private static Color SeparatorColorDefault => new(255, 228, 228, 231);

    [Styled(Category = "Appearance")]
    public partial Color RowHoverColor { get; set; }
    private static Color RowHoverColorDefault => new(14, 0, 0, 0);

    /// <summary>The property names in the left column.</summary>
    [Styled(Category = "Appearance")]
    public partial Color NameColor { get; set; }
    private static Color NameColorDefault => new(255, 80, 80, 86);

    /// <summary>The mark of a value set in code or bound, and of the row being edited.</summary>
    [Styled(Category = "Appearance")]
    public partial Color MarkerColor { get; set; }
    private static Color MarkerColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    /// <summary>The rows of an object; null — <see cref="PropertyCatalog.For"/> of its type.
    /// For objects that describe themselves — a bone with a different set of properties
    /// than a helper, though both are the same class.</summary>
    public Func<object, IReadOnlyList<PropertyDescriptor>>? PropertiesProvider
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Rebuild();
        }
    }

    /// <summary>The editors of this grid, by the property's type: they come before the
    /// ones every grid has and before the built-in ones. Changing them shows on the next
    /// <see cref="Refresh"/> or selection.</summary>
    public IDictionary<Type, Func<PropertyEditContext, UIElement>> Editors { get; } =
        new Dictionary<Type, Func<PropertyEditContext, UIElement>>();

    /// <summary>The rows grouped under their categories, each folding away; false — one
    /// list in the order of the names.</summary>
    public bool GroupByCategory
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Rebuild();
        }
    } = true;

    /// <summary>The name of what is shown, and a field filtering the rows by name.</summary>
    public bool ShowToolbar
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            _title.IsVisible = value;
            _filter.IsVisible = value;
            Invalidate();
        }
    } = true;

    /// <summary>Only the rows whose name contains this, in any case. Empty — all rows.</summary>
    public string Filter
    {
        get => _filter.Text ?? string.Empty;
        set => _filter.Text = value;
    }

    /// <summary>The share of the width the names take; the border between the columns can
    /// also be dragged. Kept between 0.15 and 0.85.</summary>
    public float NameColumnRatio
    {
        get;
        set
        {
            value = Math.Clamp(float.IsFinite(value) ? value : 0.42f, 0.15f, 0.85f);

            if (field == value) return;

            field = value;

            foreach (UIElement row in _list.Children)
                row.Invalidate();
        }
    } = 0.42f;

    /// <summary>A value is about to be written; it can be refused.</summary>
    public event EventHandler<PropertyValueChangingEventArgs>? PropertyValueChanging;

    /// <summary>An edit ended with a change: the values it replaced, for undo.</summary>
    public event EventHandler<PropertyValueChangedEventArgs>? PropertyValueChanged;

    public PropertyGrid()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        SetControlDefault(IsVisibleProperty, false);

        _title = new Label
        {
            HorizontalContentAlign = HorizontalContentAlignment.Left,
            VerticalContentAlign = VerticalContentAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _filter = new TextBox
        {
            Watermark = "Filter properties",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        _filter.TextChanged += (_, _) => Rebuild();

        _list = new StackPanel
        {
            Orientation = Orientation.Vertical,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        Children.Add(_title);
        Children.Add(_filter);
        Children.Add(_list);
    }

    // ===== editors every grid has =====

    /// <summary>The editor of a type for every grid — for a library whose models have
    /// types of their own. A grid's <see cref="Editors"/> come before it.</summary>
    public static void RegisterDefaultEditor(Type type, Func<PropertyEditContext, UIElement> editor)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(editor);

        lock (s_defaultEditorsSync)
            s_defaultEditors[type] = editor;
    }

    private static Func<PropertyEditContext, UIElement>? DefaultEditorFor(Type type)
    {
        lock (s_defaultEditorsSync)
            return s_defaultEditors.TryGetValue(type, out Func<PropertyEditContext, UIElement>? editor) ? editor : null;
    }

    // ===== rows =====

    /// <summary>Read the values again and show them — after a change the objects don't
    /// tell about, or a change of <see cref="Editors"/>.</summary>
    public void Refresh()
    {
        _refreshPending = false;
        Rebuild();
    }

    /// <summary>End the edit in progress, as leaving its field would.</summary>
    public void CommitEdit()
    {
        if (_edit is { } edit)
            CommitValue(edit.Context);
    }

    internal bool IsCategoryCollapsed(string category) => _collapsed.Contains(category);

    internal void ToggleCategory(string category)
    {
        if (!_collapsed.Remove(category))
            _collapsed.Add(category);

        Rebuild();
    }

    private void Rebuild()
    {
        // an empty grid is not shown: an invisible element drops out
        // of both drawing and hit testing, so clicks pass through
        IsVisible = _targets.Count > 0;

        _title.Text = Describe(_targets);

        while (_list.Children.Count > 0)
            _list.Children.RemoveAt(_list.Children.Count - 1);

        if (_targets.Count == 0)
        {
            Invalidate();
            return;
        }

        string filter = Filter.Trim();

        // a row is the descriptors of one property, a descriptor per object: objects of
        // different kinds read and write the same-named property each its own way
        List<PropertyDescriptor[]> rows = [.. RowsOf(_targets).Where(row =>
            filter.Length == 0 ||
            row[0].Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            (row[0].DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))];

        int stripe = 0;

        if (!GroupByCategory)
        {
            foreach (PropertyDescriptor[] row in rows.OrderBy(r => r[0].DisplayName ?? r[0].Name, StringComparer.CurrentCultureIgnoreCase))
                _list.Children.Add(CreateRow(row, stripe++));
        }
        else
        {
            // the categories in the order they first come: the catalog puts the styling
            // first, then what the control declares, then the styled properties
            foreach (IGrouping<string, PropertyDescriptor[]> category in rows.GroupBy(r => r[0].Category))
            {
                List<PropertyDescriptor[]> members = [.. category];

                _list.Children.Add(new PropertyGridCategory(this, category.Key, members.Count));

                // a filter opens every category: what it found must be seen
                if (_collapsed.Contains(category.Key) && filter.Length == 0) continue;

                stripe = 0;

                foreach (PropertyDescriptor[] row in members)
                    _list.Children.Add(CreateRow(row, stripe++));
            }
        }

        Invalidate();
    }

    private PropertyGridRow CreateRow(PropertyDescriptor[] descriptors, int index)
    {
        var context = new PropertyEditContext(this, descriptors, _targets);

        // the stripe under every other row — that way the eye doesn't lose the name/value pair
        return new PropertyGridRow(this, context, CreateEditor(context)) { IsStriped = index % 2 == 1 };
    }

    /// <summary>"Button · okButton", "3 objects" — what the rows are of.</summary>
    private static string Describe(IReadOnlyList<object> targets) => targets switch
    {
        [] => string.Empty,
        [UIElement { Name: { Length: > 0 } name } element] => $"{element.GetType().Name} · {name}",
        [var single] => single.GetType().Name,
        _ => $"{targets.Count} objects",
    };

    /// <summary>The rows of the objects: those of the first one that every other one
    /// has too — by name and type, so a "Size" of one kind isn't edited as another —
    /// each with the descriptor of every object, in the order of the objects.</summary>
    private List<PropertyDescriptor[]> RowsOf(IReadOnlyList<object> targets)
    {
        IReadOnlyList<PropertyDescriptor>[] all = [.. targets.Select(DescriptorsOf)];

        var rows = new List<PropertyDescriptor[]>();

        foreach (PropertyDescriptor first in all[0])
        {
            var row = new PropertyDescriptor[targets.Count];
            row[0] = first;

            bool everyone = true;

            for (int i = 1; i < targets.Count && everyone; i++)
            {
                PropertyDescriptor? match = null;

                foreach (PropertyDescriptor candidate in all[i])
                {
                    if (candidate.Name == first.Name && candidate.Type == first.Type)
                    {
                        match = candidate;
                        break;
                    }
                }

                if (match is null) everyone = false;
                else row[i] = match;
            }

            // a property one of the objects doesn't have is no row: a value written
            // there would go to some of them and silently miss the rest
            if (everyone)
                rows.Add(row);
        }

        return rows;
    }

    private IReadOnlyList<PropertyDescriptor> DescriptorsOf(object target) =>
        PropertiesProvider?.Invoke(target) ?? CatalogOf(target);

    /// <summary>The rows of an object: what <see cref="PropertyCatalog"/> knows of its type.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "The type of a live object is kept, and so is its static constructor whenever it " +
                        "registers styled properties: the generated accessors read the fields it initializes.")]
    private static PropertyDescriptor[] CatalogOf(object target) => PropertyCatalog.For(target.GetType());

    private UIElement CreateEditor(PropertyEditContext context)
    {
        PropertyDescriptor property = context.Property;

        if (context.IsReadOnly)
            return ReadOnlyLabel(context.IsMixed ? null : context.Value);

        Func<PropertyEditContext, UIElement>? editor =
            property.Editor ??
            (Editors.TryGetValue(property.Type, out Func<PropertyEditContext, UIElement>? own) ? own : null) ??
            DefaultEditorFor(property.Type);

        return editor?.Invoke(context) ?? BuiltInEditor(context) ?? ReadOnlyLabel(context.Value);
    }

    // ===== writing =====

    internal bool ChangeValue(PropertyEditContext context, object? value)
    {
        if (context.Targets.Count == 0) return false;

        // a change of another property ends the edit of the previous one
        if (_edit is { } open && !ReferenceEquals(open.Context, context))
            CommitValue(open.Context);

        var args = new PropertyValueChangingEventArgs(context.Property, context.Targets, value);
        PropertyValueChanging?.Invoke(this, args);

        if (args.Cancel) return false;

        _edit ??= (context, [.. context.Targets.Select(context.GetValue)]);

        // loop protection: editing a property rebuilds the target control,
        // it calls Invalidate, and we must not rebuild the grid because of that
        _applying = true;

        try
        {
            for (int i = 0; i < context.Targets.Count; i++)
            {
                context.Descriptors[i].SetValue(context.Targets[i], value);

                if (context.Targets[i] is UIElement element)
                    element.Invalidate();
            }
        }
        finally
        {
            _applying = false;
        }

        // the mark of the value's source changes with the write: set here now
        InvalidateVisual();

        return true;
    }

    internal void CommitValue(PropertyEditContext context)
    {
        if (_edit is not { } edit || !ReferenceEquals(edit.Context, context)) return;

        _edit = null;

        object? now = context.GetValue(context.Targets[0], 0);

        bool changed = false;

        for (int i = 0; i < context.Targets.Count && !changed; i++)
            changed = !Equals(edit.OldValues[i], context.GetValue(context.Targets[i], i));

        if (changed)
        {
            PropertyValueChanged?.Invoke(this, new PropertyValueChangedEventArgs(
                context.Property, context.Targets, edit.OldValues, now)
            { Descriptors = context.Descriptors });
        }
    }

    // ===== changes from outside =====

    private void Subscribe()
    {
        foreach (object target in _targets)
            if (target is INotifyPropertyChanged notifying)
                notifying.PropertyChanged += OnTargetPropertyChanged;
    }

    private void Unsubscribe()
    {
        foreach (object target in _targets)
            if (target is INotifyPropertyChanged notifying)
                notifying.PropertyChanged -= OnTargetPropertyChanged;
    }

    /// <summary>A target changed by itself — undo, an animation, the theme: the rows are
    /// read again once, after the burst — a theme switch changes dozens of properties.</summary>
    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applying || _refreshPending) return;

        _refreshPending = true;

        if (FindForm() is { } form)
            form.BeginInvoke(RefreshIfPending);
        else
            RefreshIfPending();
    }

    private void RefreshIfPending()
    {
        if (!_refreshPending) return;

        // a field being typed in is not swept from under the user's fingers: the
        // values are read again when the edit ends and the next change comes
        if (_edit is not null)
        {
            _refreshPending = false;
            return;
        }

        Refresh();
    }

    protected override void OnDetached()
    {
        CommitEdit();
        base.OnDetached();
    }

    // ===== layout =====

    // the background, border and corner radius are drawn by the base — only the toolbar's border here
    protected override void DrawContent(Graphics g)
    {
        if (!ShowToolbar) return;

        Rectangle content = ContentBounds;
        float y = content.Y + ToolbarHeight - ToolbarGap / 2f;

        g.DrawLine(new Point(content.X, y), new Point(content.X + content.Width, y), SeparatorColor, 1f);
    }

    private float ToolbarHeight => ShowToolbar ? TitleHeight + FilterHeight + ToolbarGap * 2 : 0f;

    protected override Size MeasureContentOverride(Size availableSize)
    {
        // with infinite width (that's how overlays are measured) percentages
        // and subtractions give NaN, so we rely on our own set size
        float usableWidth = float.IsFinite(availableSize.Width)
            ? availableSize.Width
            : (float.IsFinite(Size.Width) ? Size.Width : 320f);

        float innerWidth = Math.Max(0, usableWidth - Padding.Horizontal);

        if (ShowToolbar)
        {
            _title.Measure(new Size(innerWidth, TitleHeight));
            _filter.Measure(new Size(innerWidth, FilterHeight));
        }

        float listHeight = float.IsFinite(availableSize.Height)
            ? Math.Max(0, availableSize.Height - Padding.Vertical - ToolbarHeight)
            : float.PositiveInfinity;

        _list.Measure(new Size(innerWidth, listHeight));

        return ResolveSize(
            new Size(innerWidth + Padding.Horizontal, ToolbarHeight + _list.DesiredSize.Height + Padding.Vertical),
            availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var content = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical)));

        float y = content.Y;

        if (ShowToolbar)
        {
            _title.Arrange(new Rectangle(new Point(content.X + 4f, y), new Size(Math.Max(0, content.Width - 8f), TitleHeight)));
            _filter.Arrange(new Rectangle(new Point(content.X, y + TitleHeight + ToolbarGap / 2f), new Size(content.Width, FilterHeight)));

            y += ToolbarHeight;
        }

        _list.Arrange(new Rectangle(new Point(content.X, y), new Size(content.Width, Math.Max(0, content.Bottom - y))));
    }
}