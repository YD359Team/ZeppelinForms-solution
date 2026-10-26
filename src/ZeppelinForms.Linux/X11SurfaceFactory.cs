using ZeppelinForms.Drawing;

namespace ZeppelinForms.Linux;

internal static class X11SurfaceFactory
{
    /// <summary>Set to "software" to draw every window in memory even where EGL works:
    /// for a driver that draws wrong, and to compare the two paths.</summary>
    internal const string RenderingVariable = "ZF_RENDERING";

    public static IX11Surface Create(
        X11Platform platform, nuint window, nint gc, nint visual, uint depth, IGraphicsBackend graphics)
    {
        bool software = string.Equals(
            Environment.GetEnvironmentVariable(RenderingVariable), "software", StringComparison.OrdinalIgnoreCase);

        if (!software && platform.EglDisplay is { } egl)
        {
            try
            {
                if (X11EglSurface.Create(egl, window, visual, graphics) is { } gl)
                {
                    System.Diagnostics.Debug.WriteLine($"{graphics.Name}: GPU (EGL) surface");
                    return gl;
                }

                System.Diagnostics.Debug.WriteLine($"{graphics.Name}: no GL in this backend, software surface");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{graphics.Name}: EGL is unavailable for the window ({ex.Message}), software fallback");
            }
        }

        return new X11Surface(platform.Display, window, gc, visual, depth, graphics);
    }
}