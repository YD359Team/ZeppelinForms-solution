namespace ZeppelinForms.Drawing.Primitives;

/// <summary>Тень под элементом, по смыслу как CSS box-shadow.</summary>
public sealed record BoxShadow(
    float OffsetX,
    float OffsetY,
    float Blur,
    float Spread,
    Color Color)
{
    public static BoxShadow Small => new(0, 1, 3, 0, new Color(60, 0, 0, 0));
    public static BoxShadow Medium => new(0, 4, 8, 0, new Color(50, 0, 0, 0));
    public static BoxShadow Large => new(0, 10, 20, 0, new Color(45, 0, 0, 0));
}


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

public interface IDisplayProvider
{
    IReadOnlyList<DisplayInfo> GetDisplays();
}