namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// Пометка для генератора: развернуть частичное свойство в стилизуемое —
/// зарегистрировать <see cref="StyledProperty{T}"/>, создать поле
/// и написать аксессоры с учётом источника значения.
/// </summary>
/// <remarks>
/// Свойство должно быть <c>partial</c> с геттером и сеттером, а его тип —
/// <c>partial</c> наследником <c>UIElement</c>.
/// Умолчание задаётся статическим свойством с именем <c>&lt;Имя&gt;Default</c>;
/// если его нет, берётся <c>default</c> для типа значения.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class StyledAttribute : Attribute
{
    /// <summary>Раздел в PropertyGrid.</summary>
    public string Category { get; set; } = "Прочее";

    /// <summary>Изменение значения требует пересчёта раскладки,
    /// а не только перерисовки.</summary>
    public bool AffectsLayout { get; set; }

    /// <summary>Значение наследуется вниз по дереву, как шрифт.</summary>
    public bool Inherits { get; set; }
}