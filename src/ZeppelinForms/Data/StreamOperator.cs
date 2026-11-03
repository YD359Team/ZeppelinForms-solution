namespace ZeppelinForms.Data;

/// <summary>
/// A step of a path whose property is a stream of values rather than a value: a
/// <see cref="Task{TResult}"/> — its result, once there — or an
/// <see cref="IObservable{T}"/> — its latest value. What markup's <c>^</c> after a property
/// compiles to: <c>{Bind Profile^.Name}</c>, <c>{Bind Prices^}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Until the stream has a value the path has none: the element gets the binding's
/// <see cref="BindingOptions.FallbackValue"/>, or the property's default. A task that
/// fails or is cancelled never gives one. A new value — the task ending, the observable's
/// next — is written on the element's UI thread, wherever it came from.
/// </para>
/// <para>
/// The binding subscribes to the stream while the stream is on the path, and unsubscribes
/// when the property gives another one, the path breaks before it or the binding goes. A
/// value of another type than the stream's is taken as it is — a property that is a stream
/// only sometimes.
/// </para>
/// <para>
/// A step that is a stream can't be written: a binding both ways stops before it.
/// </para>
/// </remarks>
public abstract class StreamOperator
{
    private protected StreamOperator()
    {
    }

    /// <summary>The result of a <see cref="Task{TResult}"/>.</summary>
    public static StreamOperator FromTask<T>() => TaskStream<T>.Instance;

    /// <summary>The latest value of an <see cref="IObservable{T}"/>.</summary>
    public static StreamOperator FromObservable<T>() => ObservableStream<T>.Instance;

    /// <summary>Listen to <paramref name="source"/>. False — it isn't this kind of stream,
    /// and is the value itself.</summary>
    /// <param name="subscription">To end listening; null — nothing to end.</param>
    internal abstract bool TrySubscribe(object source, StreamListener listener, out IDisposable? subscription);

    private sealed class TaskStream<T> : StreamOperator
    {
        public static readonly TaskStream<T> Instance = new();

        internal override bool TrySubscribe(object source, StreamListener listener, out IDisposable? subscription)
        {
            subscription = null;

            if (source is not Task<T> task) return false;

            if (task.IsCompleted)
            {
                if (task.IsCompletedSuccessfully) listener.OnValue(task.Result);

                return true;
            }

            // the listener is dropped by the binding when the task leaves the path: a
            // result that comes after that is ignored by it
            task.ContinueWith(
                static (done, state) =>
                {
                    if (done.IsCompletedSuccessfully) ((StreamListener)state!).OnValue(done.Result);
                },
                listener,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return true;
        }
    }

    private sealed class ObservableStream<T> : StreamOperator
    {
        public static readonly ObservableStream<T> Instance = new();

        internal override bool TrySubscribe(object source, StreamListener listener, out IDisposable? subscription)
        {
            subscription = null;

            if (source is not IObservable<T> observable) return false;

            subscription = observable.Subscribe(new Observer(listener));
            return true;
        }

        /// <summary>The values; an error or the end leaves the last one where it is.</summary>
        private sealed class Observer(StreamListener listener) : IObserver<T>
        {
            public void OnNext(T value) => listener.OnValue(value);

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }
    }
}

/// <summary>The latest value of one subscription of a binding to a stream.</summary>
internal sealed class StreamListener(Action<StreamListener> changed)
{
    private readonly Lock _lock = new();
    private object? _latest;
    private bool _hasValue;

    /// <summary>Dropped by its binding: values that still come are ignored.</summary>
    public bool IsDropped { get; private set; }

    /// <summary>Set while subscribing: a value given at once — an observable's current one,
    /// a finished task's result — is only kept, as the binding reads it right after.</summary>
    public bool IsSubscribing { get; set; }

    public void OnValue(object? value)
    {
        lock (_lock)
        {
            if (IsDropped) return;

            _latest = value;
            _hasValue = true;
        }

        if (!IsSubscribing) changed(this);
    }

    public bool TryGetValue(out object? value)
    {
        lock (_lock)
        {
            value = _latest;
            return _hasValue;
        }
    }

    public void Drop()
    {
        lock (_lock)
            IsDropped = true;
    }
}
