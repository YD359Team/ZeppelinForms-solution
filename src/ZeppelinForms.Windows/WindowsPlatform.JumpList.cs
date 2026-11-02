using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ZeppelinForms.Windows;

/// <summary>The jump list of the application's taskbar button: ICustomDestinationList, the
/// shell's own list of an application's destinations and tasks.</summary>
public partial class WindowsPlatform : IJumpListPlatform
{
    bool IJumpListPlatform.ApplyJumpList(JumpList list) => Win32JumpList.Apply(list);

    void IJumpListPlatform.AddRecentDocument(string path) => Win32JumpList.AddRecentDocument(path);
}

/// <summary>A jump list built as the shell takes it: a list begun, the categories and the
/// tasks appended as arrays of shell links, the list committed — or aborted, and the old one
/// kept, when the shell refuses a part of it.</summary>
/// <remarks>
/// <para>
/// A link's title is not the link's description but its PKEY_Title: the shell reads it from
/// the link's property store, and a link without one shows the file name of its target.
/// A separator is a link with PKEY_AppUserModel_IsDestListSeparator and nothing else.
/// </para>
/// <para>
/// The list is the application user model id's: the one the application set
/// (<see cref="App.Id"/>), or the one the shell derives from the executable — the same the
/// windows are grouped by, so the list shows on their button.
/// </para>
/// </remarks>
internal static partial class Win32JumpList
{
    private static readonly Guid ClsidDestinationList = new("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");
    private static readonly Guid ClsidEnumerableObjectCollection = new("2D3468C1-36A7-43B6-AC24-D3F02FD9607A");
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");

    private static readonly Guid IidObjectArray = new("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");
    private static readonly Guid IidPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    private static readonly PROPERTYKEY TitleKey = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
    private static readonly PROPERTYKEY SeparatorKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 6);

    private const int KDC_FREQUENT = 1;
    private const int KDC_RECENT = 2;

    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const uint SHARD_PATHW = 0x3;

    private static readonly StrategyBasedComWrappers Wrappers = new();

    public static bool Apply(JumpList list)
    {
        // the UI thread is an STA already; a thread that isn't initialized yet becomes one
        _ = CoInitializeEx(0, COINIT_APARTMENTTHREADED);

        nint pointer = Create(ClsidDestinationList, typeof(ICustomDestinationList).GUID);

        if (pointer == 0) return false;

        ICustomDestinationList destinations;

        try
        {
            destinations = (ICustomDestinationList)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(pointer);
        }

        bool begun = false;

        try
        {
            if (App.IsIdSet) destinations.SetAppID(App.Id);

            destinations.BeginList(out _, IidObjectArray, out nint removed);
            begun = true;

            // what the user removed from the list: a category with one of them in it is
            // refused (below), the rest of the list still goes in
            if (removed != 0) Marshal.Release(removed);

            foreach (JumpCategory category in list.Categories)
            {
                if (category.Items.Count == 0) continue;

                nint items = Links(category.Items);

                try
                {
                    destinations.AppendCategory(category.Title, items);
                }
                catch (COMException exception)
                {
                    // E_ACCESSDENIED: the category has a link the user removed, or
                    // the user turned off recent items in the Start settings
                    System.Diagnostics.Debug.WriteLine($"ZeppelinForms: jump list category '{category.Title}': {exception.Message}");
                }
                finally
                {
                    Marshal.Release(items);
                }
            }

            if (list.ShowFrequent) AppendKnown(destinations, KDC_FREQUENT);
            if (list.ShowRecent) AppendKnown(destinations, KDC_RECENT);

            if (list.Tasks.Count > 0)
            {
                nint tasks = Links(list.Tasks);

                try
                {
                    destinations.AddUserTasks(tasks);
                }
                finally
                {
                    Marshal.Release(tasks);
                }
            }

            destinations.CommitList();
            return true;
        }
        catch (Exception exception) when (exception is COMException or ArgumentException or InvalidCastException)
        {
            System.Diagnostics.Debug.WriteLine($"ZeppelinForms: jump list: {exception.Message}");

            if (begun)
            {
                // the list the shell had stays
                try { destinations.AbortList(); }
                catch (COMException) { }
            }

            return false;
        }
    }

    /// <summary>A known category: refused when the application is registered as the opener
    /// of no file type — the shell then has nothing to list in it.</summary>
    private static void AppendKnown(ICustomDestinationList destinations, int category)
    {
        try
        {
            destinations.AppendKnownCategory(category);
        }
        catch (COMException exception)
        {
            System.Diagnostics.Debug.WriteLine($"ZeppelinForms: jump list known category {category}: {exception.Message}");
        }
    }

    /// <summary>The links as an IObjectArray: an owned reference, for the caller to release.</summary>
    private static nint Links(IEnumerable<JumpTask> tasks)
    {
        nint pointer = Create(ClsidEnumerableObjectCollection, typeof(IObjectCollection).GUID);

        if (pointer == 0) throw new COMException("No object collection.");

        try
        {
            var collection = (IObjectCollection)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);

            foreach (JumpTask task in tasks)
            {
                nint link = Link(task);

                try
                {
                    collection.AddObject(link);
                }
                finally
                {
                    Marshal.Release(link);
                }
            }

            Guid iid = IidObjectArray;
            int hr = Marshal.QueryInterface(pointer, in iid, out nint array);
            Marshal.ThrowExceptionForHR(hr);

            return array;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary>A shell link of a task: an owned reference, for the caller to release.</summary>
    private static nint Link(JumpTask task)
    {
        nint pointer = Create(ClsidShellLink, typeof(IShellLinkW).GUID);

        if (pointer == 0) throw new COMException("No shell link.");

        try
        {
            if (!task.IsSeparator)
            {
                var link = (IShellLinkW)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);

                (string program, string arguments) = Target(task);

                link.SetPath(program);
                link.SetArguments(arguments);
                link.SetIconLocation(task.IconPath ?? program, task.IconPath is null ? 0 : task.IconIndex);

                if (!string.IsNullOrEmpty(task.Description)) link.SetDescription(task.Description);
                if (!string.IsNullOrEmpty(task.WorkingDirectory)) link.SetWorkingDirectory(task.WorkingDirectory);
            }

            Guid iid = IidPropertyStore;
            int hr = Marshal.QueryInterface(pointer, in iid, out nint storePointer);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                var store = (IPropertyStore)Wrappers.GetOrCreateObjectForComInstance(storePointer, CreateObjectFlags.None);

                if (task.IsSeparator)
                {
                    var separator = new PROPVARIANT { vt = VT_BOOL, boolVal = -1 };
                    store.SetValue(SeparatorKey, separator);
                }
                else
                {
                    nint title = Marshal.StringToCoTaskMemUni(task.Title);

                    try
                    {
                        var value = new PROPVARIANT { vt = VT_LPWSTR, pointer = title };
                        store.SetValue(TitleKey, value);
                    }
                    finally
                    {
                        // SetValue took a copy
                        Marshal.FreeCoTaskMem(title);
                    }
                }

                store.Commit();
            }
            finally
            {
                Marshal.Release(storePointer);
            }

            // the caller's reference
            Marshal.AddRef(pointer);
            return pointer;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary>What a task starts: the program it names, or this application — its
    /// executable, or the host and the application's assembly before the task's own
    /// arguments when it runs as <c>dotnet app.dll</c>.</summary>
    private static (string Program, string Arguments) Target(JumpTask task)
    {
        if (task.ApplicationPath is { } program) return (program, task.Arguments);

        string process = Environment.ProcessPath ?? string.Empty;

        if (!string.Equals(Path.GetFileNameWithoutExtension(process), "dotnet", StringComparison.OrdinalIgnoreCase))
            return (process, task.Arguments);

        string assembly = Environment.GetCommandLineArgs()[0];

        return (process, $"\"{assembly}\" {task.Arguments}".TrimEnd());
    }

    private static nint Create(Guid clsid, Guid iid)
    {
        int hr = CoCreateInstance(clsid, 0, CLSCTX_INPROC_SERVER, iid, out nint pointer);

        return hr < 0 ? 0 : pointer;
    }

    public static void AddRecentDocument(string path) => SHAddToRecentDocs(SHARD_PATHW, path);

    // ===== native =====

    private const ushort VT_BOOL = 11;
    private const ushort VT_LPWSTR = 31;

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PROPERTYKEY(Guid fmtid, uint pid)
    {
        public readonly Guid FormatId = fmtid;
        public readonly uint PropertyId = pid;
    }

    /// <summary>The two kinds of value a link's properties take here: a string and a
    /// VARIANT_BOOL. Sized as on x64 — 24 bytes — on every architecture: the shell reads
    /// no more than the kind says.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public nint pointer;
        [FieldOffset(8)] public short boolVal;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint pointer);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void SHAddToRecentDocs(uint flags, string path);
}

/// <summary>ICustomDestinationList of shobjidl_core.h, in its vtable order.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E")]
internal partial interface ICustomDestinationList
{
    void SetAppID(string appId);
    void BeginList(out uint minSlots, in Guid riid, out nint removed);
    void AppendCategory(string category, nint objectArray);
    void AppendKnownCategory(int category);
    void AddUserTasks(nint objectArray);
    void CommitList();
    void GetRemovedDestinations(in Guid riid, out nint removed);
    void DeleteList(string? appId);
    void AbortList();
}

/// <summary>IObjectCollection of objectarray.h: IObjectArray's two methods, then its own.</summary>
[GeneratedComInterface]
[Guid("5632B1A4-E38A-400A-928A-D4CD63230295")]
internal partial interface IObjectCollection
{
    // IObjectArray
    void GetCount(out uint count);
    void GetAt(uint index, in Guid riid, out nint item);

    // IObjectCollection
    void AddObject(nint unknown);
    void AddFromArray(nint objectArray);
    void RemoveObjectAt(uint index);
    void Clear();
}

/// <summary>IShellLinkW of shobjidl_core.h, in its vtable order up to SetPath.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    void GetPath(nint file, int length, nint findData, uint flags);
    void GetIDList(out nint idList);
    void SetIDList(nint idList);
    void GetDescription(nint name, int length);
    void SetDescription(string name);
    void GetWorkingDirectory(nint directory, int length);
    void SetWorkingDirectory(string directory);
    void GetArguments(nint arguments, int length);
    void SetArguments(string arguments);
    void GetHotkey(out ushort hotkey);
    void SetHotkey(ushort hotkey);
    void GetShowCmd(out int showCommand);
    void SetShowCmd(int showCommand);
    void GetIconLocation(nint iconPath, int length, out int icon);
    void SetIconLocation(string iconPath, int icon);
    void SetRelativePath(string path, uint reserved);
    void Resolve(nint hwnd, uint flags);
    void SetPath(string file);
}

/// <summary>IPropertyStore of propsys.h.</summary>
[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out Win32JumpList.PROPERTYKEY key);
    void GetValue(in Win32JumpList.PROPERTYKEY key, out Win32JumpList.PROPVARIANT value);
    void SetValue(in Win32JumpList.PROPERTYKEY key, in Win32JumpList.PROPVARIANT value);
    void Commit();
}
