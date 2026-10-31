namespace ZeppelinForms.Data;

/// <summary>
/// One step of a <see cref="BindingPath"/>: a property read from the object before it —
/// <c>Customer</c> of a view model, <c>Name</c> of the customer.
/// </summary>
/// <param name="name">The property's name: a PropertyChanged of the object that names it,
/// or names nothing, makes the binding read again. Null — any PropertyChanged of the
/// object does.</param>
/// <param name="get">The read, as compiled code: no reflection, so trimming and native
/// AOT keep it.</param>
public readonly struct BindingStep(string? name, Func<object, object?> get)
{
    public string? Name { get; } = name;

    public Func<object, object?> Get { get; } = get;
}

/// <summary>
/// The way from a binding's source to its value: <c>Customer.Name</c> — the steps, each a
/// property read compiled into a delegate, and the write of the last one for a binding
/// both ways. What the markup's <c>{Bind Customer.Name}</c> compiles to, with
/// <c>x:DataType</c> saying what the source is.
/// </summary>
/// <remarks>
/// <para>
/// The binding follows every object on the way: a PropertyChanged of the view model for
/// <c>Customer</c> reads the new customer's name and starts following the new customer.
/// A null on the way is no value: the element gets the property's default.
/// </para>
/// <para>
/// From C# the typed overloads of <c>UIElement.Bind</c> make a path of one step from a
/// lambda: <c>box.Bind(TextBox.TextProperty, (OrderViewModel vm) =&gt; vm.Name)</c>.
/// </para>
/// </remarks>
public sealed class BindingPath
{
    private readonly BindingStep[] _steps;

    /// <param name="text">The path as written — "Customer.Name" — for messages.</param>
    /// <param name="sourceType">What the source must be: a source of another type —
    /// a data context of another view model — is no value, not a cast that throws.</param>
    /// <param name="valueType">The type of the last property: a value written back is
    /// converted to it before <see cref="Set"/> gets it.</param>
    /// <param name="steps">The reads, from the source on.</param>
    public BindingPath(
        string text,
        Type sourceType,
        Type valueType,
        params BindingStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(valueType);
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Length == 0)
            throw new ArgumentException("A path has at least one step.", nameof(steps));

        Text = text;
        SourceType = sourceType;
        ValueType = valueType;
        _steps = steps;
    }

    public string Text { get; }

    public Type SourceType { get; }

    public Type ValueType { get; }

    public IReadOnlyList<BindingStep> Steps => _steps;

    /// <summary>The write of the last property, given the object that has it and the value
    /// converted to <see cref="ValueType"/>. Null — the path is read only, and a binding
    /// both ways along it is refused.</summary>
    public Action<object, object?>? Set { get; init; }

    /// <summary>The last property read and written as its own type, without a box when.</summary>
    public BindingValue? Value { get; init; }

    public override string ToString() => Text;

    internal BindingStep this[int index] => _steps[index];

    internal int Length => _steps.Length;
}
