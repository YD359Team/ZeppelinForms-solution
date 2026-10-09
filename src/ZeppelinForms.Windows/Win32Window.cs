using SkiaSharp;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

    private IWin32SkiaSurface? _skiaSurface;

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

        // before the first show: a dark window must not flash a white caption
        AttachCaptionTheme();

        RegisterWindowClass();

        int width = (int)_form.Size.Width;
        int height = (int)_form.Size.Height;

        int x, y;

        switch (_form.WindowStartupLocation)
        {
            // the window doesn't know its owner — Form.ShowDialog doesn't pass it
            // to the platform — so CenterOwner centers on the screen for now
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

            if (!_form.CanMinimize) style &= ~NativeConstants.WS_MINIMIZEBOX;
            if (!_form.CanMaximize) style &= ~NativeConstants.WS_MAXIMIZEBOX;
            if (!_form.CanResize) style &= ~NativeConstants.WS_THICKFRAME;

            _handle = NativeMethods.CreateWindowEx(
                0, ClassName, _form.Title ?? string.Empty,
                style,              // ← instead of WS_OVERLAPPEDWINDOW
                x, y, width, height, 0, 0,
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

        _skiaSurface = Win32SkiaSurfaceFactory.Create(_handle);
        _scale = NativeMethods.GetDpiForWindow(_handle) / 96f;

        if (_scale != 1f)
        {
            int physicalWidth = (int)(width * _scale);
            int physicalHeight = (int)(height * _scale);

            bool center = _form.WindowStartupLocation
                is WindowStartupLocation.CenterScreen or WindowStartupLocation.CenterOwner;

            if (center)
            {
                // the working area, not SM_CXSCREEN/SM_CYSCREEN: those describe the
                // whole screen, and the window shifted by the taskbar's height —
                // while before the DPI correction it was centered on the working area
                Rectangle area = Displays.Primary.WorkingArea;

                int cx = (int)(area.X + (area.Width - physicalWidth) / 2);
                int cy = (int)(area.Y + (area.Height - physicalHeight) / 2);

                NativeMethods.SetWindowPos(
                    _handle, 0, cx, cy, physicalWidth, physicalHeight,
                    NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
            }
            else
            {
                NativeMethods.SetWindowPos(
                    _handle, 0, 0, 0, physicalWidth, physicalHeight,
                    NativeConstants.SWP_NOZORDER
                        | NativeConstants.SWP_NOACTIVATE
                        | NativeConstants.SWP_NOMOVE);
            }
        }

        // WM_SIZE during CreateWindowEx came before the surface existed,
        // so the first time it is initialized by hand
        if (NativeMethods.GetClientRect(_handle, out NativeMethods.RECT clientRect))
        {
            int clientWidth = clientRect.Right - clientRect.Left;
            int clientHeight = clientRect.Bottom - clientRect.Top;

            if (clientWidth > 0 && clientHeight > 0)
            {
                _skiaSurface.Resize(clientWidth, clientHeight);
                _form.ClientSize = new Size(clientWidth / _scale, clientHeight / _scale);
                _form.PerformLayout();
            }
        }

        if (_form.Icon is not null)
        {
            _largeIcon = Win32Icon.Create(
                _form.Icon,
                32,
                32);

            _smallIcon = Win32Icon.Create(
                _form.Icon,
                16,
                16);

            NativeMethods.SendMessage(
                _handle,
                NativeConstants.WM_SETICON,
                NativeConstants.ICON_BIG,
                _largeIcon);

            NativeMethods.SendMessage(
                _handle,
                NativeConstants.WM_SETICON,
                NativeConstants.ICON_SMALL,
                _smallIcon);
        }

        // Invoke before the window existed only queued the action: there was no
        // handle to post WM_INVOKE to, and without a later Invoke the action
        // would never run
        if (!_invokeQueue.IsEmpty)
            NativeMethods.PostMessage(_handle, NativeConstants.WM_INVOKE, 0, 0);
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

    public void SetBounds(Rectangle bounds)
    {
        if (_handle == 0)
            return;

        NativeMethods.SetWindowPos(
            _handle,
            0,
            (int)bounds.X,
            (int)bounds.Y,
            (int)bounds.Width,
            (int)bounds.Height,
            0);
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
        // Skia draws — the window would end up empty.
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
                _form.OnPointerDown(PointFromLParam(lParam), MouseButton.Left, GetModifiers());
                return 0;

            case NativeConstants.WM_LBUTTONUP:
                _form.OnPointerUp(PointFromLParam(lParam), MouseButton.Left, GetModifiers());
                return 0;

            case NativeConstants.WM_RBUTTONDOWN:
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
                    _skiaSurface?.Dispose();
                    _skiaSurface = null;

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

                    _skiaSurface?.Resize(width, height);

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

                    return 0;
                }

            case NativeConstants.WM_ERASEBKGND:
                // Skia clears the canvas itself in Render() — Windows must not wipe
                // the background with the system brush between a resize and our
                // WM_PAINT (otherwise there is flicker, as the WinForms side has
                // already been through).
                return 1;

            case NativeConstants.WM_PAINT:
                {
                    NativeMethods.BeginPaint(hWnd, out var ps);

                    try
                    {
                        if (_skiaSurface?.BeginFrame() is SKSurface surface)
                        {
                            // a GL surface contains garbage after SwapBuffers,
                            // a partial redraw is impossible for it
                            Rectangle? clip = _skiaSurface.SupportsPartialRedraw
                                ? new Rectangle(
                                    new Point(ps.rcPaint.Left / _scale, ps.rcPaint.Top / _scale),
                                    new Size(
                                        (ps.rcPaint.Right - ps.rcPaint.Left) / _scale,
                                        (ps.rcPaint.Bottom - ps.rcPaint.Top) / _scale))
                                : null;

                            Skia.SkiaRenderer.Render(_form, surface.Canvas, _scale, clip);
                            _skiaSurface.EndFrame();
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
                DetachCaptionTheme();
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

                    _form.OnPointerMove(new Point(x / _scale, y / _scale));
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
                    while (_invokeQueue.TryDequeue(out var action))
                        action();
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
            return window.ProcessMessage(hWnd, message, wParam, lParam);
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

    private static readonly Dictionary<CursorKind, nint> CursorCache = [];
    private nint _currentCursor;

    public void SetCursor(CursorKind cursor)
    {
        nint handle = LoadCursorHandle(cursor);

        if (handle == _currentCursor) return;

        _currentCursor = handle;
        NativeMethods.SetCursor(handle);
    }

    private static nint LoadCursorHandle(CursorKind cursor)
    {
        if (CursorCache.TryGetValue(cursor, out nint cached))
            return cached;

        int id = cursor switch
        {
            CursorKind.Hand => NativeConstants.IDC_HAND,
            CursorKind.IBeam => NativeConstants.IDC_IBEAM,
            CursorKind.Wait => NativeConstants.IDC_WAIT,
            CursorKind.SizeWestEast => NativeConstants.IDC_SIZEWE,
            CursorKind.SizeNorthSouth => NativeConstants.IDC_SIZENS,
            CursorKind.SizeAll => NativeConstants.IDC_SIZEALL,
            CursorKind.Cross => NativeConstants.IDC_CROSS,
            CursorKind.No => NativeConstants.IDC_NO,
            _ => NativeConstants.IDC_ARROW,
        };

        nint handle = NativeMethods.LoadCursor(0, id);
        CursorCache[cursor] = handle;
        return handle;
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
                throw new InvalidOperationException(
                    "Drag and drop from the system requires an STA thread. " +
                    "Put [STAThread] on the Main method.");

            var target = new Win32DropTarget(_form, ToClient);

            int result = Ole32.RegisterDragDrop(_handle, target);

            if (result != 0)
            {
                Ole32.OleUninitialize();

                throw new InvalidOperationException(
                    $"RegisterDragDrop returned 0x{result:X8}.");
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