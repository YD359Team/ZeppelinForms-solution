using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <param name="Scale">Отношение текущего расстояния между пальцами
/// к расстоянию в момент начала жеста.</param>
/// <param name="Center">Середина между пальцами — вокруг неё и масштабируют.</param>
public sealed record class PinchGestureEventArgs(float Scale, Point Center);

/// <summary>Масштабирование двумя пальцами.</summary>
public sealed class PinchGestureRecognizer : GestureRecognizer
{
    private float _startDistance;
    private float _scale = 1f;

    protected override int MaxContacts => 2;

    public event EventHandler<PinchGestureEventArgs>? Started;
    public event EventHandler<PinchGestureEventArgs>? Updated;
    public event EventHandler<PinchGestureEventArgs>? Completed;
    public event EventHandler? Cancelled;

    protected override void OnContactAdded(PointerContact contact)
    {
        if (Contacts.Count < 2) return;

        // отсчёт начинается с момента, когда пальцев стало двое,
        // а не с первого касания: до него расстояния не существует
        _startDistance = Distance();
        _scale = 1f;
    }

    protected override void OnContactRemoved(PointerContact contact)
    {
        if (Contacts.Count >= 2) return;

        _startDistance = 0;

        if (State == GestureState.Accepted)
            Completed?.Invoke(this, MakeArgs());
    }

    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (Contacts.Count < 2 || _startDistance <= 0) return;

        float distance = Distance();

        if (State == GestureState.Accepted)
        {
            _scale = distance / _startDistance;
            Updated?.Invoke(this, MakeArgs());
            return;
        }

        // порог тот же, что у перетаскивания: изменение расстояния между
        // пальцами — это такой же физический сдвиг, и мерить его другой
        // меркой не за что
        if (MathF.Abs(distance - _startDistance) < PointerThresholds.DragSlop(Contacts[0].Kind, Display))
            return;

        Accept();

        _scale = distance / _startDistance;
        Started?.Invoke(this, MakeArgs());
    }

    protected override void OnCancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    private float Distance()
    {
        Point a = Contacts[0].Location;
        Point b = Contacts[1].Location;

        float dx = b.X - a.X;
        float dy = b.Y - a.Y;

        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private PinchGestureEventArgs MakeArgs()
    {
        // после ухода пальца остаётся один — центром считаем его,
        // иначе Completed пришёл бы с обращением к несуществующему контакту
        Point center = Contacts.Count >= 2
            ? new Point(
                (Contacts[0].Location.X + Contacts[1].Location.X) / 2f,
                (Contacts[0].Location.Y + Contacts[1].Location.Y) / 2f)
            : Contacts.Count == 1 ? Contacts[0].Location : Point.Empty;

        return new PinchGestureEventArgs(_scale, center);
    }
}