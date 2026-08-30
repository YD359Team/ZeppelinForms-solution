using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class ToggleButton : Button
{
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

    /// <summary>Группа взаимного исключения. Если задана, нажатие снимает
    /// отметку с соседей по родителю с тем же именем группы.</summary>
    public string? GroupName { get; set; }

    public event EventHandler? CheckedChanged;

    public Color CheckedBackgroundColor { get; set; } = LightThemeColors.ButtonFill.Darken(0.25f);

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (GroupName is not null)
        {
            // в группе повторное нажатие не выключает — как у радиокнопок
            if (_isChecked) { e.Handled = true; return; }

            UncheckGroupSiblings();
            IsChecked = true;
        }
        else
        {
            IsChecked = !IsChecked;
        }

        base.OnClick(e);
    }

    private void UncheckGroupSiblings()
    {
        if (Parent is not PanelControl panel) return;

        foreach (var sibling in panel.Children.OfType<ToggleButton>())
            if (!ReferenceEquals(sibling, this) && sibling.GroupName == GroupName)
                sibling.IsChecked = false;
    }

    public override void Draw(Graphics g)
    {
        if (!_isChecked)
        {
            base.Draw(g);
            return;
        }

        // нажатое состояние: тот же макет, но фон акцентный
        Color original = BackgroundColor;
        BackgroundColor = CheckedBackgroundColor;

        try { base.Draw(g); }
        finally { BackgroundColor = original; }
    }
}