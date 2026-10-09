using System.Globalization;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A range slider: a group of two sliders, one per thumb, as the ARIA
/// pattern for a multi-thumb slider has it. The group carries the control's name;
/// each thumb is named by its end and has a range of its own.</summary>
public class RangeSliderPeer : UIElementPeer
{
    private readonly RangeSlider _slider;
    private readonly RangeThumbPeer _lower;
    private readonly RangeThumbPeer _upper;

    public RangeSliderPeer(RangeSlider owner) : base(owner)
    {
        _slider = owner;
        _lower = new RangeThumbPeer(owner, RangeThumb.Lower, this);
        _upper = new RangeThumbPeer(owner, RangeThumb.Upper, this);

        owner.RangeChanged += (_, _) =>
        {
            _lower.NotifyRange();
            _upper.NotifyRange();
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Group;

    public override IReadOnlyList<AccessibilityPeer> Children => [_lower, _upper];

    /// <summary>The thumb the keyboard moves, while the slider has the focus.</summary>
    public override AccessibilityPeer? FocusedDescendant =>
        _slider.IsFocused ? (_slider.ActiveThumb == RangeThumb.Lower ? _lower : _upper) : null;
}

/// <summary>One thumb of a range slider. Its range is the whole slider's: a screen
/// reader announces the value against the same scale for both ends.</summary>
public class RangeThumbPeer : AccessibilityPeer
{
    private readonly RangeSlider _slider;
    private readonly RangeThumb _thumb;
    private readonly RangeSliderPeer _parent;

    internal RangeThumbPeer(RangeSlider slider, RangeThumb thumb, RangeSliderPeer parent)
    {
        _slider = slider;
        _thumb = thumb;
        _parent = parent;
    }

    private float Current => _thumb == RangeThumb.Lower ? _slider.LowerValue : _slider.UpperValue;

    internal void NotifyRange() => RaisePropertyChanged(AccessibilityProperty.Range);

    public override AccessibilityRole Role => AccessibilityRole.Slider;

    public override string Name => Localization.Get(_thumb == RangeThumb.Lower ? ZfText.RangeLower : ZfText.RangeUpper);

    public override RangeInfo? Range => new(_slider.Minimum, _slider.Maximum, Current, _slider.Step);

    public override string? Value => Current.ToString("G", CultureInfo.CurrentCulture);

    public override AccessibilityStates States
    {
        get
        {
            AccessibilityStates states = AccessibilityStates.None;

            if (_slider.IsFocused && _slider.ActiveThumb == _thumb) states |= AccessibilityStates.Focused;
            if (!_slider.IsEffectivelyEnabled) states |= AccessibilityStates.Disabled;

            return states;
        }
    }

    public override AccessibilityActions Actions =>
        _slider.IsEffectivelyEnabled ? AccessibilityActions.SetValue : AccessibilityActions.None;

    public override AccessibilityPeer? Parent => _parent;

    public override IReadOnlyList<AccessibilityPeer> Children => [];

    public override Rectangle Bounds => _slider.ThumbBounds(_thumb);

    public override Form? Form => _slider.FindOwner();

    public override bool SetRangeValue(double value)
    {
        if (!_slider.IsEffectivelyEnabled) return false;

        if (_thumb == RangeThumb.Lower) _slider.LowerValue = (float)value;
        else _slider.UpperValue = (float)value;

        return true;
    }
}