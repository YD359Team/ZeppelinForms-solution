using SkiaSharp;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Windows.Rendering;

namespace ZeppelinForms.Windows;

internal sealed class Win32Window : IPlatformWindow
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

    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _invokeQueue = new();

    public Win32Window(WindowsPlatform platform, Form form)
    {
        _platform = platform;
        _form = form;
    }

    public nint Handle => _handle;

    public void Create()
    {
        if (_handle != 0)
            return;

        RegisterWindowClass();

        int width = (int)_form.Size.Width;
        int height = (int)_form.Size.Height;

        int x, y;

        switch (_form.WindowStartupLocation)
        {
            case WindowStartupLocation.CenterScreen:
            // CenterOwner пока ведёт себя как CenterScreen — понятия "владелец окна"
            // в фреймворке ещё нет (все окна создаются независимо)
            case WindowStartupLocation.CenterOwner:
                x = (NativeMethods.GetSystemMetrics(NativeConstants.SM_CXSCREEN) - width) / 2;
                y = (NativeMethods.GetSystemMetrics(NativeConstants.SM_CYSCREEN) - height) / 2;
                break;

            case WindowStartupLocation.Manual:
                x = (int)_form.Position.X;
                y = (int)_form.Position.Y;
                break;

            default: // Default — отдаём выбор системе (каскад окон)
                x = NativeConstants.CW_USEDEFAULT;
                y = NativeConstants.CW_USEDEFAULT;
                break;
        }

        try
        {
            _selfHandle = GCHandle.Alloc(this);
            _handle = NativeMethods.CreateWindowEx(
                0,
                ClassName,
                _form.Title ?? string.Empty,
                NativeConstants.WS_OVERLAPPEDWINDOW,
                x,
                y,
                width,
                height,
                0,
                0,
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

        NativeMethods.UpdateWindow(_handle);
    }

    public void Close()
    {
        if (_handle != 0)
            NativeMethods.DestroyWindow(_handle);
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

    public void Invalidate()
    {
        Debug.WriteLine("Win32Window.Invalidate");

        if (_handle == 0)
            return;

        NativeMethods.InvalidateRect(_handle, 0, false);
        NativeMethods.UpdateWindow(_handle);
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
            case NativeConstants.WM_CLOSE:
                Close();
                return 0;

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
                    int width = (int)(lParam.ToInt64() & 0xFFFF);
                    int height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    _skiaSurface?.Resize(width, height);

                    _form.ClientSize = new Size(width, height);
                    _form.PerformLayout();

                    NativeMethods.InvalidateRect(hWnd, 0, false);
                    NativeMethods.UpdateWindow(hWnd);
                    return 0;
                }

            case NativeConstants.WM_ERASEBKGND:
                // Skia сам чистит канвас в Render() — не даём Windows
                // затирать фон системной кистью между resize и нашим WM_PAINT
                // (иначе будет мерцание, вы это уже проходили на WinForms-стороне).
                return 1;

            case NativeConstants.WM_PAINT:
                {
                    if (_skiaSurface is not null)
                    {
                        SKSurface surface = _skiaSurface.BeginFrame();
                        Skia.SkiaRenderer.Render(_form, surface.Canvas);
                        _skiaSurface.EndFrame();
                    }

                    NativeMethods.BeginPaint(hWnd, out var ps);
                    NativeMethods.EndPaint(hWnd, ref ps);
                    return 0;
                }

            case NativeConstants.WM_DESTROY:
                _platform.WindowDestroyed();
                return 0;

            case NativeConstants.WM_MOUSEMOVE:
                {
                    // (short), не просто маска — координаты могут быть отрицательными
                    // на мультимониторных конфигурациях с монитором левее/выше основного
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

                    _form.OnPointerMove(new Point(x, y));
                    return 0;
                }

            case NativeConstants.WM_MOUSELEAVE:
                {
                    _trackingMouse = false;
                    _form.OnPointerLeaveWindow();
                    return 0;
                }

            case NativeConstants.WM_LBUTTONDOWN:
                {
                    int x = (short)(lParam.ToInt64() & 0xFFFF);
                    int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                    NativeMethods.SetCapture(hWnd);
                    _form.OnPointerDown(new Point(x, y));
                    return 0;
                }

            case NativeConstants.WM_LBUTTONUP:
                {
                    int x = (short)(lParam.ToInt64() & 0xFFFF);
                    int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                    NativeMethods.ReleaseCapture();
                    _form.OnPointerUp(new Point(x, y));
                    return 0;
                }

            case NativeConstants.WM_MOUSEWHEEL:
                {
                    int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);

                    var screenPoint = new NativeMethods.POINT
                    {
                        X = (short)(lParam.ToInt64() & 0xFFFF),
                        Y = (short)((lParam.ToInt64() >> 16) & 0xFFFF),
                    };
                    NativeMethods.ScreenToClient(hWnd, ref screenPoint);

                    _form.OnMouseWheel(new Point(screenPoint.X, screenPoint.Y), delta);
                    return 0;
                }

            case NativeConstants.WM_KEYDOWN:
                {
                    _form.OnKeyDown((Key)(int)wParam, GetModifiers());
                    return 0;
                }

            case NativeConstants.WM_CHAR:
                {
                    char c = (char)wParam;
                    _form.OnTextInput(c);
                    return 0;
                }

            case NativeConstants.WM_INVOKE:
                {
                    while (_invokeQueue.TryDequeue(out var action))
                        action();
                    return 0;
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
            cbSize = (uint)Marshal.SizeOf<
                NativeMethods.WNDCLASSEX>(),

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
}