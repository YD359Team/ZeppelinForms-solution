﻿using System.ComponentModel;
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

    /// <summary>Bindings of properties that aren't styled — a command — by the name the
    /// caller gave: a binding of the same name again replaces it.</summary>
    private Dictionary<string, Binding>? _valueBindings;

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

        return BindTo(property, source, usesDataContext: false, PathOf(sourceProperty), mode);
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

        return BindTo(property, source, usesDataContext: false, PathOf(sourceProperty), mode);
    }

    /// <summary>Bind a property to a value of the source, read by compiled code: no
    /// reflection, no property names. One way.</summary>
    /// <remarks>Every PropertyChanged of the source reads the value again; the element is
    /// written only when it changed. A deeper path — <c>vm.Customer.Name</c> — is read
    /// whole, but only the source itself is followed: for every object on the way, write
    /// it in markup, <c>{Bind Customer.Name}</c>, or make a <see cref="BindingPath"/>.</remarks>
    /// <example><c>status.Bind(Label.TextProperty, order, o =&gt; o.Status);</c></example>
    public Binding Bind<TSource, TValue>(StyledProperty<TValue> property, TSource source, Func<TSource, TValue> get)
    {
        ArgumentNullException.ThrowIfNull(source);

        return BindTo(property, source, usesDataContext: false, PathOf(get, set: null), BindingMode.OneWay);
    }

    /// <summary>Bind a property to a value of the source both ways: the element's changes
    /// go to the source through <paramref name="set"/>.</summary>
    /// <example><c>name.Bind(TextBox.TextProperty, order, o =&gt; o.Name, (o, v) =&gt; o.Name = v);</c></example>
    public Binding Bind<TSource, TValue>(
        StyledProperty<TValue> property, TSource source, Func<TSource, TValue> get, Action<TSource, TValue> set)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(set);

        return BindTo(property, source, usesDataContext: false, PathOf(get, set), BindingMode.TwoWay);
    }

    /// <summary>Bind a property to a value of the element's <see cref="DataContext"/> — the
    /// view model of the form, or of the part the element is in. One way.</summary>
    /// <remarks>
    /// The data context is followed: set later, set on an ancestor or the form, or the
    /// element moved under another, the binding reads the new one. A data context that is
    /// not a <typeparamref name="TSource"/> is no value — the property's default.
    /// </remarks>
    /// <example><c>status.Bind(Label.TextProperty, (OrderViewModel vm) =&gt; vm.Status);</c></example>
    public Binding Bind<TSource, TValue>(StyledProperty<TValue> property, Func<TSource, TValue> get) =>
        BindTo(property, source: null, usesDataContext: true, PathOf(get, set: null), BindingMode.OneWay);

    /// <summary>Bind a property to a value of the element's <see cref="DataContext"/> both
    /// ways: the element's changes go to the view model through <paramref name="set"/>.</summary>
    /// <example><c>name.Bind(TextBox.TextProperty, (OrderViewModel vm) =&gt; vm.Name, (vm, v) =&gt; vm.Name = v);</c></example>
    public Binding Bind<TSource, TValue>(StyledProperty<TValue> property, Func<TSource, TValue> get, Action<TSource, TValue> set)
    {
        ArgumentNullException.ThrowIfNull(set);

        return BindTo(property, source: null, usesDataContext: true, PathOf(get, set), BindingMode.TwoWay);
    }

    /// <summary>Bind a property to the end of a path from the element's
    /// <see cref="DataContext"/>, every object on the way followed. What markup's
    /// <c>{Bind Customer.Name}</c> compiles to.</summary>
    public Binding Bind(StyledProperty property, BindingPath path, BindingMode mode)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(path);

        return BindTo(property, source: null, usesDataContext: true, path, mode);
    }

    /// <summary>Bind a property to the end of a path from a source, every object on the
    /// way followed.</summary>
    public Binding Bind(StyledProperty property, object source, BindingPath path, BindingMode mode)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);

        return BindTo(property, source, usesDataContext: false, path, mode);
    }

    /// <summary>Keep a property that isn't styled — a button's <c>Command</c> — set to the
    /// end of a path from the element's <see cref="DataContext"/>. One way.</summary>
    /// <param name="name">The binding's name — the property's, by convention: a binding of
    /// the same name replaces this one.</param>
    /// <param name="path">The path from the data context.</param>
    /// <param name="apply">Writes the value into the element; null when the path leads
    /// nowhere.</param>
    public Binding BindValue(string name, BindingPath path, Action<UIElement, object?> apply)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(apply);

        return BindValueTo(name, source: null, usesDataContext: true, path, apply);
    }

    /// <summary>Keep a property that isn't styled set to the end of a path from a source.
    /// One way.</summary>
    public Binding BindValue(string name, object source, BindingPath path, Action<UIElement, object?> apply)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(apply);

        return BindValueTo(name, source, usesDataContext: false, path, apply);
    }

    /// <summary>Break the binding <see cref="BindValue(string, BindingPath, Action{UIElement, object})"/>
    /// made under the name.</summary>
    public void UnbindValue(string name)
    {
        if (_valueBindings is not null && _valueBindings.Remove(name, out Binding? binding))
            binding.Detach();
    }

    private Binding BindTo(StyledProperty property, object? source, bool usesDataContext, BindingPath path, BindingMode mode)
    {
        // a path that can't be written binds both ways all the same, as a read-only
        // property always did: the element's changes simply stay in the element.
        // The markup reports it where it's written, as ZF2008

        // binding the same property again replaces the previous binding
        Unbind(property);

        var binding = new Binding(this, property, apply: null, source, usesDataContext, path, mode);

        _bindings ??= [];
        _bindings[property.Index] = binding;

        SetBit(ref _bound, property.Index);

        binding.SetSource(usesDataContext ? FollowDataContext() : source);

        return binding;
    }

    private Binding BindValueTo(string name, object? source, bool usesDataContext, BindingPath path, Action<UIElement, object?> apply)
    {
        UnbindValue(name);

        var binding = new Binding(this, targetProperty: null, apply, source, usesDataContext, path, BindingMode.OneWay);

        _valueBindings ??= new Dictionary<string, Binding>(StringComparer.Ordinal);
        _valueBindings[name] = binding;

        binding.SetSource(usesDataContext ? FollowDataContext() : source);

        return binding;
    }

    /// <summary>A path of one step from a property found by reflection: what the
    /// overloads that take a property's name bind along.</summary>
    private static BindingPath PathOf(PropertyInfo property) =>
        new(property.Name, property.DeclaringType ?? typeof(object), property.PropertyType,
            new BindingStep(property.Name, property.GetValue))
        {
            Set = property.CanWrite ? property.SetValue : null,
        };

    /// <summary>A path of one step from compiled reads and writes; the step has no name,
    /// so every PropertyChanged of the source reads it again.</summary>
    private static BindingPath PathOf<TSource, TValue>(Func<TSource, TValue> get, Action<TSource, TValue>? set)
    {
        ArgumentNullException.ThrowIfNull(get);

        return new BindingPath($"({typeof(TSource).Name}) → {typeof(TValue).Name}", typeof(TSource), typeof(TValue),
            new BindingStep(null, source => get((TSource)source)))
        {
            Set = set is null ? null : (source, value) => set((TSource)source, (TValue)value!),
        };
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
        if (_bindings is not null)
        {
            foreach (Binding binding in _bindings.Values)
                binding.Detach();

            _bindings.Clear();
        }

        if (_valueBindings is not null)
        {
            foreach (Binding binding in _valueBindings.Values)
                binding.Detach();

            _valueBindings.Clear();
        }

        _bound = null;
    }

    /// <summary>The bindings that start from the data context: given the new one.</summary>
    private void RetargetDataContextBindings(object? dataContext)
    {
        if (_bindings is not null)
        {
            foreach (Binding binding in _bindings.Values.ToArray())
                if (binding.UsesDataContext) binding.SetSource(dataContext);
        }

        if (_valueBindings is not null)
        {
            foreach (Binding binding in _valueBindings.Values.ToArray())
                if (binding.UsesDataContext) binding.SetSource(dataContext);
        }
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
