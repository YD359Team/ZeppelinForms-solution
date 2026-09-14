using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <summary>Наблюдатель за контактами, умеющий заявить на них права.</summary>
/// <remarks>
/// Распознавание — политика элемента, арбитраж — решение общее: победитель
/// у контакта ровно один. Поэтому распознаватели висят на элементах,
/// а разбирается между ними GestureArena.
///
/// Арен у распознавателя столько же, сколько ведомых контактов: у щипка
/// их две, у нажатия одна. Списки идут парами — арена по тому же индексу,
/// что и её контакт.
/// </remarks>
public abstract class GestureRecognizer
{
    private readonly List<GestureArena> _arenas = [];
    private readonly List<PointerContact> _contacts = [];

    /// <summary>Элемент, на котором висит распознаватель.</summary>
    public UIElement? Element { get; internal set; }

    public bool IsEnabled { get; set; } = true;

    public GestureState State { get; private set; }

    /// <summary>Сколько контактов распознаватель ведёт одновременно.
    /// Лишние пальцы он не получает вовсе — они достаются другим.</summary>
    protected virtual int MaxContacts => 1;

    /// <summary>Первый из ведомых контактов. Одноконтактным хватает его.</summary>
    protected PointerContact? Contact => _contacts.Count > 0 ? _contacts[0] : null;

    protected IReadOnlyList<PointerContact> Contacts => _contacts;

    /// <summary>Экран, по которому считаются пороги. Берётся один раз
    /// на жест: Displays.Primary каждый раз заново перечисляет мониторы
    /// через P/Invoke, а вызывать его на каждое движение пальца нельзя.</summary>
    protected DisplayInfo Display { get; private set; } = Displays.Primary;

    /// <summary>Пришёл первый контакт — жест начался.</summary>
    protected virtual void OnBegin() { }

    /// <summary>Добавился контакт, в том числе первый.</summary>
    protected virtual void OnContactAdded(PointerContact contact) { }

    /// <summary>Контакт ушёл. К моменту вызова его в Contacts уже нет.</summary>
    protected virtual void OnContactRemoved(PointerContact contact) { }

    protected virtual void OnPointerDown(PointerEventArgs e) { }

    protected virtual void OnPointerMove(PointerEventArgs e) { }

    protected virtual void OnPointerUp(PointerEventArgs e) { }

    /// <summary>Жест оборвали или он проиграл. Всё начатое надо откатить,
    /// а не зафиксировать.</summary>
    protected virtual void OnCancel() { }

    /// <summary>Заявить права. Побеждаем во всех своих аренах разом:
    /// иначе у щипка один палец остался бы вести кнопку, пока второй
    /// масштабирует.</summary>
    protected void Accept()
    {
        if (State != GestureState.Possible) return;

        State = GestureState.Accepted;

        // копия: Accept арены зовёт _onWon, а тот может оборвать контакт
        // и вычистить наши списки прямо под обходом
        foreach (GestureArena arena in _arenas.ToArray())
            arena.Accept(this);
    }

    /// <summary>Выйти из борьбы: жест не наш. Зовите явно, а не молчите —
    /// молчащий участник продолжает получать события до конца контакта.</summary>
    protected void Reject()
    {
        if (State != GestureState.Possible) return;

        State = GestureState.Rejected;
        OnCancel();
    }

    /// <summary>Принять ещё один контакт. false означает «мне хватит»,
    /// и арена такого участника к себе не берёт.</summary>
    internal bool TryEnter(GestureArena arena, PointerContact contact)
    {
        if (_contacts.Count >= MaxContacts) return false;

        bool first = _contacts.Count == 0;

        if (first)
        {
            State = GestureState.Possible;
            Display = Displays.Primary;
        }

        _arenas.Add(arena);
        _contacts.Add(contact);

        if (first) OnBegin();

        OnContactAdded(contact);

        return true;
    }

    internal void Leave(GestureArena arena)
    {
        int index = _arenas.IndexOf(arena);
        if (index < 0) return;

        PointerContact contact = _contacts[index];

        _arenas.RemoveAt(index);
        _contacts.RemoveAt(index);

        OnContactRemoved(contact);

        // последний палец ушёл — распознаватель снова свободен
        if (_contacts.Count == 0)
            State = GestureState.Possible;
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