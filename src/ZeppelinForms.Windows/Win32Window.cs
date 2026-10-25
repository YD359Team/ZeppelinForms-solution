using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Windows.Rendering;
using static ZeppelinForms.Windows.NativeMethods;

namespace ZeppelinForms.Windows;

internal sealed partial class Win32Window : IPlatformWindow, IDesktopWindow
{
    private const string ClassName = "ZeppelinForms.Window";

    private static readonly NativeMethods.WndProc s_wndProc = WndProc;

    private readonly WindowsPlatform _platform;
    private readonly Form _form;

    private GCHandle _selfHandle;
    private nint _handle;

    private nint _largeIcon;
    private nint _smallIcon;

    private IWin32Surface? _surface;

    private bool _trackingMouse;

    /// <summary>We are releasing the capture ourselves. ReleaseCapture sends
    /// WM_CAPTURECHANGED synchronously, and without this flag our own release
    /// looked exactly like the capture being taken away.</summary>
    private bool _releasingCapture;

    private float _scale = 1f;
    public float Scale => _scale;

    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _invokeQueue = new();

    public Win32Window(WindowsPlatform platform, Form form)
    {
        _platform = platform;
        _form = form;

        // the driver needs a handle that doesn't exist yet at this point —
        // so not the value but a way to get it
        Frames = new Win32FrameDriver(() => _handle);
    }

    public IFrameDriver Frames { get; }

    public nint Handle => _handle;

    public bool SupportsTransparency => true;

    public void SetEnabled(bool enabled) => NativeMethods.EnableWindow(_handle, enabled);

    public void Activate() => NativeMethods.SetActiveWindow(_handle);

    public void Create()
    {
        if (_handle != 0)
            return;

        RegisterWindowClass();

        // the size the window opens at keeps within the form's limits: the first
        // WM_GETMINMAXINFO comes before WM_NCCREATE, when the window doesn't know its
        // form yet, and the system can't hold CreateWindowEx's size to them
        Size initial = _form.ClampToSizeLimits(_form.Size);

        int width = (int)initial.Width;
        int height = (int)initial.Height;

        int x, y;

        switch (_form.WindowStartupLocation)
        {
            // a first guess on the primary screen: the scale is known only once the
            // window exists, and the final place is set then, see PlaceAfterCreate.
            // Until 0.16 the window didn't know its owner — Form.ShowDialog didn't pass
            // it to the platform — and CenterOwner centered on the screen; now the
            // owner is Form.Owner, and PlaceAfterCreate centers on it
            case WindowStartupLocation.CenterScreen:
            case WindowStartupLocation.CenterOwner:
                Rectangle area = Displays.Primary.WorkingArea;

                x = (int)(area.X + (area.Width - width * _scale) / 2);
                y = (int)(area.Y + (area.Height - height * _scale) / 2);
                break;

            case WindowStartupLocation.Manual:
                x = (int)_form.Position.X;
                y = (int)_form.Position.Y;
                break;

            default: // Default — leave the choice to the system (window cascade)
                x = NativeConstants.CW_USEDEFAULT;
                y = NativeConstants.CW_USEDEFAULT;
                break;
        }

        try
        {
            _selfHandle = GCHandle.Alloc(this);

            // the frame, the title bar buttons and the taskbar button come from
            // the form: FormBorderStyle, ControlBox, ShowInTaskbar, CanMinimize,
            // CanMaximize, CanResize (see Win32Window.Chrome)
            (uint style, uint exStyle) = ChromeStyles();

            _handle = NativeMethods.CreateWindowEx(
                exStyle, ClassName, _form.Title ?? string.Empty,
                style,
                x, y, width, height, ChromeOwner(), 0,
                NativeMethods.GetModuleHandle(null),
                GCHandle.ToIntPtr(_selfHandle));
        }
        catch
        {
            _selfHandle.Free();
            throw;
        }

        if (_handle == 0)
        {
            _selfHandle.Free();
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
        }

        _surface = Win32SurfaceFactory.Create(_handle, _platform.Graphics);
        _scale = NativeMethods.GetDpiForWindow(_handle) / 96f;

        PlaceAfterCreate(width, height);

        // the moves during CreateWindowEx came before the scale was known: from here
        // on the window reports its place and size to the form, starting now
        _reportsBounds = true;
        ReportBounds();

        // a topmost form opens above the others; TopMost set later goes through
        // SetTopMost from the form
        if (_form.TopMost)
            SetTopMost(true);

        // WM_SIZE during CreateWindowEx came before the surface existed,
        // so the first time it is initialized by hand
        if (NativeMethods.GetClientRect(_handle, out NativeMethods.RECT clientRect))
        {
            int clientWidth = clientRect.Right - clientRect.Left;
            int clientHeight = clientRect.Bottom - clientRect.Top;

            if (clientWidth > 0 && clientHeight > 0)
            {
                _surface.Resize(clientWidth, clientHeight);
                _form.ClientSize = new Size(clientWidth / _scale, clientHeight / _scale);
                _form.PerformLayout();
            }
        }

        // the form's icon, or the application's: at the sizes of the window's DPI,
        // not a fixed 32 and 16 the taskbar of a 150 % screen stretched
        ApplyIcon(_form.EffectiveIcon);

        // Invoke before the window existed only queued the action: there was no
        // handle to post WM_INVOKE to, and without a later Invoke the action
        // would never run
        if (!_invokeQueue.IsEmpty)
            NativeMethods.PostMessage(_handle, NativeConstants.WM_INVOKE, 0, 0);
    }

    /// <summary>The size and place the window gets once its scale is known: the
    /// logical size becomes physical, a window sized to its content gets its frame
    /// around the client area, and a centered window is centered on its owner or on
    /// the screen.</summary>
    private void PlaceAfterCreate(int width, int height)
    {
        bool sizedToContent = _form.SizeToContent != SizeToContent.None;
        bool centered = _form.WindowStartupLocation
            is WindowStartupLocation.CenterScreen or WindowStartupLocation.CenterOwner;

        // created at its size on its spot already: nothing to correct
        if (_scale == 1f && !sizedToContent && _form.WindowStartupLocation != WindowStartupLocation.CenterOwner)
            return;

        int physicalWidth = (int)(width * _scale);
        int physicalHeight = (int)(height * _scale);

        if (sizedToContent)
        {
            // Size is the client area here, as on every other platform: the frame of
            // this window's styles at this window's DPI goes around it
            (uint style, uint exStyle) = ChromeStyles();

            var frame = new NativeMethods.RECT { Right = physicalWidth, Bottom = physicalHeight };

            if (NativeMethods.AdjustWindowRectExForDpi(ref frame, style, false, exStyle, (uint)MathF.Round(_scale * 96f)))
            {
                physicalWidth = frame.Right - frame.Left;
                physicalHeight = frame.Bottom - frame.Top;
            }
        }

        if (!centered)
        {
            NativeMethods.SetWindowPos(
                _handle, 0, 0, 0, physicalWidth, physicalHeight,
                NativeConstants.SWP_NOZORDER
                    | NativeConstants.SWP_NOACTIVATE
                    | NativeConstants.SWP_NOMOVE);
            return;
        }

        Rectangle area = CenteringArea();

        int cx = (int)(area.X + (area.Width - physicalWidth) / 2);
        int cy = (int)(area.Y + (area.Height - physicalHeight) / 2);

        NativeMethods.SetWindowPos(
            _handle, 0, cx, cy, physicalWidth, physicalHeight,
            NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
    }

    /// <summary>What a centered window is centered in: its owner's window for
    /// CenterOwner, when it has one; otherwise the primary screen's working area —
    /// not SM_CXSCREEN/SM_CYSCREEN, which describe the whole screen, and the window
    /// shifted by the taskbar's height.</summary>
    private Rectangle CenteringArea()
    {
        if (_form.WindowStartupLocation == WindowStartupLocation.CenterOwner &&
            _form.Owner?.PlatformWindow is Win32Window { _handle: not 0 } owner &&
            NativeMethods.GetWindowRect(owner._handle, out NativeMethods.RECT rect))
        {
            return new Rectangle(
                new Point(rect.Left, rect.Top),
                new Size(rect.Right - rect.Left, rect.Bottom - rect.Top));
        }

        return Displays.Primary.WorkingArea;
    }

    private void DestroyIcons()
    {
        if (_largeIcon != 0)
        {
            NativeMethods.DestroyIcon(_largeIcon);
            _largeIcon = 0;
        }

        if (_smallIcon != 0)
        {
            NativeMethods.DestroyIcon(_smallIcon);
            _smallIcon = 0;
        }
    }

    public void Show()
    {
        Create();

        NativeMethods.ShowWindow(
            _handle,
            (int)NativeConstants.SW_SHOW);

        // the first frame is painted right away: the window must not appear empty
        NativeMethods.UpdateWindow(_handle);
    }

    public void Close()
    {
        if (_handle == 0) return;

        // the drop target is revoked before the window is destroyed: RevokeDragDrop
        // works with the handle, and after DestroyWindow it is no longer valid
        SetDragDropEnabled(false);

        NativeMethods.DestroyWindow(_handle);
        _handle = 0;
    }

    public void SetTitle(string? title)
    {
        if (_handle == 0)
            return;

        NativeMethods.SetWindowText(
            _handle,
            title ?? string.Empty);
    }

    /// <summary>Form.Position or Form.Size set while the window is open: the position
    /// is in screen pixels already, the size is logical and measured as Form.Size is.</summary>
    /// <remarks>Nothing called this until 0.17, and it took the size as pixels and
    /// activated the window, flags 0 — the form now calls it, so it follows the
    /// form's units and leaves the activation and the z-order alone.</remarks>
    public void SetBounds(Rectangle bounds)
    {
        if (_handle == 0)
            return;

        (int extraWidth, int extraHeight) = FrameExtra();

        NativeMethods.SetWindowPos(
            _handle,
            0,
            (int)bounds.X,
            (int)bounds.Y,
            (int)MathF.Round(bounds.Width * _scale) + extraWidth,
            (int)MathF.Round(bounds.Height * _scale) + extraHeight,
            NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
    }

    public void SetWindowState(WindowState state)
    {
        if (_handle == 0) return;

        NativeMethods.ShowWindow(_handle, state switch
        {
            WindowState.Minimized => NativeConstants.SW_MINIMIZE,
            WindowState.Maximized => NativeConstants.SW_MAXIMIZE,
            _ => NativeConstants.SW_RESTORE,
        });
    }

    /// <summary>Mark an area dirty. The frame is painted by WM_PAINT, once,
    /// when the message queue is empty.</summary>
    /// <remarks>
    /// This used to call UpdateWindow, which paints synchronously: every Invalidate
    /// drew a whole frame, and one user action produces dozens of them — from layout,
    /// from controls, from bindings, from focus. Worse, a handler that invalidated in
    /// the middle of changing its state had that half-updated state painted. Frames
    /// don't suffer from the change: GetMessage hands out WM_PAINT before the next
    /// WM_TIMER, so every animation tick is painted before the next one comes.
    /// </remarks>
    public void Invalidate(Rectangle? bounds = null)
    {
        if (_handle == 0) return;

        if (bounds is { } rect)
        {
            // the coordinates are logical, the window expects physical ones.
            // Passed by reference: this used to allocate unmanaged memory
            // for the rectangle on every call, that is, every frame
            var native = new NativeMethods.RECT
            {
                Left = (int)Math.Floor(rect.X * _scale),
                Top = (int)Math.Floor(rect.Y * _scale),
                Right = (int)Math.Ceiling((rect.X + rect.Width) * _scale),
                Bottom = (int)Math.Ceiling((rect.Y + rect.Height) * _scale),
            };

            NativeMethods.InvalidateRect(_handle, ref native, false);
        }
        else
        {
            NativeMethods.InvalidateRect(_handle, 0, false);
        }
    }

    public void SetOpacity(float opacity)
    {
        if (_handle == 0) return;

        nint exStyle = NativeMethods.GetWindowLongPtr(_handle, NativeConstants.GWL_EXSTYLE);
        bool isLayered = (exStyle & (nint)NativeConstants.WS_EX_LAYERED) != 0;

        // A fully opaque window must NOT be layered: in that mode Windows composites
        // the window separately and ignores direct output into the DC, which is how
        // the graphics backend draws — the window would end up empty.
        if (opacity >= 1f)
        {
            if (isLayered)
            {
                NativeMethods.SetWindowLongPtr(_handle, NativeConstants.GWL_EXSTYLE,
                    exStyle & ~(nint)NativeConstants.WS_EX_LAYERED);

                NativeMethods.InvalidateRect(_handle, 0, true);
                NativeMethods.UpdateWindow(_handle);
            }

            return;
        }

        if (!isLayered)
        {
            NativeMethods.SetWindowLongPtr(_handle, NativeConstants.GWL_EXSTYLE,
                exStyle | (nint)NativeConstants.WS_EX_LAYERED);
        }

        byte alpha = (byte)Math.Clamp(opacity * 255f, 0, 255);
        NativeMethods.SetLayeredWindowAttributes(_handle, 0, alpha, NativeConstants.LWA_ALPHA);
    }

    public void Invoke(Action action)
    {
        _invokeQueue.Enqueue(action);
        if (_handle != 0)
            NativeMethods.PostMessage(_handle, NativeConstants.WM_INVOKE, 0, 0);
    }

    private nint ProcessMessage(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam)
    {
        switch (message)
        {
            case NativeConstants.WM_SETTINGCHANGE:
                // of the general settings we need one — interface animation.
                // The message then goes on to DefWindowProc: others wait for it too.
                // It used to return 0 here, contrary to this very comment
                _platform.OnSystemSettingsChanged();
                return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);

            case NativeConstants.WM_CAPTURECHANGED:
                // the capture was taken away from outside: our own state must be
                // reset, otherwise a drag would stay hanging. Our own ReleaseCapture
                // sends this message too — that is not a loss, and treating it as one
                // cancelled every interaction that ended normally: the contact lost
                // its pressed element, and the click after the release never came
                if (!_releasingCapture)
                    _form.OnCaptureLost();

                return 0;

            case NativeConstants.WM_LBUTTONDOWN:
                TakeKeyboardBack();
                _form.OnPointerDown(PointFromLParam(lParam), MouseButton.Left, GetModifiers());
                return 0;

            case NativeConstants.WM_LBUTTONUP:
                TakeKeyboardBack();
                _form.OnPointerUp(PointFromLParam(lParam), MouseButton.Left, GetModifiers());
                return 0;

            case NativeConstants.WM_RBUTTONDOWN:
                TakeKeyboardBack();
                _form.OnPointerDown(PointFromLParam(lParam), MouseButton.Right, GetModifiers());
                return 0;

            case NativeConstants.WM_RBUTTONUP:
                {
                    Point point = PointFromLParam(lParam);

                    _form.OnPointerUp(point, MouseButton.Right, GetModifiers());
                    _form.OnContextMenu(point);
                    return 0;
                }

            case NativeConstants.WM_MBUTTONDOWN:
                _form.OnPointerDown(PointFromLParam(lParam), MouseButton.Middle, GetModifiers());
                return 0;

            case NativeConstants.WM_MBUTTONUP:
                _form.OnPointerUp(PointFromLParam(lParam), MouseButton.Middle, GetModifiers());
                return 0;

            case NativeConstants.WM_KEYDOWN:
                _form.OnKeyDown((Key)(int)wParam, GetModifiers(),
                   isRepeat: ((long)lParam & (1L << 30)) != 0);
                return 0;

            case NativeConstants.WM_KEYUP:
                _form.OnKeyUp((Key)(int)wParam, GetModifiers());
                return 0;
            // Alt combinations and F10 come as system keys. They used to go straight
            // to DefWindowProc: the form never saw Alt+S or F10, and a tap of Alt put
            // the window into its system menu. Now the form gets them; Alt+F4 still
            // goes on to close the window, as the user expects of any window
            case NativeConstants.WM_SYSKEYDOWN:
                _form.OnKeyDown((Key)(int)wParam, GetModifiers(),
                   isRepeat: ((long)lParam & (1L << 30)) != 0);

                return (int)wParam == NativeConstants.VK_F4
                    ? NativeMethods.DefWindowProc(hWnd, message, wParam, lParam)
                    : 0;

            case NativeConstants.WM_SYSKEYUP:
                _form.OnKeyUp((Key)(int)wParam, GetModifiers());
                return 0;

            // the character of an Alt combination in the current layout: access keys
            // in any language. Alt+Space stays the system menu of the window
            case NativeConstants.WM_SYSCHAR:
                if ((char)wParam == ' ')
                    return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);

                _form.OnAccessKeyChar((char)wParam);
                return 0;

            // the close button, Alt+F4, the window menu, the taskbar: the form's
            // Closing may keep the window open
            case NativeConstants.WM_CLOSE:
                if (_form.RequestClose(CloseReason.UserClosing))
                    Close();

                return 0;

            // the low word is WA_INACTIVE (0), WA_ACTIVE or WA_CLICKACTIVE. The
            // system goes on with it: DefWindowProc gives the window the focus
            case NativeConstants.WM_ACTIVATE:
                _form.OnWindowActivated((wParam.ToInt64() & 0xFFFF) != 0);
                return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);

            case NativeConstants.WM_NCDESTROY:
                {
                    DestroyIcons();
                    _surface?.Dispose();
                    _surface = null;

                    nint result = NativeMethods.DefWindowProc(
                        hWnd, message, wParam, lParam);

                    ReleaseHandle();

                    return result;
                }

            // Win32Window.ProcessMessage
            case NativeConstants.WM_SIZE:
                {
                    int flag = (int)wParam;

                    _form.SetWindowStateFromPlatform(flag switch
                    {
                        NativeConstants.SIZE_MINIMIZED => WindowState.Minimized,
                        NativeConstants.SIZE_MAXIMIZED => WindowState.Maximized,
                        _ => WindowState.Normal,
                    });

                    // on minimizing the system sends a 0×0 size — computing layout
                    // for a zero area is pointless and harmful (everything collapses)
                    if (flag == NativeConstants.SIZE_MINIMIZED)
                        return 0;

                    int width = (int)(lParam.ToInt64() & 0xFFFF);
                    int height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    _surface?.Resize(width, height);

                    _form.ClientSize = new Size(width / _scale, height / _scale);
                    _form.PerformLayout();

                    // synchronous here on purpose: during a live resize of the frame
                    // a deferred paint leaves stale strips at the growing edges
                    NativeMethods.InvalidateRect(hWnd, 0, false);
                    NativeMethods.UpdateWindow(hWnd);
                    return 0;
                }

            case NativeConstants.WM_MOUSEHWHEEL:
                {
                    // the high word of wParam is signed: scrolling left gives a negative delta
                    int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);

                    // wheel coordinates are screen ones, unlike the other mouse
                    // messages. The comment used to say they were converted the same
                    // way as for WM_MOUSEWHEEL, but ScreenToClient was never called,
                    // and PointFromLParam's division by the scale was then repeated
                    _form.OnMouseWheel(
                        ClientPointFromScreenLParam(hWnd, lParam),
                        delta: 0,
                        horizontalDelta: delta);

                    return 0;
                }

            case NativeConstants.WM_KILLFOCUS:
                _form.OnWindowFocusLost();
                return 0;

            // the form's MinimumSize and MaximumSize, read anew each time: dragging an
            // edge, SetWindowPos and maximizing all ask
            case NativeConstants.WM_GETMINMAXINFO:
                OnGetMinMaxInfo(lParam);
                return 0;

            // every move and resize, the user's and the code's: the form's Position and
            // Size follow. DefWindowProc still gets it — it sends WM_SIZE and WM_MOVE
            case NativeConstants.WM_WINDOWPOSCHANGED:
                ReportBounds();
                return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);

            case NativeConstants.WM_DPICHANGED:
                {
                    _scale = (ushort)(wParam.ToInt64() & 0xFFFF) / 96f;

                    // lParam — the rectangle suggested by the system for the new DPI
                    var suggested = Marshal.PtrToStructure<NativeMethods.RECT>(lParam);

                    NativeMethods.SetWindowPos(
                        hWnd, 0,
                        suggested.Left, suggested.Top,
                        suggested.Right - suggested.Left,
                        suggested.Bottom - suggested.Top,
                        NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);

                    // the icons of the new DPI's sizes: a window moved to a 150 % screen
                    // kept its 100 % ones, stretched and blurred in the taskbar
                    if (_icon is not null)
                        ApplyIcon(_icon);

                    return 0;
                }

            case NativeConstants.WM_ERASEBKGND:
                // the backend clears the canvas itself in FormRenderer.Render() — Windows must not wipe
                // the background with the system brush between a resize and our
                // WM_PAINT (otherwise there is flicker, as the WinForms side has
                // already been through).
                return 1;

            case NativeConstants.WM_PAINT:
                {
                    NativeMethods.BeginPaint(hWnd, out var ps);

                    try
                    {
                        if (_surface?.BeginFrame() is { } g)
                        {
                            // a GL surface contains garbage after SwapBuffers,
                            // a partial redraw is impossible for it
                            Rectangle? clip = _surface.SupportsPartialRedraw
                                ? new Rectangle(
                                    new Point(ps.rcPaint.Left / _scale, ps.rcPaint.Top / _scale),
                                    new Size(
                                        (ps.rcPaint.Right - ps.rcPaint.Left) / _scale,
                                        (ps.rcPaint.Bottom - ps.rcPaint.Top) / _scale))
                                : null;

                            FormRenderer.Render(_form, g, _scale, clip);
                            _surface.EndFrame();
                        }

                        _form.TakeDirtyRegion();
                    }
                    finally
                    {
                        NativeMethods.EndPaint(hWnd, ref ps);
                    }

                    return 0;
                }

            case NativeConstants.WM_DESTROY:
                DisposeAutomation();
                _form.OnWindowClosed();
                _platform.WindowDestroyed();
                return 0;

            case NativeConstants.WM_MOUSEMOVE:
                {
                    // (short), not just a mask — coordinates may be negative on
                    // multi-monitor setups with a monitor left of or above the primary one
                    int x = (short)(lParam.ToInt64() & 0xFFFF);
                    int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                    if (!_trackingMouse)
                    {
                        var tme = new NativeMethods.TRACKMOUSEEVENT
                        {
                            cbSize = (uint)Marshal.SizeOf<NativeMethods.TRACKMOUSEEVENT>(),
                            dwFlags = NativeConstants.TME_LEAVE,
                            hwndTrack = hWnd,
                        };
                        NativeMethods.TrackMouseEvent(ref tme);
                        _trackingMouse = true;
                    }

                    // the modifiers as for a press: wParam's MK_ flags know no Alt,
                    // GetKeyState knows all three
                    _form.OnPointerMove(new Point(x / _scale, y / _scale), GetModifiers());
                    return 0;
                }

            case NativeConstants.WM_MOUSELEAVE:
                {
                    _trackingMouse = false;
                    _form.OnPointerLeaveWindow();
                    return 0;
                }

            case NativeConstants.WM_MOUSEWHEEL:
                {
                    int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);

                    _form.OnMouseWheel(ClientPointFromScreenLParam(hWnd, lParam), delta);
                    return 0;
                }

            // input methods: the composition is drawn by the field, see Win32Window.Ime
            case NativeConstants.WM_IME_SETCONTEXT:
                return OnImeSetContext(hWnd, message, wParam, lParam);

            case NativeConstants.WM_IME_STARTCOMPOSITION:
                return 0;

            case NativeConstants.WM_IME_COMPOSITION:
                return OnImeComposition(hWnd, lParam);

            case NativeConstants.WM_IME_ENDCOMPOSITION:
                _form.OnCompositionUpdate(string.Empty, 0);
                return 0;

            case NativeConstants.WM_CHAR:
                {
                    char c = (char)wParam;
                    _form.OnTextInput(c);
                    return 0;
                }

            // a screen reader or another UI Automation client asks for the window's tree
            case Automation.UiaNative.WM_GETOBJECT:
                return HandleGetObject(hWnd, message, wParam, lParam);

            // a provider call carried over from a UIA thread
            case Automation.UiaBridge.WM_UIA_CALL:
                Automation.UiaBridge.RunCall(lParam);
                return 0;

            case NativeConstants.WM_INVOKE:
                {
                    // each action on its own: one that threw used to leave the rest
                    // of the queue waiting for the next WM_INVOKE, which may never come
                    while (_invokeQueue.TryDequeue(out var action))
                        App.RunGuarded(action);
                    return 0;
                }

            case NativeConstants.WM_TIMER when (nuint)wParam == NativeConstants.AnimationTimerId:
                _form.Tick();
                return 0;

            case NativeConstants.WM_SETCURSOR:
                {
                    // we manage the cursor ourselves, only in the client area;
                    // the frame and the title bar are left to the system
                    int hitTest = (int)(lParam.ToInt64() & 0xFFFF);

                    if (hitTest == 1 /* HTCLIENT */)
                    {
                        NativeMethods.SetCursor(_currentCursor == 0
                            ? LoadCursorHandle(CursorKind.Arrow)
                            : _currentCursor);

                        return 1;
                    }

                    return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
                }

            default:
                return NativeMethods.DefWindowProc(
                    hWnd, message, wParam, lParam);
        }
    }


    private static KeyModifiers GetModifiers()
    {
        var m = KeyModifiers.None;
        if ((NativeMethods.GetKeyState(NativeConstants.VK_SHIFT) & 0x8000) != 0) m |= KeyModifiers.Shift;
        if ((NativeMethods.GetKeyState(NativeConstants.VK_CONTROL) & 0x8000) != 0) m |= KeyModifiers.Control;
        if ((NativeMethods.GetKeyState(NativeConstants.VK_MENU) & 0x8000) != 0) m |= KeyModifiers.Alt;
        return m;
    }

    private void ReleaseHandle()
    {
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();

        _handle = 0;
    }

    private static void RegisterWindowClass()
    {
        nint instance = NativeMethods.GetModuleHandle(null);

        var windowClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),

            lpfnWndProc = s_wndProc,

            hInstance = instance,

            hCursor = NativeMethods.LoadCursor(
                0,
                NativeConstants.IDC_ARROW),

            lpszClassName = ClassName
        };

        // the application's icon as the class's: what the taskbar takes when the
        // window is too busy to answer for its own, see Win32Window.Icon
        (windowClass.hIcon, windowClass.hIconSm) = ClassIcons();

        ushort atom = NativeMethods.RegisterClassEx(
            ref windowClass);

        if (atom == 0)
        {
            int error = Marshal.GetLastWin32Error();

            // ERROR_CLASS_ALREADY_EXISTS
            if (error != 1410)
                throw new Win32Exception(error);
        }
    }

    private static nint WndProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam)
    {
        if (message == NativeConstants.WM_NCCREATE)
        {
            nint createParam = Marshal.ReadIntPtr(lParam);

            NativeMethods.SetWindowLongPtr(
                hWnd,
                NativeConstants.GWLP_USERDATA,
                createParam);
        }

        Win32Window? window = GetWindow(hWnd);

        if (window is not null)
        {
            // one message is one step of the loop: an exception that escapes it goes
            // to App.UnhandledException. Without a handler the filter is false and the
            // exception flies on as it always has
            try
            {
                return window.ProcessMessage(hWnd, message, wParam, lParam);
            }
            catch (Exception exception) when (App.HasUnhandledExceptionHandler)
            {
                if (!App.OfferUnhandledException(exception)) throw;

                // handled: the message is dropped rather than passed on half done.
                // WM_NCCREATE is the exception — a zero there cancels the window
                return message == NativeConstants.WM_NCCREATE ? 1 : 0;
            }
        }

        return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
    }

    private static Win32Window? GetWindow(nint hWnd)
    {
        nint ptr = NativeMethods.GetWindowLongPtr(
            hWnd,
            NativeConstants.GWLP_USERDATA);

        if (ptr == 0)
            return null;

        GCHandle handle = GCHandle.FromIntPtr(ptr);

        return handle.Target as Win32Window;
    }

    public void SetImePosition(Point caret)
    {
        nint context = NativeMethods.ImmGetContext(_handle);
        if (context == 0) return;

        try
        {
            var form = new NativeMethods.COMPOSITIONFORM
            {
                dwStyle = NativeConstants.CFS_POINT,
                ptCurrentPos = new NativeMethods.POINT
                {
                    X = (int)(caret.X * _scale),
                    Y = (int)(caret.Y * _scale),
                },
            };

            NativeMethods.ImmSetCompositionWindow(context, ref form);
        }
        finally
        {
            NativeMethods.ImmReleaseContext(_handle, context);
        }
    }

    private Point PointFromLParam(nint lParam)
    {
        // (short) rather than a mask: coordinates may be negative
        // on multi-monitor setups
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        return new Point(x / _scale, y / _scale);
    }

    /// <summary>A screen point from a wheel message's lParam in logical client
    /// coordinates. Both wheels send screen coordinates, unlike the other
    /// mouse messages.</summary>
    private Point ClientPointFromScreenLParam(nint hWnd, nint lParam)
    {
        var screenPoint = new NativeMethods.POINT
        {
            X = (short)(lParam.ToInt64() & 0xFFFF),
            Y = (short)((lParam.ToInt64() >> 16) & 0xFFFF),
        };

        NativeMethods.ScreenToClient(hWnd, ref screenPoint);

        return new Point(screenPoint.X / _scale, screenPoint.Y / _scale);
    }

    public void CaptureMouse() => NativeMethods.SetCapture(_handle);

    public void ReleaseMouseCapture()
    {
        // ReleaseCapture sends WM_CAPTURECHANGED right inside this call:
        // the flag tells that message it is our own release, not a loss
        _releasingCapture = true;

        try
        {
            NativeMethods.ReleaseCapture();
        }
        finally
        {
            _releasingCapture = false;
        }
    }

    private Win32DropTarget? _dropTarget;

    public void SetDragDropEnabled(bool enabled)
    {
        if (enabled == (_dropTarget is not null)) return;

        if (enabled)
        {
            int ole = Ole32.OleInitialize(0);

            // RPC_E_CHANGED_MODE: the thread is in MTA. Most often this is a forgotten
            // [STAThread] on Main — and without it dragging is impossible
            if (ole == unchecked((int)0x80010106))
                throw new ZfSetupException(
                    "Drag and drop from the system requires an STA thread. " +
                    "Put [STAThread] on the Main method.");

            var target = new Win32DropTarget(_form, ToClient);

            int result = Ole32.RegisterDragDrop(_handle, target);

            if (result != 0)
            {
                Ole32.OleUninitialize();

                throw new ZfPlatformException(
                    $"RegisterDragDrop returned 0x{result:X8}.", result);
            }

            // the reference is held in a field: RegisterDragDrop doesn't keep
            // the managed object from being collected
            _dropTarget = target;

            return;
        }

        Ole32.RevokeDragDrop(_handle);
        _dropTarget = null;

        Ole32.OleUninitialize();
    }

    /// <summary>Screen coordinates to client ones, accounting for the scale.
    /// IDropTarget, unlike mouse messages, gives screen coordinates.</summary>
    private Point ToClient(Point screen)
    {
        var p = new POINT { X = (int)screen.X, Y = (int)screen.Y };

        NativeMethods.ScreenToClient(_handle, ref p);

        return new Point(p.X / _scale, p.Y / _scale);
    }
}