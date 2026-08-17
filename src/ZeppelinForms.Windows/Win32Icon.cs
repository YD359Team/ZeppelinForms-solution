using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Windows;

internal static class Win32Icon
{
    public static nint Create(
       Icon icon,
       int width,
       int height)
    {
        ReadOnlySpan<byte> data =
            icon.GetImage(width, height);

        unsafe
        {
            fixed (byte* ptr = data)
            {
                nint handle =
                    NativeMethods.CreateIconFromResourceEx(
                        (nint)ptr,
                        (uint)data.Length,
                        true,
                        0x00030000,
                        width,
                        height,
                        0);

                if (handle == 0)
                    throw new Win32Exception(
                        System.Runtime.InteropServices.Marshal
                            .GetLastWin32Error());

                return handle;
            }
        }
    }
}
