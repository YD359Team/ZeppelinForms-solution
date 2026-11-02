using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Light of an element's own: a neon border, a halo.</summary>
public partial class UIElement
{
    private const string GlowAnimation = "glow-comet";

    /// <summary>Light along the element's border: a neon outline standing round it, or a
    /// comet running along it, see <see cref="Drawing.Primitives.Glow"/>. Drawn over the element,
    /// its light past the bounds as a shadow's is. Null — none.</summary>
    [Styled(Category = "Appearance")]
    public partial Glow? Glow { get; set; }

    /// <summary>Light behind the element: its own shape glowing, or a round light, see
    /// <see cref="Drawing.Primitives.Halo"/>. Null — none.</summary>
    [Styled(Category = "Appearance")]
    public partial Halo? Halo { get; set; }

    /// <summary>Where the comet's head is, from 0 to 1 round the border.</summary>
    private float _glowPhase;

    /// <summary>The form whose clock runs the comet; null — it doesn't run.</summary>
    private Form? _glowClock;

    /// <summary>The period the running comet was started with.</summary>
    private TimeSpan _glowPeriod;

    internal float GlowPhase => _glowPhase;

    /// <summary>The comet runs: there is one, and motion is not reduced.</summary>
    internal bool GlowRuns => Glow is { Motion: GlowMotion.Comet } && !Motion.IsReduced;

    /// <summary>Start the comet if it should run and doesn't: called as the element is
    /// drawn — only an element on screen runs one, and one that stopped, with reduced
    /// motion turned off again, starts on its next frame.</summary>
    internal void EnsureGlowRunning()
    {
        // a comet stopped from outside is forgotten, and starts again here: leaving the
        // form takes the animations of the subtree with it, without a word to the element.
        // A comet on a tab page shown again, on a panel docked elsewhere, used to stand
        // still from then on — the element still thought it ran
        if (_glowClock is { } clock && (!ReferenceEquals(FindOwner(), clock) || !clock.IsAnimating(this, GlowAnimation)))
            _glowClock = null;

        if (_glowClock is not null || !GlowRuns || FindOwner() is not { } form) return;

        _glowClock = form;
        _glowPeriod = Glow!.Period;

        this.AnimateLoop(GlowAnimation, _glowPeriod, phase =>
        {
            // the glow gone or changed, motion reduced, the element out of the form:
            // the comet stops, and the element is drawn as it is now
            if (!GlowRuns || Glow!.Period != _glowPeriod || !ReferenceEquals(FindOwner(), _glowClock))
            {
                StopGlow();
                InvalidateVisual();
                return;
            }

            _glowPhase = phase;
            InvalidateVisual();
        });
    }

    private void StopGlow()
    {
        _glowClock?.RemoveAnimation(this, GlowAnimation);
        _glowClock = null;
    }

    /// <summary>How far the element's light reaches past its bounds: a glow's light and
    /// a halo's.</summary>
    internal float LightReach => Math.Max(Glow?.Reach ?? 0f, Halo?.Reach ?? 0f);
}
