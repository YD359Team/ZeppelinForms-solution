using System.Runtime.InteropServices;

namespace ZeppelinForms.Windows;

public sealed class Win32Clipboard : IClipboard
{
    public static void Register() => Clipboard.Current = new Win32Clipboard();

    public string? GetText()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(NativeConstants.CF_UNICODETEXT))
            return null;

        // the clipboard is a shared system resource; another application may be
        // holding it open, so several attempts are made
        if (!TryOpen()) return null;

        try
        {
            nint handle = NativeMethods.GetClipboardData(NativeConstants.CF_UNICODETEXT);
            if (handle == 0) return null;

            nint pointer = NativeMethods.GlobalLock(handle);
            if (pointer == 0) return null;

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public void SetText(string text)
    {
        if (!TryOpen()) return;

        try
        {
            NativeMethods.EmptyClipboard();

            nuint bytes = (nuint)((text.Length + 1) * 2);
            nint hMem = NativeMethods.GlobalAlloc(NativeConstants.GMEM_MOVEABLE, bytes);
            if (hMem == 0) return;

            nint pointer = NativeMethods.GlobalLock(hMem);
            if (pointer == 0)
            {
                NativeMethods.GlobalFree(hMem);
                return;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * 2, 0);   // the terminating zero
            }
            finally
            {
                NativeMethods.GlobalUnlock(hMem);
            }

            // after a successful SetClipboardData the memory belongs to the system —
            // freeing it ourselves is not allowed
            if (NativeMethods.SetClipboardData(NativeConstants.CF_UNICODETEXT, hMem) == 0)
                NativeMethods.GlobalFree(hMem);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool TryOpen()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (NativeMethods.OpenClipboard(0))
                return true;

            Thread.Sleep(10);
        }

        return false;
    }
}