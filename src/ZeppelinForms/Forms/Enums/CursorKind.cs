namespace ZeppelinForms.Forms.Enums;

public enum CursorKind
{
    Default,
    Arrow,
    Hand,
    IBeam,
    Wait,
    SizeWestEast,
    SizeNorthSouth,
    SizeAll,
    Cross,
    No,

    /// <summary>The diagonal from the top-left to the bottom-right: a corner grip.</summary>
    SizeNorthWestSouthEast,

    /// <summary>The diagonal from the top-right to the bottom-left.</summary>
    SizeNorthEastSouthWest,
}