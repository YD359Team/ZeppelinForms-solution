using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Windows;

/// <summary>The window's taskbar button: its progress (ITaskbarList3), a badge as an overlay
/// icon in its corner, and flashing it for attention (FlashWindowEx).</summary>
/// <remarks>
/// <para>
/// The button exists only once Explorer has made it and said so with the registered
/// "TaskbarButtonCreated" message: what ITaskbarList3 is told before goes nowhere. So the
/// state is kept here and given to the button when the message comes — again each time it
/// comes, as it does when Explorer restarts and makes every button anew.
/// </para>
/// <para>
/// A process running elevated doesn't get the message from a non-elevated Explorer unless
/// it lets it through its message filter; such an application calls
/// ChangeWindowMessageFilterEx itself.
/// </para>
/// </remarks>
internal sealed partial class Win32Window : ITaskbarWindow
{
    private static readonly uint s_taskbarButtonCreated = TaskbarNative.RegisterWindowMessage("TaskbarButtonCreated");

    private ITaskbarList3? _taskbarList;

    private TaskbarProgressState _progressState;
    private double _progressValue;
    private int? _badge;

    /// <summary>The overlay icon shown now; destroyed when replaced and with the window.</summary>
    private nint _badgeIcon;

    public void SetTaskbarProgress(TaskbarProgressState state, double value)
    {
        _progressState = state;
        _progressValue = value;

        ApplyTaskbarProgress();
    }

    public void SetTaskbarBadge(int? count)
    {
        _badge = count;

        ApplyTaskbarBadge();
    }

    /// <summary>Flash the button until the window comes to the foreground, and the caption
    /// once: FLASHW_ALL | FLASHW_TIMERNOFG. A window in the foreground isn't flashed — the
    /// form doesn't ask then.</summary>
    public void RequestAttention()
    {
        if (_handle == 0) return;

        var info = new TaskbarNative.FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<TaskbarNative.FLASHWINFO>(),
            hwnd = _handle,
            dwFlags = TaskbarNative.FLASHW_ALL | TaskbarNative.FLASHW_TIMERNOFG,
            uCount = 0,
            dwTimeout = 0,
        };

        TaskbarNative.FlashWindowEx(ref info);
    }

    /// <summary>The message <see cref="ProcessMessage"/> hands here: Explorer's own,
    /// registered by name, so not a constant a switch could hold.</summary>
    private bool IsTaskbarButtonCreated(uint message) =>
        s_taskbarButtonCreated != 0 && message == s_taskbarButtonCreated;

    /// <summary>The button is there: from now on it is told at once, and it gets what was
    /// set before.</summary>
    private void OnTaskbarButtonCreated()
    {
        _taskbarList ??= TaskbarList.Create();

        if (_taskbarList is null) return;

        ApplyTaskbarProgress();
        ApplyTaskbarBadge();
    }

    private void ApplyTaskbarProgress()
    {
        if (_taskbarList is null || _handle == 0) return;

        try
        {
            // the value first: given on a button without progress, it turns the bar
            // on in its normal state, which the state then corrects
            if (_progressState is TaskbarProgressState.Normal or TaskbarProgressState.Paused or TaskbarProgressState.Error)
                _taskbarList.SetProgressValue(_handle, (ulong)Math.Round(_progressValue * ProgressTotal), ProgressTotal);

            _taskbarList.SetProgressState(_handle, _progressState switch
            {
                TaskbarProgressState.Indeterminate => TaskbarNative.TBPF_INDETERMINATE,
                TaskbarProgressState.Normal => TaskbarNative.TBPF_NORMAL,
                TaskbarProgressState.Paused => TaskbarNative.TBPF_PAUSED,
                TaskbarProgressState.Error => TaskbarNative.TBPF_ERROR,
                _ => TaskbarNative.TBPF_NOPROGRESS,
            });
        }
        catch (COMException exception)
        {
            // the button is gone — Explorer is restarting; the next TaskbarButtonCreated
            // brings the state back
            System.Diagnostics.Debug.WriteLine($"ZeppelinForms: taskbar progress: {exception.Message}");
        }
    }

    /// <summary>A thousand steps: as fine as a bar a few dozen pixels long can show.</summary>
    private const ulong ProgressTotal = 1000;

    private void ApplyTaskbarBadge()
    {
        if (_taskbarList is null || _handle == 0) return;

        nint old = _badgeIcon;
        _badgeIcon = _badge is { } count ? CreateBadgeIcon(count) : 0;

        try
        {
            // the description is what a screen reader says for the overlay
            _taskbarList.SetOverlayIcon(_handle, _badgeIcon,
                _badge is { } shown ? shown.ToString(CultureInfo.CurrentCulture) : string.Empty);
        }
        catch (COMException exception)
        {
            System.Diagnostics.Debug.WriteLine($"ZeppelinForms: taskbar badge: {exception.Message}");
        }

        // the button took a copy of the icon
        if (old != 0) NativeMethods.DestroyIcon(old);
    }

    /// <summary>Destroy what the button was given; with the window.</summary>
    private void DisposeTaskbar()
    {
        if (_badgeIcon != 0)
        {
            NativeMethods.DestroyIcon(_badgeIcon);
            _badgeIcon = 0;
        }

        _taskbarList = null;
    }

    /// <summary>The count as an overlay icon: a red disc with the number in white, at the
    /// small icon size of the window's DPI — the size the taskbar draws an overlay at.
    /// Drawn by the graphics backend the forms are drawn with, so the digits are the
    /// application's font, smoothed as its text is.</summary>
    private nint CreateBadgeIcon(int count)
    {
        uint dpi = NativeMethods.GetDpiForWindow(_handle);
        int size = Math.Max(16, NativeMethods.GetSystemMetricsForDpi(SM_CXSMICON, dpi));

        string text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        float fontSize = size * (text.Length switch { 1 => 0.68f, 2 => 0.56f, _ => 0.42f });

        var disc = new Border
        {
            Background = BadgeColor,
            CornerRadius = new CornerRadius(size / 2f),
            Child = new Label
            {
                Text = text,
                TextColor = Colors.White,
                Font = Font.Default.WithSize(fontSize).WithWeight(FontWeight.SemiBold),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var box = new Rectangle(0, 0, size, size);
        disc.Measure(box.Size);
        disc.Arrange(box);

        try
        {
            return TaskbarNative.IconFromImage(ElementRenderer.Current.Render(disc, size, size));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ExternalException)
        {
            // no badge rather than no window
            System.Diagnostics.Debug.WriteLine($"ZeppelinForms: taskbar badge icon: {exception.Message}");
            return 0;
        }
    }

    /// <summary>The red of a Fluent critical badge, which reads on light and dark taskbars.</summary>
    private static readonly Color BadgeColor = new(255, 0xC4, 0x2B, 0x1C);

    /// <summary>ITaskbarList3 made in-process, as the shell's own buttons are.</summary>
    private static class TaskbarList
    {
        private static readonly Guid ClsidTaskbarList = new("56FDF344-FD6D-11D0-958A-006097C9A090");

        private static readonly StrategyBasedComWrappers Wrappers = new();

        public static ITaskbarList3? Create()
        {
            Guid clsid = ClsidTaskbarList;
            Guid iid = typeof(ITaskbarList3).GUID;

            int hr = TaskbarNative.CoCreateInstance(ref clsid, 0, TaskbarNative.CLSCTX_INPROC_SERVER, ref iid, out nint pointer);

            if (hr < 0 || pointer == 0) return null;

            try
            {
                var list = (ITaskbarList3)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
                list.HrInit();
                return list;
            }
            catch (COMException exception)
            {
                System.Diagnostics.Debug.WriteLine($"ZeppelinForms: ITaskbarList3: {exception.Message}");
                return null;
            }
            finally
            {
                // the wrapper holds its own reference
                Marshal.Release(pointer);
            }
        }
    }
}

/// <summary>ITaskbarList3 of shobjidl_core.h in its vtable order: ITaskbarList, then
/// ITaskbarList2's one method, then ITaskbarList3's up to the one used last.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
internal partial interface ITaskbarList3
{
    // ITaskbarList
    void HrInit();
    void AddTab(nint hwnd);
    void DeleteTab(nint hwnd);
    void ActivateTab(nint hwnd);
    void SetActiveAlt(nint hwnd);

    // ITaskbarList2
    void MarkFullscreenWindow(nint hwnd, int fullscreen);

    // ITaskbarList3
    void SetProgressValue(nint hwnd, ulong completed, ulong total);
    void SetProgressState(nint hwnd, int flags);
    void RegisterTab(nint hwndTab, nint hwndMdi);
    void UnregisterTab(nint hwndTab);
    void SetTabOrder(nint hwndTab, nint hwndInsertBefore);
    void SetTabActive(nint hwndTab, nint hwndMdi, uint reserved);
    void ThumbBarAddButtons(nint hwnd, uint count, nint buttons);
    void ThumbBarUpdateButtons(nint hwnd, uint count, nint buttons);
    void ThumbBarSetImageList(nint hwnd, nint imageList);
    void SetOverlayIcon(nint hwnd, nint icon, string description);
}

/// <summary>What the taskbar button needs of user32, gdi32, ole32 and shell32.</summary>
internal static partial class TaskbarNative
{
    public const int TBPF_NOPROGRESS = 0;
    public const int TBPF_INDETERMINATE = 0x1;
    public const int TBPF_NORMAL = 0x2;
    public const int TBPF_ERROR = 0x4;
    public const int TBPF_PAUSED = 0x8;

    public const uint CLSCTX_INPROC_SERVER = 0x1;

    public const uint FLASHW_ALL = 0x3;
    public const uint FLASHW_TIMERNOFG = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    public struct FLASHWINFO
    {
        public uint cbSize;
        public nint hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FlashWindowEx(ref FLASHWINFO info);

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint pointer);

    /// <summary>The application user model id of the process: the taskbar groups its
    /// windows by it. Before the first window, or the windows made before keep the
    /// one the system derived from the executable.</summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SetCurrentProcessExplicitAppUserModelID(string id);

    [LibraryImport("user32.dll")]
    private static partial nint CreateIconIndirect(ref ICONINFO info);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, nint bits);

    /// <summary>An icon of a picture: a 32-bit top-down DIB of straight BGRA as its
    /// color, an empty mask — the alpha decides what shows.</summary>
    /// <remarks>The pictures the backends render are RGBA with the alpha premultiplied;
    /// an icon's color isn't premultiplied, and its edges would come out dark.</remarks>
    public static nint IconFromImage(Image image)
    {
        int width = image.Width;
        int height = image.Height;

        var header = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };

        nint color = NativeMethods.CreateDIBSection(0, ref header, 0, out nint bits, 0, 0);
        if (color == 0) return 0;

        nint mask = CreateBitmap(width, height, 1, 1, 0);

        try
        {
            byte[] source = image.Pixels;
            byte[] pixels = new byte[width * height * 4];

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte a = source[i + 3];

                if (a == 0) continue;

                // RGBA premultiplied → BGRA straight
                pixels[i + 0] = (byte)Math.Min(255, source[i + 2] * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, source[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, source[i + 0] * 255 / a);
                pixels[i + 3] = a;
            }

            Marshal.Copy(pixels, 0, bits, pixels.Length);

            var info = new ICONINFO { fIcon = 1, hbmMask = mask, hbmColor = color };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            // the icon took copies of both
            NativeMethods.DeleteObject(color);
            if (mask != 0) NativeMethods.DeleteObject(mask);
        }
    }
}
