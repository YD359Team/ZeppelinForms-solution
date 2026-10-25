using System.ComponentModel;
using System.Windows.Input;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Commands;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class ToggleButton : Button
{
    /// <summary>The caption as shown: the text, or — never set — the name given to the
    /// control, see <see cref="ZeppelinForms.Forms.Controls.Base.UIElement.NameCaption"/>.</summary>
    internal override string? ShownText => Text ?? NameCaption;

    protected override void OnNameCaptionChanged()
    {
        if (Text is null) Invalidate();
    }

    /// <summary>Text color in the checked state.</summary>
    [Styled(Category = "States")]
    public partial Color CheckedTextColor { get; set; }

    private static Color CheckedTextColorDefault => Colors.White;

    // The backdrop color is chosen by ButtonBase.CurrentBackground through
    // IsCheckedState. There used to be an override here that checked the press
    // before the checked state: CheckedPressedBackgroundColor from the theme was
    // never shown, and pressing a checked toggle briefly flashed the color of
    // a pressed unchecked one

    // unchecked, the press dims the text as on any button
    protected override Color CurrentTextColor =>
    !IsEnabled ? DisabledTextColor
    : _isChecked ? CheckedTextColor
    : UsesPressedTextColor ? PressedTextColor
    : TextColor;

    private bool _isChecked;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;

            _isChecked = value;

            // the shared state follows the button, and the menu with it
            if (Command is ToggleCommand toggle)
                toggle.IsChecked = value;

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
        // a toggle command keeps the state: it flips as the command runs, right after
        // this, and the button follows it — flipping here as well would undo it
        if (Command is ToggleCommand) return;

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

    // ===== toggle command =====

    private protected override void OnCommandChanged()
    {
        if (Command is ToggleCommand toggle)
            IsChecked = toggle.IsChecked;
    }

    private protected override void OnCommandHooked(ICommand command)
    {
        if (command is not ToggleCommand toggle) return;

        toggle.PropertyChanged += OnToggleCommandChanged;

        // switched while the button was out of every form
        IsChecked = toggle.IsChecked;
    }

    private protected override void OnCommandUnhooked(ICommand command)
    {
        if (command is ToggleCommand toggle)
            toggle.PropertyChanged -= OnToggleCommandChanged;
    }

    private void OnToggleCommandChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ToggleCommand toggle && e.PropertyName == nameof(ToggleCommand.IsChecked))
            IsChecked = toggle.IsChecked;
    }

    private void UncheckGroupSiblings()
    {
        if (Parent is not PanelControl panel) return;

        foreach (ToggleButton sibling in panel.Children.OfType<ToggleButton>())
            if (!ReferenceEquals(sibling, this) && sibling.GroupName == GroupName)
                sibling.IsChecked = false;
    }
}