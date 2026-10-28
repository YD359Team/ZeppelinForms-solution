using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <summary>The fight for one contact. There is exactly one winner.</summary>
internal sealed class GestureArena
{
    private readonly PointerContact _contact;
    private readonly List<GestureRecognizer> _members;
    private readonly Action<PointerContact, PointerCancelReason> _onWon;

    private GestureRecognizer? _winner;

    /// <summary>The fight is already decided: someone has claimed the contact.</summary>
    public bool HasWinner => _winner is not null;

    private GestureArena(
        PointerContact contact,
        List<GestureRecognizer> members,
        Action<PointerContact, PointerCancelReason> onWon)
    {
        _contact = contact;
        _members = members;
        _onWon = onWon;
    }

    /// <summary>Assemble an arena along the contact's chain. Null if there are no
    /// recognizers on any element — and that is the usual case, so no extra
    /// object appears per press.</summary>
    public static GestureArena? TryCreate(
        PointerContact contact, Action<PointerContact, PointerCancelReason> onWon)
    {
        List<GestureRecognizer>? candidates = null;

        // from the target to the root: the more specific element gets the right
        // to refuse first. An ancestor must not intercept what the descendant
        // handles by itself
        for (int i = contact.Chain.Length - 1; i >= 0; i--)
        {
            IReadOnlyList<GestureRecognizer>? recognizers = contact.Chain[i].GestureRecognizersOrNull;

            if (recognizers is null) continue;

            foreach (GestureRecognizer recognizer in recognizers)
            {
                if (!recognizer.IsEnabled) continue;

                // a multi-contact recognizer may have taken its share on previous
                // fingers: it doesn't need an extra one and must not hinder the others
                candidates ??= [];
                candidates.Add(recognizer);
            }
        }

        // a finger or a pen held still on an element with a context menu opens it, as
        // a right click does: the platforms send no context menu for a held touch —
        // Android none at all — so the form recognizes the hold itself. Last in the
        // arena: a pan or a swipe the contact turns into goes first
        if (contact.Kind != PointerKind.Mouse && ContextMenuHold(contact) is { } hold)
        {
            candidates ??= [];
            candidates.Add(hold);
        }

        if (candidates is null) return null;

        List<GestureRecognizer> members = [];
        var arena = new GestureArena(contact, members, onWon);

        foreach (GestureRecognizer recognizer in candidates)
        {
            if (recognizer.TryEnter(arena, contact))
                members.Add(recognizer);
        }

        if (members.Count == 0) return null;

        // a participant may have won already on a previous finger: a new contact
        // doesn't reopen the fight, it joins the one already won
        foreach (GestureRecognizer recognizer in members)
        {
            if (recognizer.State != GestureState.Accepted) continue;

            arena.AdoptWinner(recognizer);
            break;
        }

        return arena;
    }

    /// <summary>A hold that opens the context menu of the element nearest the target that
    /// has one. None where an element on the way to it holds a long press of its own —
    /// a list's hold to drag — which then decides what holding does.</summary>
    private static LongPressGestureRecognizer? ContextMenuHold(PointerContact contact)
    {
        for (int i = contact.Chain.Length - 1; i >= 0; i--)
        {
            UIElement element = contact.Chain[i];

            if (element.GestureRecognizersOrNull is { } own &&
                own.Any(recognizer => recognizer is LongPressGestureRecognizer { IsEnabled: true }))
            {
                return null;
            }

            if (element.ContextMenu is not { Count: > 0 } && element.DefaultContextMenu() is not { Count: > 0 })
                continue;

            var hold = new LongPressGestureRecognizer { Element = element };

            hold.Triggered += (_, e) => element.FindOwner()?.OnContextMenu(e.Location);

            return hold;
        }

        return null;
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

        // no copy of the list: Accept changes the participants' states,
        // but not the list itself — it is assembled once when the arena is created
        foreach (GestureRecognizer recognizer in _members)
        {
            if (recognizer.State != GestureState.Possible) continue;

            action(recognizer, e);

            // a victory during the walk: the others don't need this same walk,
            // they have already got LoseToOther from Accept
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

    /// <summary>Acknowledge as the winner the one who won in another arena.
    /// There was no fight here — it ended earlier, on another finger.</summary>
    internal void AdoptWinner(GestureRecognizer winner)
    {
        if (_winner is not null) return;

        _winner = winner;

        foreach (GestureRecognizer recognizer in _members)
            if (!ReferenceEquals(recognizer, winner))
                recognizer.LoseToOther();

        _onWon(_contact, PointerCancelReason.GestureWon);
    }

    /// <summary>The contact ended normally.</summary>
    public void Complete()
    {
        foreach (GestureRecognizer recognizer in _members)
            recognizer.Leave(this);
    }

    /// <summary>The contact was cut off.</summary>
    public void Cancel()
    {
        foreach (GestureRecognizer recognizer in _members)
        {
            recognizer.CancelExternally();
            recognizer.Leave(this);
        }
    }
}