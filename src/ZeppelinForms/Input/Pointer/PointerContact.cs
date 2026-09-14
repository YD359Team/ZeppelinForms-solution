using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Gestures;

namespace ZeppelinForms.Input.Pointer;

/// <summary>Живой контакт: палец на экране, нажатая кнопка мыши, перо.
/// Существует от нажатия до отпускания или отмены.</summary>
public sealed class PointerContact
{
    public required int Id { get; init; }
    public required PointerKind Kind { get; init; }

    /// <summary>Ведущий контакт поднимает совместимые события мыши.</summary>
    public required bool IsPrimary { get; init; }

    public required Point DownLocation { get; init; }
    public required long DownTimestamp { get; init; }

    /// <summary>Удерживаемые кнопки. У касания и пера всегда Left.</summary>
    public PointerButtons Buttons { get; set; }

    public Point Location { get; private set; }
    public long Timestamp { get; private set; }

    /// <summary>Элемент, на который пришлось нажатие.</summary>
    public UIElement? Pressed { get; set; }

    /// <summary>Цепочка от корня к <see cref="Pressed"/>, снятая в момент
    /// нажатия и дальше не пересчитываемая.</summary>
    /// <remarks>
    /// Пересчитывать её по текущей точке нельзя: раскладка за время
    /// удержания могла поехать, а предок, следивший за нажатием через
    /// предпросмотр, обязан получить и отпускание, и отмену — даже если
    /// палец давно уехал за его границы.
    /// </remarks>
    public UIElement[] Chain { get; set; } = [];

    /// <summary>Элемент, забравший себе этот контакт до отпускания.</summary>
    public UIElement? Capture { get; set; }

    /// <summary>Куда идут события контакта: захвативший, иначе нажатый.</summary>
    public UIElement? Target => Capture ?? Pressed;

    /// <summary>Сколько контакт уехал от точки нажатия. Основа порога
    /// срыва в pan: до него взаимодействие принадлежит потомку,
    /// после — претендовать может предок.</summary>
    public float TravelDistance => Point.DistanceBetween(DownLocation, Location);

    public long Duration => Timestamp - DownTimestamp;

    /// <summary>Создаётся только конвейером ввода: снаружи контакт
    /// придумать нельзя, иначе арена и захват разъедутся с реальностью.</summary>
    internal PointerContact() { }

    /// <summary>Совместимые события мыши по этому контакту отменены —
    /// его ведёт выигравший распознаватель. Сам контакт при этом жив.</summary>
    internal bool IsCompatCancelled { get; set; }

    internal GestureArena? Arena { get; set; }

    public void Update(Point location, long timestamp)
    {
        Location = location;
        Timestamp = timestamp;
    }
}