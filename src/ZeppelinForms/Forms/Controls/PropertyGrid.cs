using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

public partial class PropertyGrid : DecoratedPanel
{
    private const float RowHeight = 26f;
    private const float LabelRatio = 0.45f;

    private object? _target;
    private bool _isUpdating;

    public object? SelectedObject
    {
        get => _target;
        set
        {
            if (ReferenceEquals(_target, value)) return;

            _target = value;
            Rebuild();
        }
    }

    public Color RowTextColor { get; set; } = Colors.Black;

    /// <summary>The stripe under every other row. A styled property, so the theme
    /// sets it: the fixed light gray stayed light in the dark theme, under
    /// the theme's light text.</summary>
    [Styled(Category = "Appearance")]
    public partial Color AlternateRowColor { get; set; }
    private static Color AlternateRowColorDefault => new(255, 248, 248, 248);

    public PropertyGrid()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        SetControlDefault(IsVisibleProperty, false);
    }

    private void Rebuild()
    {
        while (Children.Count > 0)
            Children.RemoveAt(Children.Count - 1);

        // an empty grid is not shown: an invisible element drops out
        // of both drawing and hit testing, so clicks pass through
        IsVisible = _target is not null;

        if (_target is null)
        {
            Invalidate();
            return;
        }

        foreach (PropertyDescriptor property in RowsOf(_target))
        {
            Children.Add(new Label
            {
                Text = Caption(property),
                HorizontalContentAlign = HorizontalContentAlignment.Left,
                VerticalContentAlign = VerticalContentAlignment.Center,
                Padding = new Thickness(6, 2),
            });

            Children.Add(CreateEditor(property));
        }

        Invalidate();
    }

    /// <summary>The rows of an object: what <see cref="PropertyCatalog"/> knows of its type.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "The type of a live object is kept, and so is its static constructor whenever it " +
                        "registers styled properties: the generated accessors read the fields it initializes.")]
    private static PropertyDescriptor[] RowsOf(object target) => PropertyCatalog.For(target.GetType());

    /// <summary>The property's name, and where its value came from when it is not
    /// a default: "BackgroundColor · style" says at a glance which step of the
    /// ladder won.</summary>
    private string Caption(PropertyDescriptor property)
    {
        if (property.StyledProperty is not { } styled || _target is not UIElement element)
            return property.Name;

        return element.GetValueSource(styled) switch
        {
            ValueSource.Local => $"{property.Name} · local",
            ValueSource.Binding => $"{property.Name} · binding",
            ValueSource.Style => $"{property.Name} · style",
            ValueSource.Theme => $"{property.Name} · theme",
            _ => property.Name,
        };
    }

    private UIElement CreateEditor(PropertyDescriptor property)
    {
        object? current = property.GetValue(_target!);

        // the editor is chosen by the property type; unknown types are shown
        // as read-only text, so that the grid doesn't crash
        if (property.IsReadOnly)
            return ReadOnlyLabel(current);

        if (property.Type == typeof(bool))
        {
            var checkBox = new CheckBox { IsChecked = current is true };
            checkBox.CheckedChanged += (_, _) => Apply(property, checkBox.IsChecked);
            return checkBox;
        }

        if (property.Type == typeof(string))
        {
            var textBox = new TextBox { Text = current as string ?? string.Empty };
            textBox.TextChanged += (_, _) => Apply(property, textBox.Text);
            return textBox;
        }

        if (property.Type == typeof(float) || property.Type == typeof(int))
        {
            // Size.Auto is NaN, and decimal knows neither NaN nor infinities.
            // Such values are shown as 0, otherwise Convert.ToDecimal throws.
            decimal initial = 0m;

            if (current is float f)
                initial = float.IsFinite(f) ? (decimal)Math.Clamp(f, -100000f, 100000f) : 0m;
            else if (current is int i)
                initial = i;
            else if (current is not null)
            {
                try { initial = Convert.ToDecimal(current, CultureInfo.InvariantCulture); }
                catch (OverflowException) { initial = 0m; }
            }

            var numeric = new NumericUpDown
            {
                Minimum = -100000,
                Maximum = 100000,
                DecimalPlaces = property.Type == typeof(float) ? 2 : 0,
                Value = initial,
            };

            numeric.ValueChanged += (_, _) => Apply(property,
                property.Type == typeof(float) ? (float)numeric.Value : (int)numeric.Value);

            return numeric;
        }

        if (property.Type.IsEnum)
        {
            var combo = new ComboBox();

            // the members as boxed enum values, made from their numbers: an array of
            // the enum type itself may need code that native AOT didn't generate
            foreach (object number in Enum.GetValuesAsUnderlyingType(property.Type))
                combo.Items.Add(Enum.ToObject(property.Type, number));

            combo.SelectedItem = current;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is object selected)
                    Apply(property, selected);
            };

            return combo;
        }

        if (property.Type == typeof(Color))
        {
            // theme colors are often translucent — grid lines, hover tints
            var picker = new ColorPicker { Value = current is Color c ? c : Colors.Black, AllowAlpha = true };
            picker.ValueChanged += (_, _) => Apply(property, picker.Value);
            return picker;
        }

        return ReadOnlyLabel(current);
    }

    private static Label ReadOnlyLabel(object? value) => new()
    {
        Text = value?.ToString() ?? "—",
        TextColor = new Color(255, 130, 130, 130),
        HorizontalContentAlign = HorizontalContentAlignment.Left,
        VerticalContentAlign = VerticalContentAlignment.Center,
        Padding = new Thickness(6, 2),
    };

    private void Apply(PropertyDescriptor property, object? value)
    {
        // loop protection: editing a property rebuilds the target control,
        // it calls Invalidate, and we must not rebuild the grid because of that
        if (_isUpdating || _target is null) return;

        _isUpdating = true;

        try
        {
            property.SetValue(_target, value);

            if (_target is UIElement element)
                element.Invalidate();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    // the background, border and corner radius are drawn by the base — only the row stripes here
    protected override void DrawContent(Graphics g)
    {
        // the stripe under every other row — that way the eye doesn't lose the name/value pair
        var content = this.ContentBounds;

        for (int row = 0; row * 2 < Children.Count; row++)
        {
            if (row % 2 == 0) continue;

            g.FillRectangle(
                new Rectangle(
                    new Point(content.X, content.Y + row * RowHeight),
                    new Size(content.Width, RowHeight)),
                AlternateRowColor);
        }
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        // with infinite width (that's how overlays are measured) percentages
        // and subtractions give NaN, so we rely on our own set size
        float usableWidth = float.IsFinite(availableSize.Width)
            ? availableSize.Width
            : (float.IsFinite(Size.Width) ? Size.Width : 320f);

        var inner = new Size(
            Math.Max(0, usableWidth - Padding.Horizontal),
            float.IsFinite(availableSize.Height)
                ? Math.Max(0, availableSize.Height - Padding.Vertical)
                : float.PositiveInfinity);

        float labelWidth = inner.Width * LabelRatio;
        float editorWidth = Math.Max(0, inner.Width - labelWidth);

        for (int i = 0; i < Children.Count; i++)
            Children[i].Measure(new Size(i % 2 == 0 ? labelWidth : editorWidth, RowHeight));

        int rows = (Children.Count + 1) / 2;

        return ResolveSize(
            new Size(inner.Width + Padding.Horizontal, rows * RowHeight + Padding.Vertical),
            availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var content = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical)));

        float labelWidth = content.Width * LabelRatio;

        for (int i = 0; i < Children.Count; i++)
        {
            int row = i / 2;
            bool isLabel = i % 2 == 0;

            var slot = new Rectangle(
                new Point(
                    isLabel ? content.X : content.X + labelWidth,
                    content.Y + row * RowHeight),
                new Size(isLabel ? labelWidth : content.Width - labelWidth, RowHeight));

            Children[i].Arrange(slot);
        }
    }
}