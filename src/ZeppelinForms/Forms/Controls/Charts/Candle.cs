namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>Одна свеча: цены открытия, максимума, минимума и закрытия
/// за период.</summary>
/// <remarks>
/// Подпись хранится рядом со значениями, а не отдельным списком категорий:
/// свечи приходят и уходят целиком, и разъехаться с подписями они
/// при таком хранении не могут.
/// </remarks>
public sealed class Candle
{
    public string? Label { get; set; }

    public float Open { get; set; }
    public float High { get; set; }
    public float Low { get; set; }
    public float Close { get; set; }

    /// <summary>Закрытие выше открытия — свеча растущая.</summary>
    public bool IsBullish => Close >= Open;
}