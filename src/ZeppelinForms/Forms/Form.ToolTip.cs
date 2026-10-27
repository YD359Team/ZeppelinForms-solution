using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;

namespace ZeppelinForms.Forms;

/// <summary>The tooltip of the hovered element: shown after <see cref="ToolTipDelay"/>,
/// near the pointer and inside the client area.</summary>
public partial class Form
{
    public int ToolTipDelay { get; set; } = 700;
    private IDisposable? _toolTipWake;
    private UIElement? _toolTipOwner;
    private UIElement? _activeToolTip;
    private Point _lastPointerPosition;

    private void ScheduleToolTip(UIElement? target)
    {
        HideToolTip();

        _toolTipOwner = target is not null && !string.IsNullOrEmpty(target.GetToolTip(_lastPointerPosition)) ? target : null;

        if (_toolTipOwner is not null)
            _toolTipWake = Schedule(ToolTipDelay, ShowToolTipCore);
    }

    private void ShowToolTipCore()
    {
        if (_toolTipOwner?.GetToolTip(_lastPointerPosition) is not { Length: > 0 } text)
            return;

        var tip = new Border
        {
            BorderColor = Colors.Black,
            BorderWidth = 1,
            Background = new Color(240, 250, 250, 210),
            Padding = new Thickness(6, 3),
            IsHitTestVisible = false,
            Child = new Label
            {
                Text = text,
                TextColor = Colors.Black,
                IsHitTestVisible = false,
            },
        };

        AttachOverlay(tip);

        tip.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        // slightly below and to the right of the cursor, as system tooltips do
        float x = _lastPointerPosition.X + 12;
        float y = _lastPointerPosition.Y + 20;

        // don't let it go beyond the client area
        if (x + tip.DesiredSize.Width > ClientSize.Width)
            x = Math.Max(0, ClientSize.Width - tip.DesiredSize.Width);

        if (y + tip.DesiredSize.Height > ClientSize.Height)
            y = Math.Max(0, _lastPointerPosition.Y - tip.DesiredSize.Height - 4);

        tip.Position = new Point(x, y);

        _activeToolTip = tip;

        Invalidate();
    }

    /// <summary>The hovered element's tooltip changed with the part under the pointer.</summary>
    internal void RefreshToolTip(UIElement element)
    {
        if (!ReferenceEquals(element, _hoveredElement)) return;

        bool shown = _activeToolTip is not null;

        HideToolTip();

        _toolTipOwner = !string.IsNullOrEmpty(element.GetToolTip(_lastPointerPosition)) ? element : null;

        if (_toolTipOwner is null) return;

        // moving between the parts of one control, the tip follows without
        // the delay again — it was asked for already
        if (shown) ShowToolTipCore();
        else _toolTipWake = Schedule(ToolTipDelay, ShowToolTipCore);
    }

    private void HideToolTip()
    {
        _toolTipWake?.Dispose();
        _toolTipWake = null;

        if (_activeToolTip is not null)
        {
            DetachOverlay(_activeToolTip);
            _activeToolTip = null;
            Invalidate();
        }
    }
}
