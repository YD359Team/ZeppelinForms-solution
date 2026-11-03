using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Input.Commands;

/// <summary>A command whose action is asynchronous — a download, a save, a query: the
/// buttons and menu items with it are disabled while it runs, and it can be cancelled.</summary>
/// <remarks>
/// <para>
/// As <c>AsyncRelayCommand</c> of CommunityToolkit.Mvvm and Avalonia's reactive commands
/// do: <see cref="CanExecute"/> is false while a run goes on — a second click doesn't
/// start a second download — unless <see cref="AllowConcurrentExecutions"/>.
/// <see cref="IsRunning"/> says it, for a spinner bound to it, and the buttons are told
/// through <see cref="CanExecuteChanged"/> as a run starts and ends.
/// </para>
/// <para>
/// The action gets a <see cref="CancellationToken"/>: <see cref="Cancel"/> — or
/// <see cref="CancelCommand"/> on a button — cancels it. A run that ends by the token's
/// <see cref="OperationCanceledException"/> after a cancel is no error.
/// </para>
/// <para>
/// An exception of the action goes where an exception of an <c>async void</c> handler goes:
/// to the UI thread, where <see cref="App.UnhandledException"/> is offered it. Awaiting
/// <see cref="ExecuteAsync"/> or <see cref="ExecutionTask"/> observes it instead.
/// </para>
/// <para>
/// Run it from the UI thread, as a button does: the action's continuations, and the
/// events of the command, come back there.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Load = new AsyncCommand(async token =&gt; Orders = await _repository.LoadAsync(token));
/// </code>
/// </example>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The token source lives while runs go on and is disposed as the last one ends: a command " +
                    "that is no longer running holds none, and nobody disposes the commands of a view model.")]
public class AsyncCommand : ICommand, INotifyPropertyChanged, IGestureCommand
{
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>Cancels the runs going on; null — none runs.</summary>
    private CancellationTokenSource? _cancellation;

    private int _running;
    private Command? _cancelCommand;

    // Overloads rather than an optional canExecute, as Command has them (RS0026)

    /// <param name="execute">The action; it can always run.</param>
    public AsyncCommand(Func<Task> execute)
        : this(execute, null)
    {
    }

    /// <param name="execute">The action.</param>
    /// <param name="canExecute">Whether it can run now; null — always.</param>
    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = (_, _) => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    /// <param name="execute">The action, with the token <see cref="Cancel"/> cancels; it
    /// can always run.</param>
    public AsyncCommand(Func<CancellationToken, Task> execute)
        : this(execute, null)
    {
    }

    /// <param name="execute">The action, with the token <see cref="Cancel"/> cancels.</param>
    /// <param name="canExecute">Whether it can run now; null — always.</param>
    public AsyncCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = (_, token) => execute(token);
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    /// <param name="execute">The action, with the parameter of the button or the menu item
    /// that runs it and the token <see cref="Cancel"/> cancels; it can always run.</param>
    public AsyncCommand(Func<object?, CancellationToken, Task> execute)
        : this(execute, null)
    {
    }

    /// <param name="execute">The action, with the parameter of the button or the menu item
    /// that runs it and the token <see cref="Cancel"/> cancels.</param>
    /// <param name="canExecute">Whether it can run now with that parameter; null — always.</param>
    public AsyncCommand(Func<object?, CancellationToken, Task> execute, Func<object?, bool>? canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>The shortcut: shown beside the caption of a menu item with this command,
    /// and run by the form, as with <see cref="Command.Gesture"/>.</summary>
    public KeyGesture? Gesture { get; init; }

    /// <summary>Whether a run may start while another goes on. False — the default: the
    /// command can't run until the run ends.</summary>
    public bool AllowConcurrentExecutions { get; init; }

    /// <summary>Whether a run goes on.</summary>
    public bool IsRunning => _running > 0;

    /// <summary>The task of the latest run's action; null — it never ran. Awaiting it
    /// gives the action's exception.</summary>
    public Task? ExecutionTask { get; private set; }

    /// <summary>Whether the runs going on were asked to stop.</summary>
    public bool IsCancellationRequested => _cancellation?.IsCancellationRequested ?? false;

    /// <summary>A command that cancels this one's runs, available while one goes on: for
    /// the Cancel button beside a progress bar.</summary>
    public ICommand CancelCommand => _cancelCommand ??= new Command(Cancel, () => IsRunning && !IsCancellationRequested);

    public event EventHandler? CanExecuteChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual bool CanExecute(object? parameter) =>
        (AllowConcurrentExecutions || !IsRunning) && (_canExecute?.Invoke(parameter) ?? true);

    /// <summary>Start a run, if it can run now. What a button does; the run's exception
    /// goes to the UI thread.</summary>
    public void Execute(object? parameter) => Observe(ExecuteAsync(parameter));

    /// <summary>Start a run, if it can run now, and give its task: completed at once when
    /// it can't run.</summary>
    public Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter)) return Task.CompletedTask;

        // a run after a cancel gets a token of its own: the old one stays cancelled for the
        // runs that still have it, and isn't disposed under them — a source without a
        // timer holds nothing to free
        if (_cancellation is null || _cancellation.IsCancellationRequested)
            _cancellation = new CancellationTokenSource();

        return Run(parameter, _cancellation);
    }

    /// <summary>Ask the runs going on to stop: their token is cancelled.</summary>
    public void Cancel()
    {
        if (_cancellation is not { IsCancellationRequested: false } cancellation) return;

        cancellation.Cancel();

        OnPropertyChanged(nameof(IsCancellationRequested));
        _cancelCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>Tell the buttons with this command to ask <see cref="CanExecute"/> again.</summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    protected virtual void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private async Task Run(object? parameter, CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;

        _running++;

        if (_running == 1)
        {
            OnPropertyChanged(nameof(IsRunning));
            _cancelCommand?.NotifyCanExecuteChanged();
        }

        NotifyCanExecuteChanged();

        try
        {
            Task task = _execute(parameter, token);

            ExecutionTask = task;
            OnPropertyChanged(nameof(ExecutionTask));

            await task;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // stopped as it was asked to: no error
        }
        finally
        {
            _running--;

            if (_running == 0)
            {
                // the last run is over: the next one starts with a token of its own
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    bool wasCancelled = cancellation.IsCancellationRequested;

                    _cancellation = null;
                    cancellation.Dispose();

                    if (wasCancelled) OnPropertyChanged(nameof(IsCancellationRequested));
                }

                OnPropertyChanged(nameof(IsRunning));
                _cancelCommand?.NotifyCanExecuteChanged();
            }

            NotifyCanExecuteChanged();
        }
    }

    /// <summary>A run started by <see cref="Execute"/>, which returns nothing: its exception
    /// is thrown on the UI thread, as from an async handler of an event.</summary>
    private static async void Observe(Task run) => await run;
}
