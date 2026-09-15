using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls;

/// <summary>Насколько просторно тому, кто спрашивает.</summary>
/// <remarks>
/// Классифицируется участок раскладки, а не экран: панель шириной
/// 300 единиц внутри широкого окна стеснена ровно так же, как всё окно
/// на телефоне, и вести себя должна так же.
/// </remarks>
public enum SizeClass
{
    /// <summary>Телефон, узкая колонка, выдвижная панель.</summary>
    Compact,

    /// <summary>Планшет, половина окна.</summary>
    Medium,

    /// <summary>Настольное окно во весь экран.</summary>
    Expanded,
}

/// <summary>Границы классов размера. Одни на приложение — в этом весь смысл:
/// пороги, разбросанные по экранам, свести потом дороже, чем задать сразу.</summary>
public static class Breakpoints
{
    /// <summary>Ниже этой ширины — Compact. В логических единицах.</summary>
    public static float Medium { get; set; } = 600f;

    /// <summary>От этой ширины — Expanded.</summary>
    public static float Expanded { get; set; } = 1000f;

    public static SizeClass Classify(float width) =>
        width < Medium ? SizeClass.Compact
        : width < Expanded ? SizeClass.Medium
        : SizeClass.Expanded;

    /// <summary>Классификация по ширине. Высота сознательно не участвует:
    /// по ней различают разве что альбомную ориентацию, а это отдельный
    /// вопрос, и мешать его с теснотой значит получить шесть состояний
    /// вместо трёх.</summary>
    public static SizeClass Classify(Size size) => Classify(size.Width);
}