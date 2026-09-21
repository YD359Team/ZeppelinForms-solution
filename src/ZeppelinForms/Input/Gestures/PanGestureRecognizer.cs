using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

public enum PanDirection
{
    Both,
    Horizontal,
    Vertical,
}

public sealed record class PanGestureEventArgs(
    Point Location, Point Delta, Point TotalOffset, Point DownLocation)
{
    /// <summary>Скорость указателя в пикселях в секунду. На отпускании —
    /// скорость броска по последним ста миллисекундам.</summary>
    public Point Velocity { get; init; }
}

/// <summary>Перетаскивание после преодоления порога срыва.</summary>
public sealed class PanGestureRecognizer : GestureRecognizer
{
    private readonly VelocityTracker _velocity = new();
    private Point _last;

    /// <summary>Какое направление считать своим. Направленный pan отказывается
    /// от контакта, если движение идёт преимущественно поперёк — иначе
    /// вертикальный список отбирал бы горизонтальные жесты у соседа.</summary>
    public PanDirection Direction { get; set; } = PanDirection.Both;

    /// <summary>Спрашивается в самом начале контакта: брать ли его вообще.
    /// Прокручивающая панель отказывается, если прокручивать нечего, —
    /// иначе она отбирала бы жест у внешней панели, которой есть куда ехать.</summary>
    public Func<PointerContact, bool>? CanBegin { get; set; }

    public event EventHandler<PanGestureEventArgs>? Started;
    public event EventHandler<PanGestureEventArgs>? Updated;
    public event EventHandler<PanGestureEventArgs>? Completed;
    public event EventHandler? Cancelled;

    protected override void OnBegin()
    {
        _last = Contact?.DownLocation ?? Point.Empty;
        _velocity.Reset();

        if (Contact is { } contact)
        {
            if (CanBegin is { } canBegin && !canBegin(contact))
            {
                Reject();
                return;
            }

            _velocity.Add(contact.DownLocation, contact.DownTimestamp);
        }
    }

    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (Contact is not PointerContact contact) return;

        _velocity.Add(contact.Location, e.Timestamp);

        if (State == GestureState.Accepted)
        {
            Updated?.Invoke(this, MakeArgs(contact, e.Timestamp));
            _last = contact.Location;
            return;
        }

        float dx = contact.Location.X - contact.DownLocation.X;
        float dy = contact.Location.Y - contact.DownLocation.Y;

        float travel = Direction switch
        {
            PanDirection.Horizontal => MathF.Abs(dx),
            PanDirection.Vertical => MathF.Abs(dy),
            _ => MathF.Abs(dx) + MathF.Abs(dy),
        };

        if (travel < PointerThresholds.DragSlop(contact.Kind, Display)) return;

        // поперёк ушли дальше, чем вдоль — жест не наш, и держать контакт
        // дальше значит мешать тому, кто ждёт перпендикулярного
        if (Direction == PanDirection.Horizontal && MathF.Abs(dy) > MathF.Abs(dx))
        {
            Reject();
            return;
        }

        if (Direction == PanDirection.Vertical && MathF.Abs(dx) > MathF.Abs(dy))
        {
            Reject();
            return;
        }

        Accept();

        // с этого момента жест наш, и отпускание за пределами окна
        // обязано до нас дойти
        CaptureContact();

        // старт отсчитываем от точки нажатия, а не от текущей: порог —
        // это задержка распознавания, а не потерянное расстояние
        _last = contact.DownLocation;
        Started?.Invoke(this, MakeArgs(contact, e.Timestamp));
        _last = contact.Location;
    }

    protected override void OnPointerUp(PointerEventArgs e)
    {
        if (State != GestureState.Accepted || Contact is not PointerContact contact) return;

        _velocity.Add(contact.Location, e.Timestamp);

        Completed?.Invoke(this, MakeArgs(contact, e.Timestamp));
    }

    protected override void OnCancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    private PanGestureEventArgs MakeArgs(PointerContact contact, long timestamp) => new(
        contact.Location,
        new Point(contact.Location.X - _last.X, contact.Location.Y - _last.Y),
        new Point(
            contact.Location.X - contact.DownLocation.X,
            contact.Location.Y - contact.DownLocation.Y),
        contact.DownLocation)
    {
        Velocity = _velocity.GetVelocity(timestamp),
    };
}