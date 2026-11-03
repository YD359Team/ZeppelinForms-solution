using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Commands;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.21: a command whose action is asynchronous — disabled while it runs,
/// cancelled by its token, its exception where an async handler's goes.</summary>
[Collection("Platform")]
public sealed class AsyncCommandTests
{
    /// <summary>Runs what is posted at once and keeps what it throws: the UI thread of a
    /// test.</summary>
    private sealed class InlineContext : SynchronizationContext
    {
        public List<Exception> Errors { get; } = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                d(state);
            }
            catch (Exception e)
            {
                Errors.Add(e);
            }
        }
    }

    [Fact]
    public async Task ItCantRunAgainWhileItRuns()
    {
        var work = new TaskCompletionSource();
        int runs = 0;
        var command = new AsyncCommand(() =>
        {
            runs++;
            return work.Task;
        });

        var changes = new List<string?>();
        command.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Task run = command.ExecuteAsync(null);

        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));

        // a second click while the first run goes on starts nothing
        await command.ExecuteAsync(null);
        Assert.Equal(1, runs);

        work.SetResult();
        await run;

        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
        Assert.Equal(2, changes.Count(name => name == nameof(AsyncCommand.IsRunning)));
    }

    [Fact]
    public async Task ConcurrentRunsWhenAllowed()
    {
        var work = new TaskCompletionSource();
        int runs = 0;
        var command = new AsyncCommand(() =>
        {
            runs++;
            return work.Task;
        })
        {
            AllowConcurrentExecutions = true,
        };

        Task first = command.ExecuteAsync(null);
        Task second = command.ExecuteAsync(null);

        Assert.Equal(2, runs);
        Assert.True(command.CanExecute(null));

        work.SetResult();
        await Task.WhenAll(first, second);

        Assert.False(command.IsRunning);
    }

    [Fact]
    public async Task CancelCancelsTheTokenAndIsNoError()
    {
        CancellationToken seen = default;
        var command = new AsyncCommand(async token =>
        {
            seen = token;
            await Task.Delay(Timeout.Infinite, token);
        });

        Task run = command.ExecuteAsync(null);

        Assert.True(command.CancelCommand.CanExecute(null));

        command.CancelCommand.Execute(null);

        Assert.True(seen.IsCancellationRequested);
        Assert.True(command.IsCancellationRequested);

        await run;

        Assert.False(command.IsRunning);
        Assert.False(command.IsCancellationRequested);
        Assert.False(command.CancelCommand.CanExecute(null));

        // the next run has a token of its own
        Task again = command.ExecuteAsync(null);
        Assert.False(seen.IsCancellationRequested);

        command.Cancel();
        await again;
    }

    [Fact]
    public async Task AnExceptionComesOutOfExecuteAsync()
    {
        var command = new AsyncCommand(() => Task.FromException(new InvalidOperationException("offline")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(null));

        Assert.Equal("offline", error.Message);
        Assert.True(command.ExecutionTask!.IsFaulted);
        Assert.False(command.IsRunning);
    }

    [Fact]
    public void ExecutesExceptionGoesToTheUIThread()
    {
        SynchronizationContext? before = SynchronizationContext.Current;
        var context = new InlineContext();
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            var work = new TaskCompletionSource();
            var command = new AsyncCommand(() => work.Task);

            command.Execute(null);
            work.SetException(new InvalidOperationException("offline"));

            Exception error = Assert.Single(context.Errors);
            Assert.Equal("offline", error.Message);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }

    [Fact]
    public async Task TheParameterAndTheCanExecuteArePassed()
    {
        object? got = null;
        var command = new AsyncCommand((parameter, _) =>
        {
            got = parameter;
            return Task.CompletedTask;
        }, parameter => parameter is string);

        Assert.False(command.CanExecute(42));

        await command.ExecuteAsync("order-7");

        Assert.Equal("order-7", got);
    }

    [Fact]
    public void AButtonIsDisabledWhileItsCommandRuns()
    {
        var work = new TaskCompletionSource();
        var command = new AsyncCommand(() => work.Task);
        var button = new Button { Text = "Load", Command = command };

        var form = new Form { Size = new Size(200, 100), Content = button };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        Assert.True(button.IsEnabled);

        command.Execute(null);
        Assert.False(button.IsEnabled);

        work.SetResult();
        Assert.True(button.IsEnabled);
    }

    [Fact]
    public void ItsShortcutIsAKeyBindingsAndAMenuItems()
    {
        var gesture = new KeyGesture(Key.R, KeyModifiers.Control);
        var command = new AsyncCommand(() => Task.CompletedTask) { Gesture = gesture };

        Assert.Equal(gesture, new KeyBinding(command).Gesture);
        Assert.Equal(gesture, new MenuItem { Command = command }.EffectiveGesture);
    }
}
