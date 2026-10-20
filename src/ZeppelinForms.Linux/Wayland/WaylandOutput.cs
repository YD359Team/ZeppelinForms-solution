using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>A screen as the compositor describes it: wl_output's geometry, current mode
/// and scale.</summary>
/// <remarks>The position is the compositor's layout of the screens — a hint it may not
/// keep, and no window can be placed by it anyway.</remarks>
internal sealed class WaylandOutput : IDisposable
{
    private const uint CurrentMode = 0x1;

    public WaylandOutput(WlOutput proxy)
    {
        Proxy = proxy;

        proxy.Geometry += (x, y, physicalWidth, _, _, _, model, _) =>
        {
            X = x;
            Y = y;
            PhysicalWidthMm = physicalWidth;
            Model = model;
        };

        proxy.Mode += (flags, width, height, _) =>
        {
            if ((flags & CurrentMode) == 0) return;

            Width = width;
            Height = height;
        };

        proxy.Scale += scale => Scale = Math.Max(1, scale);
        proxy.Name += name => Name = name;
    }

    public WlOutput Proxy { get; }

    public int X { get; private set; }
    public int Y { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int PhysicalWidthMm { get; private set; }
    public int Scale { get; private set; } = 1;
    public string? Name { get; private set; }
    public string? Model { get; private set; }

    /// <summary>The pixel density from the screen's size in millimeters; 96 for each
    /// unit of the scale where the screen doesn't say — a projector, a virtual output.</summary>
    public float Dpi => PhysicalWidthMm > 0 && Width > 0
        ? Width / (PhysicalWidthMm / 25.4f)
        : 96f * Scale;

    public void Dispose()
    {
        // wl_output.release is version 3; an older one is only forgotten
        if (Proxy.Version >= 3) Proxy.Release();
        else Proxy.Abandon();
    }
}

/// <summary>The screens of <see cref="Displays"/> from the compositor's outputs. Wayland
/// tells a client nothing of panels: the working area is the whole screen.</summary>
internal sealed class WaylandDisplayProvider(WaylandPlatform platform) : IDisplayProvider
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        List<DisplayInfo> displays = [];

        foreach (WaylandOutput output in platform.Outputs)
        {
            if (output.Width <= 0 || output.Height <= 0) continue;

            var bounds = new Rectangle(new Point(output.X, output.Y), new Size(output.Width, output.Height));

            displays.Add(new DisplayInfo
            {
                Bounds = bounds,
                WorkingArea = bounds,
                Scale = output.Scale,
                Dpi = output.Dpi,
                // Wayland has no primary screen: the first one is taken for it
                IsPrimary = displays.Count == 0,
                Name = output.Name ?? output.Model,
            });
        }

        // no output announced yet — a headless compositor: Displays puts its stand-in
        // screen in place of an empty list
        return displays;
    }
}