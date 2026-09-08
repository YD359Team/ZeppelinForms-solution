using System.Diagnostics;
using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Input.DragDrop;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Windows;

/// <summary>
/// Приёмник перетаскивания для одного окна. Живёт столько же, сколько окно:
/// RegisterDragDrop держит ссылку на стороне COM, и если объект соберут
/// сборщиком, система обратится по мёртвому указателю.
/// </summary>
/// [ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class Win32DropTarget(Form form, Func<Point, Point> toClient) : IDropTarget
{
    private DragDropData _data = new();

    public int DragEnter(IDataObject data, uint keyState, POINTL point, ref int effect)
    {
        try
        {
            _data = Read(data);

            effect = ToNative(form.OnDragEnterWindow(
                _data, toClient(new Point(point.x, point.y)), ToModifiers(keyState)));

            return 0;
        }
        catch (Exception exception)
        {
            return Fail(exception, ref effect);
        }
    }

    public int DragOver(uint keyState, POINTL point, ref int effect)
    {
        try
        {
            effect = ToNative(form.OnDragOverWindow(
                _data, toClient(new Point(point.x, point.y)), ToModifiers(keyState)));

            return 0;
        }
        catch (Exception exception)
        {
            return Fail(exception, ref effect);
        }
    }

    public int DragLeave()
    {
        try
        {
            form.OnDragLeaveWindow();
            _data = new DragDropData();

            return 0;
        }
        catch (Exception exception)
        {
            int ignored = 0;

            return Fail(exception, ref ignored);
        }
    }

    public int Drop(IDataObject data, uint keyState, POINTL point, ref int effect)
    {
        try
        {
            // данные читаем заново: между enter и drop источник мог их поменять
            _data = Read(data);

            effect = ToNative(form.OnDropWindow(
                _data, toClient(new Point(point.x, point.y)), ToModifiers(keyState)));

            _data = new DragDropData();

            return 0;
        }
        catch (Exception exception)
        {
            return Fail(exception, ref effect);
        }
    }

    /// <summary>Исключение не имеет права выйти за границу COM: при PreserveSig
    /// runtime его не преобразует, источник увидит сбой и покажет запрет,
    /// а сам текст ошибки потеряется. Поэтому гасим здесь и пишем в отладку.</summary>
    private static int Fail(Exception exception, ref int effect)
    {
        effect = Ole32.DROPEFFECT_NONE;

        Debug.WriteLine($"ZeppelinForms: сбой в IDropTarget — {exception}");

        return 0;
    }

    /// <summary>Вытащить из COM-объекта то, что мы умеем понимать.</summary>
    private static DragDropData Read(IDataObject data)
    {
        return new DragDropData
        {
            Files = ReadFiles(data),
            Text = ReadText(data),
        };
    }

    private static string[] ReadFiles(IDataObject data)
    {
        var format = new FORMATETC
        {
            cfFormat = Ole32.CF_HDROP,
            dwAspect = 1,
            lindex = -1,
            tymed = Ole32.TYMED_HGLOBAL,
        };

        if (data.QueryGetData(ref format) != 0) return [];
        if (data.GetData(ref format, out STGMEDIUM medium) != 0) return [];

        try
        {
            uint count = NativeMethods.DragQueryFile(medium.unionmember, 0xFFFFFFFF, null, 0);
            var files = new string[count];

            for (uint i = 0; i < count; i++)
            {
                uint length = NativeMethods.DragQueryFile(medium.unionmember, i, null, 0);
                var buffer = new char[length + 1];

                NativeMethods.DragQueryFile(medium.unionmember, i, buffer, (uint)buffer.Length);
                files[i] = new string(buffer, 0, (int)length);
            }

            return files;
        }
        finally
        {
            NativeMethods.ReleaseStgMedium(ref medium);
        }
    }

    private static string? ReadText(IDataObject data)
    {
        var format = new FORMATETC
        {
            cfFormat = Ole32.CF_UNICODETEXT,
            dwAspect = 1,
            lindex = -1,
            tymed = Ole32.TYMED_HGLOBAL,
        };

        if (data.QueryGetData(ref format) != 0) return null;
        if (data.GetData(ref format, out STGMEDIUM medium) != 0) return null;

        try
        {
            nint pointer = NativeMethods.GlobalLock(medium.unionmember);

            return pointer == 0 ? null : Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            NativeMethods.GlobalUnlock(medium.unionmember);
            NativeMethods.ReleaseStgMedium(ref medium);
        }
    }

    private static KeyModifiers ToModifiers(uint keyState)
    {
        var modifiers = KeyModifiers.None;

        if ((keyState & 0x0004) != 0) modifiers |= KeyModifiers.Shift;
        if ((keyState & 0x0008) != 0) modifiers |= KeyModifiers.Control;
        if ((keyState & 0x0020) != 0) modifiers |= KeyModifiers.Alt;

        return modifiers;
    }

    private static int ToNative(DragDropEffect effect) => effect switch
    {
        DragDropEffect.Copy => Ole32.DROPEFFECT_COPY,
        DragDropEffect.Move => Ole32.DROPEFFECT_MOVE,
        DragDropEffect.Link => Ole32.DROPEFFECT_LINK,
        _ => Ole32.DROPEFFECT_NONE,
    };
}