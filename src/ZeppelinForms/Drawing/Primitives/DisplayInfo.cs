namespace ZeppelinForms.Drawing.Primitives;

public sealed record DisplayInfo
{
    /// <summary>Полная область экрана в физических пикселях.</summary>
    public required Rectangle Bounds { get; init; }

    /// <summary>Область без панели задач и системных панелей.</summary>
    public required Rectangle WorkingArea { get; init; }

    public required float Scale { get; init; }

    public required bool IsPrimary { get; init; }

    public string? Name { get; init; }

    /// <summary>Логический размер рабочей области — в этих единицах живут контролы.</summary>
    public Size LogicalWorkingSize =>
        new(WorkingArea.Width / Scale, WorkingArea.Height / Scale);
}
