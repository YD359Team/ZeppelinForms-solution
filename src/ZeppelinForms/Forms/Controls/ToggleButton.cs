using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class ToggleButton : Button
{
    /// <summary>Text color in the checked state.</summary>
    [Styled(Category = "States")]
    public partial Color CheckedTextColor { get; set; }

    private static Color CheckedTextColorDefault => Colors.White;

    // The backdrop color is chosen by ButtonBase.CurrentBackground through
    // IsCheckedState. There used to be an override here that checked the press
    // before the checked state: CheckedPressedBackgroundColor from the theme was
    // never shown, and pressing a checked toggle briefly flashed the color of
    // a pressed unchecked one

    protected override Color CurrentTextColor =>
    !IsEnabled ? DisabledTextColor
    : _isChecked ? CheckedTextColor
    : TextColor;

    private bool _isChecked;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;

            _isChecked = value;

            SetPseudoClass(PseudoClass.Checked, value);

            CheckedChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public string? GroupName { get; set; }

    public event EventHandler? CheckedChanged;

    // the base itself substitutes CheckedBackgroundColor —
    // swapping the color during drawing is no longer needed
    protected override bool IsCheckedState => _isChecked;

    protected override void OnActivated()
    {
        if (GroupName is not null)
        {
            if (_isChecked) return;   // in a group a repeated press doesn't uncheck

            UncheckGroupSiblings();
            IsChecked = true;
        }
        else
        {
            IsChecked = !IsChecked;
        }
    }

    private void UncheckGroupSiblings()
    {
        if (Parent is not PanelControl panel) return;

        foreach (ToggleButton sibling in panel.Children.OfType<ToggleButton>())
            if (!ReferenceEquals(sibling, this) && sibling.GroupName == GroupName)
                sibling.IsChecked = false;
    }
}