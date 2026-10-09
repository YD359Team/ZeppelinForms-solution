using System.Diagnostics;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Dispatchers;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Dispatchers;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.DragDrop;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms;

public partial class Form : IDisposable
{
    /// <summary>A flyout was closed — by a click outside, from code or together with the form.
    /// Controls that opened it must reset their reference here.</summary>
    public event EventHandler<UIElement>? FlyoutClosed;
    public event EventHandler? Shown;

    /// <summary>The window was destroyed. Raised no matter where the closing
    /// came from: Accept, Cancel, the close button in the title bar or the system itself.</summary>
    public event EventHandler? Closed;

    internal IPlatformWindow? PlatformWindow
    {
        get;
        set
        {
            field = value;

            if (value is null) return;

            // a new window is a new life: the form may have been closed and shown again
            _isClosed = false;

            // the thread that creates the window is the form's UI thread, and
            // Invoke from other threads goes through the window's queue
            _dispatcher = Dispatcher.Attach(value).ForWindow(value);

            // the theme subscription is renewed with every window: it is dropped
            // at close (see OnWindowClosed), and a form shown again must follow
            // the theme again. -= first, so that the subscription from the
            // constructor is not doubled on the first show
            App.ThemeChanged -= OnThemeChanged;
            App.ThemeChanged += OnThemeChanged;

            // the theme was switched while the form was closed — apply it now
            if (_themeAtUnsubscribe is not null && !ReferenceEquals(_themeAtUnsubscribe, App.Theme))
                OnThemeChanged(null, EventArgs.Empty);

            _themeAtUnsubscribe = null;

            // the text scale the same way: renewed with every window, and a change
            // made while the form was closed is caught up with now
            RenewTextScaleSubscription();

            // the language subscription is renewed the same way as the theme's,
            // and a language switched while the form was closed is applied now
            Localization.Changed -= OnLocalizationChanged;
            Localization.Changed += OnLocalizationChanged;

            if (_localizationVersionAtUnsubscribe >= 0 &&
                _localizationVersionAtUnsubscribe != Localization.Version)
                OnLocalizationChanged(null, EventArgs.Empty);

            _localizationVersionAtUnsubscribe = -1;

            if (!s_openForms.Contains(this))
                s_openForms.Add(this);

            // the window has just appeared: if drag-and-drop was enabled
            // before it was shown, the platform doesn't know about it yet
            if (AllowDrop)
                value.SetDragDropEnabled(true);

            // the same with animations: those added before the window was created
            // sit in the clock, but there was nobody to deliver frames — ask for them here
            ReviewFrames();
        }
    }

    private static readonly List<Form> s_openForms = [];

    /// <summary>Forms with a live window. Needed by the application life cycle:
    /// going to the background concerns all windows, not just the main one.</summary>
    public static IReadOnlyList<Form> OpenForms => s_openForms;

    /// <summary>The window as a desktop object. null where there is no desktop:
    /// in the browser and on Android there is no title bar, opacity or window
    /// state, and silently doing nothing is the correct behavior.</summary>
    internal IDesktopWindow? DesktopWindow => PlatformWindow as IDesktopWindow;

    public WindowStartupLocation WindowStartupLocation { get; set; }

    /// <summary>Whether to accept drag-and-drop from the system into this window.
    /// Without it AllowDrop on elements won't work: the window is not registered
    /// as a drop target, and the system doesn't know about it.</summary>
    public bool AllowDrop
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            PlatformWindow?.SetDragDropEnabled(value);
        }
    }

    private Point _themeRippleOrigin;
    private float _themeRippleRadius;
    private Color _themeRippleColor;
    private bool _themeRippleActive;

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

    private float _opacity = 1f;

    public float Opacity
    {
        get => _opacity;
        set
        {
            _opacity = Math.Clamp(value, 0f, 1f);
            DesktopWindow?.SetOpacity(_opacity);
        }
    }

    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Icon? Icon { get; set; }
    public Point Position { get; set; }

    /// <summary>The size of the window, frame included. <see cref="DefaultSize"/>
    /// until set: a form nobody sized opens as a usable window, not as a speck.</summary>
    public Size Size
    {
        get => _size ?? DefaultSize;
        set => _size = value;
    }

    private Size? _size;

    /// <summary>The size of a form whose <see cref="Size"/> was not set, as WinForms
    /// has it. A form of the application overrides it for its own default.</summary>
    protected virtual Size DefaultSize => new(800, 600);

    /// <summary>Whether <see cref="Size"/> was set: the browser and Android size an
    /// unsized dialog to their own surface instead.</summary>
    internal bool IsSizeSet => _size is not null;

    // TODO: add min\max size support
    public Size MinimumSize { get; set; } = Size.Auto;
    public Size MaximumSize { get; set; } = Size.Auto;

    /// <summary>The form is shown as a modal dialog.</summary>
    public bool IsDialog { get; private set; }

    public Font? Font { get; set; }

    private WindowState _windowState = WindowState.Normal;

    // the title bar follows these while the window is open too: they used to
    // be read only when the window was created

    public bool CanMinimize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            UpdateChrome();
        }
    } = true;

    public bool CanMaximize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            UpdateChrome();
        }
    } = true;

    /// <summary>Whether the user can resize the window by its edges. Only a
    /// <see cref="FormBorderStyle"/> with a sizing frame can be resized at all.</summary>
    public bool CanResize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            UpdateChrome();
        }
    } = true;

    public WindowState WindowState
    {
        get => _windowState;
        set
        {
            if (_windowState == value) return;

            _windowState = value;
            DesktopWindow?.SetWindowState(value);
            OnWindowStateChanged(EventArgs.Empty);
        }
    }

    public event EventHandler? WindowStateChanged;

    public NameScope NameScope { get; } = new();

    public UIElement? Content
    {
        get;
        set
        {
            if (field == value) return;

            if (field is not null)
            {
                DetachTree(field);
                field.Owner = null;
            }

            field = value;

            if (value is not null)
            {
                value.Owner = this;
                AttachTree(value);
            }

            // changing the content is a complete change of geometry:
            // a layout pass and a redraw of the whole window are needed
            Invalidate();
        }
    }

    public Size ClientSize
    {
        get;
        internal set
        {
            if (field == value) return;

            field = value;

            // before the layout the platform runs next: an overlay that covers
            // the window — an image viewer — takes the new size in the same pass
            OnClientSizeChanged(EventArgs.Empty);
        }
    }

    /// <summary>The client area changed its size: the window was resized, maximized,
    /// or the screen turned.</summary>
    public event EventHandler? ClientSizeChanged;

    public FlowDirection? FlowDirection { get; set; }

    // The list is private and changes only through AttachOverlay/DetachOverlay.
    // It used to be a plain List, and five methods out of six added the element
    // directly — the theme never reached the overlay, and DetachTree was not
    // called on close. There is no direct Add anymore, so attaching can't be
    // forgotten: the only way in goes through it.
    private readonly List<UIElement> _overlays = [];
    public IReadOnlyList<UIElement> Overlays => _overlays;
    private readonly List<UIElement> _flyouts = [];
    private readonly List<UIElement> _toasts = [];

    private UIElement? _dropTarget;
    private UIElement? _hoveredElement;
    private CursorKind _lastCursor = CursorKind.Arrow;
    private readonly FocusDispatcher _focusDispatcher = new();

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

    // ===== ToolTip =====
    public int ToolTipDelay { get; set; } = 700;
    private IDisposable? _toolTipWake;
    private UIElement? _toolTipOwner;
    private UIElement? _activeToolTip;
    private Point _lastPointerPosition;

    // ===== Inspector (F12) =====
    public bool IsInspectorEnabled { get; private set; }
    public UIElement? InspectedElement { get; private set; }

    private bool IsInsideInspector(Point point) =>
        (_inspectorGrid is not null && HitTester.HitTest(_inspectorGrid, point) is not null) ||
        IsInsideAudit(point);

    /// <summary>The width of the inspector's property grid at the right edge.</summary>
    private const float InspectorWidth = 320f;

    private bool _dialogAccepted;
    private object? _dialogValue;

    internal IPlatform? Platform { get; set; }

    public Form()
    {
        App.ThemeChanged += OnThemeChanged;
        App.TextScaleChanged += OnTextScaleChanged;
        Localization.Changed += OnLocalizationChanged;
        _focusDispatcher.FocusChanged += OnFocusChangedForKeyboard;
        _focusDispatcher.FocusChanged += OnFocusChangedForAccessibility;
        _focusDispatcher.FocusChanged += OnFocusChangedForStyles;
    }

    /// <summary>The keyboard follows focus: a field got it — show the keyboard,
    /// focus moved to a button or nowhere — hide it.</summary>
    private void OnFocusChangedForKeyboard(object? sender, UIElement? focused)
    {
        if (PlatformWindow is not ISoftKeyboard keyboard) return;

        if (focused is { AcceptsTextInput: true } input)
            keyboard.ShowSoftKeyboard(input.SoftKeyboardKind);
        else
            keyboard.HideSoftKeyboard();
    }

    // ===== Adapters from the old signatures =====
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

            if (e.Button == MouseButton.Left && hit is not null)
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

    // ===== Contact pipeline =====

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

            var moveArgs = new MouseMoveEventArgs(e.Location);

            // the preview goes from the root to the target, before the target itself
            // gets the move. It works with a button held down too — that is exactly
            // where it is needed, so that an ancestor can follow a drag over its descendants
            foreach (UIElement element in chain)
                element.RaisePreviewMouseMove(moveArgs);

            if (contact?.Target is UIElement held)
            {
                held.RaiseMouseMove(e.Location);
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
                _hoveredElement?.RaiseMouseExit(e.Location, hit);
                hit?.RaiseMouseEnter(e.Location, _hoveredElement);

                _hoveredElement = hit;

                ScheduleToolTip(hit);
            }

            hit?.RaiseMouseMove(e.Location);

            CursorKind cursor = hit?.EffectiveCursor ?? CursorKind.Arrow;

            if (cursor != _lastCursor)
            {
                _lastCursor = cursor;
                PlatformWindow?.SetCursor(cursor);
            }

            if (IsInspectorEnabled)
            {
                InspectedElement = !IsInsideInspector(e.Location) && Content is not null
                    ? HitTester.HitTest(Content, e.Location)
                    : null;

                InvalidateVisual();
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

    internal void OnKeyDown(Key key, KeyModifiers modifiers, bool isRepeat)
    {
        Keyboard.OnDown(key, modifiers);

        // before dispatching: a handler that moves focus on this very key must
        // already find the form in keyboard mode, and the new focus shown
        NoteKeyboardInput(key);

        if (key == Key.F12 || (key == Key.I && modifiers.HasFlag(KeyModifiers.Control) && modifiers.HasFlag(KeyModifiers.Shift)))
        {
            ToggleInspector();
            return;
        }

        var args = new KeyEventArgs(key, modifiers);

        // the form's preview goes before everything: a shortcut of the window
        // as a whole takes the key from menus and elements alike
        OnPreviewKeyDown(args);
        if (args.Handled) return;

        // menus and access keys go before the focused element
        if (HandleKeyboardBeforeFocus(key, modifiers)) return;

        // the preview goes from the root to the focused element
        UIElement? focused = _focusDispatcher.FocusedElement;

        if (focused is not null)
        {
            List<UIElement> chain = [];

            for (UIElement? current = focused; current is not null; current = current.Parent)
                chain.Add(current);

            chain.Reverse();

            foreach (UIElement element in chain)
            {
                element.RaisePreviewKeyDown(args);
                if (args.Handled) return;
            }
        }

        for (UIElement? current = focused; current is not null; current = current.Parent)
        {
            current.RaiseKeyDown(args);
            if (args.Handled) break;
        }

        // the form hears what no element took, before its own keys: Tab, Escape, Enter
        if (!args.Handled)
            OnKeyDown(args);

        if (!args.Handled && key == Key.Tab && Content is not null)
        {
            if (modifiers.HasFlag(KeyModifiers.Shift))
                _focusDispatcher.MovePrevious(Content);
            else
                _focusDispatcher.MoveNext(Content);
        }
        else if (!args.Handled)
        {
            // Escape and Enter for the window as a whole
            HandleKeyboardAfterFocus(key, modifiers);
        }
    }

    internal void OnKeyUp(Key key, KeyModifiers modifiers)
    {
        Keyboard.OnUp(key, modifiers);
        HandleKeyboardKeyUp(key);

        var args = new KeyEventArgs(key, modifiers);

        for (UIElement? current = _focusDispatcher.FocusedElement; current is not null; current = current.Parent)
        {
            current.RaiseKeyUp(args);
            if (args.Handled) break;
        }

        if (!args.Handled)
            OnKeyUp(args);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (Content is not null)
            ApplyTheme(Content);

        // overlays live separately from Content: without this an open inspector,
        // menu or flyout stays in the old theme until it is closed
        foreach (UIElement overlay in _overlays.ToArray())
            ApplyTheme(overlay);

        // the theme changes Font.Default as well, and it affects the size of
        // everything that didn't set a font itself. Such an element doesn't go
        // through its properties, so the measure cache is reset for the whole tree
        InvalidateMeasureTree();

        Invalidate();
    }

    /// <summary>The window title as a key: follows the language like the controls' texts.</summary>
    public Form LocalizeTitle(TextKey key, params object?[] args)
    {
        object?[] captured = [.. args];

        _localizedTitle = () => Localization.Get(key, captured);
        Title = _localizedTitle();

        return this;
    }

    private void OnLocalizationChanged(object? sender, EventArgs e)
    {
        if (_localizedTitle is not null)
            Title = _localizedTitle();

        if (Content is not null)
            Walk(Content, static element => element.ApplyLocalization());

        // overlays live separately from Content: an open menu or flyout
        // must not stay in the old language until it is closed
        foreach (UIElement overlay in _overlays.ToArray())
            Walk(overlay, static element => element.ApplyLocalization());

        // a right-to-left language changes what :rtl and :ltr select
        RestyleForDirection();

        // texts of another language have other lengths, and a right-to-left
        // language mirrors the whole layout — every measure is stale
        InvalidateMeasureTree();

        Invalidate();
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

    /// <summary>Style a subtree according to the current theme. Called on
    /// attaching to the form and on a theme change.</summary>
    internal void ApplyTheme(UIElement root)
    {
        Walk(root, App.Theme.Apply);
    }

    /// <summary>Reset the measure cache for the whole tree. Needed where sizes
    /// depend not on the elements' properties but on something shared:
    /// the base font, the scale, the text measurer.</summary>
    internal void InvalidateMeasureTree()
    {
        if (Content is not null)
            Walk(Content, static element => element.InvalidateMeasure());

        foreach (UIElement overlay in _overlays.ToArray())
            Walk(overlay, static element => element.InvalidateMeasure());
    }

    /// <summary>Switch the theme with a ripple spreading from a point.</summary>
    public void SwitchTheme(Theme theme, Point origin)
    {
        // the ripple is decoration: with reduced motion the theme changes at once
        if (Motion.IsReduced)
        {
            App.Theme = theme;
            return;
        }

        _themeRippleOrigin = origin;
        _themeRippleColor = theme.Colors.Background;
        _themeRippleActive = true;

        float maxRadius = MathF.Sqrt(
            ClientSize.Width * ClientSize.Width + ClientSize.Height * ClientSize.Height);

        // the theme is applied at once, and the ripple covers the moment of repainting
        App.Theme = theme;

        var animation = new Animation<float>(
            this, "theme-ripple", 0f, maxRadius, TimeSpan.FromMilliseconds(450),
            Interpolators.Float,
            value => { _themeRippleRadius = value; InvalidateVisual(); },
            Easing.EaseOut,
            completed: () => { _themeRippleActive = false; InvalidateVisual(); });

        AddAnimation(animation);
    }

    internal (bool Active, Point Origin, float Radius, Color Color) ThemeRipple =>
        (_themeRippleActive, _themeRippleOrigin, _themeRippleRadius, _themeRippleColor);

    /// <summary>Give focus to the first text field when the form is shown.
    /// Without it the keyboard does nothing until the user clicks a field,
    /// although there is only one place the typing could go.</summary>
    public bool FocusOnShow { get; set; } = true;

    public void Show()
    {
        // once, before the window is first seen: the place to fill the form
        // with data, as WinForms' Load is
        if (!_loaded)
        {
            _loaded = true;
            OnLoad(EventArgs.Empty);
        }

        DesktopWindow?.SetOpacity(_opacity);
        DesktopWindow?.SetTitle(Title);
        PlatformWindow?.Show();

        // before Shown: a handler that wants the focus elsewhere moves it after us
        if (FocusOnShow && Content is not null)
            FocusFirstTextInput(Content);

        OnShown(EventArgs.Empty);
    }

    private bool _loaded;

    /// <summary>Focus the first text field under root — on showing the form
    /// or a page. Leaves the focus alone if it is already inside root.</summary>
    internal bool FocusFirstTextInput(UIElement root)
    {
        // on a touch screen focus on a field raises the on-screen keyboard over
        // half the window, and nobody asked for it yet: there the first tap decides
        if (PlatformWindow is ISoftKeyboard) return false;

        // the user is already working somewhere inside — don't pull the focus away
        if (_focusDispatcher.FocusedElement is { } focused && IsInTree(root, focused))
            return false;

        return _focusDispatcher.FocusFirstTextInput(root);
    }


    /// <summary>The element with the keyboard focus, for a control that hands the
    /// focus over and takes it back — a split view opening its pane.</summary>
    internal UIElement? FocusedElement => _focusDispatcher.FocusedElement;

    /// <summary>Focus an element of this form, if it takes the focus.</summary>
    internal bool FocusElement(UIElement element) =>
        element.FindOwner() == this && element is IInputElement && _focusDispatcher.FocusElement(element);

    /// <summary>Focus the first Tab stop under root, unless the focus is already inside.</summary>
    internal bool FocusFirstStop(UIElement root)
    {
        if (_focusDispatcher.FocusedElement is { } focused && IsInTree(root, focused))
            return true;

        return _focusDispatcher.MoveNext(root);
    }

    /// <summary>Close the window. <see cref="Closing"/> may keep it open.</summary>
    public void Close()
    {
        if (RequestClose(CloseReason.Code))
            PlatformWindow?.Close();
    }

    /// <summary>The dispatcher of the thread the form's window lives on: the way
    /// back to it from any thread. It goes through the form's own window while the
    /// window is open. Before the window is created — the application's.</summary>
    public Dispatcher Dispatcher => _dispatcher ?? Dispatcher.UIThread;

    private Dispatcher? _dispatcher;

    /// <summary>Run an action on the UI thread and wait for it, as WinForms' Invoke:
    /// at once when already there.</summary>
    /// <remarks>Until 0.14 this only queued the action, as <see cref="BeginInvoke"/>
    /// does now.</remarks>
    public void Invoke(Action action) => Dispatcher.Invoke(action);

    /// <summary>Run a function on the UI thread and wait for its result.</summary>
    public T Invoke<T>(Func<T> function) => Dispatcher.Invoke(function);

    /// <summary>Queue an action to the UI thread and return at once.</summary>
    public void BeginInvoke(Action action) => Dispatcher.BeginInvoke(action);

    public UIElement? FindByName(string name) => NameScope.Find(name);
    public T? FindByName<T>(string name) where T : UIElement => NameScope.Find<T>(name);

    // ===== Attaching a subtree to the form =====

    /// <summary>Show an overlay: attach the subtree (theme, font, names,
    /// OnAttached) and put it on top of the content. The only way into
    /// the overlay list — flyouts, toasts, tooltips, menus and the inspector
    /// all go through it.</summary>
    /// <remarks>Attaching happens before measuring: the theme sets fonts
    /// and padding, and the overlay's position is computed from DesiredSize.</remarks>
    private void AttachOverlay(UIElement content)
    {
        if (_overlays.Contains(content)) return;

        ZfContract.Require(
            content.Owner is null,
            "The overlay already belongs to a form. One element cannot be " +
            "an overlay of two forms at once: Owner would be overwritten, " +
            "and the first form would lose its link to it.");

        content.Owner = this;
        AttachTree(content);

        _overlays.Add(content);
        RaiseOverlaysChanged();
    }

    /// <summary>Remove an overlay and detach the subtree. Without detaching,
    /// a closed overlay stays in the NameScope, its animations keep spinning,
    /// and the input and focus dispatchers keep references to it.</summary>
    private bool DetachOverlay(UIElement content)
    {
        if (!_overlays.Remove(content)) return false;

        DetachTree(content);
        content.Owner = null;

        RaiseOverlaysChanged();
        return true;
    }

    internal void AttachTree(UIElement root)
    {
        Walk(root, element =>
        {
            NameScope.Register(element);

            // the theme is applied before the first layout, so that sizes
            // are computed with the right fonts and padding from the start
            App.Theme.Apply(element);

            element.RaiseAttached();
        });
    }

    internal void DetachTree(UIElement root)
    {
        Walk(root, element =>
        {
            NameScope.Unregister(element);
            element.RaiseDetached();
        });

        // otherwise the discarded tree stays alive through the dispatcher's references
        if (_hoveredElement is not null && IsInTree(root, _hoveredElement))
            _hoveredElement = null;

        // a contact whose target left the tree must learn about the cancellation.
        // Previously the fields here were simply nulled, and an element that was
        // later returned to the tree stayed pressed
        foreach (PointerContact contact in _contacts.Values.ToArray())
        {
            bool affected =
                (contact.Pressed is not null && IsInTree(root, contact.Pressed)) ||
                (contact.Capture is not null && IsInTree(root, contact.Capture));

            if (affected)
                CancelContact(contact, PointerCancelReason.Detached);
        }

        // the remembered press of the right and the middle button is a reference
        // into the tree too, and a click must not come to an element that left it
        if (_rightPressed is not null && IsInTree(root, _rightPressed))
            _rightPressed = null;

        if (_middlePressed is not null && IsInTree(root, _middlePressed))
            _middlePressed = null;

        if (_dropTarget is not null && IsInTree(root, _dropTarget))
        {
            _dropTarget = null;
            _dropEffect = DragDropEffect.None;
        }

        if (_focusDispatcher.FocusedElement is { } focused && IsInTree(root, focused))
            _focusDispatcher.ClearFocus();

        // like the other references above — only if the element being reset is
        // actually in the subtree being detached. An unconditional reset became
        // noticeable once hiding the tooltip started going through DetachTree:
        // that happens on every mouse move and would wipe out the inspector's
        // selection along with it
        if (InspectedElement is not null && IsInTree(root, InspectedElement))
            InspectedElement = null;

        if (_toolTipOwner is not null && IsInTree(root, _toolTipOwner))
            _toolTipOwner = null;

        CancelAnimationsIn(root);
    }

    private static bool IsInTree(UIElement root, UIElement candidate)
    {
        for (UIElement? current = candidate; current is not null; current = current.Parent)
            if (ReferenceEquals(current, root))
                return true;

        return false;
    }

    private static void Walk(UIElement root, Action<UIElement> action)
    {
        action(root);

        switch (root)
        {
            case WrapControl wrap when wrap.Child is not null:
                Walk(wrap.Child, action);
                break;

            case PanelControl panel:
                foreach (var child in panel.Children)
                    Walk(child, action);
                break;
        }
    }

    private int _layoutDepth;

    /// <summary>The layout is stale and waits for the next frame.</summary>
    private bool _layoutDirty;

    /// <summary>How many passes are allowed per call.</summary>
    /// <remarks>
    /// Two are enough for the regular case: the first creates the containers,
    /// the second computes the final size from them. The margin up to four is
    /// for nested virtualizing panels, a tree inside a scrollable panel.
    /// </remarks>
    private const int MaxLayoutPasses = 4;

    internal void PerformLayout()
    {
        // Invalidate during layout is legitimate: a virtualizing panel creates
        // containers right in measuring, because until then it is unknown how
        // many rows fit, and creating a container changes Children. Re-entering
        // the pass is not allowed, and the request is already recorded
        // in _layoutDirty — the pass loop below will fulfil it
        if (_layoutDepth > 0) return;

        _layoutDepth++;

        try
        {
            for (int pass = 0; ; pass++)
            {
                // styles that depend on places among siblings, deferred from
                // changes of children: before measuring, they change sizes
                FlushStructureRestyles();

                _layoutDirty = false;

                LayoutPass();

                if (!_layoutDirty) break;

                if (pass + 1 >= MaxLayoutPasses)
                {
                    // the layout doesn't converge: someone asks for a new pass
                    // every time. Silently spinning on that would hang the frame
                    ZfContract.Fail(
                        $"Layout did not converge in {MaxLayoutPasses} passes: " +
                        "something keeps calling Invalidate from MeasureOverride " +
                        "or ArrangeOverride. Change the geometry there directly " +
                        "instead of requesting a new pass.");

                    break;
                }
            }
        }
        finally
        {
            _layoutDepth--;
            _layoutDirty = false;
        }
    }

    /// <summary>Perform the deferred layout if it is needed. Called before
    /// drawing and before hit testing — that is, everywhere the geometry
    /// must be fresh.</summary>
    internal void EnsureLayout()
    {
        if (!_layoutDirty && !HasPendingRestyles) return;

        PerformLayout();
    }

    /// <summary>Complete the deferred layout immediately. Needed where code reads
    /// the geometry right after a change: a position, a size, the set of
    /// containers of a virtualizing panel.</summary>
    public void UpdateLayout() => EnsureLayout();

    private void LayoutPass()
    {
        if (Content is not null)
        {
            Content.Measure(ClientSize);
            Content.Arrange(new Rectangle(Point.Empty, ClientSize));
        }

        foreach (var overlay in _overlays)
        {
            overlay.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
            overlay.Arrange(new Rectangle(overlay.Position, overlay.DesiredSize));
        }
    }

    private Rectangle? _dirtyRegion;

    internal Rectangle? TakeDirtyRegion()
    {
        Rectangle? region = _dirtyRegion;
        _dirtyRegion = null;
        return region;
    }

    internal void InvalidateRect(Rectangle bounds)
    {
        _dirtyRegion = _dirtyRegion is { } existing ? existing.Union(bounds) : bounds;
        PlatformWindow?.Invalidate(bounds);
    }

    /// <summary>Redraw the whole client area without a layout pass.</summary>
    internal void InvalidateVisual()
    {
        _dirtyRegion = new Rectangle(Point.Empty, ClientSize);
        PlatformWindow?.Invalidate(null);
    }

    /// <summary>Mark the layout stale and ask for a frame.</summary>
    /// <remarks>
    /// This used to run a full Measure/Arrange pass over the whole tree right here.
    /// Then setting five properties meant five layouts, and scrolling with a finger
    /// meant a layout per move event, and those arrive more often than frames.
    /// Now there is one pass, and it runs before the frame: before drawing and
    /// before hit testing the geometry will be fresh anyway, and nobody sees
    /// the intermediate states. Code that needs the geometry right away uses UpdateLayout.
    /// </remarks>
    internal void Invalidate()
    {
        _layoutDirty = true;

        // an element's visibility is a layout property, so its change always
        // passes through here. The clock reconsiders whether frames are needed:
        // an animation on a page shown again must come alive, and one on a hidden
        // page must stop waking the window
        ReviewFrames();

        // a full redraw: accumulate the whole client area
        _dirtyRegion = new Rectangle(Point.Empty, ClientSize);
        PlatformWindow?.Invalidate(null);
    }

    // ===== Flyout API =====

    public void ShowFlyout(UIElement anchor, UIElement content, FlyoutPlacement placement = FlyoutPlacement.Bottom)
    {
        AttachOverlay(content);

        content.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        Point anchorPos = anchor.GetAbsolutePosition();
        Size anchorSize = anchor.ActualSize;
        Size contentSize = content.DesiredSize;

        content.Position = placement switch
        {
            FlyoutPlacement.Bottom => new Point(anchorPos.X, anchorPos.Y + anchorSize.Height),
            FlyoutPlacement.Top => new Point(anchorPos.X, anchorPos.Y - contentSize.Height),
            FlyoutPlacement.Right => new Point(anchorPos.X + anchorSize.Width, anchorPos.Y),
            FlyoutPlacement.Left => new Point(anchorPos.X - contentSize.Width, anchorPos.Y),
            _ => anchorPos,
        };

        _flyouts.Add(content);

        Invalidate();
    }

    public void CloseFlyout(UIElement content)
    {
        if (!DetachOverlay(content)) return;

        _flyouts.Remove(content);

        FlyoutClosed?.Invoke(this, content);
        Invalidate();
    }

    public void CloseAllFlyouts()
    {
        if (_flyouts.Count == 0) return;

        // a copy: event handlers may open a new flyout,
        // and the collection would change during the walk
        UIElement[] closing = [.. _flyouts];

        foreach (UIElement flyout in closing)
            DetachOverlay(flyout);

        _flyouts.Clear();

        foreach (UIElement flyout in closing)
            FlyoutClosed?.Invoke(this, flyout);

        Invalidate();
    }

    /// <summary>Put an element on top of the content. Unlike ShowFlyout, it is not
    /// closed by a click outside and takes no part in the popup logic.</summary>
    public void AddOverlay(UIElement content)
    {
        AttachOverlay(content);
        Invalidate();
    }

    public void RemoveOverlay(UIElement content)
    {
        if (!DetachOverlay(content)) return;

        Invalidate();
    }

    // ==== Dialog ====

    private bool _isClosed;

    /// <summary>The theme at the moment the form stopped listening to theme changes —
    /// that is, at window close. Null while the form is subscribed.</summary>
    private Theme? _themeAtUnsubscribe;

    /// <summary>Localization.Version at window close; −1 while the form is subscribed.</summary>
    private int _localizationVersionAtUnsubscribe = -1;

    /// <summary>The window title as a key, if it was given as one.</summary>
    private Func<string>? _localizedTitle;

    /// <summary>The platform destroyed the window. The only point where waiting
    /// for a dialog completes: there is no need to tell apart where the closing
    /// came from, but it must not be missed — ShowDialogAsync would hang forever.</summary>
    internal void OnWindowClosed()
    {
        // DestroyWindow in Win32 sends both WM_DESTROY and WM_NCDESTROY;
        // on X11 Close() may come both from us and from WM_DELETE_WINDOW
        if (_isClosed) return;
        _isClosed = true;

        s_openForms.Remove(this);

        // App.ThemeChanged is static: while the form stays subscribed, the event
        // holds it together with its whole tree. A closed form nobody disposes —
        // every MessageBox, every InputBox — was never collected. The subscription
        // is dropped here and renewed if the form is shown again
        App.ThemeChanged -= OnThemeChanged;
        _themeAtUnsubscribe = App.Theme;

        // the text scale's event is static too, and would hold the form the same way
        DropTextScaleSubscription();

        // static like the theme's event, and it would hold the form the same way
        Localization.Changed -= OnLocalizationChanged;
        _localizationVersionAtUnsubscribe = Localization.Version;

        _clock?.Stop();

        _dialogClosed?.TrySetResult();

        // a destroyed window runs nothing: Invoke goes through the windows left
        if (PlatformWindow is { } window)
            _dispatcher?.Detach(window);

        _isActive = false;
        OnClosed(EventArgs.Empty);
    }

    /// <summary>Show the dialog and wait for the result. Requires a platform
    /// with a nested loop; where there is none, use ShowDialogAsync.</summary>
    public DialogResult<T> ShowDialog<T>(Form owner)
    {
        IPlatform platform = owner.Platform
            ?? throw new InvalidOperationException("The dialog owner is not bound to a platform yet.");

        if (platform is not INestedLoopSupport loop)
            throw new NotSupportedException(
                $"Platform {platform.GetType().Name} does not support a nested loop. " +
                "Use ShowDialogAsync.");

        BeginDialog(owner, platform);

        try
        {
            loop.RunNestedLoop(PlatformWindow!);
        }
        finally
        {
            EndDialog(owner);
        }

        return Result<T>();
    }

    /// <summary>Show the dialog without blocking the calling code. Works
    /// on any platform, including those without a nested loop.</summary>
    public async Task<DialogResult<T>> ShowDialogAsync<T>(Form owner)
    {
        IPlatform platform = owner.Platform
            ?? throw new InvalidOperationException("The dialog owner is not bound to a platform yet.");

        BeginDialog(owner, platform);

        try
        {
            await _dialogClosed!.Task;
        }
        finally
        {
            EndDialog(owner);
        }

        return Result<T>();
    }

    private TaskCompletionSource? _dialogClosed;

    private void BeginDialog(Form owner, IPlatform platform)
    {
        IsDialog = true;
        Platform = platform;

        _dialogAccepted = false;
        _dialogValue = null;

        // completion is awaited both synchronously and asynchronously: the source
        // is one — the window closing, wherever it came from
        _dialogClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        platform.CreateWindow(this);

        // modality is a disabled owner, not a nested loop:
        // the first is needed everywhere, the second only on desktop platforms
        owner.PlatformWindow?.SetEnabled(false);

        Show();
    }

    private void EndDialog(Form owner)
    {
        if (owner.PlatformWindow is { } ownerWindow)
        {
            ownerWindow.SetEnabled(true);
            ownerWindow.Activate();
        }

        _dialogClosed = null;
    }

    internal DialogResult<T> Result<T>() =>
        _dialogAccepted && _dialogValue is T typed
            ? new DialogResult<T>(true, typed)
            : DialogResult<T>.Cancelled();

    /// <summary>Close the dialog with a result.</summary>
    public void Accept(object? value = null) => CloseWithResult(accepted: true, value);

    public void Cancel() => CloseWithResult(accepted: false, value: null);

    /// <summary>Close with a result. The result is set before <see cref="Closing"/>,
    /// so a handler sees what the dialog is closing with; if the handler keeps the
    /// dialog open, the result is taken back — the dialog is not closed, and a later
    /// close from the title bar must not return what was refused.</summary>
    private void CloseWithResult(bool accepted, object? value)
    {
        bool previousAccepted = _dialogAccepted;
        object? previousValue = _dialogValue;

        _dialogAccepted = accepted;
        _dialogValue = value;

        if (RequestClose(CloseReason.Code))
        {
            PlatformWindow?.Close();
            return;
        }

        _dialogAccepted = previousAccepted;
        _dialogValue = previousValue;
    }

    // ==== Toast =====

    public void ShowToast(string message, int durationMs = 3000, ToastPosition position = ToastPosition.BottomRight)
    {
        var toast = new Border
        {
            BorderColor = new Color(255, 60, 60, 60),
            BorderWidth = 1,
            Background = new Color(235, 45, 45, 45),
            Padding = new Thickness(14, 10),
            IsHitTestVisible = false,
            Child = new Label { Text = message, TextColor = Colors.White },
        };

        // Owner is set by AttachOverlay itself; assigning it here first made
        // its "not owned yet" contract fail on every toast
        AttachOverlay(toast);

        toast.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        _toasts.Add(toast);
        ArrangeToasts(position);
        Invalidate();

        Schedule(durationMs, () =>
        {
            DetachOverlay(toast);
            _toasts.Remove(toast);
            ArrangeToasts(position);   // the remaining ones move up into the freed space
            Invalidate();
        });
    }

    private void ArrangeToasts(ToastPosition position)
    {
        const float margin = 16f;
        const float gap = 8f;

        bool fromTop = position is ToastPosition.TopRight or ToastPosition.TopCenter;
        bool centered = position is ToastPosition.TopCenter or ToastPosition.BottomCenter;

        float offset = margin;

        // at the bottom new ones appear lower and old ones move up, so we go from the end
        IEnumerable<UIElement> order = fromTop ? _toasts : Enumerable.Reverse(_toasts);

        foreach (var toast in order)
        {
            Size size = toast.DesiredSize;

            float x = centered
                ? (ClientSize.Width - size.Width) / 2f
                : ClientSize.Width - size.Width - margin;

            float y = fromTop ? offset : ClientSize.Height - size.Height - offset;

            toast.Position = new Point(x, y);
            offset += size.Height + gap;
        }
    }

    // ===== ToolTip =====

    private void ScheduleToolTip(UIElement? target)
    {
        HideToolTip();

        _toolTipOwner = target is not null && !string.IsNullOrEmpty(target.GetToolTip(_lastPointerPosition)) ? target : null;

        if (_toolTipOwner is not null)
            _toolTipWake = Schedule(ToolTipDelay, ShowToolTipCore);
    }

    private void ShowToolTipCore()
    {
        if (_toolTipOwner?.GetToolTip(_lastPointerPosition) is not { Length: > 0 } text)
            return;

        var tip = new Border
        {
            BorderColor = Colors.Black,
            BorderWidth = 1,
            Background = new Color(240, 250, 250, 210),
            Padding = new Thickness(6, 3),
            IsHitTestVisible = false,
            Child = new Label
            {
                Text = text,
                TextColor = Colors.Black,
                IsHitTestVisible = false,
            },
        };

        AttachOverlay(tip);

        tip.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        // slightly below and to the right of the cursor, as system tooltips do
        float x = _lastPointerPosition.X + 12;
        float y = _lastPointerPosition.Y + 20;

        // don't let it go beyond the client area
        if (x + tip.DesiredSize.Width > ClientSize.Width)
            x = Math.Max(0, ClientSize.Width - tip.DesiredSize.Width);

        if (y + tip.DesiredSize.Height > ClientSize.Height)
            y = Math.Max(0, _lastPointerPosition.Y - tip.DesiredSize.Height - 4);

        tip.Position = new Point(x, y);

        _activeToolTip = tip;

        Invalidate();
    }

    /// <summary>The hovered element's tooltip changed with the part under the pointer.</summary>
    internal void RefreshToolTip(UIElement element)
    {
        if (!ReferenceEquals(element, _hoveredElement)) return;

        bool shown = _activeToolTip is not null;

        HideToolTip();

        _toolTipOwner = !string.IsNullOrEmpty(element.GetToolTip(_lastPointerPosition)) ? element : null;

        if (_toolTipOwner is null) return;

        // moving between the parts of one control, the tip follows without
        // the delay again — it was asked for already
        if (shown) ShowToolTipCore();
        else _toolTipWake = Schedule(ToolTipDelay, ShowToolTipCore);
    }

    private void HideToolTip()
    {
        _toolTipWake?.Dispose();
        _toolTipWake = null;

        if (_activeToolTip is not null)
        {
            DetachOverlay(_activeToolTip);
            _activeToolTip = null;
            Invalidate();
        }
    }

    // ===== Keyboard =====

    private PropertyGrid? _inspectorGrid;

    private void ToggleInspector()
    {
        IsInspectorEnabled = !IsInspectorEnabled;

        if (IsInspectorEnabled)
        {
            // the accessibility audit: its outlines go under the grid
            ShowAudit();

            _inspectorGrid = new PropertyGrid
            {
                Size = new Size(InspectorWidth, ClientSize.Height),
                Position = new Point(Math.Max(0, ClientSize.Width - InspectorWidth), 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            AttachOverlay(_inspectorGrid);
        }
        else if (_inspectorGrid is not null)
        {
            DetachOverlay(_inspectorGrid);
            _inspectorGrid = null;

            HideAudit();
        }

        Invalidate();
    }

    internal void OnTextInput(char c)
    {
        var args = new TextInputEventArgs(c);

        // the form sees the character first and may keep it from the field
        OnTextInput(args);
        if (args.Handled) return;

        _focusDispatcher.FocusedElement?.RaiseTextInput(c);
    }

    // ===== Input dispatching =====

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

    /// <summary>The window lost focus: we won't learn about key releases
    /// anymore, so we assume nothing is pressed.</summary>
    internal void OnWindowFocusLost() => Keyboard.Reset();


    /// <summary>The window became the active one, or stopped being it. Called by
    /// the platform; Deactivated comes together with the focus loss.</summary>
    internal void OnWindowActivated(bool active)
    {
        if (active == _isActive) return;

        _isActive = active;

        if (active) OnActivated(EventArgs.Empty);
        else OnDeactivated(EventArgs.Empty);
    }

    private bool _isActive;

    /// <summary>Whether the window is the active one: the one the keyboard types into.</summary>
    public bool IsActive => _isActive;

    // called by the platform when the user changed the state themselves —
    // without calling back into SetWindowState, otherwise we'd get a loop
    internal void SetWindowStateFromPlatform(WindowState state)
    {
        if (_windowState == state) return;
        _windowState = state;
        OnWindowStateChanged(EventArgs.Empty);
    }

    internal void OnContextMenu(Point point)
    {
        UIElement? hit = HitTestAll(point);

        // the menu is searched up the tree: if the button itself has none,
        // ask the panel, then the form
        for (UIElement? current = hit; current is not null; current = current.Parent)
        {
            if (current.ContextMenu is { Count: > 0 } items)
            {
                ShowContextMenu(items, point);
                return;
            }
        }
    }

    public void ShowContextMenu(List<MenuItem> items, Point position)
    {
        CloseAllFlyouts();

        var menu = new MenuList { Items = items };
        menu.ItemInvoked += (_, _) => CloseAllFlyouts();

        AttachOverlay(menu);

        menu.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        float x = Math.Min(position.X, Math.Max(0, ClientSize.Width - menu.DesiredSize.Width));
        float y = Math.Min(position.Y, Math.Max(0, ClientSize.Height - menu.DesiredSize.Height));

        menu.Position = new Point(x, y);

        _flyouts.Add(menu);   // closes on a click outside — as a menu should

        Invalidate();
    }

    private DragDropEffect _dropEffect;

    /// <summary>A drag entered the window. The source shows the returned
    /// effect with its cursor.</summary>
    internal DragDropEffect OnDragEnterWindow(DragDropData data, Point point, KeyModifiers modifiers) =>
        UpdateDropTarget(data, point, modifiers);

    internal DragDropEffect OnDragOverWindow(DragDropData data, Point point, KeyModifiers modifiers) =>
        UpdateDropTarget(data, point, modifiers);

    /// <summary>The drag left the window or was cancelled.</summary>
    internal void OnDragLeaveWindow()
    {
        _dropTarget?.RaiseDragLeave();
        _dropTarget = null;
    }

    internal DragDropEffect OnDropWindow(DragDropData data, Point point, KeyModifiers modifiers)
    {
        // the target is recomputed: a drop may come without a preceding over —
        // for example, if the source sent only enter and then drop right away
        DragDropEffect effect = UpdateDropTarget(data, point, modifiers);

        UIElement? target = _dropTarget;
        _dropTarget = null;

        if (target is null || effect == DragDropEffect.None)
        {
            target?.RaiseDragLeave();

            return DragDropEffect.None;
        }

        var args = new DragDropEventArgs(data, point, modifiers) { Effect = effect };

        target.RaiseDrop(args);

        // DragLeave after a drop is mandatory: the target highlighted itself
        // on enter, and it has no other place to remove the highlight
        target.RaiseDragLeave();

        return args.Effect;
    }

    /// <summary>Find the drop target under the cursor, send enter and leave when
    /// it changes, and ask for the effect.</summary>
    private DragDropEffect UpdateDropTarget(DragDropData data, Point point, KeyModifiers modifiers)
    {
        _lastPointerPosition = point;

        UIElement? target = FindDropTarget(HitTestAll(point));

        if (!ReferenceEquals(target, _dropTarget))
        {
            _dropTarget?.RaiseDragLeave();
            _dropTarget = target;

            if (target is not null)
            {
                var enterArgs = new DragDropEventArgs(data, point, modifiers);

                target.RaiseDragEnter(enterArgs);

                // the effect set on enter is the initial value for the following
                // over events: the target doesn't need to repeat it
                _dropEffect = enterArgs.Effect;
            }
        }

        if (_dropTarget is null) return DragDropEffect.None;

        var args = new DragDropEventArgs(data, point, modifiers) { Effect = _dropEffect };

        _dropTarget.RaiseDragOver(args);
        _dropEffect = args.Effect;

        return _dropEffect;
    }

    /// <summary>The nearest element with AllowDrop, starting from the hit one.
    /// That way highlighting can be enabled on a panel without marking up every row.</summary>
    private static UIElement? FindDropTarget(UIElement? hit)
    {
        for (UIElement? current = hit; current is not null; current = current.Parent)
            if (current is { AllowDrop: true, IsEnabled: true })
                return current;

        return null;
    }

    public void Dispose()
    {
        App.ThemeChanged -= OnThemeChanged;
        App.TextScaleChanged -= OnTextScaleChanged;

        Localization.Changed -= OnLocalizationChanged;

        _toolTipWake?.Dispose();
        _clock?.Dispose();

        _focusDispatcher.FocusChanged -= OnFocusChangedForKeyboard;
        _focusDispatcher.FocusChanged -= OnFocusChangedForStyles;
        _focusedForStyles = null;

        // a contact holds Chain — the whole path from the root to the pressed element.
        // The form may have been closed in the middle of a drag, and then
        // the release will never come
        _contacts.Clear();
        _primaryContactId = null;
        _platformCaptureCount = 0;

        _rightPressed = null;
        _middlePressed = null;
    }
}