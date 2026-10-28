using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// A short message at the bottom of the form, with an action the user may take on it —
/// "Message deleted · Undo" —: shown by <see cref="Form.ShowSnackbarAsync"/>, one at a time,
/// for a few seconds. Above the navigation bar of a <see cref="Scaffold"/>, and above the
/// gesture bar of a form that draws under it.
/// </summary>
/// <remarks>The control is public to be styled: <c>Snackbar { Background: … }</c> in a sheet,
/// <c>For&lt;Snackbar&gt;</c> in a theme. The theme draws it inverted, dark on a light page.</remarks>
public partial class Snackbar : DecoratedPanel
{
    private readonly Label _message;
    private readonly Button? _action;

    internal Snackbar(string message, string? action)
    {
        _message = new Label
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalContentAlign = VerticalContentAlignment.Center,
            IsHitTestVisible = false,

            // read out as it appears, after whatever the reader is saying
            LiveSetting = AccessibilityLiveSetting.Polite,
        };

        Children.Add(_message);

        if (action is not null)
        {
            _action = new Button { Text = action };
            _action.Click += (_, _) => ActionInvoked?.Invoke(this, EventArgs.Empty);
            Children.Add(_action);
        }

        SetControlDefault(PaddingProperty, new Thickness(16f, 6f, 8f, 6f));
        SetControlDefault(CornerRadiusProperty, new CornerRadius(4f));
        SetControlDefault(BackgroundProperty, new Color(255, 0x32, 0x32, 0x32));

        ApplyColors();
    }

    /// <summary>The width the form gives it; overlays are measured without a limit.</summary>
    internal float PlacedWidth { get; set; } = 344f;

    internal event EventHandler? ActionInvoked;

    internal string Message => _message.Text ?? string.Empty;

    /// <summary>The message's color; the theme gives the page's background, inverted.</summary>
    [Styled(Category = "Appearance")]
    public partial Color MessageColor { get; set; }
    private static Color MessageColorDefault => Colors.White;

    /// <summary>The action's color.</summary>
    [Styled(Category = "Appearance")]
    public partial Color ActionColor { get; set; }
    private static Color ActionColorDefault => new(255, 0x8A, 0xB4, 0xF8);

    protected override void OnStyledPropertyChanged(StyledProperty property)
    {
        base.OnStyledPropertyChanged(property);

        if (property == MessageColorProperty || property == ActionColorProperty) ApplyColors();
    }

    /// <summary>The message and the action in the snackbar's colors: the theme's button
    /// colors would be those of the page, not of the inverted bar.</summary>
    private void ApplyColors()
    {
        _message.TextColor = MessageColor;

        if (_action is null) return;

        _action.TextColor = ActionColor;
        _action.BackgroundColor = Colors.Transparent;
        _action.HoverBackgroundColor = new Color(30, ActionColor.R, ActionColor.G, ActionColor.B);
        _action.PressedBackgroundColor = new Color(50, ActionColor.R, ActionColor.G, ActionColor.B);
        _action.BorderWidth = 0f;
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : PlacedWidth;
        float inner = Math.Max(0, width - Padding.Horizontal);

        Size action = Size.Empty;

        if (_action is not null)
        {
            _action.Measure(new Size(inner / 2f, float.PositiveInfinity));
            action = _action.DesiredSize;
        }

        _message.Measure(new Size(Math.Max(0, inner - action.Width - 8f), float.PositiveInfinity));

        float height = Math.Max(Math.Max(_message.DesiredSize.Height, action.Height), 36f);

        return ResolveSize(new Size(width, height + Padding.Vertical), availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        float left = Padding.Left;
        float top = Padding.Top;
        float width = Math.Max(0, contentSize.Width - Padding.Horizontal);
        float height = Math.Max(0, contentSize.Height - Padding.Vertical);

        float actionWidth = 0f;

        if (_action is not null)
        {
            Size size = _action.DesiredSize;
            actionWidth = size.Width;

            float x = IsRightToLeft ? left : left + width - size.Width;
            _action.Arrange(new Rectangle(new Point(x, top + (height - size.Height) / 2f), size));
        }

        float messageLeft = IsRightToLeft && actionWidth > 0 ? left + actionWidth + 8f : left;
        float messageWidth = Math.Max(0, width - (actionWidth > 0 ? actionWidth + 8f : 0f));

        _message.Arrange(new Rectangle(new Point(messageLeft, top), new Size(messageWidth, height)));
    }
}
