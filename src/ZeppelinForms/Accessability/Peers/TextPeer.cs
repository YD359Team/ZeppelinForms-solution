using System.Globalization;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>Static text: a label, rich text, a hint. Its text is its name.</summary>
public class TextPeer : UIElementPeer
{
    public TextPeer(UIElement owner) : base(owner)
    {
        // a live label announces its new text: that is what LiveSetting is for
        owner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "Text")
                RaisePropertyChanged(AccessibilityProperty.Name);
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Text;

    protected override bool NameFromContent => true;

    /// <summary>A label names its target; it is still read where it stands,
    /// but has no children of its own.</summary>
    public override IReadOnlyList<AccessibilityPeer> Children => [];
}

/// <summary>A text field: the text is its value, not its name.</summary>
public class TextBoxPeer : UIElementPeer
{
    private readonly TextBox _box;

    public TextBoxPeer(TextBox owner) : base(owner)
    {
        _box = owner;
        owner.TextChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
        owner.ValidationChanged += (_, _) =>
        {
            RaisePropertyChanged(AccessibilityProperty.States);
            RaisePropertyChanged(AccessibilityProperty.Description);
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.TextBox;

    /// <summary>A password is never given out: its length is all a screen reader
    /// may know, and it says "protected" for it.</summary>
    public override string? Value =>
        _box.PasswordChar is not null ? null : _box.Text ?? string.Empty;

    /// <summary>The validation message comes first: it is what needs attention.</summary>
    public override string? Description =>
        _box.ValidationState == ValidationState.Error && _box.ValidationMessage is { Length: > 0 } message
            ? message
            : base.Description;

    protected override AccessibilityStates ControlStates =>
        (_box.IsReadOnly ? AccessibilityStates.ReadOnly : 0) |
        (_box.PasswordChar is not null ? AccessibilityStates.Protected : 0) |
        (_box.IsMultiline ? AccessibilityStates.Multiline : 0) |
        (_box.ValidationState == ValidationState.Error ? AccessibilityStates.Invalid : 0);

    protected override AccessibilityActions ControlActions =>
        _box.IsReadOnly ? AccessibilityActions.None : AccessibilityActions.SetValue;

    public override bool SetValue(string value)
    {
        if (_box.IsReadOnly || !Owner.IsEffectivelyEnabled) return false;

        _box.Text = value;
        return true;
    }
}

/// <summary>A field typed through a mask: what is shown, prompts included,
/// is what is read.</summary>
public class MaskedTextBoxPeer : UIElementPeer
{
    private readonly MaskedTextBox _box;

    public MaskedTextBoxPeer(MaskedTextBox owner) : base(owner)
    {
        _box = owner;
        owner.TextChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.TextBox;

    public override string? Value => _box.DisplayText;
}

/// <summary>A number with step buttons: both a value and a range.</summary>
public class SpinButtonPeer : UIElementPeer
{
    private readonly NumericUpDown _numeric;

    public SpinButtonPeer(NumericUpDown owner) : base(owner)
    {
        _numeric = owner;
        owner.ValueChanged += (_, _) =>
        {
            RaisePropertyChanged(AccessibilityProperty.Value);
            RaisePropertyChanged(AccessibilityProperty.Range);
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.SpinButton;

    public override string? Value =>
        _numeric.Value.ToString("F" + _numeric.DecimalPlaces, CultureInfo.CurrentCulture);

    public override RangeInfo? Range => new(
        (double)_numeric.Minimum,
        (double)_numeric.Maximum,
        (double)_numeric.Value,
        (double)_numeric.Step);

    protected override AccessibilityStates ControlStates =>
        _numeric.IsEditable ? AccessibilityStates.None : AccessibilityStates.ReadOnly;

    protected override AccessibilityActions ControlActions =>
        _numeric.IsEditable ? AccessibilityActions.SetValue : AccessibilityActions.None;

    public override bool SetRangeValue(double value)
    {
        if (!_numeric.IsEditable || !Owner.IsEffectivelyEnabled) return false;

        _numeric.Value = Math.Clamp((decimal)value, _numeric.Minimum, _numeric.Maximum);
        return true;
    }

    public override bool SetValue(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal number) &&
        SetRangeValue((double)number);
}