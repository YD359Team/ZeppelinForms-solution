namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>One candle: the open, high, low and close prices for a period.</summary>
/// <remarks>
/// The label is stored next to the values rather than in a separate list of
/// categories: candles come and go whole, and stored this way they can't
/// drift apart from their labels.
/// </remarks>
public sealed class Candle
{
    public string? Label { get; set; }

    public float Open { get; set; }
    public float High { get; set; }
    public float Low { get; set; }
    public float Close { get; set; }

    /// <summary>The close is above the open — a rising candle.</summary>
    public bool IsBullish => Close >= Open;
}