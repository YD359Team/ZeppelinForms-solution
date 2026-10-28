using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Forms;

/// <summary>Snackbars: a short message at the bottom of the form, with an action.</summary>
public partial class Form
{
    private readonly Queue<Func<Task>> _snackbars = new();
    private bool _snackbarShowing;

    /// <summary>
    /// Show a short message at the bottom of the form, with an action the user may take on
    /// it, for <paramref name="duration"/> — 4 seconds by default, 10 with an action. One
    /// at a time: a second waits for the first.
    /// </summary>
    /// <returns>True — the action was taken; false — the message timed out.</returns>
    /// <example>
    /// <code>
    /// if (await ShowSnackbarAsync("Message deleted", "Undo"))
    ///     Restore(message);
    /// </code>
    /// </example>
    public Task<bool> ShowSnackbarAsync(string message, string? action = null, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeSpan shown = duration ?? TimeSpan.FromSeconds(action is null ? 4 : 10);

        _snackbars.Enqueue(() => ShowOneSnackbar(message, action, shown, result));

        if (!_snackbarShowing) ShowNextSnackbar();

        return result.Task;
    }

    private async void ShowNextSnackbar()
    {
        // async void: the queue runs by itself; each snackbar's task is its caller's
        while (_snackbars.TryDequeue(out Func<Task>? next))
        {
            _snackbarShowing = true;
            await next();
        }

        _snackbarShowing = false;
    }

    private async Task ShowOneSnackbar(string message, string? action, TimeSpan duration, TaskCompletionSource<bool> result)
    {
        var bar = new Snackbar(message, action);
        var gone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool taken = false;

        AttachOverlay(bar);
        PlaceSnackbar(bar, progress: 0f);

        IDisposable? timer = null;

        void Dismiss()
        {
            if (gone.Task.IsCompleted) return;

            timer?.Dispose();

            AnimateSnackbar(bar, to: 0f, () =>
            {
                ClientSizeChanged -= OnResized;
                SafeAreaInsetsChanged -= OnResized;
                DetachOverlay(bar);
                Invalidate();
                gone.TrySetResult();
            });
        }

        void OnResized(object? sender, EventArgs e) => PlaceSnackbar(bar, progress: 1f);

        bar.ActionInvoked += (_, _) =>
        {
            taken = true;
            Dismiss();
        };

        ClientSizeChanged += OnResized;
        SafeAreaInsetsChanged += OnResized;

        AnimateSnackbar(bar, to: 1f, () => timer = Schedule((int)duration.TotalMilliseconds, Dismiss));

        await gone.Task;

        result.TrySetResult(taken);
    }

    /// <summary>The snackbar's progress in, for the placement.</summary>
    private float _snackbarProgress;

    private void AnimateSnackbar(Snackbar bar, float to, Action done)
    {
        if (PlatformWindow is null || Motion.IsReduced)
        {
            _snackbarProgress = to;
            PlaceSnackbar(bar, to);
            done();
            return;
        }

        bar.Animate("snackbar", _snackbarProgress, to, TimeSpan.FromMilliseconds(200), Interpolators.Float,
            value =>
            {
                _snackbarProgress = value;
                PlaceSnackbar(bar, value);
            },
            Easing.EaseOut,
            done);
    }

    /// <summary>At the bottom, in the middle, above the navigation bar or the gesture bar;
    /// coming in, it rises from a little lower and fades in.</summary>
    private void PlaceSnackbar(Snackbar bar, float progress)
    {
        const float margin = 8f;

        float width = Math.Max(0, Math.Min(ClientSize.Width - 2f * margin - SafeAreaInsets.Horizontal, 560f));

        bar.PlacedWidth = width;
        bar.Measure(new Size(width, float.PositiveInfinity));

        float bottom = Content is Scaffold scaffold ? scaffold.BottomObstruction : SafeAreaInsets.Bottom;
        float y = ClientSize.Height - bottom - margin - bar.DesiredSize.Height + (1f - progress) * 16f;

        bar.Position = new Point(SafeAreaInsets.Left + (ClientSize.Width - SafeAreaInsets.Horizontal - width) / 2f, y);
        bar.Opacity = progress;

        Invalidate();
    }
}
