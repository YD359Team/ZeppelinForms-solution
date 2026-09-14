using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Input.Pointer;

public sealed record class PointerEventArgs(
    int PointerId,
    PointerKind Kind,
    Point Location,
    MouseButton Button = MouseButton.Left,
    float Pressure = 1f,
    KeyModifiers Modifiers = KeyModifiers.None) : ZfEventArgs
{
    /// <summary>Момент события по <see cref="Environment.TickCount64"/>.
    /// Проставляет платформа, а не получатель: между приходом события
    /// из системы и его разбором проходит время, и скорость, посчитанная
    /// по времени разбора, будет тем неправдивее, чем сильнее подтормаживает
    /// кадр — а скорость нужна swipe и fling.</summary>
    public long Timestamp { get; init; } = Environment.TickCount64;

    /// <summary>Ведущий контакт — тот, от которого поднимаются совместимые
    /// события мыши. Первый коснувшийся; когда он отпускается, ведущий
    /// не переназначается, иначе контрол получил бы нажатие от одного
    /// пальца и отпускание от другого.</summary>
    public bool IsPrimary { get; init; } = true;

    /// <summary>Размер пятна контакта в логических единицах. У мыши нулевой.
    /// Нужен там, где попадание пальцем шире точки: увеличенная зона
    /// нажатия мелких элементов.</summary>
    public Size ContactSize { get; init; }

    public bool Handled { get; set; }
}