using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Data;

/// <summary>
/// The last property of a <see cref="BindingPath"/>, read and written as its own type:
/// <see cref="BindingValue{TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// A path's steps read <see cref="object"/>: they must, as a path walks through objects of
/// any type. For the last property that means a box on every read of a value type — an
/// <c>int</c>, a <c>bool</c>, a <c>Color</c> — another on every write back, and a boxed
/// comparison with the value written before.
/// </para>
/// <para>
/// With a typed value of the bound property's own type the binding moves the value as it
/// is: read, compared by <see cref="EqualityComparer{T}.Default"/>, written into the
/// element and back into the source without a box. Of any other type — a number bound to
/// a text — it converts through <see cref="object"/>, as before.
/// </para>
/// </remarks>
public abstract class BindingValue
{
    private protected BindingValue()
    {
    }

    /// <summary>The type of the last property.</summary>
    public abstract Type ValueType { get; }

    /// <summary>The typed end of a binding of <paramref name="property"/>; null — the
    /// property is of another type, and the binding converts through object.</summary>
    internal abstract BindingEnd? EndFor(StyledProperty property);
}

/// <summary>The last property of a path, as its type: see <see cref="BindingValue"/>.</summary>
/// <param name="get">Reads the property from the object that has it.</param>
/// <param name="set">Writes it; null — the path is read only.</param>
/// <example>
/// <code>
/// new BindingPath("Quantity", typeof(OrderViewModel), typeof(int),
///     new BindingStep("Quantity", static s =&gt; ((OrderViewModel)s).Quantity))
/// {
///     Value = new BindingValue&lt;int&gt;(static s =&gt; ((OrderViewModel)s).Quantity,
///                                   static (s, v) =&gt; ((OrderViewModel)s).Quantity = v),
/// };
/// </code>
/// </example>
public sealed class BindingValue<TValue>(Func<object, TValue> get, Action<object, TValue>? set = null) : BindingValue
{
    public Func<object, TValue> Get { get; } = get ?? throw new ArgumentNullException(nameof(get));

    public Action<object, TValue>? Set { get; } = set;

    public override Type ValueType => typeof(TValue);

    internal override BindingEnd? EndFor(StyledProperty property) =>
        property is StyledProperty<TValue> typed ? new BindingEnd<TValue>(this, typed) : null;
}

/// <summary>A binding's end at the element, typed: the last value written and how to
/// move the next one. One per binding — the last value is its state.</summary>
internal abstract class BindingEnd
{
    /// <summary>Whether the value can go back to the source.</summary>
    public abstract bool CanWrite { get; }

    /// <summary>Read the value from <paramref name="owner"/> — the default where it is
    /// null, a null on the way — and keep it if it isn't the one written last.</summary>
    /// <returns>Whether there is a new value to <see cref="Write"/>.</returns>
    public abstract bool Take(object? owner);

    /// <summary>Write the value <see cref="Take"/> kept into the element.</summary>
    public abstract void Write(UIElement element);

    /// <summary>The element's value went to the source another way, boxed: it is what
    /// the element shows now.</summary>
    public abstract void RememberBoxed(object? value);
}

internal sealed class BindingEnd<TValue>(BindingValue<TValue> value, StyledProperty<TValue> property) : BindingEnd
{
    private TValue _last = default!;
    private bool _hasLast;

    public override bool CanWrite => value.Set is not null;

    public override bool Take(object? owner)
    {
        TValue next = owner is null ? property.DefaultValue : value.Get(owner);

        if (_hasLast && EqualityComparer<TValue>.Default.Equals(_last, next)) return false;

        _last = next;
        _hasLast = true;

        return true;
    }

    public override void Write(UIElement element)
    {
        property.Write(element, _last);
        element.AfterBoundWrite(property);
    }

    /// <summary>Write the element's value into the source, and take it for the last one
    /// written: the source changed back later is a change to write again.</summary>
    public void WriteBack(object owner, TValue next)
    {
        value.Set!(owner, next);

        _last = next;
        _hasLast = true;
    }

    public override void RememberBoxed(object? boxed)
    {
        if (boxed is TValue typed)
        {
            _last = typed;
            _hasLast = true;
        }
        else if (boxed is null && default(TValue) is null)
        {
            // "null is T" is false even where T takes null: a cleared text
            _last = default!;
            _hasLast = true;
        }
        else
        {
            // a value of another type, or a null of a value type: not comparable with
            // what comes next, so the next read is written whatever it is
            _hasLast = false;
        }
    }
}