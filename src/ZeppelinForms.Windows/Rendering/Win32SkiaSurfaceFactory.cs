namespace ZeppelinForms.Windows.Rendering;

internal static class Win32SkiaSurfaceFactory
{
    public static IWin32SkiaSurface Create(nint hWnd)
    {
        try
        {
            return GlSkiaSurface.Create(hWnd);
        }
        catch
        {
            // Как в Avalonia: если GPU-контекст не поднялся
            // (нет драйвера, RDP-сессия и т.п.) — программный путь.
            return new SoftwareSkiaSurface(hWnd);
        }
    }
}