using ZeppelinForms.Windows.Automation;

namespace ZeppelinForms.Windows;

/// <summary>UI Automation for the window: WM_GETOBJECT hands over the root provider,
/// built on the first request — until a client asks, nothing exists.</summary>
internal sealed partial class Win32Window
{
    private UiaBridge? _automation;

    /// <summary>UIA asks with its own object id; MSAA and the others get the
    /// default answer of the system.</summary>
    private nint HandleGetObject(nint hWnd, uint message, nint wParam, nint lParam)
    {
        // the object id is a DWORD: on a 64-bit system only the low half counts
        if ((int)lParam != UiaNative.UiaRootObjectId)
            return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);

        _automation ??= new UiaBridge(_handle, _form, () => _scale);

        return _automation.HandleGetObject(wParam, lParam);
    }

    /// <summary>The window is going: UIA lets go of the providers.</summary>
    private void DisposeAutomation()
    {
        _automation?.Dispose();
        _automation = null;
    }
}