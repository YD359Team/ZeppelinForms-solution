using System.Globalization;
using System.Numerics;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls;

/// <summary>The editors every property grid has, by the property's type.</summary>
public partial class PropertyGrid
{
    /// <summary>The bound of a number editor without one from its descriptor: wide enough
    /// for coordinates and sizes, narrow enough for the field to stay narrow.</summary>
    private const double DefaultBound = 100000;

    private static UIElement? BuiltInEditor(PropertyEditContext context)
    {
        Type type = context.Property.Type;

        if (type == typeof(bool)) return BoolEditor(context);
        if (type == typeof(string)) return StringEditor(context);
        if (IsNumber(type)) return NumberEditor(context);

        if (type.IsEnum)
        {
            return type.IsDefined(typeof(FlagsAttribute), inherit: false)
                ? FlagsEditor(context)
                : EnumEditor(context);
        }

        if (type == typeof(Color)) return ColorEditor(context);

        return ComponentsEditor(context);
    }

    private static Label ReadOnlyLabel(object? value) => new()
    {
        Text = value?.ToString() ?? "—",
        TextColor = new Color(255, 130, 130, 130),
        HorizontalContentAlign = HorizontalContentAlignment.Left,
        VerticalContentAlign = VerticalContentAlignment.Center,
        Padding = new Thickness(6, 2),
    };

    private static CheckBox BoolEditor(PropertyEditContext context)
    {
        var checkBox = new CheckBox
        {
            IsChecked = !context.IsMixed && context.Value is true,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        checkBox.CheckedChanged += (_, _) => context.Set(checkBox.IsChecked);
        return checkBox;
    }

    /// <summary>A text field: written as it is typed, so the change shows at once, and
    /// one step for undo when the field is left or Enter is pressed.</summary>
    private static TextBox StringEditor(PropertyEditContext context)
    {
        var textBox = new TextBox
        {
            Text = context.IsMixed ? string.Empty : context.Value as string ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        textBox.TextChanged += (_, _) => context.Change(textBox.Text);
        textBox.LostFocus += (_, _) => context.Commit();
        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) context.Commit();
        };

        return textBox;
    }

    private static bool IsNumber(Type type) =>
        type == typeof(float) || type == typeof(double) || type == typeof(decimal) ||
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
        type == typeof(uint) || type == typeof(ushort);

    private static bool IsWhole(Type type) => !(type == typeof(float) || type == typeof(double) || type == typeof(decimal));

    /// <summary>A number field in the descriptor's range and precision; the type's own
    /// range where it is narrower — a byte doesn't go to a hundred thousand.</summary>
    private static NumericUpDown NumberEditor(PropertyEditContext context)
    {
        Type type = context.Property.Type;

        (double typeMin, double typeMax) = type switch
        {
            _ when type == typeof(byte) => (byte.MinValue, byte.MaxValue),
            _ when type == typeof(short) => (short.MinValue, short.MaxValue),
            _ when type == typeof(ushort) => (ushort.MinValue, ushort.MaxValue),
            _ when type == typeof(uint) => (0d, DefaultBound),
            _ => (-DefaultBound, DefaultBound),
        };

        double minimum = Math.Max(context.Property.Minimum ?? -DefaultBound, typeMin);
        double maximum = Math.Min(context.Property.Maximum ?? DefaultBound, typeMax);

        int decimals = context.Property.DecimalPlaces ?? (IsWhole(type) ? 0 : 2);

        var numeric = new NumericUpDown
        {
            Minimum = (decimal)minimum,
            Maximum = (decimal)maximum,
            DecimalPlaces = decimals,
            Step = (decimal)(context.Property.Increment ?? 1d),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Value = context.IsMixed ? 0m : ToDecimal(context.Value),
        };

        numeric.ValueChanged += (_, _) => context.Change(FromDecimal(numeric.Value, type));

        // the arrows and the wheel write as they go; leaving the field or Enter ends the edit
        numeric.LostFocus += (_, _) => context.Commit();
        numeric.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) context.Commit();
        };

        return numeric;
    }

    /// <summary>Size.Auto is NaN, and decimal knows neither NaN nor infinities.
    /// Such values are shown as 0, otherwise Convert.ToDecimal throws.</summary>
    private static decimal ToDecimal(object? value)
    {
        switch (value)
        {
            case float f:
                return float.IsFinite(f) ? (decimal)Math.Clamp(f, -(float)DefaultBound, (float)DefaultBound) : 0m;

            case double d:
                return double.IsFinite(d) ? (decimal)Math.Clamp(d, -DefaultBound, DefaultBound) : 0m;

            case null:
                return 0m;

            default:
                try { return Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
                catch (OverflowException) { return 0m; }
        }
    }

    /// <summary>The number boxed as the property's own type: a float property set from a
    /// boxed decimal would throw in the setter's cast.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
        Justification = "Each branch boxes another type: object is the only type they share.")]
    private static object FromDecimal(decimal value, Type type)
    {
        if (type == typeof(float)) return (float)value;
        if (type == typeof(double)) return (double)value;
        if (type == typeof(decimal)) return value;
        if (type == typeof(long)) return (long)value;
        if (type == typeof(short)) return (short)value;
        if (type == typeof(byte)) return (byte)value;
        if (type == typeof(uint)) return (uint)value;
        if (type == typeof(ushort)) return (ushort)value;

        return (int)value;
    }

    private static ComboBox EnumEditor(PropertyEditContext context)
    {
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // the members as boxed enum values, made from their numbers: an array of
        // the enum type itself may need code that native AOT didn't generate
        foreach (object number in Enum.GetValuesAsUnderlyingType(context.Property.Type))
            combo.Items.Add(Enum.ToObject(context.Property.Type, number));

        if (!context.IsMixed)
            combo.SelectedItem = context.Value;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is object selected)
                context.Set(selected);
        };

        return combo;
    }

    /// <summary>A flags enum — layers, sides, options: a list of check boxes, one per
    /// flag, the value their sum. The zero member and the combinations of others
    /// aren't flags of their own and are left out.</summary>
    private static CheckedComboBox FlagsEditor(PropertyEditContext context)
    {
        Type type = context.Property.Type;

        var combo = new CheckedComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        ulong current = context.IsMixed || context.Value is null ? 0 : Bits(context.Value, type);

        foreach (object number in Enum.GetValuesAsUnderlyingType(type))
        {
            ulong bits = Bits(number, type);

            if (bits == 0 || !ulong.IsPow2(bits)) continue;

            combo.Items.Add(Enum.ToObject(type, number));
        }

        for (int i = 0; i < combo.Items.Count; i++)
        {
            ulong bits = Bits(combo.Items[i], type);

            if ((current & bits) == bits)
                combo.SetChecked(i, true);
        }

        combo.SelectionChanged += (_, _) =>
        {
            ulong sum = 0;

            foreach (object flag in combo.CheckedItems)
                sum |= Bits(flag, type);

            context.Set(Enum.ToObject(type, sum));
        };

        return combo;
    }

    /// <summary>The bits of an enum value or of its number, whatever the underlying type:
    /// Convert.ToUInt64 throws on a negative member — the All = -1 of many flags enums.</summary>
    private static ulong Bits(object value, Type enumType)
    {
        object number = value.GetType().IsEnum
            ? Convert.ChangeType(value, Enum.GetUnderlyingType(enumType), CultureInfo.InvariantCulture)
            : value;

        return number switch
        {
            ulong u => u,
            long l => unchecked((ulong)l),
            uint ui => ui,
            int i => unchecked((ulong)i),
            ushort us => us,
            short s => unchecked((ulong)s),
            byte b => b,
            sbyte sb => unchecked((ulong)sb),
            _ => 0,
        };
    }

    /// <summary>Theme colors are often translucent — grid lines, hover tints — so the
    /// alpha is offered. Dragging across the spectrum writes as it goes; leaving the
    /// picker ends the edit.</summary>
    private static ColorPicker ColorEditor(PropertyEditContext context)
    {
        var picker = new ColorPicker
        {
            Value = !context.IsMixed && context.Value is Color c ? c : Colors.Black,
            AllowAlpha = true,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        picker.ValueChanged += (_, _) => context.Change(picker.Value);
        picker.LostFocus += (_, _) => context.Commit();

        return picker;
    }

    /// <summary>A struct of numbers, a field per part: vectors and quaternions of
    /// System.Numerics, and the framework's points, sizes, thicknesses and corner radii.</summary>
    private static PropertyGridComponents? ComponentsEditor(PropertyEditContext context)
    {
        Type type = context.Property.Type;
        int decimals = context.Property.DecimalPlaces ?? (type == typeof(Quaternion) ? 4 : 2);

        PropertyGridComponents Make(string[] names, Func<object, float[]> split, Func<float[], object> join) =>
            new(context, names, split, join, decimals);

        if (type == typeof(Vector2))
            return Make(["X", "Y"], v => [((Vector2)v).X, ((Vector2)v).Y], p => new Vector2(p[0], p[1]));

        if (type == typeof(Vector3))
            return Make(["X", "Y", "Z"],
                v => [((Vector3)v).X, ((Vector3)v).Y, ((Vector3)v).Z],
                p => new Vector3(p[0], p[1], p[2]));

        if (type == typeof(Vector4))
            return Make(["X", "Y", "Z", "W"],
                v => [((Vector4)v).X, ((Vector4)v).Y, ((Vector4)v).Z, ((Vector4)v).W],
                p => new Vector4(p[0], p[1], p[2], p[3]));

        if (type == typeof(Quaternion))
            return Make(["X", "Y", "Z", "W"],
                v => [((Quaternion)v).X, ((Quaternion)v).Y, ((Quaternion)v).Z, ((Quaternion)v).W],
                p => new Quaternion(p[0], p[1], p[2], p[3]));

        if (type == typeof(Point))
            return Make(["X", "Y"], v => [((Point)v).X, ((Point)v).Y], p => new Point(p[0], p[1]));

        if (type == typeof(Size))
            return Make(["W", "H"], v => [((Size)v).Width, ((Size)v).Height], p => new Size(p[0], p[1]));

        if (type == typeof(Thickness))
            return Make(["L", "T", "R", "B"],
                v => [((Thickness)v).Left, ((Thickness)v).Top, ((Thickness)v).Right, ((Thickness)v).Bottom],
                p => new Thickness(p[0], p[1], p[2], p[3]));

        if (type == typeof(CornerRadius))
            return Make(["TL", "TR", "BR", "BL"],
                v => [((CornerRadius)v).TopLeft, ((CornerRadius)v).TopRight, ((CornerRadius)v).BottomRight, ((CornerRadius)v).BottomLeft],
                p => new CornerRadius(p[0], p[1], p[2], p[3]));

        return null;
    }
}