using System.Runtime.InteropServices;

namespace ZeppelinForms.Windows;

internal static class Ole32
{
    private const string Lib = "ole32.dll";

    [DllImport(Lib)]
    public static extern int OleInitialize(nint reserved);

    [DllImport(Lib)]
    public static extern void OleUninitialize();

    [DllImport(Lib)]
    public static extern int RegisterDragDrop(nint hwnd, IDropTarget target);

    [DllImport(Lib)]
    public static extern int RevokeDragDrop(nint hwnd);

    // эффекты из oleidl.h
    public const int DROPEFFECT_NONE = 0;
    public const int DROPEFFECT_COPY = 1;
    public const int DROPEFFECT_MOVE = 2;
    public const int DROPEFFECT_LINK = 4;

    public const int CF_HDROP = 15;
    public const int CF_UNICODETEXT = 13;

    public const int TYMED_HGLOBAL = 1;
    public const int DV_E_FORMATETC = unchecked((int)0x80040064);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FORMATETC
{
    public ushort cfFormat;
    public nint ptd;
    public uint dwAspect;
    public int lindex;
    public uint tymed;
}

[StructLayout(LayoutKind.Sequential)]
internal struct STGMEDIUM
{
    public uint tymed;
    public nint unionmember;
    public nint pUnkForRelease;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINTL
{
    public int x;
    public int y;
}

[ComImport]
[Guid("0000010E-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDataObject
{
    [PreserveSig] int GetData(ref FORMATETC format, out STGMEDIUM medium);
    [PreserveSig] int GetDataHere(ref FORMATETC format, ref STGMEDIUM medium);
    [PreserveSig] int QueryGetData(ref FORMATETC format);
    [PreserveSig] int GetCanonicalFormatEtc(ref FORMATETC format, out FORMATETC result);
    [PreserveSig] int SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release);
    [PreserveSig] int EnumFormatEtc(uint direction, out nint enumerator);
    [PreserveSig] int DAdvise(ref FORMATETC format, uint flags, nint sink, out uint connection);
    [PreserveSig] int DUnadvise(uint connection);
    [PreserveSig] int EnumDAdvise(out nint enumerator);
}

[ComImport]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTarget
{
    [PreserveSig] int DragEnter(IDataObject data, uint keyState, POINTL point, ref int effect);
    [PreserveSig] int DragOver(uint keyState, POINTL point, ref int effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(IDataObject data, uint keyState, POINTL point, ref int effect);
}
