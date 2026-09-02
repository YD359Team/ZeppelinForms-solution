using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Linux;

public sealed class X11DisplayProvider : IDisplayProvider
{
    private readonly nint _display;
    private readonly float _scale;

    internal X11DisplayProvider(nint display, float scale)
    {
        _display = display;
        _scale = scale;
    }

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        int screen = X11.XDefaultScreen(_display);
        nuint root = X11.XRootWindow(_display, screen);

        List<DisplayInfo> displays = [];

        try
        {
            nint resourcesPtr = X11.XRRGetScreenResourcesCurrent(_display, root);

            if (resourcesPtr != 0)
            {
                try
                {
                    var resources = Marshal.PtrToStructure<X11.XRRScreenResources>(resourcesPtr);

                    for (int i = 0; i < resources.ncrtc; i++)
                    {
                        nuint crtc = (nuint)Marshal.ReadIntPtr(resources.crtcs, i * nint.Size);
                        nint infoPtr = X11.XRRGetCrtcInfo(_display, resourcesPtr, crtc);

                        if (infoPtr == 0) continue;

                        try
                        {
                            var info = Marshal.PtrToStructure<X11.XRRCrtcInfo>(infoPtr);

                            // crtc без режима — выключенный выход
                            if (info.mode == 0 || info.width == 0) continue;

                            var bounds = new Rectangle(
                                new Point(info.x, info.y),
                                new Size(info.width, info.height));

                            displays.Add(new DisplayInfo
                            {
                                Bounds = bounds,
                                // рабочую область без _NET_WORKAREA не узнать,
                                // поэтому берём полную
                                WorkingArea = bounds,
                                Scale = _scale,
                                IsPrimary = displays.Count == 0,
                                Name = $"CRTC{i}",
                            });
                        }
                        finally
                        {
                            X11.XRRFreeCrtcInfo(infoPtr);
                        }
                    }
                }
                finally
                {
                    X11.XRRFreeScreenResources(resourcesPtr);
                }
            }
        }
        catch (DllNotFoundException)
        {
            // libXrandr нет — довольствуемся одним экраном
        }

        if (displays.Count > 0)
            return displays;

        var single = new Rectangle(
            Point.Empty,
            new Size(X11.XDisplayWidth(_display, screen), X11.XDisplayHeight(_display, screen)));

        return
        [
            new DisplayInfo
            {
                Bounds = single,
                WorkingArea = single,
                Scale = _scale,
                IsPrimary = true,
            },
        ];
    }
}