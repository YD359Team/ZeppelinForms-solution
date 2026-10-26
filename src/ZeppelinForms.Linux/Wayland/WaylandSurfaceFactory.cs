using ZeppelinForms.Drawing;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>The window's frames on the GPU through EGL where the machine has it, in shared
/// memory otherwise — as on X11, and ZF_RENDERING=software keeps them in memory.</summary>
internal static class WaylandSurfaceFactory
{
    public static IWaylandSurface Create(WaylandPlatform platform, WlSurface surface, IGraphicsBackend graphics)
    {
        bool software = string.Equals(
            Environment.GetEnvironmentVariable(X11SurfaceFactory.RenderingVariable), "software",
            StringComparison.OrdinalIgnoreCase);

        if (!software && platform.EglDisplay is { } egl)
        {
            try
            {
                if (WaylandEglSurface.Create(egl, surface, graphics) is { } gl)
                {
                    System.Diagnostics.Debug.WriteLine($"{graphics.Name}: GPU (EGL) surface");
                    return gl;
                }

                System.Diagnostics.Debug.WriteLine($"{graphics.Name}: no GL in this backend, shared-memory surface");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{graphics.Name}: EGL is unavailable for the window ({ex.Message}), shared-memory fallback");
            }
        }

        return new WaylandShmSurface(platform.Shm, graphics);
    }
}