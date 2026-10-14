namespace ZeppelinForms.Forms.Controls.Tools;

public sealed class PropertyDescriptor(
    string name, Type type,
    Func<object, object?> get,
    Action<object, object?>? set = null)
{
    public string Name { get; } = name;

    /// <summary>The caption of the row; null — the <see cref="Name"/>. The name stays the
    /// key: rows of several objects are matched by it, and the filter finds both.</summary>
    public string? DisplayName { get; init; }

    /// <summary>What the property does, shown as the row's tooltip.</summary>
    public string? Description { get; init; }

    // the same default as StyledAttribute.Category: PropertyCatalog merges descriptors
    // from both, and two different defaults split one category into two
    public string Category { get; init; } = "Other";

    public Type Type { get; } = type;

    /// <summary>The styled property behind the descriptor, if there is one: the
    /// inspector shows where its value came from.</summary>
    public Styling.StyledProperty? StyledProperty { get; init; }

    /// <summary>The smallest value a number editor takes; null — the grid's own bound.</summary>
    public double? Minimum { get; init; }

    /// <summary>The largest value a number editor takes; null — the grid's own bound.</summary>
    public double? Maximum { get; init; }

    /// <summary>The digits after the point a number editor shows and keeps; null — none
    /// for whole numbers, two for the others. A quaternion or a normal needs four or more.</summary>
    public int? DecimalPlaces { get; init; }

    /// <summary>The step of a number editor's arrows and keys; null — one.</summary>
    public double? Increment { get; init; }

    /// <summary>The editor of this property alone, ahead of any editor of its type.
    /// Null — the grid's choice, see <see cref="PropertyGrid.Editors"/>.</summary>
    public Func<PropertyEditContext, Base.UIElement>? Editor { get; init; }

    public bool IsReadOnly => set is null;

    public object? GetValue(object target) => get(target);
    public void SetValue(object target, object? value) => set?.Invoke(target, value);
}