using System.ComponentModel;
using System.Reflection;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Data;

public enum BindingMode
{
    /// <summary>Source → element.</summary>
    OneWay,

    /// <summary>Both ways.</summary>
    TwoWay,
}

/// <summary>
/// A link between an element's styled property and a property of the source.
/// </summary>
/// <remarks>
/// The target is kept as a weak reference: a subscription to PropertyChanged is
/// a reference from the source to the handler, and a strong reference to the
/// element would mean a live model holds the whole closed window.
/// On finding a dead target, the binding unsubscribes itself.
/// </remarks>
public sealed class Binding
{
    private readonly WeakReference<UIElement> _target;
    private readonly StyledProperty _targetProperty;
    private readonly PropertyInfo _sourceProperty;
    private readonly string _sourcePropertyName;

    /// <summary>The source. The reference is strong: while the element is alive,
    /// it needs the model.</summary>
    public object Source { get; }

    public BindingMode Mode { get; }

    /// <summary>A write from the binding is in progress — to tell it apart from
    /// a user assignment and not to loop in TwoWay.</summary>
    private bool _updating;

    private bool _detached;

    internal Binding(
        UIElement target,
        StyledProperty targetProperty,
        object source,
        PropertyInfo sourceProperty,
        BindingMode mode)
    {
        _target = new WeakReference<UIElement>(target);
        _targetProperty = targetProperty;
        Source = source;
        _sourceProperty = sourceProperty;
        _sourcePropertyName = sourceProperty.Name;
        Mode = mode;

        if (source is INotifyPropertyChanged notify)
            notify.PropertyChanged += OnSourceChanged;
    }

    /// <summary>Take the value from the source and write it into the element.</summary>
    internal void PushToTarget()
    {
        if (_detached) return;

        if (!_target.TryGetTarget(out UIElement? element))
        {
            Detach();
            return;
        }

        object? raw = _sourceProperty.GetValue(Source);

        object? value;

        try
        {
            value = Convert(raw, _targetProperty.PropertyType);
        }
        // ArgumentException comes from Enum.Parse on an unknown name
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            // the source's type doesn't fit the property's type — there is nothing
            // to write, but crashing in the middle of drawing is wrong
            ZfContract.Fail(
                $"Value '{raw}' from '{_sourcePropertyName}' cannot be converted " +
                $"to {_targetProperty.PropertyType.Name}.");

            return;
        }

        _updating = true;

        try
        {
            element.SetBoundValue(_targetProperty, value);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Write the element's value to the source. TwoWay only.</summary>
    internal void PushToSource(object? value)
    {
        if (_detached || Mode != BindingMode.TwoWay || _updating) return;

        if (!_sourceProperty.CanWrite) return;

        _updating = true;

        try
        {
            _sourceProperty.SetValue(Source, Convert(value, _sourceProperty.PropertyType));
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            // The value doesn't fit the source's type — in TwoWay that's the ordinary
            // case: the user types into a field, and intermediate states ("-", "1,")
            // are not numbers. The write is dropped, not the application.
            //
            // Not a contract violation either: ZfContract throws in debug builds by
            // default, and it used to throw right on the minus sign typed into
            // a field bound to a number
            System.Diagnostics.Debug.WriteLine(
                $"[ZF] binding: value '{value}' cannot be converted to " +
                $"{_sourceProperty.PropertyType.Name} for '{_sourcePropertyName}', the write is skipped.");
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        // an empty name by convention means "everything changed"
        if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != _sourcePropertyName)
            return;

        if (_updating) return;

        PushToTarget();
    }

    /// <summary>Break the link and unsubscribe from the source.</summary>
    internal void Detach()
    {
        if (_detached) return;
        _detached = true;

        if (Source is INotifyPropertyChanged notify)
            notify.PropertyChanged -= OnSourceChanged;
    }

    private static object? Convert(object? value, Type targetType)
    {
        if (value is null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return underlying.IsEnum
            ? Enum.Parse(underlying, value.ToString()!)
            : System.Convert.ChangeType(value, underlying);
    }
}