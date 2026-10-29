using System.ComponentModel;
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
/// A link between a property of an element and the end of a <see cref="BindingPath"/> from
/// a source — an object given, or the element's <see cref="UIElement.DataContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every object on the path that implements <see cref="INotifyPropertyChanged"/> is
/// followed: a change of the view model's <c>Customer</c> reads the new customer's name
/// and follows the new customer from then on. A null on the way, or a source of another
/// type than the path's, is no value: the element's property gets its default.
/// </para>
/// <para>
/// The target is kept as a weak reference: a subscription to PropertyChanged is a reference
/// from the source to the binding, and a strong reference to the element would mean a live
/// model holds the whole closed window. On finding a dead target, the binding unsubscribes
/// itself.
/// </para>
/// <para>
/// A value that doesn't fit the other end — a text that is no number, a number for
/// a property of a type it has no conversion to — is no exception: the write is
/// skipped and reported to the debug output. It is a mistake of the view model or of
/// the user's typing, not of the framework, and an exception in the middle of layout
/// took a whole previewed form down with it, again on every frame.
/// </para>
/// </remarks>
public sealed class Binding
{
    private readonly WeakReference<UIElement> _target;

    /// <summary>A styled property of the target, or — for a property that isn't styled —
    /// <see cref="_apply"/>.</summary>
    private readonly StyledProperty? _targetProperty;

    private readonly Action<UIElement, object?>? _apply;

    /// <summary>The objects the steps are read from: <c>_owners[0]</c> is the source,
    /// <c>_owners[i]</c> what step i−1 read. Null past a null on the way.</summary>
    private readonly object?[] _owners;

    /// <summary>The objects followed for PropertyChanged, by step.</summary>
    private readonly INotifyPropertyChanged?[] _followed;

    private readonly PropertyChangedEventHandler[] _handlers;

    /// <summary>The value last written into the element: a change of the source that
    /// leaves it as it was — any property of a view model followed as a whole — writes
    /// nothing.</summary>
    private object? _written;

    private bool _hasWritten;

    /// <summary>A write from the binding is in progress — to tell it apart from
    /// a user assignment and not to loop in TwoWay.</summary>
    private bool _updating;

    private bool _detached;

    // The conversions last used, by the runtime type of the value they were made for:
    // a binding sees values of one type almost always, so the shared table of
    // BindingConversion is asked once, not on every write. The UI thread's, as the
    // rest of the binding: no lock
    private Type? _toTargetFrom;
    private Func<object, object?>? _toTarget;
    private Type? _toSourceFrom;
    private Func<object, object?>? _toSource;

    /// <summary>A value that couldn't be converted, with the message written to the
    /// debug output: for tests, and for whatever wants to show broken bindings.</summary>
    internal static event EventHandler<string>? ConversionFailed;

    internal Binding(
        UIElement target,
        StyledProperty? targetProperty,
        Action<UIElement, object?>? apply,
        object? source,
        bool usesDataContext,
        BindingPath path,
        BindingMode mode)
    {
        _target = new WeakReference<UIElement>(target);
        _targetProperty = targetProperty;
        _apply = apply;
        Path = path;
        Mode = mode;
        UsesDataContext = usesDataContext;

        _owners = new object?[path.Length];
        _followed = new INotifyPropertyChanged?[path.Length];
        _handlers = new PropertyChangedEventHandler[path.Length];

        for (int i = 0; i < path.Length; i++)
        {
            int step = i;
            _handlers[i] = (_, e) => OnSourceChanged(step, e);
        }

        Source = source;
    }

    /// <summary>The source the path starts from: the object given, or the element's data
    /// context as it is now. Null — none yet.</summary>
    /// <remarks>The reference is strong: while the element is alive, it needs the model.</remarks>
    public object? Source { get; private set; }

    public BindingMode Mode { get; }

    public BindingPath Path { get; }

    /// <summary>The source is the element's <see cref="UIElement.DataContext"/>, followed
    /// as it changes — set on the element, on an ancestor or on the form, or the element
    /// moved under another.</summary>
    public bool UsesDataContext { get; }

    /// <summary>Start from a source — the first, or another one the data context became —
    /// and write what it gives.</summary>
    internal void SetSource(object? source)
    {
        if (_detached) return;

        Source = source;
        _owners[0] = source is not null && Path.SourceType.IsInstanceOfType(source) ? source : null;

        Follow(0);
        PushToTarget();
    }

    /// <summary>Follow the objects on the path from a step on: <c>_owners[from]</c> is
    /// in place, the ones after it are read again.</summary>
    private void Follow(int from)
    {
        for (int i = from; i < _followed.Length; i++)
        {
            if (_followed[i] is { } followed)
                followed.PropertyChanged -= _handlers[i];

            _followed[i] = null;
        }

        object? current = _owners[from];

        for (int i = from; i < _owners.Length; i++)
        {
            _owners[i] = current;

            if (current is null) continue;

            if (current is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += _handlers[i];
                _followed[i] = notify;
            }

            // the last owner's property is the value: read when it is written
            if (i < _owners.Length - 1)
                current = Path[i].Get(current);
        }
    }

    /// <summary>The value at the end of the path; false — a null on the way.</summary>
    private bool TryRead(out object? value)
    {
        value = null;

        if (_owners[^1] is not { } owner) return false;

        value = Path[Path.Length - 1].Get(owner);
        return true;
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

        object? value;

        if (!TryRead(out object? raw))
        {
            // nothing at the end of the path: the property's own default
            value = _targetProperty?.DefaultBoxed;
        }
        else if (_targetProperty is null)
        {
            // a property that isn't styled: the generated code casts it, as it was typed
            value = raw;
        }
        else
        {
            try
            {
                value = ToTarget(raw);
            }
            // ArgumentException comes from Enum.Parse on an unknown name
            catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException or ArgumentException)
            {
                // the source's type doesn't fit the property's type — there is nothing
                // to write, but crashing in the middle of drawing is wrong.
                //
                // Not ZfContract.Fail either, as it used to be: a contract is the
                // framework's own rule, and here the view model's type is wrong, not
                // the framework. ZfContract throws in debug builds, and in the
                // previewer that exception came out of layout on every frame — the
                // preview showed nothing but it
                ReportConversionFailure(raw, _targetProperty.PropertyType, "the element keeps its value");

                return;
            }
        }

        if (_hasWritten && Equals(_written, value)) return;

        _written = value;
        _hasWritten = true;
        _updating = true;

        try
        {
            if (_targetProperty is not null)
                element.SetBoundValue(_targetProperty, value);
            else
                _apply!(element, value);
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

        if (Path.Set is not { } set || _owners[^1] is not { } owner) return;

        _updating = true;

        try
        {
            set(owner, ToSource(value));

            // what the element shows is what the source has now: the source changed
            // back to the old value later is a change to write again
            _written = value;
            _hasWritten = true;
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
            ReportConversionFailure(value, Path.ValueType, "the write is skipped");
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnSourceChanged(int step, PropertyChangedEventArgs e)
    {
        string? name = Path[step].Name;

        // an empty name by convention means "everything changed"; a step without
        // a name follows every property of its object
        if (name is not null && !string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != name)
            return;

        if (_updating) return;

        // an object on the way gave another one: read it, and follow from there
        if (step < _owners.Length - 1)
        {
            _owners[step + 1] = _owners[step] is { } owner ? Path[step].Get(owner) : null;
            Follow(step + 1);
        }

        PushToTarget();
    }

    /// <summary>Break the link and unsubscribe from the source.</summary>
    internal void Detach()
    {
        if (_detached) return;
        _detached = true;

        for (int i = 0; i < _followed.Length; i++)
        {
            if (_followed[i] is { } followed)
                followed.PropertyChanged -= _handlers[i];

            _followed[i] = null;
        }
    }

    /// <summary>A value of the source as the target property's type.</summary>
    private object? ToTarget(object? value)
    {
        if (value is null) return null;

        Type type = value.GetType();

        if (!ReferenceEquals(type, _toTargetFrom))
        {
            _toTarget = BindingConversion.For(type, _targetProperty!.PropertyType);
            _toTargetFrom = type;
        }

        return _toTarget!(value);
    }

    /// <summary>A value of the element as the type of the path's last property.</summary>
    private object? ToSource(object? value)
    {
        if (value is null) return null;

        Type type = value.GetType();

        if (!ReferenceEquals(type, _toSourceFrom))
        {
            _toSource = BindingConversion.For(type, Path.ValueType);
            _toSourceFrom = type;
        }

        return _toSource!(value);
    }

    private void ReportConversionFailure(object? value, Type type, string outcome)
    {
        string message =
            $"[ZF] binding '{Path.Text}': value '{value}' cannot be converted to {type.Name}, {outcome}.";

        System.Diagnostics.Debug.WriteLine(message);
        ConversionFailed?.Invoke(this, message);
    }
}