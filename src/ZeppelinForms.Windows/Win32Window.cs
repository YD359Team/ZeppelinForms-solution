using SkiaSharp;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Tools;
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

    private float _scale = 1f;
    public float Scale => _scale;

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
                // GetSystemMetrics отдаёт физические пиксели, а width/height у нас
                // логические — на 150% окно уедет левее и выше центра
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
            uint style = NativeConstants.WS_OVERLAPPEDWINDOW;

            if (!_form.CanMinimize) style &= ~NativeConstants.WS_MINIMIZEBOX;
            if (!_form.CanMaximize) style &= ~NativeConstants.WS_MAXIMIZEBOX;
            if (!_form.CanResize) style &= ~NativeConstants.WS_THICKFRAME;

            _handle = NativeMethods.CreateWindowEx(
                0, ClassName, _form.Title ?? string.Empty,
                style,              // ← вместо WS_OVERLAPPEDWINDOW
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
                int cx = (NativeMethods.GetSystemMetrics(NativeConstants.SM_CXSCREEN) - physicalWidth) / 2;
                int cy = (NativeMethods.GetSystemMetrics(NativeConstants.SM_CYSCREEN) - physicalHeight) / 2;

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

        // WM_SIZE во время CreateWindowEx пришёл раньше, чем появилась
        // поверхность, поэтому первый раз инициализируем её вручную
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

    public void Invalidate()
    {
        Debug.WriteLine("Win32Window.Invalidate");

        if (_handle == 0)
            return;

        NativeMethods.InvalidateRect(_handle, 0, false);
        NativeMethods.UpdateWindow(_handle);
    }

    public void SetOpacity(float opacity)
    {
        if (_handle == 0) return;

        nint exStyle = NativeMethods.GetWindowLongPtr(_handle, NativeConstants.GWL_EXSTYLE);
        bool isLayered = (exStyle & (nint)NativeConstants.WS_EX_LAYERED) != 0;

        // Полностью непрозрачное окно НЕ должно быть layered: в этом режиме
        // Windows композитит окно отдельно и игнорирует прямой вывод в DC,
        // которым рисует Skia — окно окажется пустым.
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

    // debug
    private bool _dumped;

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
                    int flag = (int)wParam;

                    _form.SetWindowStateFromPlatform(flag switch
                    {
                        NativeConstants.SIZE_MINIMIZED => WindowState.Minimized,
                        NativeConstants.SIZE_MAXIMIZED => WindowState.Maximized,
                        _ => WindowState.Normal,
                    });

                    // при сворачивании система шлёт размер 0×0 — считать layout
                    // по нулевой области бессмысленно и вредно (всё схлопнется)
                    if (flag == NativeConstants.SIZE_MINIMIZED)
                        return 0;

                    int width = (int)(lParam.ToInt64() & 0xFFFF);
                    int height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    _skiaSurface?.Resize(width, height);

                    _form.ClientSize = new Size(width / _scale, height / _scale);
                    _form.PerformLayout();

                    NativeMethods.InvalidateRect(hWnd, 0, false);
                    NativeMethods.UpdateWindow(hWnd);
                    return 0;
                }

            case NativeConstants.WM_DPICHANGED:
                {
                    _scale = (ushort)(wParam.ToInt64() & 0xFFFF) / 96f;

                    // lParam — предложенный системой прямоугольник для нового DPI
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
                // Skia сам чистит канвас в Render() — не даём Windows
                // затирать фон системной кистью между resize и нашим WM_PAINT
                // (иначе будет мерцание, вы это уже проходили на WinForms-стороне).
                return 1;

            case NativeConstants.WM_PAINT:
                {
                    if (!_dumped)
                    {
                        _dumped = true;
                        System.Diagnostics.Debug.WriteLine($"ClientSize={_form.ClientSize.Width}x{_form.ClientSize.Height} scale={_scale}");
                        System.Diagnostics.Debug.WriteLine(_form.Content?.DumpTree() ?? "Content == null");
                    }

                    try
                    {
                        if (_skiaSurface?.BeginFrame() is SKSurface surface)
                        {
                            Skia.SkiaRenderer.Render(_form, surface.Canvas, _scale);
                            _skiaSurface.EndFrame();
                        }
                    }
                    catch (Exception ex)
                    {
                        // Исключение из WndProc никуда не долетает, а невалидированный
                        // регион заставляет Windows слать WM_PAINT бесконечно.
                        System.Diagnostics.Debug.WriteLine($"Ошибка рендера: {ex}");
                        System.Diagnostics.Debugger.Break();
                    }
                    finally
                    {
                        NativeMethods.BeginPaint(hWnd, out var ps);
                        NativeMethods.EndPaint(hWnd, ref ps);
                    }

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

                    _form.OnPointerMove(new Point(x / _scale, y / _scale));
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
                    _form.OnPointerDown(new Point(x / _scale, y / _scale));
                    return 0;
                }

            case NativeConstants.WM_LBUTTONUP:
                {
                    int x = (short)(lParam.ToInt64() & 0xFFFF);
                    int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                    NativeMethods.ReleaseCapture();
                    _form.OnPointerUp(new Point(x / _scale, y / _scale));
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

                    _form.OnMouseWheel(new Point(screenPoint.X / _scale, screenPoint.Y / _scale), delta);
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