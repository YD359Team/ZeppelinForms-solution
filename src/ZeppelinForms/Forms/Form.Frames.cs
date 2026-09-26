using ZeppelinForms.Animation;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms;

/// <summary>Frames, animations and deferred work of the form. All of it lives
/// in FrameClock — here is only the entry point for the rest of the code.</summary>
public partial class Form
{
    private FrameClock? _clock;

    /// <summary>The form's clock. Created on first access: a form without
    /// animations and deferred work doesn't need it.</summary>
    internal FrameClock Clock => _clock ??= new FrameClock(this);

    public int FrameIntervalMs { get; set; } = 16;   // ~60 frames per second

    internal void AddAnimation(IAnimation animation) => Clock.Add(animation);

    internal void RemoveAnimation(object target, string key) => Clock.Remove(target, key);

    /// <summary>A subtree is leaving the form — its animations are removed.</summary>
    internal void CancelAnimationsIn(UIElement root) => Clock.CancelIn(root);

    /// <summary>A frame from the platform: the window timer, requestAnimationFrame,
    /// Choreographer.</summary>
    internal void Tick()
    {
        // animations read geometry: PageControl offsets pages from their slot,
        // the theme ripple — from the size of the client area
        EnsureLayout();

        Clock.Tick();
    }

    /// <summary>Reconsider whether frames are needed. Visibility is a layout
    /// property, so it is enough to call this from Invalidate: an animation
    /// on a page that is shown again gets its frames back by itself.</summary>
    internal void ReviewFrames() => _clock?.Review();

    /// <summary>The application went to the background.</summary>
    internal void SuspendFrames() => _clock?.Suspend();

    /// <summary>The application came back from the background.</summary>
    internal void ResumeFrames() => _clock?.Resume();

    /// <summary>Run an action on the UI thread after a delay.</summary>
    /// <returns>Cancellation: dispose the result to prevent the call.</returns>
    internal IDisposable Schedule(int delayMs, Action action) =>
        Clock.Schedule(TimeSpan.FromMilliseconds(delayMs), action);
}