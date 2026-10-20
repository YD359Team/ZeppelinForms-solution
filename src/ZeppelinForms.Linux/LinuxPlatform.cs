using ZeppelinForms.Core;
using ZeppelinForms.Drawing;
using ZeppelinForms.Linux.Wayland;

namespace ZeppelinForms.Linux;

/// <summary>
/// The Linux platform of the session the application runs in: Wayland under a Wayland
/// compositor, X11 otherwise.
/// </summary>
/// <remarks>
/// <para>
/// <c>ZF_LINUX_BACKEND=x11</c> or <c>=wayland</c> in the environment chooses instead —
/// X11 under XWayland, say, for an application that places its windows itself. Without it
/// Wayland is taken when WAYLAND_DISPLAY is set and libwayland-client is installed, and
/// X11 when the compositor can't be connected to or lacks what a window needs.
/// </para>
/// <para>
/// What the chosen platform can do is in <see cref="IPlatform.Capabilities"/>: Wayland
/// has no <see cref="PlatformCapabilities.WindowPositioning"/>.
/// </para>
/// </remarks>
public static class LinuxPlatform
{
    /// <summary>The environment variable that chooses the platform: x11 or wayland.</summary>
    public const string BackendVariable = "ZF_LINUX_BACKEND";

    /// <summary>The platform of this session, drawing with Skia.</summary>
    public static IPlatform Create() => Create(Skia.SkiaGraphicsBackend.Instance);

    /// <summary>The platform of this session, drawing with <paramref name="graphics"/>.</summary>
    /// <exception cref="ZfPlatformException">Neither a compositor nor an X server answered,
    /// or the one ZF_LINUX_BACKEND names didn't.</exception>
    public static IPlatform Create(IGraphicsBackend graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        string? chosen = Environment.GetEnvironmentVariable(BackendVariable)?.Trim();

        if (string.Equals(chosen, "x11", StringComparison.OrdinalIgnoreCase))
            return new X11Platform(graphics);

        if (string.Equals(chosen, "wayland", StringComparison.OrdinalIgnoreCase))
            return new WaylandPlatform(graphics);

        if (!string.IsNullOrEmpty(chosen))
            throw new ZfPlatformException($"{BackendVariable}={chosen}: expected x11 or wayland.");

        if (WaylandConnection.IsSessionAvailable)
        {
            try
            {
                return new WaylandPlatform(graphics);
            }
            catch (ZfPlatformException exception)
            {
                // XWayland or a plain X server may still be there
                System.Diagnostics.Debug.WriteLine($"Wayland is unavailable ({exception.Message}): X11 is tried");
            }
        }

        return new X11Platform(graphics);
    }
}