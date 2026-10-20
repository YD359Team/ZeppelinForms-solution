using ZeppelinForms.Drawing;

namespace ZeppelinForms.Windows.Rendering;

internal static class Win32SurfaceFactory
{
    public static IWin32Surface Create(nint hWnd, IGraphicsBackend graphics)
    {
        try
        {
            if (Win32GlSurface.Create(hWnd, graphics) is { } gl)
            {
                System.Diagnostics.Debug.WriteLine($"{graphics.Name}: GPU (WGL) surface");
                return gl;
            }

            System.Diagnostics.Debug.WriteLine($"{graphics.Name}: no GL in this backend, software surface");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{graphics.Name}: GL is unavailable ({ex.Message}), software fallback");
        }

        return new Win32DibSurface(hWnd, graphics);
    }
}