using System.Runtime.CompilerServices;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Windows;

/// <summary>The cursor over the client area: the system's of a kind, or one made of a
/// picture. WM_SETCURSOR puts back <see cref="_currentCursor"/> whenever the system asks.</summary>
internal sealed partial class Win32Window
{
    private static readonly Dictionary<CursorKind, nint> CursorCache = [];

    /// <summary>The native cursors of pictures, kept while their CursorImage lives.
    /// 0 — Windows refused that picture, and its fallback is shown.</summary>
    private static readonly ConditionalWeakTable<CursorImage, StrongBox<nint>> ImageCursors = new();

    private nint _currentCursor;

    public void SetCursor(CursorKind cursor) => ApplyCursor(LoadCursorHandle(cursor));

    public void SetCursor(CursorImage cursor)
    {
        nint handle = ImageCursors.GetValue(cursor, static c => new StrongBox<nint>(Win32Cursor.TryCreate(c))).Value;

        ApplyCursor(handle != 0 ? handle : LoadCursorHandle(cursor.Fallback));
    }

    private void ApplyCursor(nint handle)
    {
        if (handle == _currentCursor) return;

        _currentCursor = handle;
        NativeMethods.SetCursor(handle);
    }

    private static nint LoadCursorHandle(CursorKind cursor)
    {
        if (CursorCache.TryGetValue(cursor, out nint cached))
            return cached;

        int id = cursor switch
        {
            CursorKind.Hand => NativeConstants.IDC_HAND,
            CursorKind.IBeam => NativeConstants.IDC_IBEAM,
            CursorKind.Wait => NativeConstants.IDC_WAIT,
            CursorKind.SizeWestEast => NativeConstants.IDC_SIZEWE,
            CursorKind.SizeNorthSouth => NativeConstants.IDC_SIZENS,
            CursorKind.SizeNorthWestSouthEast => NativeConstants.IDC_SIZENWSE,
            CursorKind.SizeNorthEastSouthWest => NativeConstants.IDC_SIZENESW,
            CursorKind.SizeAll => NativeConstants.IDC_SIZEALL,
            CursorKind.Cross => NativeConstants.IDC_CROSS,
            CursorKind.No => NativeConstants.IDC_NO,
            _ => NativeConstants.IDC_ARROW,
        };

        nint handle = NativeMethods.LoadCursor(0, id);
        CursorCache[cursor] = handle;
        return handle;
    }
}