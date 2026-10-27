using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ZeppelinForms.Data;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Bindings: a styled property following a property of a source, and the
/// source a styled property of this element makes.</summary>
public abstract partial class UIElement
{
    //

    /// <summary>Bindings by property index. The dictionary is created on the first
    /// binding: most elements have no bindings at all.</summary>
    private Dictionary<int, Binding>? _bindings;

    /// <summary>A property controlled by a binding. The third value source,
    /// between the theme and explicit assignment: the theme does not override
    /// a binding, while an assignment from code overrides and breaks it.</summary>
    private ulong[]? _bound;

    public bool IsBound(StyledProperty property) => GetBit(_bound, property.Index);

    /// <summary>Bind a property to a property of the source.</summary>
    /// <remarks>
    /// <para>
    /// The element holds the binding, the binding holds the source and looks
    /// at the element weakly — so a live model does not keep a closed window alive.
    /// </para>
    /// <para>
    /// The property is looked for on the source's type as the call sees it,
    /// <typeparamref name="TSource"/>, which tells the trimmer to keep its properties.
    /// A source passed as an <see cref="object"/> goes to the other overload, which looks
    /// at the runtime type and is not safe to trim.
    /// </para>
    /// </remarks>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters",
        Justification = "The two overloads differ by the source's type, not by the optional mode: no argument " +
                        "list fits both differently, and a typed source picks this one.")]
    public Binding Bind<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TSource>(
        StyledProperty<T> property,
        TSource source,
        string sourcePropertyName,
        BindingMode mode = BindingMode.OneWay)
    {
        ArgumentNullException.ThrowIfNull(source);

        PropertyInfo sourceProperty = typeof(TSource).GetProperty(sourcePropertyName)
            ?? throw new ArgumentException(
                $"{typeof(TSource).Name} has no property '{sourcePropertyName}'. A property of a derived " +
                "type is found when the source is passed as that type.",
                nameof(sourcePropertyName));

        return BindTo(property, source, sourceProperty, mode);
    }

    /// <summary>Bind a property to a property of a source known only at run time.</summary>
    [RequiresUnreferencedCode("The property is looked for on the source's runtime type, whose properties " +
                              "trimming may remove. Pass the source as its own type to use the trimming-safe overload.")]
    public Binding Bind<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(
        StyledProperty<T> property,
        object source,
        string sourcePropertyName,
        BindingMode mode = BindingMode.OneWay)
    {
        ArgumentNullException.ThrowIfNull(source);

        PropertyInfo sourceProperty = source.GetType().GetProperty(sourcePropertyName)
            ?? throw new ArgumentException(
                $"{source.GetType().Name} has no property '{sourcePropertyName}'.",
                nameof(sourcePropertyName));

        return BindTo(property, source, sourceProperty, mode);
    }

    private Binding BindTo<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(StyledProperty<T> property, object source, PropertyInfo sourceProperty, BindingMode mode)
    {
        // binding the same property again replaces the previous binding
        Unbind(property);

        var binding = new Binding(this, property, source, sourceProperty, mode);

        _bindings ??= [];
        _bindings[property.Index] = binding;

        SetBit(ref _bound, property.Index);

        binding.PushToTarget();

        return binding;
    }

    /// <summary>Break the property's binding.</summary>
    public void Unbind(StyledProperty property)
    {
        if (_bindings is null || !_bindings.Remove(property.Index, out Binding? binding))
            return;

        binding.Detach();
        ClearBit(_bound, property.Index);
    }

    /// <summary>Break all of the element's bindings.</summary>
    public void UnbindAll()
    {
        if (_bindings is null) return;

        foreach (Binding binding in _bindings.Values)
            binding.Detach();

        _bindings.Clear();
        _bound = null;
    }

    /// <summary>A write from a binding. Bypasses the explicit-assignment check,
    /// but is not itself considered explicit — otherwise the very first write
    /// from the source would close the property to all subsequent ones.</summary>
    internal void SetBoundValue(StyledProperty property, object? value)
    {
        property.WriteBoxedDirect(this, value);

        SetBit(ref _assigned, property.Index);

        // the element's own reaction goes before the invalidation,
        // see OnStyledPropertyChanged
        OnStyledPropertyChanged(property);

        if (property.AffectsLayout) InvalidateLayoutFor(property);
        else InvalidateVisual();

        RaisePropertyChanged(property);
    }

    /// <summary>An explicit assignment to a bound property: TwoWay hands the value
    /// to the source, OneWay is broken.</summary>
    private void PushOrBreakBinding(StyledProperty property, object? value)
    {
        if (_bindings is null || !_bindings.TryGetValue(property.Index, out Binding? binding))
            return;

        if (binding.Mode == BindingMode.TwoWay)
            binding.PushToSource(value);
        else
            Unbind(property);
    }

    /// <summary>Tell the binding that the property's value changed other than
    /// through assignment. Needed by controls that edit their state directly:
    /// typing in a TextBox changes the document, not the property,
    /// and the ladder of sources doesn't see such a change.</summary>
    /// <remarks>
    /// Outside subscribers are notified here as well: a binding that uses this
    /// element as its source learns about typing only from this call.
    /// </remarks>
    protected void NotifyBoundValueChanged(StyledProperty property, object? value)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property.Name));

        if (_bindings is null ||
            !_bindings.TryGetValue(property.Index, out Binding? binding) ||
            binding.Mode != BindingMode.TwoWay)
            return;

        binding.PushToSource(value);
    }
}
