namespace ZeppelinForms.Windows.Rendering;

internal static class Win32SkiaSurfaceFactory
{
    public static IWin32SkiaSurface Create(nint hWnd)
    {
        try
        {
            var gl = GlSkiaSurface.Create(hWnd);
            System.Diagnostics.Debug.WriteLine("Skia: GPU (WGL) surface");
            return gl;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Skia: GL недоступен ({ex.Message}), software fallback");
            return new SoftwareSkiaSurface(hWnd);
        }
    }
}