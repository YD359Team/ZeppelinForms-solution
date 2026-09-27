namespace ZeppelinForms.Drawing.Primitives;

public sealed record DisplayInfo
{
    /// <summary>The full screen area in physical pixels.</summary>
    public required Rectangle Bounds { get; init; }

    /// <summary>The area without the taskbar and system panels.</summary>
    public required Rectangle WorkingArea { get; init; }

    public required float Scale { get; init; }

    /// <summary>The real pixel density, dots per inch.</summary>
    /// <remarks>
    /// Separate from Scale and mandatory to fill. Scale is a decision about the
    /// interface size: X11Dpi rounds it to a quarter and clamps it to [1, 4],
    /// Android counts it from a base of 160 rather than 96. The density can't be
    /// recovered from such a number, and gesture thresholds need exactly the
    /// density: a pan break threshold set in pixels gets tuned for the mouse
    /// and turns out not to work on a phone.
    /// </remarks>
    public required float Dpi { get; init; }

    public required bool IsPrimary { get; init; }

    public string? Name { get; init; }

    /// <summary>The logical size of the working area — controls live in these units.</summary>
    public Size LogicalWorkingSize =>
        new(WorkingArea.Width / Scale, WorkingArea.Height / Scale);

    /// <summary>How many logical units are in a millimeter. All thresholds given
    /// in physical units are converted through this.</summary>
    public float LogicalUnitsPerMillimeter => Dpi / 25.4f / Scale;

    /// <summary>Millimeters into this screen's logical units.</summary>
    public float MillimetersToLogical(float millimeters) =>
        millimeters * LogicalUnitsPerMillimeter;
}