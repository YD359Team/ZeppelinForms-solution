using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms;

/// <summary>The pointer: contacts of the mouse, touch and pen from the platform to the
/// elements — hit testing, capture, clicks and their count, the wheel.</summary>
public partial class Form
{
    private long _lastClickTicks;
    private Point _lastClickPoint;
    private int _clickCount;
    private MouseButton _lastClickButton;

    /// <summary>The element under the press of the right and of the middle button.
    /// The left one is tracked by the contact itself (PointerContact.Pressed);
    /// the other buttons share the contact with it and need a slot of their own.</summary>
    private UIElement? _rightPressed;
    private UIElement? _middlePressed;

    public int DoubleClickIntervalMs { get; set; } = 400;
    public float DoubleClickSlop { get; set; } = 4f;
    private UIElement? _hoveredElement;
    private CursorKind _lastCursor = CursorKind.Arrow;
    private CursorImage? _lastCursorImage;

    /// <summary>The mouse identifier. One rather than zero — W3C Pointer Events
    /// number the mouse the same way, and the browser backend passes pointerId as is.</summary>
    public const int MousePointerId = 1;

    /// <summary>Live contacts by identifier. Empty while nothing is pressed:
    /// for most forms it stays that way.</summary>
    private readonly Dictionary<int, PointerContact> _contacts = [];

    private int? _primaryContactId;

    /// <summary>How many contacts hold the system capture. It has to be counted
    /// because IPlatformWindow.CaptureMouse() takes no arguments — there is one
    /// system capture per window, and a second finger, when released, would take
    /// the capture away from the first.</summary>
    private int _platformCaptureCount;

    /// <summary>The contact being processed right now. Needed by CaptureMouse:
    /// an element calls it from inside a handler and does not have to know
    /// the contact identifier.</summary>
    private PointerContact? _dispatching;

    private PointerContact? PrimaryContact =>
        _primaryContactId is int id && _contacts.TryGetValue(id, out PointerContact? c) ? c : null;

    // The backends still send the mouse as positional arguments. There is no need
    // to change them in this pass: the entry point is one, and the pipeline under it is new.

    internal void OnPointerMove(Point point, KeyModifiers modifiers = KeyModifiers.None) =>
        OnPointerMove(new PointerEventArgs(
            MousePointerId, PointerKind.Mouse, point, MouseButton.Left, 1f, modifiers));

    internal void OnPointerDown(PointerEventArgs e)
    {
        // a copy: the form observes, and a Handled set there must not take
        // the pointer from the element under it
        OnPointerPressed(e with { });

        // the pointer is in use: focus rings go away until the next key press
        NotePointerInput();

        HideToolTip();

        if (e.Button == MouseButton.Left && _flyouts.Count > 0 && !IsInsideAnyFlyout(e.Location))
        {
            CloseAllFlyouts();
            return;
        }

        UIElement? hit = HitTestAll(e.Location);
        if (hit is { IsEnabled: false }) return;

        if (IsInspectorEnabled && _inspectorGrid is not null && !IsInsideInspector(e.Location))
        {
            UIElement? picked = Content is not null ? HitTester.HitTest(Content, e.Location) : null;

            if (picked is not null)
            {
                _inspectorGrid.SelectedObject = picked;

                // the picked element is outlined on the form, as one chosen from the
                // audit list is: the grid alone didn't say which of two alike it was
                InspectedElement = picked;

                Invalidate();
                return;
            }
        }

        // the right and the middle button, like the left one, click only when
        // the press and the release land on the same element — see OnPointerUp
        RememberPress(e.Button, hit);

        // the same button on a live contact: the mouse sent a second press
        // without a release. The contact continues, only the mask changes —
        // a second contact with the same identifier must not be created
        if (_contacts.TryGetValue(e.PointerId, out PointerContact? existing))
        {
            existing.Buttons |= e.Button.ToFlag();
            existing.Update(e.Location, e.Timestamp);

            DispatchDown(existing, hit, e, isNew: false);
            return;
        }

        bool isPrimary = _contacts.Count == 0;

        var contact = new PointerContact
        {
            Id = e.PointerId,
            Kind = e.Kind,
            IsPrimary = isPrimary,
            DownLocation = e.Location,
            DownTimestamp = e.Timestamp,
            Buttons = e.Button.ToFlag(),
            Pressed = hit,
            Chain = BuildChain(hit),
        };

        contact.Update(e.Location, e.Timestamp);

        _contacts[e.PointerId] = contact;

        if (isPrimary) _primaryContactId = e.PointerId;

        // the click count is tracked only for the primary contact: two fingers
        // in a row on the same spot are not a double click
        if (isPrimary) UpdateClickCount(e.Location, e.Button);

        DispatchDown(contact, hit, e, isNew: true);
    }

    private void DispatchDown(PointerContact contact, UIElement? hit, PointerEventArgs e, bool isNew)
    {
        PointerContact? previousDispatch = _dispatching;
        _dispatching = contact;

        try
        {
            foreach (UIElement element in contact.Chain)
            {
                element.RaisePointerDown(e);
                if (e.Handled) return;
            }

            // the arena is assembled once per contact, not per button
            if (isNew)
                contact.Arena = GestureArena.TryCreate(contact, CancelCompatInteraction);

            // the arena goes before the compatibility events: a recognizer that
            // is satisfied by the press itself must manage to cancel them before
            // the button paints itself pressed
            contact.Arena?.PointerDown(e);

            if (contact.IsCompatCancelled) return;

            if (!contact.IsPrimary) return;

            var downArgs = new MouseButtonEventArgs(
                e.Button, MouseButtonState.Down, e.Location, e.Modifiers);

            foreach (UIElement element in contact.Chain)
                element.RaisePreviewMouseDown(downArgs);

            hit?.RaiseMouseDown(downArgs);

            // a press on a scroll bar scrolls, and leaves the focus where it was
            if (e.Button == MouseButton.Left && hit is not null && !(hit is PanelControl panel && panel.IsOverScrollBar(e.Location)))
                FocusFromHit(hit);
        }
        finally
        {
            _dispatching = previousDispatch;
        }
    }

    internal void OnPointerDown(Point point, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None) =>
    OnPointerDown(new PointerEventArgs(
        MousePointerId, PointerKind.Mouse, point, button, 1f, modifiers));

    internal void OnPointerUp(Point point, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None) =>
        OnPointerUp(new PointerEventArgs(
            MousePointerId, PointerKind.Mouse, point, button, 1f, modifiers));

    internal void OnPointerMove(PointerEventArgs e)
    {
        OnPointerMoved(e with { });

        // the cursor position is a mouse concept: the tooltip and the inspector
        // rely on it, and a finger must not move it
        if (e.Kind == PointerKind.Mouse)
            _lastPointerPosition = e.Location;

        _contacts.TryGetValue(e.PointerId, out PointerContact? contact);
        contact?.Update(e.Location, e.Timestamp);

        // with a live contact the chain is built from the capturing or the pressed
        // element, not from whatever is under the cursor: otherwise the preview
        // would spill into someone else's subtree
        UIElement? target = contact?.Target
            ?? (contact is null && e.Kind == PointerKind.Mouse ? HitTestAll(e.Location) : null);

        UIElement[] chain = BuildChain(target);

        // save and restore rather than reset: RaiseMouseDown may open a modal
        // dialog, it will spin a nested loop with its own contacts, and a reset
        // in its finally would cut our frame short
        PointerContact? previousDispatch = _dispatching;
        _dispatching = contact;

        try
        {
            foreach (UIElement element in chain)
            {
                element.RaisePointerMove(e);
                if (e.Handled) return;
            }

            contact?.Arena?.PointerMove(e);

            // the contact went to a gesture — there is no compatibility part
            // of the interaction anymore, and hover and the cursor don't concern it
            if (contact is { IsCompatCancelled: true }) return;

            // the held buttons come from the contact, which tracks every press and
            // release of this pointer; no contact means nothing is held — hover
            KeyModifiers modifiers = e.Modifiers;
            PointerButtons buttons = contact?.Buttons ?? PointerButtons.None;

            var moveArgs = new MouseMoveEventArgs(e.Location) { Modifiers = modifiers, Buttons = buttons };

            // the preview goes from the root to the target, before the target itself
            // gets the move. It works with a button held down too — that is exactly
            // where it is needed, so that an ancestor can follow a drag over its descendants
            foreach (UIElement element in chain)
                element.RaisePreviewMouseMove(moveArgs);

            if (contact?.Target is UIElement held)
            {
                held.RaiseMouseMove(e.Location, modifiers, buttons);
                return;
            }

            // what follows is only hover, and touch has none: a finger is either
            // on the screen or not, there is no "over the element" state
            if (e.Kind != PointerKind.Mouse) return;

            UIElement? hit = target;

            if (hit != _hoveredElement)
            {
                // the arguments carry "from" and "to", so that a handler can tell
                // moving into a descendant from leaving altogether
                _hoveredElement?.RaiseMouseExit(e.Location, hit, modifiers, buttons);
                hit?.RaiseMouseEnter(e.Location, _hoveredElement, modifiers, buttons);

                _hoveredElement = hit;

                ScheduleToolTip(hit);
            }

            hit?.RaiseMouseMove(e.Location, modifiers, buttons);

            (CursorKind cursor, CursorImage? cursorImage) = hit?.EffectiveCursor ?? (CursorKind.Arrow, null);

            if (cursor != _lastCursor || !ReferenceEquals(cursorImage, _lastCursorImage))
            {
                _lastCursor = cursor;
                _lastCursorImage = cursorImage;

                if (cursorImage is not null)
                    PlatformWindow?.SetCursor(cursorImage);
                else
                    PlatformWindow?.SetCursor(cursor);
            }
        }
        finally
        {
            _dispatching = previousDispatch;
        }
    }

    internal void OnPointerLeaveWindow()
    {
        HideToolTip();
        _hoveredElement?.RaiseMouseExit(_lastPointerPosition, null);
        _hoveredElement = null;
    }

    internal void OnPointerUp(PointerEventArgs e)
    {
        OnPointerReleased(e with { });

        if (e.Kind == PointerKind.Mouse)
            _lastPointerPosition = e.Location;

        // a release without a press is common after a cancel: the capture was
        // taken away, the contact is gone, and the system still sends Up
        if (!_contacts.TryGetValue(e.PointerId, out PointerContact? contact))
            return;

        contact.Update(e.Location, e.Timestamp);

        UIElement? hit = HitTestAll(e.Location);

        // save and restore rather than reset: RaiseMouseDown may open a modal
        // dialog, it will spin a nested loop with its own contacts, and a reset
        // in its finally would cut our frame short
        PointerContact? previousDispatch = _dispatching;
        _dispatching = contact;

        try
        {
            foreach (UIElement element in contact.Chain)
            {
                element.RaisePointerUp(e);
                if (e.Handled) break;
            }

            contact.Arena?.PointerUp(e);

            if (contact.IsCompatCancelled) return;

            if (!contact.IsPrimary) return;

            var upArgs = new MouseButtonEventArgs(
                e.Button, MouseButtonState.Up, e.Location, e.Modifiers);

            if (e.Button == MouseButton.Left)
            {
                // the same chain as on the press: whoever followed the press through
                // the preview must learn about the release too
                for (UIElement? current = contact.Pressed; current is not null; current = current.Parent)
                    current.RaisePreviewMouseUp(upArgs);

                contact.Pressed?.RaiseMouseUp(upArgs);

                // the capturing element must learn about the release even if
                // the press landed on its descendant
                if (contact.Capture is not null && !ReferenceEquals(contact.Capture, contact.Pressed))
                    contact.Capture.RaiseMouseUp(upArgs);

                ReleaseCapture(contact);

                // click = press and release on the same element
                if (hit is not null && ReferenceEquals(hit, contact.Pressed))
                    BubbleClick(hit, e.Button, e.Location);
            }
            else
            {
                hit?.RaiseMouseUp(upArgs);

                // the right and the middle button follow the same rule as the left one:
                // a click is a press and a release on the same element. Previously they
                // clicked on whatever was under the release, with no match to the press:
                // pressing the right button on one row and releasing it on another
                // delivered RightClick to the second row. The system context menu
                // (OnContextMenu) is a separate path and is not affected
                UIElement? pressed = TakePress(e.Button);

                if (hit is not null && ReferenceEquals(hit, pressed))
                    BubbleClick(hit, e.Button, e.Location);
            }
        }
        finally
        {
            _dispatching = previousDispatch;

            // ending the contact is in finally rather than after try. The exits above
            // (a gesture took the contact, the finger is not primary, a handler marked
            // the event handled) bypassed this code, and the contact stayed in the
            // dictionary forever: the next mouse move went not to whoever was under
            // the cursor but to a dead element from a dead contact, recognizers never
            // got Leave, and the capture was never released.
            // From the outside it looked like lost clicks and a swipe that worked every other time
            contact.Arena?.Complete();

            contact.Buttons &= ~e.Button.ToFlag();

            // touch and pen have no buttons — the contact ends together with Up
            if (contact.Kind != PointerKind.Mouse || contact.Buttons == PointerButtons.None)
                EndContact(contact);
        }
    }

    /// <summary>Remember what the right or the middle button was pressed on.
    /// The left one is not stored here: the contact tracks it itself.</summary>
    private void RememberPress(MouseButton button, UIElement? hit)
    {
        if (button == MouseButton.Right) _rightPressed = hit;
        else if (button == MouseButton.Middle) _middlePressed = hit;
    }

    /// <summary>Take the element the button was pressed on. One-shot:
    /// a release consumes its press.</summary>
    private UIElement? TakePress(MouseButton button)
    {
        UIElement? pressed = null;

        if (button == MouseButton.Right)
        {
            pressed = _rightPressed;
            _rightPressed = null;
        }
        else if (button == MouseButton.Middle)
        {
            pressed = _middlePressed;
            _middlePressed = null;
        }

        return pressed;
    }

    /// <summary>The platform reported that a contact was cancelled: pointercancel
    /// in the browser, ACTION_CANCEL on Android, a system shell gesture.</summary>
    internal void OnPointerCancel(int pointerId)
    {
        if (_contacts.TryGetValue(pointerId, out PointerContact? contact))
            CancelContact(contact, PointerCancelReason.Platform);
    }

    /// <summary>The chain from the root to the element. Exactly this order:
    /// both the preview and pointer events tunnel from the top down.</summary>
    private static UIElement[] BuildChain(UIElement? element)
    {
        if (element is null) return [];

        List<UIElement> chain = [];

        for (UIElement? current = element; current is not null; current = current.Parent)
            chain.Add(current);

        chain.Reverse();

        return [.. chain];
    }

    /// <summary>Cut off the compatibility part of the interaction, leaving the contact alive.
    /// Needed when a gesture wins: the button under the finger must be told that
    /// there was no press, while the contact goes on — the recognizer drives it.</summary>
    private void CancelCompatInteraction(PointerContact contact, PointerCancelReason reason)
    {
        if (contact.IsCompatCancelled) return;

        contact.IsCompatCancelled = true;

        var args = new PointerCancelEventArgs(
            contact.Id, contact.Kind, contact.Location, reason);

        // cancel the whole chain, not only the target: an ancestor that followed
        // the press through the preview has set up state for it too
        foreach (UIElement element in contact.Chain)
            element.RaisePointerCanceled(args);

        if (contact.Capture is not null && Array.IndexOf(contact.Chain, contact.Capture) < 0)
            contact.Capture.RaisePointerCanceled(args);

        ReleaseCapture(contact);

        // reset the target, otherwise moves would keep going to the cancelled element
        contact.Pressed = null;
    }

    private void CancelContact(PointerContact contact, PointerCancelReason reason)
    {
        contact.Arena?.Cancel();

        CancelCompatInteraction(contact, reason);

        // a cancelled interaction produces no clicks with any button
        _rightPressed = null;
        _middlePressed = null;

        EndContact(contact);
    }

    private void EndContact(PointerContact contact)
    {
        ReleaseCapture(contact);

        _contacts.Remove(contact.Id);

        if (_primaryContactId == contact.Id)
            _primaryContactId = null;
    }

    private void ReleaseCapture(PointerContact contact)
    {
        if (contact.Capture is null) return;

        contact.Capture = null;

        ZfContract.Require(_platformCaptureCount > 0,
            "The system capture counter went negative: the capture was released twice.");

        if (--_platformCaptureCount == 0)
            PlatformWindow?.ReleaseMouseCapture();
    }

    private void BubbleClick(UIElement hit, MouseButton button, Point point)
    {
        var args = new MouseClickEventArgs(button, MouseButtonState.Up, point, _clickCount);

        for (UIElement? current = hit; current is not null; current = current.Parent)
        {
            current.RaiseClick(args);
            if (args.Handled) break;
        }
    }

    private void UpdateClickCount(Point point, MouseButton button)
    {
        long now = Environment.TickCount64;

        bool sameSpot =
            Math.Abs(point.X - _lastClickPoint.X) <= DoubleClickSlop &&
            Math.Abs(point.Y - _lastClickPoint.Y) <= DoubleClickSlop;

        bool inTime = now - _lastClickTicks <= DoubleClickIntervalMs;

        _clickCount = inTime && sameSpot && button == _lastClickButton ? _clickCount + 1 : 1;

        _lastClickTicks = now;
        _lastClickPoint = point;
        _lastClickButton = button;
    }

    /// <summary>Take mouse moves for itself until the button is released.
    /// The press stays with whatever was hit, so a click on a descendant
    /// of the capturing element keeps working as usual.</summary>
    internal void CaptureMouse(UIElement element)
    {
        // capture is taken from inside contact processing — DragList calls it
        // from OnPreviewMouseDown. Outside processing we take the primary contact:
        // that is how old code that called CaptureMouse from a timer works
        PointerContact? contact = _dispatching ?? PrimaryContact;

        if (contact is null)
        {
            // before 0.11 capture was taken unconditionally, so a silent refusal
            // here would be a silently broken drag for whoever called
            // CaptureMouse outside a press handler
            ZfContract.Require(false, "CaptureMouse outside a live contact: there is nothing to capture.");
            return;
        }

        if (ReferenceEquals(contact.Capture, element)) return;

        // capture means the element took the pointer for itself — so the fight
        // of gestures on this contact is over. Otherwise an ancestor's recognizer
        // calmly waited for the release and won an already captured contact:
        // a drag in DragList ended as a page swipe, and the dragged row stayed
        // hanging because the capturing element never got the release.
        //
        // But only while the fight is on. If there is already a winner, it takes
        // the capture itself — the scroll recognizer does so right after winning —
        // and cancelling the arena would mean removing the winner from the contact
        if (contact.Arena is { HasWinner: false } arena)
        {
            arena.Cancel();
            contact.Arena = null;
        }

        if (contact.Capture is null && _platformCaptureCount++ == 0)
            PlatformWindow?.CaptureMouse();

        contact.Capture = element;
    }

    internal void ReleaseMouseCapture(UIElement element)
    {
        foreach (PointerContact contact in _contacts.Values)
        {
            if (!ReferenceEquals(contact.Capture, element)) continue;

            ReleaseCapture(contact);
            return;
        }
    }

    /// <summary>The system took the capture away.</summary>
    /// <remarks>
    /// This used to send a fake MouseUp. The button doesn't care — Click bubbles
    /// separately, from BubbleClick. But TrackBar and GridSplitter took it for
    /// a real release and committed the value, even though the interaction
    /// was cut off.
    /// </remarks>
    internal void OnCaptureLost()
    {
        // ToArray: CancelContact modifies the dictionary under us
        foreach (PointerContact contact in _contacts.Values.ToArray())
            CancelContact(contact, PointerCancelReason.CaptureLost);

        // there is no system capture anymore, the counter is brought to zero
        // without calling ReleaseMouseCapture — there is nothing to release
        _platformCaptureCount = 0;
    }

    /// <summary>Give focus to whoever can take it: the pressed element itself
    /// or the nearest ancestor that accepts input.</summary>
    /// <remarks>
    /// A click almost never lands directly on the element that accepts input:
    /// in a list the cursor is over a row, in a tree — over a node, in a grid —
    /// over a cell. Previously focus simply didn't move in such cases, and the
    /// keyboard kept working on the control where it had been left.
    /// </remarks>
    private void FocusFromHit(UIElement hit)
    {
        for (UIElement? element = hit; element is not null; element = element.Parent)
            if (element is IInputElement { TabStop: true } && _focusDispatcher.FocusElement(element))
                return;
    }

    private UIElement? HitTestAll(Point point)
    {
        // hit testing works on geometry: if the layout is waiting for a frame,
        // a click would land on what was on screen before the last change
        EnsureLayout();

        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            var hit = HitTester.HitTest(_overlays[i], point);
            if (hit is not null)
                return hit;
        }

        return Content is not null ? HitTester.HitTest(Content, point) : null;
    }

    private bool IsInsideAnyFlyout(Point point)
    {
        foreach (var flyout in _flyouts)
            if (HitTester.HitTest(flyout, point) is not null)
                return true;

        return false;
    }

    internal void OnMouseWheel(Point point, int delta, int horizontalDelta = 0)
    {
        EnsureLayout();

        var args = new MouseWheelEventArgs(point, delta, horizontalDelta);

        OnPreviewMouseWheel(args);
        if (args.Handled) return;

        UIElement? hit = HitTestAll(point);
        if (hit is null) return;

        for (UIElement? current = hit; current is not null; current = current.Parent)
        {
            current.RaiseMouseWheel(args);
            if (args.Handled)
                break;
        }
    }
}
