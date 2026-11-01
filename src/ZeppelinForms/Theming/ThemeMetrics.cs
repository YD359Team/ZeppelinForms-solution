using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Theming;

/// <summary>Shape tokens of a theme: roundings and stroke thicknesses.
/// Colors describe what a control is painted with, these — what it is shaped like.</summary>
/// <remarks>
/// <para>
/// Before them every control carried its geometry as its own constant: the button
/// its 4 px rounding, the check box its 3 px, the popups their own. A theme could
/// repaint all of them but not reshape a single one, and Fluent differs from the
/// classic look by shape as much as by color: small controls stay at 4 px, while
/// menus, flyouts and dialogs are rounded to 8.
/// </para>
/// <para>
/// A token is data, not a rule: controls don't read it by themselves. A theme
/// hands it out in its rules through <see cref="Theme.For{T}(Action{T, Theme})"/>,
/// the same way it hands out colors. The defaults describe the classic look.
/// </para>
/// </remarks>
public sealed record ThemeMetrics
{
    /// <summary>The classic look: the shapes the controls draw on their own.</summary>
    public static ThemeMetrics Default { get; } = new();

    /// <summary>Fluent 2 (WinUI 3): ControlCornerRadius 4, OverlayCornerRadius 8,
    /// and the focus visual of a 2 px outer stroke over a 1 px inner one.</summary>
    public static ThemeMetrics Fluent { get; } = new()
    {
        ControlCornerRadius = new(4f),
        OverlayCornerRadius = new(8f),
        StrokeThickness = 1f,
        FocusStrokeThickness = 2f,
        FocusStrokeInnerThickness = 1f,
    };

    /// <summary>Material 3: the extra-small shape for fields and small controls — 4 dp —
    /// and the medium one, 12 dp, for what floats; a focus indicator of 3 dp in one stroke.
    /// Buttons are pills, whatever the token: the theme's rule shapes them.</summary>
    public static ThemeMetrics Material { get; } = new()
    {
        ControlCornerRadius = new(4f),
        OverlayCornerRadius = new(12f),
        StrokeThickness = 1f,
        FocusStrokeThickness = 3f,
        FocusStrokeInnerThickness = 0f,
    };

    /// <summary>iOS: controls rounded 10, menus and alerts 13; the focus of a keyboard as
    /// iPadOS draws it, a ring of the accent, 3 thick.</summary>
    public static ThemeMetrics Cupertino { get; } = new()
    {
        ControlCornerRadius = new(10f),
        OverlayCornerRadius = new(13f),
        StrokeThickness = 1f,
        FocusStrokeThickness = 3f,
        FocusStrokeInnerThickness = 0f,
    };

    /// <summary>The rounding of small controls: buttons, text boxes,
    /// check boxes, combo boxes.</summary>
    public CornerRadius ControlCornerRadius { get; init; } = new(4f);

    /// <summary>The rounding of what floats above the content: menus,
    /// flyouts, tooltips, dropdowns, dialogs.</summary>
    public CornerRadius OverlayCornerRadius { get; init; } = new(4f);

    /// <summary>The thickness of a control's stroke.</summary>
    public float StrokeThickness { get; init; } = 1f;

    /// <summary>The thickness of the outer stroke of the keyboard focus ring.</summary>
    public float FocusStrokeThickness { get; init; } = 1f;

    /// <summary>The thickness of the inner stroke of the focus ring,
    /// the one between the outer stroke and the control's fill.
    /// Zero — no inner stroke, a ring of one line as in the classic look.</summary>
    public float FocusStrokeInnerThickness { get; init; }
}