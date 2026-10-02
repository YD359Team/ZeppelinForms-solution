using System.Globalization;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A closed box with a drop-down: a combo box, a date, a time, a color.
/// Its value is what it shows; expanding opens the drop-down.</summary>
/// <remarks>
/// The drop-down itself is a flyout over the form, so while open it appears in the
/// tree as the form's overlay — the list or the calendar inside it with its own
/// peers. Expanding and collapsing go through the control's own click, which
/// toggles the drop-down: the peer only checks the state first.
/// </remarks>
public class ComboBoxPeer : UIElementPeer
{
    private readonly Func<bool> _isOpen;
    private readonly Func<string?> _value;

    public ComboBoxPeer(ComboBox owner) : this(owner,
        () => owner.IsDropDownOpen,
        () => owner.SelectedItem is { } item
            ? owner.DisplaySelector?.Invoke(item) ?? item.ToString()
            : null)
    {
        owner.SelectionChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
    }

    public ComboBoxPeer(CheckedComboBox owner) : this(owner,
        () => owner.IsDropDownOpen,
        () => string.Join(", ", owner.CheckedItems.Select(item =>
            owner.DisplaySelector?.Invoke(item) ?? item.ToString())))
    {
        owner.SelectionChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
    }

    public ComboBoxPeer(DateTimePicker owner) : this(owner,
        () => owner.IsDropDownOpen,
        () => owner.FormattedValue)
    {
        owner.ValueChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
    }

    public ComboBoxPeer(TimePicker owner) : this(owner,
        () => owner.IsDropDownOpen,
        () => owner.FormattedValue)
    {
        owner.ValueChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
    }

    /// <summary>A color is read as its hex code — the same the picker shows.</summary>
    public ComboBoxPeer(ColorPicker owner) : this(owner,
        () => owner.IsDropDownOpen,
        () => Hex(owner.Value))
    {
        owner.ValueChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
    }

    private ComboBoxPeer(UIElement owner, Func<bool> isOpen, Func<string?> value) : base(owner)
    {
        _isOpen = isOpen;
        _value = value;
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.ComboBox;

    public override string? Value => _value() ?? string.Empty;

    protected override AccessibilityStates ControlStates =>
        _isOpen() ? AccessibilityStates.Expanded : AccessibilityStates.Collapsed;

    protected override AccessibilityActions ControlActions =>
        _isOpen() ? AccessibilityActions.Collapse : AccessibilityActions.Expand;

    public override bool Expand() => !_isOpen() && Owner.PerformAccessibilityActivation();

    public override bool Collapse() => _isOpen() && Owner.PerformAccessibilityActivation();

    private static string Hex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}