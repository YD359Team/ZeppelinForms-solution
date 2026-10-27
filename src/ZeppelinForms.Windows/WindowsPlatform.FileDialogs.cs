using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Dialogs;

namespace ZeppelinForms.Windows;

/// <summary>
/// The system's file dialogs: IFileOpenDialog and IFileSaveDialog — Explorer's own, with
/// its navigation pane, search, previews and the user's recent places — owned by the form.
/// </summary>
/// <remarks>
/// <para>
/// IFileDialog.Show is modal and runs its own message loop, as a WinForms OpenFileDialog
/// does: the forms keep painting and their timers keep ticking under it, and the owner is
/// disabled until it closes. So the synchronous <see cref="FileDialog.OpenFile"/> shows it
/// too; the asynchronous methods return when it closes.
/// </para>
/// <para>
/// It needs COM on an STA thread — <c>[STAThread]</c> on Main. Where it can't be made, the
/// framework's own dialog is shown instead.
/// </para>
/// </remarks>
public partial class WindowsPlatform : INativeFileDialogs
{
    string[]? INativeFileDialogs.Show(Form owner, FileDialogOptions options, FileDialogMode mode) =>
        Win32FileDialog.Show((owner.PlatformWindow as Win32Window)?.Handle ?? 0, options, mode);

    Task<string[]?> INativeFileDialogs.ShowAsync(Form owner, FileDialogOptions options, FileDialogMode mode) =>
        Task.FromResult(Win32FileDialog.Show((owner.PlatformWindow as Win32Window)?.Handle ?? 0, options, mode));
}

/// <summary>The dialog itself: made, set up from the options, shown, read.</summary>
internal static partial class Win32FileDialog
{
    private static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid ClsidFileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");

    private static readonly StrategyBasedComWrappers Wrappers = new();

    private const int ErrorCancelled = unchecked((int)0x800704C7);

    private const uint FOS_OVERWRITEPROMPT = 0x2;
    private const uint FOS_NOCHANGEDIR = 0x8;
    private const uint FOS_PICKFOLDERS = 0x20;
    private const uint FOS_FORCEFILESYSTEM = 0x40;
    private const uint FOS_ALLOWMULTISELECT = 0x200;
    private const uint FOS_PATHMUSTEXIST = 0x800;
    private const uint FOS_FILEMUSTEXIST = 0x1000;

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    /// <summary>The paths chosen, empty when cancelled; null — no dialog could be made,
    /// and the framework's own is to be shown.</summary>
    public static string[]? Show(nint owner, FileDialogOptions options, FileDialogMode mode)
    {
        // the shell's dialogs and the extensions they load want an STA thread; in an MTA
        // one they half work — previews missing, a hang in a shell extension
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            System.Diagnostics.Debug.WriteLine(
                "ZeppelinForms: the system file dialog needs [STAThread] on Main; the framework's own is shown.");
            return null;
        }

        bool save = mode == FileDialogMode.Save;

        // the open dialog for folders too: FOS_PICKFOLDERS makes it a folder picker
        if (Create(save ? ClsidFileSaveDialog : ClsidFileOpenDialog) is not { } dialog) return null;

        var strings = new List<nint>();

        try
        {
            Configure(dialog, options, mode, strings);

            int hr = dialog.Show(owner);

            if (hr == ErrorCancelled) return [];
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            if (mode == FileDialogMode.Open && options.AllowMultiple && dialog is IFileOpenDialog open)
            {
                IShellItemArray items = open.GetResults();
                uint count = items.GetCount();

                var paths = new List<string>((int)count);

                for (uint i = 0; i < count; i++)
                    if (PathOf(items.GetItemAt(i)) is { } path) paths.Add(path);

                return [.. paths];
            }

            return PathOf(dialog.GetResult()) is { } chosen ? [chosen] : [];
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            System.Diagnostics.Debug.WriteLine($"ZeppelinForms: the system file dialog failed: {exception.Message}");
            return null;
        }
        finally
        {
            foreach (nint text in strings)
                Marshal.FreeHGlobal(text);
        }
    }

    private static IFileDialog? Create(Guid clsid)
    {
        // IFileDialog for both: what the open and the save dialogs share. The open
        // dialog's GetResults is asked for by a cast, which is a QueryInterface
        Guid iid = typeof(IFileDialog).GUID;

        int hr = TaskbarNative.CoCreateInstance(ref clsid, 0, 0x1 /* CLSCTX_INPROC_SERVER */, ref iid, out nint pointer);

        if (hr < 0 || pointer == 0) return null;

        try
        {
            return (IFileDialog)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static void Configure(IFileDialog dialog, FileDialogOptions options, FileDialogMode mode, List<nint> strings)
    {
        uint flags = dialog.GetOptions() | FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR | FOS_PATHMUSTEXIST;

        flags |= mode switch
        {
            FileDialogMode.Save => FOS_OVERWRITEPROMPT,
            FileDialogMode.Folder => FOS_PICKFOLDERS,
            _ => FOS_FILEMUSTEXIST | (options.AllowMultiple ? FOS_ALLOWMULTISELECT : 0),
        };

        dialog.SetOptions(flags);

        if (!string.IsNullOrEmpty(options.Title)) dialog.SetTitle(options.Title);

        if (mode != FileDialogMode.Folder && options.Filters.Count > 0)
        {
            // COMDLG_FILTERSPEC: two string pointers per filter, "Images" and "*.png;*.jpg"
            nint specs = Marshal.AllocHGlobal(options.Filters.Count * 2 * nint.Size);
            strings.Add(specs);

            for (int i = 0; i < options.Filters.Count; i++)
            {
                FileFilter filter = options.Filters[i];

                string pattern = filter.Extensions.Length == 0
                    ? "*.*"
                    : string.Join(";", filter.Extensions.Select(e => "*." + e.TrimStart('*', '.')));

                nint name = Marshal.StringToHGlobalUni(filter.Description);
                nint spec = Marshal.StringToHGlobalUni(pattern);
                strings.Add(name);
                strings.Add(spec);

                Marshal.WriteIntPtr(specs, i * 2 * nint.Size, name);
                Marshal.WriteIntPtr(specs, (i * 2 + 1) * nint.Size, spec);
            }

            dialog.SetFileTypes((uint)options.Filters.Count, specs);

            // what the save dialog adds to a name typed without one
            if (mode == FileDialogMode.Save &&
                options.Filters[0].Extensions is [string first, ..] &&
                first.TrimStart('*', '.') is { Length: > 0 } extension and not "*")
            {
                dialog.SetDefaultExtension(extension);
            }
        }

        if (!string.IsNullOrEmpty(options.FileName))
            dialog.SetFileName(Path.GetFileName(options.FileName));

        // the folder asked for, or the one of a file name given with its path
        string? folder = !string.IsNullOrEmpty(options.InitialDirectory)
            ? options.InitialDirectory
            : Path.GetDirectoryName(options.FileName);

        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder) && ItemOf(folder) is { } item)
            dialog.SetFolder(item);
    }

    private static IShellItem? ItemOf(string path)
    {
        Guid iid = typeof(IShellItem).GUID;

        if (SHCreateItemFromParsingName(Path.GetFullPath(path), 0, ref iid, out nint pointer) < 0 || pointer == 0)
            return null;

        try
        {
            return (IShellItem)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static string? PathOf(IShellItem item)
    {
        nint text = item.GetDisplayName(SIGDN_FILESYSPATH);

        try
        {
            return Marshal.PtrToStringUni(text);
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, out nint pointer);
}

// shobjidl_core.h, in vtable order. IFileDialog is declared flat — IModalWindow's Show,
// then its own methods — and IFileOpenDialog adds GetResults after them. The methods
// never called are there for their slots.

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
internal partial interface IFileDialog
{
    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED) is an answer, not a failure.</summary>
    [PreserveSig]
    int Show(nint owner);

    void SetFileTypes(uint count, nint filterSpecs);
    void SetFileTypeIndex(uint index);
    uint GetFileTypeIndex();
    uint Advise(nint events);
    void Unadvise(uint cookie);
    void SetOptions(uint options);
    uint GetOptions();
    void SetDefaultFolder(IShellItem item);
    void SetFolder(IShellItem item);
    nint GetFolder();
    nint GetCurrentSelection();
    void SetFileName(string name);
    nint GetFileName();
    void SetTitle(string title);
    void SetOkButtonLabel(string text);
    void SetFileNameLabel(string label);
    IShellItem GetResult();
    void AddPlace(IShellItem item, int place);
    void SetDefaultExtension(string extension);
    void Close(int result);
    void SetClientGuid(in Guid guid);
    void ClearClientData();
    void SetFilter(nint filter);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
internal partial interface IFileOpenDialog : IFileDialog
{
    IShellItemArray GetResults();
    IShellItemArray GetSelectedItems();
}

[GeneratedComInterface]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
internal partial interface IShellItem
{
    nint BindToHandler(nint bindContext, in Guid handler, in Guid iid);
    nint GetParent();

    /// <summary>The text is the caller's to free with CoTaskMemFree.</summary>
    nint GetDisplayName(uint sigdn);

    uint GetAttributes(uint mask);
    int Compare(nint other, uint hint);
}

[GeneratedComInterface]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
internal partial interface IShellItemArray
{
    nint BindToHandler(nint bindContext, in Guid handler, in Guid iid);
    nint GetPropertyStore(int flags, in Guid iid);
    nint GetPropertyDescriptionList(nint keyType, in Guid iid);
    uint GetAttributes(int flags, uint mask);
    uint GetCount();
    IShellItem GetItemAt(uint index);
    nint EnumItems();
}
