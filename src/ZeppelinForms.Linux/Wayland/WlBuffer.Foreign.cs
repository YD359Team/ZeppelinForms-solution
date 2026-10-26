namespace ZeppelinForms.Linux.Wayland;

/// <summary>A wl_buffer another library made — libwayland-cursor's theme, libwayland-egl:
/// attached by this client, owned and destroyed by that library.</summary>
internal sealed partial class WlBuffer
{
    private WlBuffer(nint handle, bool listen) : base(handle, listen) { }

    /// <summary>A buffer of another library's: no events are listened to — its owner may
    /// have its own listener — and it is never destroyed from here.</summary>
    public static WlBuffer Foreign(nint handle) => new(handle, listen: false);
}