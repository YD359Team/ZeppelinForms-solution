using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Theming;

/// <summary>Semantic colors: controls refer to a role, not to a shade.</summary>
public sealed record ThemeColors
{
    public required Color Background { get; init; }
    public required Color Surface { get; init; }
    public required Color SurfaceHover { get; init; }
    public required Color SurfacePressed { get; init; }

    public required Color Border { get; init; }
    public required Color BorderFocused { get; init; }

    public required Color Text { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color TextDisabled { get; init; }
    public required Color TextOnAccent { get; init; }

    public required Color Accent { get; init; }
    public required Color AccentHover { get; init; }
    public required Color AccentPressed { get; init; }

    public required Color Selection { get; init; }
    public required Color TextSelection { get; init; }

    public required Color Success { get; init; }
    public required Color Warning { get; init; }
    public required Color Error { get; init; }

    public required Color ScrollTrack { get; init; }
    public required Color ScrollThumb { get; init; }

    // ===== optional roles =====
    //
    // Fluent draws controls with roles the classic themes don't have: a fill of
    // its own for controls, a two-tone stroke that gives them elevation, and a
    // two-stroke focus ring. They are optional so that every theme written
    // before them keeps compiling: a role that wasn't given falls back to the
    // nearest required one. The fallback is resolved on read, not at
    // construction, so a copy made with `with` that changes the base role
    // carries the unset optional one along with it.

    private readonly Color? _controlFill;
    private readonly Color? _controlStroke;
    private readonly Color? _controlStrokeSecondary;
    private readonly Color? _controlStrongStroke;
    private readonly Color? _focusStrokeOuter;
    private readonly Color? _focusStrokeInner;

    /// <summary>The rest fill of a control: a button, a text box, a check box.
    /// Falls back to <see cref="Surface"/>.</summary>
    public Color ControlFill
    {
        get => _controlFill ?? Surface;
        init => _controlFill = value;
    }

    /// <summary>The control's stroke. Falls back to <see cref="Border"/>.</summary>
    public Color ControlStroke
    {
        get => _controlStroke ?? Border;
        init => _controlStroke = value;
    }

    /// <summary>The bottom edge of the control's stroke — the one that makes
    /// a Fluent control look raised. Falls back to <see cref="ControlStroke"/>:
    /// a stroke of one color throughout is simply a flat control.</summary>
    public Color ControlStrokeSecondary
    {
        get => _controlStrokeSecondary ?? ControlStroke;
        init => _controlStrokeSecondary = value;
    }

    /// <summary>The stroke of a control that is an outline and nothing else: an empty
    /// check box, an empty radio circle, a switch that is off, the lower edge of
    /// a text field. Falls back to <see cref="TextSecondary"/>.</summary>
    /// <remarks>
    /// Added with the Fluent themes, beyond the roles planned first. The ordinary
    /// control stroke is too faint for such a control — there is no fill inside to
    /// see it by — and the secondary text color is close but not the same shade:
    /// a theme should be able to tell the two apart.
    /// </remarks>
    public Color ControlStrongStroke
    {
        get => _controlStrongStroke ?? TextSecondary;
        init => _controlStrongStroke = value;
    }

    /// <summary>The outer stroke of the keyboard focus ring. Falls back to
    /// <see cref="Text"/>: as in Fluent, the ring takes the content color,
    /// so it reads on any fill the control may have.</summary>
    public Color FocusStrokeOuter
    {
        get => _focusStrokeOuter ?? Text;
        init => _focusStrokeOuter = value;
    }

    /// <summary>The inner stroke of the keyboard focus ring — it separates the
    /// outer one from the control's fill. Falls back to <see cref="Background"/>.</summary>
    public Color FocusStrokeInner
    {
        get => _focusStrokeInner ?? Background;
        init => _focusStrokeInner = value;
    }

    /// <summary>Whether the palette is a dark one: the page background takes
    /// white text better than black.</summary>
    /// <remarks>
    /// Decided by contrast rather than by a guess at the name: a theme built
    /// on the fly — from the system accent, from a user's settings — has no
    /// name to tell by, and the colors are the only thing that describes it.
    /// </remarks>
    public bool IsDark =>
        Background.ContrastRatio(Colors.White) > Background.ContrastRatio(Colors.Black);

    /// <summary>The same palette with another accent. The shades of the accent,
    /// the selection and the focus border that followed the old accent are
    /// derived from the new one; everything else is kept as is.</summary>
    /// <remarks>
    /// <para>
    /// Needed wherever the accent is not known in advance: the system accent
    /// color, a brand color, a user's choice. Without it the whole palette had to
    /// be restated for a single color, together with shades nobody wants to
    /// compute by hand.
    /// </para>
    /// <para>
    /// The shades follow the direction of the palette: on a light one hover and
    /// press darken the accent, on a dark one hover lightens it — the way the
    /// built-in Light and Dark themes do. The text on the accent is kept while it
    /// stays readable (3:1, the WCAG threshold for controls and large text) and is
    /// switched to black or white, whichever reads better, only when it doesn't:
    /// white text on a yellow accent is lost, while on a medium blue it is the
    /// expected look, even though black would contrast slightly more there.
    /// </para>
    /// </remarks>
    public ThemeColors WithAccent(Color accent)
    {
        bool dark = IsDark;
        Color previous = Accent;

        Color hover = dark ? accent.Lighten(0.1f) : accent.Darken(0.15f);
        Color pressed = dark ? accent.Darken(0.12f) : accent.Darken(0.25f);

        Color textOnAccent = TextOnAccent;

        if (accent.ContrastRatio(textOnAccent) < 3f)
        {
            textOnAccent = accent.ContrastRatio(Colors.White) >= accent.ContrastRatio(Colors.Black)
                ? Colors.White
                : Colors.Black;
        }

        return this with
        {
            Accent = accent,
            AccentHover = hover,
            AccentPressed = pressed,
            TextOnAccent = textOnAccent,

            // the focus border is replaced only where it was the accent itself:
            // a theme that gave it a color of its own chose it deliberately
            BorderFocused = BorderFocused == previous ? accent : BorderFocused,

            // the selection is a tint of the accent over the surface: left as it
            // was, a blue selection under a green accent reads as a leftover
            Selection = Color.Lerp(Surface, accent, dark ? 0.4f : 0.33f),
        };
    }
}