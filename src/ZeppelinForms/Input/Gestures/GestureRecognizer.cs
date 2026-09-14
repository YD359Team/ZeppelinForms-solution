using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <summary>Наблюдатель за одним контактом, умеющий заявить на него права.</summary>
/// <remarks>
/// Распознавание — политика элемента, арбитраж — решение общее: победитель
/// у контакта ровно один. Поэтому распознаватели висят на элементах,
/// а разбирается между ними GestureArena.
/// </remarks>
public abstract class GestureRecognizer
{
    private GestureArena? _arena;

    /// <summary>Элемент, на котором висит распознаватель.</summary>
    public UIElement? Element { get; internal set; }

    public bool IsEnabled { get; set; } = true;

    public GestureState State { get; private set; }

    /// <summary>Контакт, за которым сейчас следим. Null вне жеста.</summary>
    protected PointerContact? Contact { get; private set; }

    /// <summary>Экран, по которому считаются пороги. Берётся один раз
    /// на контакт: Displays.Primary каждый раз заново перечисляет мониторы
    /// через P/Invoke, а вызывать его на каждое движение пальца нельзя.
    /// За время одного жеста экран всё равно не меняется.</summary>
    protected DisplayInfo Display { get; private set; } = Displays.Primary;

    protected virtual void OnBegin() { }

    protected virtual void OnPointerDown(PointerEventArgs e) { }

    protected virtual void OnPointerMove(PointerEventArgs e) { }

    protected virtual void OnPointerUp(PointerEventArgs e) { }

    /// <summary>Контакт оборвали или распознаватель проиграл.
    /// Всё начатое надо откатить, а не зафиксировать.</summary>
    protected virtual void OnCancel() { }

    /// <summary>Заявить права на контакт. Все остальные участники арены
    /// выбывают, а элементы под контактом получают отмену.</summary>
    protected void Accept()
    {
        if (State != GestureState.Possible) return;

        State = GestureState.Accepted;
        _arena?.Accept(this);
    }

    /// <summary>Выйти из борьбы: жест не наш. Зовите явно, а не молчите —
    /// молчащий участник продолжает получать события до конца контакта.</summary>
    protected void Reject()
    {
        if (State != GestureState.Possible) return;

        State = GestureState.Rejected;
        OnCancel();
    }

    internal void Enter(GestureArena arena, PointerContact contact)
    {
        _arena = arena;
        Contact = contact;
        State = GestureState.Possible;
        Display = Displays.Primary;

        OnBegin();
    }

    internal void Leave()
    {
        _arena = null;
        Contact = null;
    }

    internal void DispatchDown(PointerEventArgs e) => OnPointerDown(e);

    internal void DispatchMove(PointerEventArgs e) => OnPointerMove(e);

    internal void DispatchUp(PointerEventArgs e) => OnPointerUp(e);

    internal void LoseToOther()
    {
        if (State == GestureState.Rejected) return;

        State = GestureState.Rejected;
        OnCancel();
    }

    internal void CancelExternally()
    {
        if (State == GestureState.Rejected) return;

        State = GestureState.Rejected;
        OnCancel();
    }
}