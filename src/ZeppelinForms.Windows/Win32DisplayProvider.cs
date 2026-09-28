using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Windows;

public sealed class Win32DisplayProvider : IDisplayProvider
{
    public static void Register() => Displays.Current = new Win32DisplayProvider();

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        List<DisplayInfo> displays = [];

        // the delegate must be kept alive for the duration of the enumeration,
        // otherwise the collector may collect it in the middle of the call
        NativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            };

            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                return true;

            float scale = 1f;
            float dpi = 96f;

            // MDT_EFFECTIVE_DPI = 0
            if (NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                dpi = dpiX;
                scale = dpiX / 96f;
            }

            displays.Add(new DisplayInfo
            {
                Bounds = ToRectangle(info.rcMonitor),
                WorkingArea = ToRectangle(info.rcWork),
                Scale = scale,
                // on Windows the effective DPI is the density the system considers
                // true: there is no rounding and no base other than 96 here,
                // so Dpi and Scale are tied rigidly
                Dpi = dpi,
                IsPrimary = (info.dwFlags & NativeConstants.MONITORINFOF_PRIMARY) != 0,
                Name = info.szDevice,
            });

            return true;
        };

        NativeMethods.EnumDisplayMonitors(0, 0, callback, 0);

        return displays.Count > 0 ? displays : [Fallback()];
    }

    private static Rectangle ToRectangle(NativeMethods.RECT rect) => new(
        new Point(rect.Left, rect.Top),
        new Size(rect.Right - rect.Left, rect.Bottom - rect.Top));

    private static DisplayInfo Fallback()
    {
        int width = NativeMethods.GetSystemMetrics(NativeConstants.SM_CXSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeConstants.SM_CYSCREEN);

        var bounds = new Rectangle(Point.Empty, new Size(width, height));

        return new DisplayInfo
        {
            Bounds = bounds,
            WorkingArea = bounds,
            Scale = 1f,
            Dpi = 96f,
            IsPrimary = true,
        };
    }
}