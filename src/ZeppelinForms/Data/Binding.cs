using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
/// <para>
/// A change told on another thread than the form's — a view model filled by a worker,
/// a timer, a socket — is carried to the form's UI thread: the element is written there,
/// once for any number of changes told before it got its turn. Until 0.21 the element
/// was written on the thread that raised <c>PropertyChanged</c>, in the middle of
/// whatever layout and drawing were doing on the UI thread.
/// </para>
/// <para>
/// <see cref="BindingOptions"/> add a converter, a format and the values written where
/// there is none on the way to the element; a step of the path may be a stream — a task's
/// result, an observable's latest value (<see cref="StreamOperator"/>).
/// </para>
/// </remarks>
public sealed class Binding
{
    /// <summary>"No value": returned by a converter, it makes the binding write its
    /// <see cref="BindingOptions.FallbackValue"/>, or the property's default. The value of
    /// an option not set.</summary>
    public static object UnsetValue { get; } = new Marker("{Binding.UnsetValue}");

    /// <summary>Returned by a converter: the other end keeps the value it has.</summary>
    public static object DoNothing { get; } = new Marker("{Binding.DoNothing}");

    private sealed class Marker(string text)
    {
        public override string ToString() => text;
    }

    /// <summary>A binding's subscription to the stream a step of its path read.</summary>
    private sealed class StreamSlot(object source, StreamListener listener, IDisposable? subscription)
    {
        /// <summary>The task or the observable listened to.</summary>
        public object Source { get; } = source;

        public StreamListener Listener { get; } = listener;

        public void Drop()
        {
            Listener.Drop();
            subscription?.Dispose();
        }
    }

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

    /// <summary>1 — a change told on another thread waits for its turn on the UI thread;
    /// the changes told after it ride along.</summary>
    private int _refreshQueued;

    // The conversions last used, by the runtime type of the value they were made for:
    // a binding sees values of one type almost always, so the shared table of
    // BindingConversion is asked once, not on every write. The UI thread's, as the
    // rest of the binding: no lock
    private Type? _toTargetFrom;
    private Func<object, object?>? _toTarget;
    private Type? _toSourceFrom;
    private Func<object, object?>? _toSource;

    /// <summary>The end at the element as the property's own type, when the path gives
    /// its last property as that type: the value goes through without a box. Null —
    /// through object, converted.</summary>
    private readonly BindingEnd? _end;

    /// <summary>The streams the steps listen to, by step; null — the path has none.</summary>
    private readonly StreamSlot?[]? _streams;

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
        BindingMode mode,
        BindingOptions? options = null)
    {
        _target = new WeakReference<UIElement>(target);
        _targetProperty = targetProperty;
        _apply = apply;
        Path = path;
        Mode = mode;
        Options = options;
        UsesDataContext = usesDataContext;

        // the typed end moves the last property's value as it is: none where options
        // change it on the way, or where the last step is a stream and its property is
        // the task, not the value
        _end = targetProperty is not null && options is null && path[path.Length - 1].Stream is null
            ? path.Value?.EndFor(targetProperty)
            : null;

        if (path.Steps.Any(step => step.Stream is not null))
            _streams = new StreamSlot?[path.Length];

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

    /// <summary>The converter, the format and the values where there is none; null — the
    /// value goes as it is.</summary>
    public BindingOptions? Options { get; }

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

            if (current is null)
            {
                // no object to read the step from: its stream is off the path
                DropStream(i);
                continue;
            }

            if (current is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += _handlers[i];
                _followed[i] = notify;
            }

            // the last owner's property is the value: read when it is written
            if (i < _owners.Length - 1)
            {
                object? next = ReadStep(i, current, out bool has);
                current = has ? next : null;
            }
        }
    }

    /// <summary>A step's value: its property, or what the stream the property is gives.
    /// False — a stream with no value yet.</summary>
    private object? ReadStep(int step, object owner, out bool has)
    {
        object? read = Path[step].Get(owner);

        if (Path[step].Stream is not { } stream)
        {
            has = true;
            return read;
        }

        StreamSlot? slot = _streams![step];

        // the same stream as before: its latest value
        if (slot is not null && ReferenceEquals(slot.Source, read))
        {
            has = slot.Listener.TryGetValue(out object? latest);
            return latest;
        }

        DropStream(step);

        if (read is null)
        {
            has = false;
            return null;
        }

        var listener = new StreamListener(_ => OnStreamValue(step)) { IsSubscribing = true };

        // not a stream of the kind: the value itself
        if (!stream.TrySubscribe(read, listener, out IDisposable? subscription))
        {
            has = true;
            return read;
        }

        listener.IsSubscribing = false;
        _streams[step] = new StreamSlot(read, listener, subscription);

        has = listener.TryGetValue(out object? value);
        return value;
    }

    private void DropStream(int step)
    {
        if (_streams?[step] is not { } slot) return;

        _streams[step] = null;
        slot.Drop();
    }

    /// <summary>A stream on the path gave a value: on any thread, carried to the form's.</summary>
    private void OnStreamValue(int step)
    {
        if (QueueIfOffThread()) return;

        if (_detached) return;

        if (step < _owners.Length - 1)
        {
            object? next = null;
            bool has = false;

            if (_owners[step] is { } owner)
                next = ReadStep(step, owner, out has);

            _owners[step + 1] = has ? next : null;

            Follow(step + 1);
        }

        PushToTarget();
    }

    /// <summary>The value at the end of the path; false — a null on the way.</summary>
    private bool TryRead(out object? value)
    {
        value = null;

        if (_owners[^1] is not { } owner) return false;

        value = ReadStep(Path.Length - 1, owner, out bool has);
        return has;
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

        // the typed end: read, compared and written as the property's type, no box
        if (_end is not null)
        {
            if (!_end.Take(_owners[^1])) return;

            _updating = true;

            try
            {
                _end.Write(element);
            }
            finally
            {
                _updating = false;
            }

            return;
        }

        object? value;

        if (!TryRead(out object? raw))
        {
            // nothing at the end of the path: the fallback, or the property's own default
            value = Fallback();
        }
        else if (_targetProperty is null)
        {
            // a property that isn't styled: the generated code casts it, as it was typed
            value = raw;
        }
        else if (!TryMakeTargetValue(raw, out value))
        {
            return;
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

    /// <summary>The value for the element from the source's: through the options, then as
    /// the property's type. False — the element keeps the value it has.</summary>
    private bool TryMakeTargetValue(object? raw, out object? value)
    {
        value = null;
        object? converted = raw;

        try
        {
            if (Options is { } options)
            {
                if (raw is null && options.HasTargetNullValue)
                {
                    converted = options.TargetNullValue;
                }
                else
                {
                    if (options.Converter is { } converter)
                    {
                        converted = converter.Convert(raw, _targetProperty!.PropertyType, options.ConverterParameter, options.CurrentCulture);

                        if (ReferenceEquals(converted, DoNothing)) return false;

                        if (ReferenceEquals(converted, UnsetValue))
                        {
                            value = Fallback();
                            return true;
                        }
                    }

                    converted = options.Format(converted);
                }
            }

            value = ToTarget(converted);
            return true;
        }
        // ArgumentException comes from Enum.Parse on an unknown name
        catch (Exception e) when (IsConversionError(e))
        {
            // the source's type doesn't fit the property's type — there is nothing
            // to write, but crashing in the middle of drawing is wrong.
            //
            // Not ZfContract.Fail either, as it used to be: a contract is the
            // framework's own rule, and here the view model's type is wrong, not
            // the framework. ZfContract throws in debug builds, and in the
            // previewer that exception came out of layout on every frame — the
            // preview showed nothing but it
            if (Options is { HasFallbackValue: true })
            {
                value = Fallback();
                return true;
            }

            ReportConversionFailure(raw, _targetProperty!.PropertyType, "the element keeps its value");

            return false;
        }
    }

    /// <summary>What the element gets where the path gives no value: the fallback as the
    /// property's type, or the property's default.</summary>
    private object? Fallback()
    {
        if (Options is not { HasFallbackValue: true } options || _targetProperty is null)
            return _targetProperty?.DefaultBoxed;

        try
        {
            return ToTarget(options.FallbackValue);
        }
        catch (Exception e) when (IsConversionError(e))
        {
            ReportConversionFailure(options.FallbackValue, _targetProperty.PropertyType, "the property's default is written");

            return _targetProperty.DefaultBoxed;
        }
    }

    private static bool IsConversionError(Exception e) =>
        e is InvalidCastException or FormatException or OverflowException or ArgumentException;

    /// <summary>Write the element's value to the source, as its type: without a box
    /// where the path's end is of it. TwoWay only.</summary>
    internal void PushToSource<[DynamicallyAccessedMembers(StyledProperty.ValueTypeMembers)] T>(T value)
    {
        if (_end is not BindingEnd<T> end || !end.CanWrite)
        {
            PushToSource((object?)value);
            return;
        }

        if (_detached || Mode != BindingMode.TwoWay || _updating) return;

        if (_owners[^1] is not { } owner) return;

        _updating = true;

        try
        {
            end.WriteBack(owner, value);
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
            object? back = value;

            if (Options is { } options)
            {
                if (options.HasTargetNullValue && Equals(value, options.TargetNullValue))
                {
                    // what stands for null on the element is null at the source
                    back = null;
                }
                else if (options.Converter is { } converter)
                {
                    back = converter.ConvertBack(value, Path.ValueType, options.ConverterParameter, options.CurrentCulture);

                    if (ReferenceEquals(back, DoNothing) || ReferenceEquals(back, UnsetValue)) return;
                }
            }

            set(owner, ToSource(back));

            // what the element shows is what the source has now: the source changed
            // back to the old value later is a change to write again
            _written = value;
            _hasWritten = true;
            _end?.RememberBoxed(value);
        }
        catch (Exception e) when (IsConversionError(e))
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

        // told on a worker thread: nothing of the binding or the element is touched
        // here, the whole path is read again on the form's thread
        if (QueueIfOffThread()) return;

        if (_updating) return;

        // an object on the way gave another one: read it, and follow from there
        if (step < _owners.Length - 1)
        {
            object? next = null;
            bool has = false;

            if (_owners[step] is { } owner)
                next = ReadStep(step, owner, out has);

            _owners[step + 1] = has ? next : null;

            Follow(step + 1);
        }

        PushToTarget();
    }

    /// <summary>A change told on another thread than the form's: queue a refresh to the
    /// form's dispatcher, unless one waits already. False — the calling thread is the
    /// form's, or the element is in no form and nobody draws it.</summary>
    private bool QueueIfOffThread()
    {
        if (!_target.TryGetTarget(out UIElement? element)) return false;

        // the form's dispatcher, not Dispatcher.UIThread: a form has the dispatcher of
        // the thread that opened its window. An element out of every form is written
        // where it is told, as before — nothing draws it, and the form it enters
        // reads it on its own thread
        if (element.PeekOwner()?.Dispatcher is not { } dispatcher || dispatcher.CheckAccess())
            return false;

        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
            dispatcher.BeginInvoke(Refresh);

        return true;
    }

    /// <summary>The changes told on other threads, taken on the UI thread: which steps
    /// changed is no longer known, and several may have, so the whole path is followed
    /// and read again from the source.</summary>
    private void Refresh()
    {
        // first: a change told while this runs queues the next refresh
        Volatile.Write(ref _refreshQueued, 0);

        if (_detached) return;

        Follow(0);
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

            DropStream(i);
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