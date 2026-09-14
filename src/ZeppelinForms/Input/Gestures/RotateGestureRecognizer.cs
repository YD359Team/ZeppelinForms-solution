using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <param name="Angle">Поворот от начала жеста, в градусах. По часовой — плюс.</param>
public sealed record class RotateGestureEventArgs(float Angle, Point Center);

/// <summary>Поворот двумя пальцами.</summary>
public sealed class RotateGestureRecognizer : GestureRecognizer
{
    /// <summary>Порог в градусах, а не в миллиметрах: угол физической
    /// длины не имеет, и плотность экрана на него не влияет. Зато влияет
    /// расстояние между пальцами — при близко сведённых пальцах угол
    /// скачет, поэтому ниже есть проверка на минимальную базу.</summary>
    public float ThresholdDegrees { get; set; } = 8f;

    /// <summary>Минимальное расстояние между пальцами, при котором угол
    /// вообще имеет смысл. В миллиметрах.</summary>
    public float MinimumSpanMm { get; set; } = 12f;

    private float _startAngle;
    private float _angle;

    protected override int MaxContacts => 2;

    public event EventHandler<RotateGestureEventArgs>? Started;
    public event EventHandler<RotateGestureEventArgs>? Updated;
    public event EventHandler<RotateGestureEventArgs>? Completed;
    public event EventHandler? Cancelled;

    protected override void OnContactAdded(PointerContact contact)
    {
        if (Contacts.Count < 2) return;

        _startAngle = Angle();
        _angle = 0;
    }

    protected override void OnContactRemoved(PointerContact contact)
    {
        if (Contacts.Count >= 2) return;

        if (State == GestureState.Accepted)
            Completed?.Invoke(this, MakeArgs());
    }

    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (Contacts.Count < 2) return;

        if (Span() < Display.MillimetersToLogical(MinimumSpanMm)) return;

        float delta = Normalize(Angle() - _startAngle);

        if (State == GestureState.Accepted)
        {
            _angle = delta;
            Updated?.Invoke(this, MakeArgs());
            return;
        }

        if (MathF.Abs(delta) < ThresholdDegrees) return;

        Accept();

        _angle = delta;
        Started?.Invoke(this, MakeArgs());
    }

    protected override void OnCancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    private float Angle()
    {
        Point a = Contacts[0].Location;
        Point b = Contacts[1].Location;

        return MathF.Atan2(b.Y - a.Y, b.X - a.X) * (180f / MathF.PI);
    }

    private float Span()
    {
        Point a = Contacts[0].Location;
        Point b = Contacts[1].Location;

        float dx = b.X - a.X;
        float dy = b.Y - a.Y;

        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Свести разность к диапазону от -180 до 180: без этого
    /// переход через 180 градусов давал бы скачок на полный оборот.</summary>
    private static float Normalize(float degrees)
    {
        while (degrees > 180f) degrees -= 360f;
        while (degrees < -180f) degrees += 360f;

        return degrees;
    }

    private RotateGestureEventArgs MakeArgs()
    {
        Point center = Contacts.Count >= 2
            ? new Point(
                (Contacts[0].Location.X + Contacts[1].Location.X) / 2f,
                (Contacts[0].Location.Y + Contacts[1].Location.Y) / 2f)
            : Contacts.Count == 1 ? Contacts[0].Location : Point.Empty;

        return new RotateGestureEventArgs(_angle, center);
    }
}