using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Forms;

/// <summary>The native controls among the form's elements: their hosts are followed
/// after every frame, and their controls go when the window does.</summary>
public partial class Form
{
    private readonly List<NativeControlHost> _nativeHosts = [];

    internal void RegisterNativeHost(NativeControlHost host)
    {
        if (!_nativeHosts.Contains(host))
            _nativeHosts.Add(host);
    }

    internal void UnregisterNativeHost(NativeControlHost host) => _nativeHosts.Remove(host);

    /// <summary>After a frame: every element is where the frame drew it, and the native
    /// controls follow their hosts — a scroll, a resize, a page switched. A host whose
    /// control can't be made or placed doesn't cost the others theirs.</summary>
    internal void PlaceNativeControls()
    {
        if (_nativeHosts.Count == 0) return;

        // a copy: making a control may add or remove hosts
        foreach (NativeControlHost host in _nativeHosts.ToArray())
            App.RunGuarded(() => host.UpdatePlacement(this));
    }

    /// <summary>The window is closing: the controls go with it, each by its host's own
    /// destruction — a player stopped before its window is gone.</summary>
    internal void ReleaseNativeControls()
    {
        foreach (NativeControlHost host in _nativeHosts.ToArray())
            App.RunGuarded(host.ReleaseNativeControl);
    }
}