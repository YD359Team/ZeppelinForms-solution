# Threading and timers

The UI belongs to one thread — the one `App.Run` was called on, or the first one that opened
a window. Elements are read and changed there; everything else reaches it through a
dispatcher.

## The dispatcher

```csharp
// from a worker
form.Invoke(() => status.Text = "Done");            // runs on the UI thread, waits for it
form.BeginInvoke(() => progress.Value = percent);   // queues it, doesn't wait
await form.Dispatcher.InvokeAsync(() => list.Items.Add(item));

if (!Dispatcher.UIThread.CheckAccess())
    Dispatcher.UIThread.BeginInvoke(Refresh);
```

`Dispatcher.UIThread` is the application's; `form.Dispatcher` goes through that form's window
while it is open. `async` code started on the UI thread comes back to it after `await`, as in
WinForms and WPF.

Two things need no marshalling at all:

- `DataGridView.Refresh()` may be called from any thread — the redraw is queued, once for a
  burst of calls.
- A [data source](Data-Binding#threads) changed on another thread updates its control on
  the UI thread by itself.

## DispatcherTimer

A timer whose `Tick` comes on the UI thread — a clock in the status bar, a poll, an autosave —
with no `Invoke` in the handler.

```csharp
var clock = new DispatcherTimer(this) { Interval = TimeSpan.FromSeconds(1) };
clock.Tick += (_, _) => status.Text = DateTime.Now.ToLongTimeString();
clock.Start();
```

| Member | |
| ------ | - |
| `DispatcherTimer(form)` | Goes through the form's dispatcher and **stops when the form closes** |
| `DispatcherTimer()` | Goes through `Dispatcher.UIThread` |
| `DispatcherTimer(interval, tick)` | Made and started in one line |
| `Interval`, `IsEnabled`, `Start()`, `Stop()`, `Tag` | As in WinForms and WPF |
| `Dispose()` | Stops it for good |

- **Ticks don't pile up.** While one is queued and not yet run, the next one is dropped: a UI
  thread busy for a second gets one tick after it, not sixty in a row.
- **The cadence is kept.** The interval is measured from the start, not from the end of the
  last handler, so a slow handler doesn't stretch it.
- **A stopped timer is stopped.** A tick queued before `Stop()` doesn't run after it, and one
  queued before a restart doesn't count for the new run. `Start`, `Stop` and the properties
  may be used from any thread.

A tick that reached the controls of a closed form was the classic bug of a timer started in
`Load` and forgotten; a timer made for a form can't do that.

### Slow work on a timer

The handler runs on the UI thread: keep the slow part off it.

```csharp
private bool _polling;

private async void OnTick(object? sender, EventArgs e)
{
    if (_polling) return;          // a slow poll isn't overlapped by the next tick
    _polling = true;

    try
    {
        Snapshot snapshot = await Task.Run(TakeSnapshot);   // on a worker
        Apply(snapshot);                                    // back on the UI thread
    }
    finally
    {
        _polling = false;
    }
}
```

## Unhandled exceptions

```csharp
App.UnhandledException += (_, e) =>
{
    log.Error(e.Exception);
    e.Handled = true;              // keep the application running
};
```

Raised on the UI thread for an exception that escaped an event handler, a queued action, an
`await` continuation or `async void` method, an animation step, deferred work, layout or
painting. Without a handler the exception flies on as it would anyway, and a debugger stops
where it was thrown.
