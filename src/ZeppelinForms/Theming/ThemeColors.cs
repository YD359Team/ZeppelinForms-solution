using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Theming;

/// <summary>Семантические цвета: контролы обращаются к роли, а не к оттенку.</summary>
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
}
