using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class ToggleButton : Button
{
    public static readonly StyledProperty<Color> CheckedTextColorProperty =
        StyledProperty<Color>.Register<ToggleButton>(
            nameof(CheckedTextColor),
            button => button._checkedTextColor,
            (button, value) => button._checkedTextColor = value,
            Colors.White,
            category: "Состояния");

    private Color _checkedTextColor = Colors.White;

    /// <summary>Цвет текста во включённом состоянии.</summary>
    public Color CheckedTextColor
    {
        get => _checkedTextColor;
        set => SetValue(CheckedTextColorProperty, ref _checkedTextColor, value);
    }

    protected override Color CurrentBackground
    {
        get
        {
            if (!IsEnabled && DisabledBackgroundColor.A > 0) return DisabledBackgroundColor;
            if (IsPressed && PressedBackgroundColor.A > 0) return PressedBackgroundColor;

            if (IsCheckedState)
                return IsHovered && CheckedHoverBackgroundColor.A > 0
                    ? CheckedHoverBackgroundColor
                    : CheckedBackgroundColor;

            if (IsHovered && HoverBackgroundColor.A > 0) return HoverBackgroundColor;

            return BackgroundColor;
        }
    }

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
            CheckedChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public string? GroupName { get; set; }

    public event EventHandler? CheckedChanged;

    // база сама подставит CheckedBackgroundColor — подмена цвета
    // во время отрисовки больше не нужна
    protected override bool IsCheckedState => _isChecked;

    protected override void OnActivated()
    {
        if (GroupName is not null)
        {
            if (_isChecked) return;   // в группе повторное нажатие не выключает

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