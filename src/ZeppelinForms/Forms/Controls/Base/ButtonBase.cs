using System.Windows.Input;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// The common basis of pressable controls: states, a color for each state
/// and the press ripple. The content is drawn by derived classes.
/// </summary>
public abstract partial class ButtonBase : InteractiveControl
{
    private readonly RippleAnimation _ripple;

    /// <summary>Show a ripple spreading from the press point.</summary>
    [Styled(Category = "Button")]
    public partial bool RippleEnabled { get; set; }

    private static bool RippleEnabledDefault => true;

    // the ripple has no field of its own on the button — the value is stored
    // by RippleAnimation, hence the manual SetValue overload without ref
    public static readonly StyledProperty<Color> RippleColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(RippleColor),
            button => button._ripple.Color,
            (button, value) => button._ripple.Color = value,
            new Color(60, 255, 255, 255),
            category: "Button");

    public Color RippleColor
    {
        get => _ripple.Color;
        set => SetValue(RippleColorProperty, value);
    }

    [Styled(Category = "States")]
    public partial Color BackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color HoverBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color PressedBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color CheckedBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color DisabledBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color CheckedPressedBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color CheckedHoverBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color DisabledTextColor { get; set; }
    private static Color DisabledTextColorDefault => new(255, 160, 160, 160);

    /// <summary>The text and icon color while the button is held down; transparent —
    /// the text color.</summary>
    /// <remarks>
    /// Fluent answers a press mostly with the text: the fill of a pressed neutral
    /// button is a shade off the hovered one, too little to see, and the text going
    /// to the secondary color is what says the button is held. Like the state
    /// backgrounds, it counts only while it was set at least as firmly as the text
    /// color and the background it would stand on: the theme's grey doesn't replace
    /// a color a style sheet or code chose.
    /// </remarks>
    [Styled(Category = "States")]
    public partial Color PressedTextColor { get; set; }
    private static Color PressedTextColorDefault => Colors.Transparent;

    // FocusRingColor and ShowFocusRing moved to InteractiveControl: check boxes,
    // radio buttons and switches need the ring too. The names stay the same, so
    // code that sets them on a button doesn't change

    /// <summary>How far inside the button's bounds the focus ring runs. Inside, not
    /// outside: a parent clips its children, and a ring past the edge was cut off.</summary>
    [Styled(Category = "Focus")]
    public partial float FocusRingInset { get; set; }
    private static float FocusRingInsetDefault => 2f;

    /// <summary>The lower edge of the border — the one that makes a Fluent button
    /// look raised above the page. Transparent — a flat border of one color.</summary>
    /// <remarks>Not drawn while the button is pressed or disabled: a pressed button
    /// is pushed into the page, a disabled one doesn't respond, and both are flat.</remarks>
    [Styled(Category = "Button")]
    public partial Color ElevationBorderColor { get; set; }
    private static Color ElevationBorderColorDefault => Colors.Transparent;

    /// <summary>The latched state — for ToggleButton and the like.</summary>
    protected virtual bool IsCheckedState => false;

    protected ButtonBase()
    {
        _ripple = new RippleAnimation(this);

        Cursor = CursorKind.Hand;
        SetControlDefault(PaddingProperty, new(14, 6));
        SetControlDefault(CornerRadiusProperty, new CornerRadius(4f));
        SetControlDefault(BorderWidthProperty, 1f);
        // events, not the OnAttached/OnDetached overrides: a derived button that
        // overrides them and forgets the base call would keep the command's
        // subscription — and through it the whole form — alive
        Attached += (_, _) => HookCommand();
        Detached += (_, _) => UnhookCommand();
    }

    /// <summary>The backdrop color for the current state. The order of checks
    /// defines priority: disabled beats pressed, pressed beats hover.</summary>
    protected override Color CurrentBackground
    {
        get
        {
            if (!IsEnabled && DisabledBackgroundColor.A > 0)
                return DisabledBackgroundColor;

            // the latched state has its own shades for pressed and hover,
            // otherwise the button briefly repaints in the color of the unchecked state
            if (IsCheckedState)
            {
                if (IsPressed && CheckedPressedBackgroundColor.A > 0) return CheckedPressedBackgroundColor;
                if (IsHovered && CheckedHoverBackgroundColor.A > 0) return CheckedHoverBackgroundColor;

                return CheckedBackgroundColor;
            }

            if (IsPressed && PressedBackgroundColor.A > 0)
                return StateColor(PressedBackgroundColorProperty, PressedBackgroundColor, tint: 0.16f);

            if (IsHovered && HoverBackgroundColor.A > 0)
                return StateColor(HoverBackgroundColorProperty, HoverBackgroundColor, tint: 0.08f);

            return BackgroundColor;
        }
    }


    /// <summary>The color of a state — hover, press — over the button's background.</summary>
    /// <remarks>
    /// <para>
    /// A state's own color counts only while it was set at least as firmly as the
    /// background it replaces. The theme gives every button the same grey hover;
    /// a style sheet or code that made a button blue with white text didn't ask
    /// for that grey, and white text on it was unreadable. Such a background keeps
    /// its own states instead.
    /// </para>
    /// <para>
    /// A style sheet says its states itself, the CSS way —
    /// <c>Button.accent:hover { BackgroundColor: … }</c> — so its background is
    /// used as it is. A background set from code or a binding has nobody to say
    /// them, and gets them by leaning toward the text color: darker on a light
    /// button, lighter on a dark one, readable either way.
    /// </para>
    /// </remarks>
    private Color StateColor(StyledProperty<Color> state, Color stateColor, float tint)
    {
        ValueSource background = GetValueSource(BackgroundColorProperty);

        if (background < ValueSource.Style || GetValueSource(state) >= background)
            return stateColor;

        return background == ValueSource.Style
            ? BackgroundColor
            : Color.Lerp(BackgroundColor, TextColor, tint);
    }

    /// <summary>On a button, focus is shown by the ring in DrawDecoration.
    /// Swapping the border as well is a double signal: you get two rings
    /// two pixels apart.</summary>
    protected override Color CurrentBorderColor => BorderColor;

    protected virtual Color CurrentTextColor =>
        !IsEnabled ? DisabledTextColor
        : UsesPressedTextColor ? PressedTextColor
        : TextColor;

    /// <summary>Held down, with a pressed text color set as firmly as the colors it
    /// replaces — see <see cref="PressedTextColor"/>.</summary>
    private protected bool UsesPressedTextColor
    {
        get
        {
            if (!IsPressed || PressedTextColor.A == 0) return false;

            ValueSource own = GetValueSource(PressedTextColorProperty);

            return own >= GetValueSource(TextColorProperty) && own >= GetValueSource(BackgroundColorProperty);
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (!RippleEnabled || e.Button != MouseButton.Left) return;

        Point abs = GetAbsolutePosition();
        _ripple.Start(new Point(e.Location.X - abs.X, e.Location.Y - abs.Y));
    }

    /// <summary>
    /// The ripple is drawn first, before the content: it must lie
    /// on the backdrop and under the text.
    /// </summary>
    protected sealed override void DrawContent(Graphics g)
    {
        _ripple.Draw(g, LocalBounds, CornerRadius);

        DrawButtonContent(g);
    }

    /// <summary>The button content on top of the backdrop and the ripple.</summary>
    protected abstract void DrawButtonContent(Graphics g);

    protected override void DrawDecoration(Graphics g)
    {
        DrawElevation(g);

        // the ring sits slightly inside the bounds, otherwise the parent's clip cuts it off
        DrawFocusRing(g, Grow(LocalBounds, -FocusRingInset), CornerRadius);
    }

    /// <summary>The lower edge of the border in its own color. The border is drawn
    /// again over itself, clipped to a band at the bottom: that way the corners get
    /// the second color along their curve rather than a straight line cut across.</summary>
    private void DrawElevation(Graphics g)
    {
        if (ElevationBorderColor.A == 0 || BorderWidth <= 0f) return;
        if (IsPressed || !IsEnabled) return;

        // the same box as the border's, so the edge lies exactly over it
        Rectangle bounds = g.AlignToPixels(LocalBounds);

        // the band covers the lower corners, and at least twice the stroke
        // on a button with square ones — otherwise the edge is lost in antialiasing
        float band = Math.Max(
            Math.Max(CornerRadius.BottomLeft, CornerRadius.BottomRight),
            BorderWidth * 2f);

        g.Save();
        g.ClipRect(new Rectangle(
            new Point(bounds.X, bounds.Bottom - band),
            new Size(bounds.Width, band)));

        BorderPainter.Draw(g, bounds, CornerRadius, ElevationBorderColor, BorderWidth);

        g.Restore();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (!IsEnabled) return;

        OnActivated();
        ExecuteCommand();
        e.Handled = true;
    }

    /// <summary>The control was pressed — click, Space or Enter.</summary>
    protected virtual void OnActivated() { }

    // ===== command =====

    /// <summary>Whether the command's subscription is in place: only while the
    /// button is in a form, so a long-lived command doesn't keep the form alive.</summary>
    private bool _commandHooked;

    /// <summary>Runs when the button is pressed, after <see cref="UIElement.Click"/>.
    /// While it can't run, the button is disabled.</summary>
    /// <remarks>
    /// The command enables and disables the button as a binding would: an
    /// <see cref="UIElement.IsEnabled"/> set from code takes over from it. A
    /// <see cref="Input.Commands.ToggleCommand"/> on a <see cref="ToggleButton"/>
    /// also gives it its checked state.
    /// </remarks>
    public ICommand? Command
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            UnhookCommand();

            ICommand? old = field;
            field = value;

            if (FindOwner() is not null) HookCommand();

            // without a command the button goes back to what it would be without one
            if (value is null && old is not null && !IsLocal(IsEnabledProperty))
                ClearValue(IsEnabledProperty);

            UpdateCanExecute();
            OnCommandChanged();
        }
    }

    public object? CommandParameter
    {
        get;
        set
        {
            if (Equals(field, value)) return;

            field = value;
            UpdateCanExecute();
        }
    }

    /// <summary>Run the command, if it can run. The press comes here.</summary>
    private protected void ExecuteCommand()
    {
        if (Command is not { } command || !command.CanExecute(CommandParameter)) return;

        command.Execute(CommandParameter);
    }

    /// <summary>The command or its subscription changed — for a derived button that
    /// takes more from it than whether it can run.</summary>
    private protected virtual void OnCommandChanged() { }

    /// <summary>The button entered a form, or got a command there.</summary>
    private protected virtual void OnCommandHooked(ICommand command) { }

    /// <summary>The button left its form, or lost its command.</summary>
    private protected virtual void OnCommandUnhooked(ICommand command) { }

    private void HookCommand()
    {
        if (_commandHooked || Command is not { } command) return;

        command.CanExecuteChanged += OnCanExecuteChanged;
        _commandHooked = true;

        OnCommandHooked(command);

        // whatever changed while the button was out of every form
        UpdateCanExecute();
    }

    private void UnhookCommand()
    {
        if (!_commandHooked || Command is not { } command) return;

        command.CanExecuteChanged -= OnCanExecuteChanged;
        _commandHooked = false;

        OnCommandUnhooked(command);
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e) => UpdateCanExecute();

    /// <summary>Enable or disable the button as the command says — unless code set
    /// <see cref="UIElement.IsEnabled"/> itself, which wins as it wins over a binding.</summary>
    private void UpdateCanExecute()
    {
        if (Command is not { } command || IsLocal(IsEnabledProperty)) return;

        bool can = command.CanExecute(CommandParameter);

        if (IsEnabled != can)
            SetBoundValue(IsEnabledProperty, can);
    }
}