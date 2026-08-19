using SkiaSharp;
using System.ComponentModel;
using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
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

    private long _lastResizeTicks;

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

        int x = (int)_form.Position.X;
        int y = (int)_form.Position.Y;

        int width = (int)_form.Size.Width;
        int height = (int)_form.Size.Height;

        try
        {
            _selfHandle = GCHandle.Alloc(this);
            _handle = NativeMethods.CreateWindowEx(
                0,
                ClassName,
                _form.Title ?? string.Empty,
                NativeMethods.WS_OVERLAPPEDWINDOW,
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
                NativeMethods.WM_SETICON,
                NativeMethods.ICON_BIG,
                _largeIcon);

            NativeMethods.SendMessage(
                _handle,
                NativeMethods.WM_SETICON,
                NativeMethods.ICON_SMALL,
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
            (int)NativeMethods.SW_SHOW);

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
        if (_handle != 0)
            NativeMethods.InvalidateRect(_handle, 0, false);
    }

    private nint ProcessMessage(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WM_CLOSE:
                Close();
                return 0;

            case NativeMethods.WM_NCDESTROY:
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
            case NativeMethods.WM_SIZE:
                {
                    int width = (int)(lParam.ToInt64() & 0xFFFF);
                    int height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    long now = Environment.TickCount64;
                    if (now - _lastResizeTicks >= 16) // не чаще ~60 раз/сек
                    {
                        _skiaSurface?.Resize(width, height);
                        _lastResizeTicks = now;
                    }

                    if (_form.Content is not null)
                        _form.Content.Size = new Size(width, height);

                    NativeMethods.InvalidateRect(hWnd, 0, false);
                    NativeMethods.UpdateWindow(hWnd);
                    return 0;
                }

            case NativeMethods.WM_ERASEBKGND:
                // Skia сам чистит канвас в Render() — не даём Windows
                // затирать фон системной кистью между resize и нашим WM_PAINT
                // (иначе будет мерцание, вы это уже проходили на WinForms-стороне).
                return 1;

            case NativeMethods.WM_PAINT:
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

            case NativeMethods.WM_DESTROY:
                _platform.WindowDestroyed();
                return 0;

            default:
                return NativeMethods.DefWindowProc(
                    hWnd, message, wParam, lParam);
        }
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
                NativeMethods.IDC_ARROW),

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
        if (message == NativeMethods.WM_NCCREATE)
        {
            nint createParam = Marshal.ReadIntPtr(lParam);

            NativeMethods.SetWindowLongPtr(
                hWnd,
                NativeMethods.GWLP_USERDATA,
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
            NativeMethods.GWLP_USERDATA);

        if (ptr == 0)
            return null;

        GCHandle handle = GCHandle.FromIntPtr(ptr);

        return handle.Target as Win32Window;
    }
}