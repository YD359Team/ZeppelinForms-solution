using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <summary>Борьба за один контакт. Победитель ровно один.</summary>
internal sealed class GestureArena
{
    private readonly PointerContact _contact;
    private readonly List<GestureRecognizer> _members;
    private readonly Action<PointerContact, PointerCancelReason> _onWon;

    private GestureRecognizer? _winner;

    private GestureArena(
        PointerContact contact,
        List<GestureRecognizer> members,
        Action<PointerContact, PointerCancelReason> onWon)
    {
        _contact = contact;
        _members = members;
        _onWon = onWon;
    }

    /// <summary>Собрать арену по цепочке контакта. Null, если распознавателей
    /// нет ни на одном элементе — а это обычный случай, и лишнего объекта
    /// на каждое нажатие в нём не появится.</summary>
    public static GestureArena? TryCreate(
        PointerContact contact, Action<PointerContact, PointerCancelReason> onWon)
    {
        List<GestureRecognizer>? members = null;

        // от цели к корню: более конкретный элемент получает право
        // отказаться первым. Предок не должен перехватывать то,
        // с чем потомок справляется сам
        for (int i = contact.Chain.Length - 1; i >= 0; i--)
        {
            IReadOnlyList<GestureRecognizer>? recognizers = contact.Chain[i].GestureRecognizersOrNull;

            if (recognizers is null) continue;

            foreach (GestureRecognizer recognizer in recognizers)
            {
                if (!recognizer.IsEnabled) continue;

                members ??= [];
                members.Add(recognizer);
            }
        }

        if (members is null) return null;

        var arena = new GestureArena(contact, members, onWon);

        foreach (GestureRecognizer recognizer in members)
            recognizer.Enter(arena, contact);

        return arena;
    }

    public void PointerDown(PointerEventArgs e) => Dispatch(e, static (r, a) => r.DispatchDown(a));

    public void PointerMove(PointerEventArgs e) => Dispatch(e, static (r, a) => r.DispatchMove(a));

    public void PointerUp(PointerEventArgs e) => Dispatch(e, static (r, a) => r.DispatchUp(a));

    private void Dispatch(PointerEventArgs e, Action<GestureRecognizer, PointerEventArgs> action)
    {
        if (_winner is not null)
        {
            action(_winner, e);
            return;
        }

        // копии списка не делаем: Accept меняет состояния участников,
        // но не сам список — он собран один раз при создании арены
        foreach (GestureRecognizer recognizer in _members)
        {
            if (recognizer.State != GestureState.Possible) continue;

            action(recognizer, e);

            // победа во время обхода: остальным этот же обход не нужен,
            // они уже получили LoseToOther из Accept
            if (_winner is not null) return;
        }
    }

    internal void Accept(GestureRecognizer winner)
    {
        if (_winner is not null) return;

        _winner = winner;

        foreach (GestureRecognizer recognizer in _members)
            if (!ReferenceEquals(recognizer, winner))
                recognizer.LoseToOther();

        _onWon(_contact, PointerCancelReason.GestureWon);
    }

    /// <summary>Контакт кончился штатно.</summary>
    public void Complete()
    {
        foreach (GestureRecognizer recognizer in _members)
            recognizer.Leave();
    }

    /// <summary>Контакт оборвали.</summary>
    public void Cancel()
    {
        foreach (GestureRecognizer recognizer in _members)
        {
            recognizer.CancelExternally();
            recognizer.Leave();
        }
    }
}