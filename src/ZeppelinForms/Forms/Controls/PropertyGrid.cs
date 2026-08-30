using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

public class PropertyGrid : PanelControl
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
    public Color AlternateRowColor { get; set; } = new Color(255, 248, 248, 248);

    public PropertyGrid()
    {
        Background = Colors.White;
        IsVisible = false;
    }

    private void Rebuild()
    {
        while (Children.Count > 0)
            Children.RemoveAt(Children.Count - 1);

        // пустой грид не показываем: невидимый элемент выпадает
        // и из отрисовки, и из хит-теста, так что клики пройдут насквозь
        IsVisible = _target is not null;

        if (_target is null)
        {
            Invalidate();
            return;
        }

        foreach (PropertyDescriptor property in PropertyCatalog.For(_target.GetType()))
        {
            Children.Add(new Label
            {
                Text = property.Name,
                HorizontalContentAlign = HorizontalContentAlignment.Left,
                VerticalContentAlign = VerticalContentAlignment.Center,
                Padding = new Thickness(6, 2),
            });

            Children.Add(CreateEditor(property));
        }

        Invalidate();
    }

    private UIElement CreateEditor(PropertyDescriptor property)
    {
        object? current = property.GetValue(_target!);

        // редактор выбирается по типу свойства; неизвестные типы
        // показываем как read-only текст, чтобы грид не падал
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
            // Size.Auto — это NaN, а decimal не знает ни NaN, ни бесконечностей.
            // Показываем такие значения как 0, иначе Convert.ToDecimal падает.
            decimal initial = 0m;

            if (current is float f)
                initial = float.IsFinite(f) ? (decimal)Math.Clamp(f, -100000f, 100000f) : 0m;
            else if (current is int i)
                initial = i;
            else if (current is not null)
            {
                try { initial = Convert.ToDecimal(current); }
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

            foreach (object value in Enum.GetValues(property.Type))
                combo.Items.Add(value);

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
            var picker = new ColorPicker { Value = current is Color c ? c : Colors.Black };
            picker.ValueChanged += (_, _) => Apply(property, picker.Value);
            return picker;
        }

        return ReadOnlyLabel(current);
    }

    private Label ReadOnlyLabel(object? value) => new()
    {
        Text = value?.ToString() ?? "—",
        TextColor = new Color(255, 130, 130, 130),
        HorizontalContentAlign = HorizontalContentAlignment.Left,
        VerticalContentAlign = VerticalContentAlignment.Center,
        Padding = new Thickness(6, 2),
    };

    private void Apply(PropertyDescriptor property, object? value)
    {
        // защита от петли: правка свойства перестраивает целевой контрол,
        // тот дёргает Invalidate, а мы не должны на это пересобирать грид
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

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        // подложка чётных строк — так глаз не теряет пару «имя/значение»
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
        // при бесконечной ширине (так меряются оверлеи) проценты и вычитания
        // дают NaN, поэтому опираемся на собственный заданный размер
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

    protected override void ArrangeContentOverride(Size finalSize)
    {
        var content = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical)));

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