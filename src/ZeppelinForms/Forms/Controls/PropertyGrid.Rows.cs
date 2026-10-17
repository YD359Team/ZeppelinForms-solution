using System.Globalization;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>The header of a category: a chevron and the name, on a band of its own;
/// a click folds the category's rows away or opens them.</summary>
internal sealed class PropertyGridCategory : DecoratedControl
{
    private readonly PropertyGrid _grid;

    public PropertyGridCategory(PropertyGrid grid, string name, int count)
    {
        _grid = grid;
        CategoryName = name;
        Count = count;

        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        Cursor = CursorKind.Hand;
    }

    public string CategoryName { get; }

    /// <summary>How many rows the category has — shown while it is folded, so a folded
    /// category still says it isn't empty.</summary>
    public int Count { get; }

    private bool IsCollapsed => _grid.IsCategoryCollapsed(CategoryName);

    protected override void DrawContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;

        g.FillRectangle(bounds, _grid.CategoryColor);
        g.DrawLine(new Point(0, bounds.Bottom - 0.5f), new Point(bounds.Width, bounds.Bottom - 0.5f),
            _grid.SeparatorColor, 1f);

        float middle = bounds.Height / 2f;

        Tools.Glyphs.DrawChevron(g, new Point(12f, middle), 3.5f, !IsCollapsed, _grid.CategoryTextColor, 1.6f);

        string caption = IsCollapsed ? $"{CategoryName}  ·  {Count}" : CategoryName;

        Font font = EffectiveFont with { Weight = FontWeight.SemiBold };

        g.DrawText(caption,
            new Rectangle(new Point(24f, 0), new Size(Math.Max(0, bounds.Width - 28f), bounds.Height)),
            _grid.CategoryTextColor, font,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        _grid.ToggleCategory(CategoryName);
        e.Handled = true;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(0, PropertyGrid.CategoryHeight), availableSize);
}

/// <summary>One property: its name in the left column, its editor in the right one,
/// a mark of where the value came from, and the border between the columns, which
/// can be dragged.</summary>
internal sealed class PropertyGridRow : DecoratedPanel
{
    /// <summary>How close to the column border the pointer grabs it.</summary>
    private const float GrabDistance = 4f;

    /// <summary>Room left of the name for the mark of the value's source.</summary>
    private const float MarkWidth = 16f;

    private readonly PropertyGrid _grid;

    private bool _dragging;

    public PropertyGridRow(PropertyGrid grid, PropertyEditContext context, UIElement editor)
    {
        _grid = grid;
        Context = context;
        Editor = editor;

        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

        Children.Add(editor);
    }

    public PropertyEditContext Context { get; }

    public PropertyDescriptor Property => Context.Property;

    public UIElement Editor { get; private set; }

    /// <summary>Every other row is striped. Drawn with the grid's color as it is now,
    /// not copied into the row: a theme switch recolors the stripes with the grid.</summary>
    public bool IsStriped { get; init; }

    /// <summary>Put another editor in place — the value changed from outside the grid.</summary>
    public void ReplaceEditor(UIElement editor)
    {
        Children.Remove(Editor);
        Editor = editor;
        Children.Add(editor);
        Invalidate();
    }

    private string Caption => Property.DisplayName ?? Property.Name;

    /// <summary>Where the value came from, for a styled property of a single element.</summary>
    private ValueSource? Source =>
        Property.StyledProperty is { } styled && Context.Targets is [UIElement element]
            ? element.GetValueSource(styled)
            : null;

    private float SplitX => ContentBounds.X + ContentBounds.Width * _grid.NameColumnRatio;

    /// <summary>The editor has the keyboard: the row is marked, so the eye finds which
    /// property the keys go to.</summary>
    private bool HasFocusWithin
    {
        get
        {
            for (UIElement? node = FindForm()?.FocusedElement; node is not null; node = node.Parent)
            {
                if (ReferenceEquals(node, this)) return true;
            }

            return false;
        }
    }

    protected override void DrawContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;
        float split = SplitX;
        float middle = bounds.Height / 2f;

        if (IsStriped)
            g.FillRectangle(bounds, _grid.AlternateRowColor);

        if (HasFocusWithin)
        {
            g.FillRectangle(bounds, _grid.RowHoverColor);
            g.FillRectangle(new Rectangle(Point.Empty, new Size(2f, bounds.Height)), _grid.MarkerColor);
        }
        else if (IsHovered)
        {
            g.FillRectangle(bounds, _grid.RowHoverColor);
        }

        DrawSourceMark(g, new Point(MarkWidth / 2f + 2f, middle));

        // the name keeps to its column: a long one is cut at the border, not drawn under the editor
        g.Save();
        g.ClipRect(new Rectangle(new Point(MarkWidth, 0), new Size(Math.Max(0, split - MarkWidth - 6f), bounds.Height)));

        g.DrawText(Caption,
            new Rectangle(new Point(MarkWidth, 0), new Size(Math.Max(0, split - MarkWidth - 6f), bounds.Height)),
            Property.IsReadOnly ? _grid.NameColor.WithA((byte)(_grid.NameColor.A * 2 / 3)) : _grid.NameColor,
            EffectiveFont, HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

        g.Restore();

        // the border between the columns, and the one under the row
        g.DrawLine(new Point(split, 4f), new Point(split, bounds.Height - 4f), _grid.SeparatorColor, 1f);
        g.DrawLine(new Point(0, bounds.Height - 0.5f), new Point(bounds.Width, bounds.Height - 0.5f), _grid.SeparatorColor, 1f);
    }

    /// <summary>A dot left of the name: filled — set right here, in code or in the grid;
    /// a ring — bound; a pale dot — a style; a pale ring — the theme. No mark — the
    /// control's default. The same ladder the tooltip tells in words.</summary>
    private void DrawSourceMark(Graphics g, Point center)
    {
        if (Source is not { } source) return;

        Color strong = _grid.MarkerColor;
        Color pale = _grid.NameColor.WithA((byte)(_grid.NameColor.A / 2));

        var dot = new Rectangle(new Point(center.X - 3.5f, center.Y - 3.5f), new Size(7f, 7f));

        switch (source)
        {
            case ValueSource.Local:
                g.FillEllipse(dot, strong);
                break;

            case ValueSource.Binding:
                g.DrawEllipse(dot, strong, 1.5f);
                break;

            case ValueSource.Style:
                g.FillEllipse(dot, pale);
                break;

            case ValueSource.Theme:
                g.DrawEllipse(dot, pale, 1.5f);
                break;
        }
    }

    protected internal override string? GetToolTip(Point location)
    {
        float localX = location.X - GetAbsolutePosition().X;

        if (localX > SplitX) return null;

        string? source = Source switch
        {
            ValueSource.Local => "Set in code or here",
            ValueSource.Binding => "Bound",
            ValueSource.Style => "From a style",
            ValueSource.Theme => "From the theme",
            ValueSource.ControlDefault => "The control's default",
            _ => null,
        };

        string head = Property.DisplayName is { } shown && shown != Property.Name
            ? $"{shown} ({Property.Name})"
            : Property.Name;

        IEnumerable<string> lines = new[] { head, Property.Description, source }.OfType<string>();

        return string.Join(Environment.NewLine, lines);
    }

    // ===== the column border =====

    private bool IsNearSplit(Point location) =>
        Math.Abs(location.X - GetAbsolutePosition().X - SplitX) <= GrabDistance;

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (_dragging)
        {
            float localX = e.Location.X - GetAbsolutePosition().X - ContentBounds.X;
            _grid.NameColumnRatio = ContentBounds.Width <= 0 ? 0.5f : localX / ContentBounds.Width;
            return;
        }

        Cursor = IsNearSplit(e.Location) ? CursorKind.SizeWestEast : CursorKind.Default;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left || !IsNearSplit(e.Location)) return;

        _dragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 320f;
        float editorWidth = Math.Max(0, width * (1f - _grid.NameColumnRatio) - 8f);

        Editor.Measure(new Size(editorWidth, PropertyGrid.RowHeight - 4f));

        return ResolveSize(new Size(width, PropertyGrid.RowHeight), availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        float split = ContentBounds.X + contentSize.Width * _grid.NameColumnRatio;

        Editor.Arrange(new Rectangle(
            new Point(split + 4f, 2f),
            new Size(Math.Max(0, contentSize.Width - split - 6f), Math.Max(0, contentSize.Height - 4f))));
    }
}

/// <summary>A struct of numbers edited as its parts — X, Y, Z of a vector, the four
/// sides of a thickness — each in a field of its own, the fields sharing the width.</summary>
internal sealed class PropertyGridComponents : DecoratedPanel
{
    private const float Gap = 4f;

    /// <summary>The room of the part names: a letter, or two — "TL" of a corner radius.</summary>
    private readonly float _labelWidth;

    private readonly List<(Label Axis, TextBox Field)> _parts = [];

    public PropertyGridComponents(
        PropertyEditContext context,
        string[] names,
        Func<object, float[]> split,
        Func<float[], object> join,
        int decimals)
    {
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

        _labelWidth = 6f + 7f * names.Max(name => name.Length);

        float[] current = context.Value is { } value && !context.IsMixed ? split(value) : new float[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            int index = i;

            var axis = new Label
            {
                Text = names[i],
                HorizontalContentAlign = HorizontalContentAlignment.Center,
                VerticalContentAlign = VerticalContentAlignment.Center,
                IsHitTestVisible = false,
            };

            var field = new TextBox
            {
                Text = context.IsMixed ? string.Empty : Format(current[i], decimals),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            // the part is written when the field is left or Enter is pressed: a number
            // half typed — "1." — is not a value yet, and writing it per keystroke would
            // throw the model's geometry around under the user's fingers
            void Write()
            {
                if (!float.TryParse(field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float typed) &&
                    !float.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out typed))
                    return;

                // the other parts as the objects have them now, not as they were:
                // another field may have been written since
                float[] parts = context.Targets[0] is { } target && context.Property.GetValue(target) is { } now
                    ? split(now)
                    : new float[names.Length];

                if (parts[index].Equals(typed)) return;

                parts[index] = typed;

                if (context.Change(join(parts)))
                    context.Commit();
            }

            field.LostFocus += (_, _) => Write();
            field.KeyDown += (_, e) =>
            {
                if (e.Key != Input.Keyboard.Key.Enter) return;

                Write();
                e.Handled = true;
            };

            Children.Add(axis);
            Children.Add(field);
            _parts.Add((axis, field));
        }
    }

    private static string Format(float value, int decimals) =>
        float.IsFinite(value)
            ? Math.Round(value, decimals).ToString("0." + new string('#', Math.Max(1, decimals)), CultureInfo.CurrentCulture)
            : string.Empty;

    protected override Size MeasureContentOverride(Size availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 160f;
        float height = float.IsFinite(availableSize.Height) ? availableSize.Height : PropertyGrid.RowHeight;
        float each = PartWidth(width);

        foreach ((Label axis, TextBox field) in _parts)
        {
            axis.Measure(new Size(_labelWidth, height));
            field.Measure(new Size(Math.Max(0, each - _labelWidth), height));
        }

        return ResolveSize(new Size(width, height), availableSize);
    }

    private float PartWidth(float width) =>
        _parts.Count == 0 ? 0 : Math.Max(0, (width - Gap * (_parts.Count - 1)) / _parts.Count);

    protected override void ArrangeContentOverride(Size contentSize)
    {
        float each = PartWidth(contentSize.Width);
        float x = 0;

        foreach ((Label axis, TextBox field) in _parts)
        {
            axis.Arrange(new Rectangle(new Point(x, 0), new Size(_labelWidth, contentSize.Height)));
            field.Arrange(new Rectangle(
                new Point(x + _labelWidth, 0),
                new Size(Math.Max(0, each - _labelWidth), contentSize.Height)));

            x += each + Gap;
        }
    }
}