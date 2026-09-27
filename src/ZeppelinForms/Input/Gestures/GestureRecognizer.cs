using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <summary>An observer of contacts that can claim them.</summary>
/// <remarks>
/// Recognition is the element's policy, arbitration is a shared decision:
/// a contact has exactly one winner. So recognizers hang on elements,
/// and GestureArena sorts it out between them.
///
/// A recognizer has as many arenas as contacts it drives: a pinch has two,
/// a tap one. The lists go in pairs — an arena at the same index as its contact.
/// </remarks>
public abstract class GestureRecognizer
{
    private readonly List<GestureArena> _arenas = [];
    private readonly List<PointerContact> _contacts = [];

    /// <summary>The element the recognizer hangs on.</summary>
    public UIElement? Element { get; internal set; }

    public bool IsEnabled { get; set; } = true;

    public GestureState State { get; private set; }

    /// <summary>How many contacts the recognizer drives at once.
    /// It doesn't get extra fingers at all — they go to others.</summary>
    protected virtual int MaxContacts => 1;

    /// <summary>The first of the driven contacts. Single-contact recognizers need only it.</summary>
    protected PointerContact? Contact => _contacts.Count > 0 ? _contacts[0] : null;

    protected IReadOnlyList<PointerContact> Contacts => _contacts;

    /// <summary>The screen the thresholds are computed for. Taken once per
    /// gesture: Displays.Primary enumerates the monitors through P/Invoke
    /// every time, and it must not be called on every finger movement.</summary>
    protected DisplayInfo Display { get; private set; } = Displays.Primary;

    /// <summary>The first contact came — the gesture began.</summary>
    protected virtual void OnBegin() { }

    /// <summary>A contact was added, including the first one.</summary>
    protected virtual void OnContactAdded(PointerContact contact) { }

    /// <summary>A contact left. By the time of the call it is no longer in Contacts.</summary>
    protected virtual void OnContactRemoved(PointerContact contact) { }

    protected virtual void OnPointerDown(PointerEventArgs e) { }

    protected virtual void OnPointerMove(PointerEventArgs e) { }

    protected virtual void OnPointerUp(PointerEventArgs e) { }

    /// <summary>The gesture was cut off or it lost. Everything started must be
    /// rolled back, not committed.</summary>
    protected virtual void OnCancel() { }

    /// <summary>Take the system capture for the duration of the gesture.</summary>
    /// <remarks>
    /// Needed by gestures that continue beyond the window: without capture mouse
    /// moves don't get there, and the drag gets stuck. Touch doesn't care — the
    /// screen gives the contact away whole anyway — so capture is taken on demand,
    /// not by everyone. No need to release it: Form removes it together with
    /// the end of the contact.
    /// </remarks>
    protected void CaptureContact() => Element?.CaptureForGesture();

    /// <summary>Claim the contacts. We win in all our arenas at once: otherwise
    /// one finger of a pinch would keep driving a button while the other scales.</summary>
    protected void Accept()
    {
        if (State != GestureState.Possible) return;

        State = GestureState.Accepted;

        // a copy: the arena's Accept calls _onWon, and that may cut off
        // a contact and clean our lists right under the walk
        foreach (GestureArena arena in _arenas.ToArray())
            arena.Accept(this);
    }

    /// <summary>Leave the fight: the gesture isn't ours. Call it explicitly rather
    /// than stay silent — a silent participant keeps getting events until
    /// the end of the contact.</summary>
    protected void Reject()
    {
        if (State != GestureState.Possible) return;

        State = GestureState.Rejected;
        OnCancel();
    }

    /// <summary>Take one more contact. false means "I have enough",
    /// and the arena doesn't take such a participant.</summary>
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

        // the last finger left — the recognizer is free again
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