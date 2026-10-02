using System.Globalization;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A progress bar, straight or round: a range nobody sets but the code.</summary>
public class ProgressBarPeer : UIElementPeer
{
    private readonly Func<RangeInfo> _range;

    public ProgressBarPeer(ProgressBar owner) : base(owner) =>
        _range = () => new RangeInfo(owner.Minimum, owner.Maximum, owner.Value, 0);

    public ProgressBarPeer(CircularProgressBar owner) : base(owner) =>
        _range = () => new RangeInfo(owner.Minimum, owner.Maximum, owner.Value, 0);

    protected override AccessibilityRole DefaultRole => AccessibilityRole.ProgressBar;

    public override RangeInfo? Range => _range();

    /// <summary>As a percentage: "45 %" is what a person wants to hear, not "0.45"
    /// or "45 of 100".</summary>
    public override string? Value
    {
        get
        {
            RangeInfo range = _range();
            double span = range.Maximum - range.Minimum;
            double percent = span <= 0 ? 0 : (range.Value - range.Minimum) / span;

            return percent.ToString("P0", CultureInfo.CurrentCulture);
        }
    }
}

/// <summary>An indeterminate progress: busy, with no value to give.</summary>
public class LoaderPeer(Loader owner) : UIElementPeer(owner)
{
    protected override AccessibilityRole DefaultRole => AccessibilityRole.ProgressBar;

    protected override AccessibilityStates ControlStates =>
        owner.IsRunning ? AccessibilityStates.Busy : AccessibilityStates.None;
}

/// <summary>A slider: a range the user sets.</summary>
public class SliderPeer : UIElementPeer
{
    private readonly TrackBar _bar;

    public SliderPeer(TrackBar owner) : base(owner)
    {
        _bar = owner;
        owner.ValueChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Range);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Slider;

    public override RangeInfo? Range => new(_bar.Minimum, _bar.Maximum, _bar.Value, _bar.Step);

    public override string? Value => _bar.Value.ToString("G", CultureInfo.CurrentCulture);

    protected override AccessibilityActions ControlActions => AccessibilityActions.SetValue;

    public override bool SetRangeValue(double value)
    {
        if (!Owner.IsEffectivelyEnabled) return false;

        _bar.Value = (float)value;
        return true;
    }
}

/// <summary>A scroll bar: the position within the content.</summary>
public class ScrollBarPeer : UIElementPeer
{
    private readonly ScrollBar _bar;

    public ScrollBarPeer(ScrollBar owner) : base(owner)
    {
        _bar = owner;
        owner.ValueChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Range);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.ScrollBar;

    public override RangeInfo? Range => new(0, _bar.MaxValue, _bar.Value, _bar.ViewportSize / 10f);

    protected override AccessibilityActions ControlActions => AccessibilityActions.SetValue;

    public override bool SetRangeValue(double value)
    {
        _bar.Value = (float)value;
        return true;
    }
}