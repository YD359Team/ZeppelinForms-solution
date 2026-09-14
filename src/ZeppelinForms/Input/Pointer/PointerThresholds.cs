using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Pointer;

/// <summary>Пороги распознавания в миллиметрах и перевод их в логические
/// единицы конкретного экрана.</summary>
/// <remarks>
/// Числа в миллиметрах, а не в пикселях, сознательно. Палец накрывает
/// пятно около 8 мм и дрожит на 2–3 мм даже при неподвижном нажатии;
/// мышь не дрожит вовсе. Порог, подобранный на десктопе в пикселях,
/// на телефоне окажется либо неотличим от нуля, либо непреодолим.
/// </remarks>
public static class PointerThresholds
{
    /// <summary>Дрожание, которое ещё считается нажатием в одну точку.
    /// У мыши отдельное значение: у неё дрожания нет, и большой допуск
    /// только съедал бы короткие перетаскивания.</summary>
    public const float TouchTapSlopMm = 2.5f;
    public const float MouseTapSlopMm = 0.6f;

    /// <summary>Порог срыва: до него контакт принадлежит потомку,
    /// после — за него может побороться предок с pan.</summary>
    public const float TouchDragSlopMm = 3.5f;
    public const float MouseDragSlopMm = 1f;

    /// <summary>Минимальный путь, после которого движение считается
    /// проведением, а не промахом мимо нажатия.</summary>
    public const float SwipeMinDistanceMm = 12f;

    /// <summary>Удержание без движения — до срабатывания долгого нажатия.</summary>
    public const int LongPressMs = 500;

    /// <summary>Дольше — это уже не касание, даже если палец не сдвинулся.</summary>
    public const int TapMaxDurationMs = 300;

    public static float TapSlop(PointerKind kind, DisplayInfo display) =>
        display.MillimetersToLogical(kind == PointerKind.Mouse ? MouseTapSlopMm : TouchTapSlopMm);

    public static float DragSlop(PointerKind kind, DisplayInfo display) =>
        display.MillimetersToLogical(kind == PointerKind.Mouse ? MouseDragSlopMm : TouchDragSlopMm);

    public static float SwipeMinDistance(DisplayInfo display) =>
        display.MillimetersToLogical(SwipeMinDistanceMm);
}