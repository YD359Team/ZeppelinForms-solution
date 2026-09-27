using System.Runtime.CompilerServices;
using ZeppelinForms.Animation;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Drawing.Effects;

public static class GlitchExtensions
{
    private const string AnimationKey = "glitch";

    /// <summary>A glitch attached to an element: the effect and the handlers that
    /// keep its animation in step with the element's presence in the tree.</summary>
    private sealed class Attachment(GlitchEffect effect)
    {
        public GlitchEffect Effect { get; } = effect;

        public EventHandler? Attached { get; set; }

        public EventHandler? Detached { get; set; }
    }

    /// <summary>Glitches by element. Weak on the element: a discarded element must
    /// not be kept alive by its glitch.</summary>
    /// <remarks>
    /// The handlers used to be anonymous and were never unsubscribed. StopGlitch
    /// removed the effect and the animation, but the next time the element came
    /// back into the tree, Attached started the animation again — driving the steps
    /// of an effect no longer in the chain and invalidating the element forever,
    /// so frames never stopped. A second Glitch call stacked a second effect and
    /// a second pair of handlers on top of the first.
    /// </remarks>
    private static readonly ConditionalWeakTable<UIElement, Attachment> Attachments = new();

    /// <summary>Remove the glitch and stop its animation.</summary>
    public static void StopGlitch(this UIElement element)
    {
        element.FindOwner()?.RemoveAnimation(element, AnimationKey);

        if (Attachments.TryGetValue(element, out Attachment? attachment))
        {
            Attachments.Remove(element);

            element.Attached -= attachment.Attached;
            element.Detached -= attachment.Detached;

            element.Effects.Remove(attachment.Effect);
            return;
        }

        // a GlitchEffect added to the chain by hand, without Glitch():
        // removing it is still what "stop the glitch" means
        if (element.Effects.Get<GlitchEffect>() is { } effect)
            element.Effects.Remove(effect);
    }

    /// <summary>Start a continuous glitch.</summary>
    /// <remarks>
    /// The animation's target is the element itself rather than the effect:
    /// Form.Tick redraws the target by its type, and the effect, not being
    /// a UIElement, wouldn't cause a redraw.
    ///
    /// The phase is turned into a step with a jump: a glitch twitches rather than
    /// crawls, and intermediate phase values within a step give the same picture.
    ///
    /// A repeated call replaces the previous glitch rather than stacking on it.
    /// </remarks>
    public static GlitchEffect Glitch(
        this UIElement element,
        float intensity = 1f,
        int stepsPerSecond = 12)
    {
        element.StopGlitch();

        var effect = new GlitchEffect { Intensity = intensity };
        element.Effects.Add(effect);

        int steps = Math.Max(1, stepsPerSecond);

        void Start()
        {
            Form? form = element.FindOwner();

            if (form is null) return;

            // a repeated Attached must not start a second animation on top of the first
            form.RemoveAnimation(element, AnimationKey);

            form.AddAnimation(new LoopAnimation(
                element, AnimationKey, TimeSpan.FromSeconds(1),
                phase =>
                {
                    int step = (int)(phase * steps);
                    if (step == effect.Step) return;

                    effect.Step = step;
                    element.InvalidateVisual();
                }));
        }

        var attachment = new Attachment(effect)
        {
            // the animation must be removed when leaving the tree: otherwise it lives
            // until the form closes and keeps calling InvalidateVisual on an invisible
            // page — frames never stop
            Detached = (_, _) => element.FindOwner()?.RemoveAnimation(element, AnimationKey),

            // re-attaching is possible: a page may be detached when leaving it
            // and attached back on returning
            Attached = (_, _) => Start(),
        };

        element.Detached += attachment.Detached;
        element.Attached += attachment.Attached;

        Attachments.Add(element, attachment);

        if (element.FindOwner() is not null) Start();

        return effect;
    }
}