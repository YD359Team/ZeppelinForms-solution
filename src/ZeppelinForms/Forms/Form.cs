using System.Diagnostics.CodeAnalysis;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Dispatchers;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Dispatchers;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
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

    /// <summary>The form got a platform window: shown, or shown again.</summary>
    internal event EventHandler? PlatformWindowAttached;

    internal IPlatformWindow? PlatformWindow
    {
        get;
        set
        {
            field = value;

            if (value is null) return;

            // a new window is a new life: the form may have been closed and shown again
            _isClosed = false;

            // what needs the window itself — a navigation stack the browser's history
            // follows — was attached before the form had one
            PlatformWindowAttached?.Invoke(this, EventArgs.Empty);

            // the thread that creates the window is the form's UI thread, and
            // Invoke from other threads goes through the window's queue
            _dispatcher = Dispatcher.Attach(value).ForWindow(value);

            // the theme, text scale and language subscriptions are renewed with every
            // window: they are dropped at close (see OnWindowClosed), and a form shown
            // again must follow them again. A theme, scale or language switched while
            // the form was closed is applied now. On the first show the form follows
            // them already, from the constructor, and nothing is doubled
            FollowApp();

            if (!s_openForms.Contains(this))
                s_openForms.Add(this);

            // the window has just appeared: if drag-and-drop was enabled
            // before it was shown, the platform doesn't know about it yet
            if (AllowDrop)
                value.SetDragDropEnabled(true);

            // the same with animations: those added before the window was created
            // sit in the clock, but there was nobody to deliver frames — ask for them here
            ReviewFrames();

            // and with the taskbar button: a progress or a badge set before showing
            _taskbar?.ApplyAll();
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

    /// <summary>The form's name; a form without a <see cref="Title"/> is captioned with it,
    /// as a form the WinForms designer adds is captioned "Form1".</summary>
    public string Name
    {
        get;
        set
        {
            field = value ?? string.Empty;

            if (Title is null) DesktopWindow?.SetTitle(ShownTitle);
        }
    } = string.Empty;

    /// <summary>The window's caption. An open window shows a change at once.</summary>
    public string? Title
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            DesktopWindow?.SetTitle(ShownTitle);
        }
    }

    /// <summary>The caption the window shows: the title, or — never set — the form's
    /// <see cref="Name"/>.</summary>
    internal string? ShownTitle => Title ?? (Name.Length > 0 ? Name : null);

    /// <summary>The window's icon: the title bar's, the taskbar button's. Null — the
    /// application's, <see cref="App.Icon"/>. Changing it shows at once.</summary>
    public Icon? Icon
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;

            // an icon set after the window opened went nowhere: the platform read it
            // only when making the window
            DesktopWindow?.SetIcon(EffectiveIcon);
        }
    }

    /// <summary>The icon the window shows: its own, or the application's.</summary>
    internal Icon? EffectiveIcon => Icon ?? App.DefaultIcon;

    /// <summary>Where the window's top-left corner is on the screen, in screen pixels —
    /// the units <see cref="Displays"/> reports the screens in. On Linux it is the corner
    /// of the client area: the frame is the window manager's. Used when the window opens
    /// with <see cref="WindowStartupLocation.Manual"/>; afterwards it follows the window
    /// — a move by the user updates it, and setting it moves an open window.</summary>
    /// <remarks>Screen pixels rather than logical units: with screens of different
    /// scales a logical point of the desktop has no single meaning.</remarks>
    public Point Position
    {
        get => _position;
        set
        {
            if (_position == value) return;

            // asked of the window before the value is taken: a refused move leaves
            // the form where it was
            if (DesktopWindow is { SupportsPositioning: false })
                throw new PlatformNotSupportedException(
                    "This platform doesn't let a window place itself: on Wayland the compositor " +
                    "decides where windows go. Check App.Capabilities for WindowPositioning first.");

            _position = value;
            ApplyBounds();
            OnLocationChanged(EventArgs.Empty);
        }
    }

    private Point _position;

    /// <summary>The size of the window, frame included. <see cref="DefaultSize"/>
    /// until set: a form nobody sized opens as a usable window, not as a speck.</summary>
    /// <remarks>In logical units. It follows the window as <see cref="Position"/> does:
    /// the user's resize updates it, and setting it resizes an open window — until 0.17
    /// it was read once, when the window was made.</remarks>
    public Size Size
    {
        get => _size ?? DefaultSize;
        set
        {
            if (_size == value) return;

            _size = value;
            ApplyBounds();
            OnSizeChanged(EventArgs.Empty);
        }
    }

    private Size? _size;

    /// <summary>The size of a form whose <see cref="Size"/> was not set, as WinForms
    /// has it. A form of the application overrides it for its own default.</summary>
    protected virtual Size DefaultSize => new(800, 600);

    /// <summary>Whether <see cref="Size"/> was set: the browser and Android size an
    /// unsized dialog to their own surface instead.</summary>
    internal bool IsSizeSet => _size is not null;

    /// <summary>The form is shown as a modal dialog.</summary>
    public bool IsDialog { get; private set; }

    /// <summary>The font of every element of the form that sets none of its own and
    /// inherits none from an ancestor. Until set — <see cref="Font.Default"/>, the
    /// theme's font, read live: a theme switch reaches a form that set no font.
    /// Null resets it back to that.</summary>
    /// <remarks>
    /// The property used to be a bare field: it read null on a form nobody gave a
    /// font, though its elements drew with Font.Default all along, and setting it on
    /// an open form changed nothing on screen until something else asked for a layout.
    /// </remarks>
    [AllowNull]
    public Font Font
    {
        get => _font ?? Font.Default;
        set
        {
            if (Equals(_font, value)) return;

            _font = value;

            // no element's own property changed, only what the elements without a
            // font of their own inherit — as with the text scale, the whole tree
            // is measured again
            InvalidateMeasureTree();
            Invalidate();
        }
    }

    private Font? _font;

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

            Size previous = field;
            field = value;

            // before the layout the platform runs next: an overlay that covers
            // the window — an image viewer — takes the new size in the same pass
            OnClientSizeChanged(EventArgs.Empty);

            // shorter: on a phone the keyboard came up, maybe over the field typed in
            KeepFocusedFieldInView(previous, value);
        }
    }

    /// <summary>The client area changed its size: the window was resized, maximized,
    /// or the screen turned.</summary>
    public event EventHandler? ClientSizeChanged;

    public FlowDirection? FlowDirection { get; set; }
    private readonly FocusDispatcher _focusDispatcher = new();

    internal IPlatform? Platform { get; set; }

    public Form()
    {
        FollowApp();
        _focusDispatcher.FocusChanged += OnFocusChangedForKeyboard;
        _focusDispatcher.FocusChanged += OnFocusChangedForAccessibility;
        _focusDispatcher.FocusChanged += OnFocusChangedForStyles;
        _focusDispatcher.FocusChanged += OnFocusChangedForToolTip;
    }

    /// <summary>Give focus to the first text field when the form is shown.
    /// Without it the keyboard does nothing until the user clicks a field,
    /// although there is only one place the typing could go.</summary>
    public bool FocusOnShow { get; set; } = true;

    public void Show()
    {
        // a window shown already: one on the screen stays as it is, a hidden one comes
        // back as it was — Load is of its first showing, Shown of every one, see Hide
        if (TryShowAgain())
        {
            OnShown(EventArgs.Empty);
            return;
        }

        // once, before the window is first seen: the place to fill the form
        // with data, as WinForms' Load is
        if (!_loaded)
        {
            _loaded = true;
            OnLoad(EventArgs.Empty);
        }

        DesktopWindow?.SetOpacity(_opacity);
        DesktopWindow?.SetTitle(ShownTitle);

        // before the window is seen: a dark caption must not flash white first
        ApplyTitleBar();

        PlatformWindow?.Show();
        OnFirstShown();

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
    /// <remarks>Public since 0.16: a control of another package that opens a pane or
    /// a picker needs it to give the focus back, as the built-in ones do. Null — no
    /// element of this form has the focus.</remarks>
    public UIElement? FocusedElement => _focusDispatcher.FocusedElement;

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

    private bool _isClosed;

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

        // the native controls go before the window's children are destroyed with it:
        // each host stops what draws into its control first
        ReleaseNativeControls();

        // the forms this one owns close with it, and this one leaves its owner,
        // see Form.Owner
        CloseOwnedForms();
        SetOwner(null);

        // App.ThemeChanged is static: while the form stays subscribed, the event
        // holds it together with its whole tree. A closed form nobody disposes —
        // every MessageBox, every InputBox — was never collected. The subscription
        // is dropped here and renewed if the form is shown again; the text scale's
        // and the language's events are static too, and go with it
        StopFollowingApp();

        _clock?.Stop();
        _clock?.Dispose();

        _flyouts.Clear();
        _overlays.Clear();
        _toasts.Clear();

        _dialogClosed?.TrySetResult();

        // a destroyed window runs nothing: Invoke goes through the windows left
        if (PlatformWindow is { } window)
            _dispatcher?.Detach(window);

        _isActive = false;
        // a closed window is off the screen, whether it was shown or hidden
        _shownWindow = null;
        SetVisibleState(false);
        OnClosed(EventArgs.Empty);
    }

    /// <summary>The window lost focus: we won't learn about key releases
    /// anymore, so we assume nothing is pressed.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Each platform calls it on the form of the window that lost the focus.")]
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

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopFollowingApp();

        // the focus dispatcher's events are not unsubscribed: the dispatcher is the
        // form's own and never leaves it, so its handlers hold nothing the form
        // doesn't. Until 0.20 three of the four were dropped here, two of them twice

        _toolTipWake?.Dispose();
        _clock?.Dispose();

        _focusedForStyles = null;

        // a contact holds Chain — the whole path from the root to the pressed element.
        // The form may have been closed in the middle of a drag, and then
        // the release will never come
        _contacts.Clear();
        _primaryContactId = null;
        _platformCaptureCount = 0;

        _rightPressed = null;
        _middlePressed = null;

        // a derived form with a finalizer of its own needn't run it after this
        GC.SuppressFinalize(this);
    }
}