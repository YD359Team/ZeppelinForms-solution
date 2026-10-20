using System.Runtime.InteropServices;
using ZeppelinForms.Core;

namespace ZeppelinForms.Linux;

/// <summary>poll(2) over a few descriptors: the Wayland loop waits on the compositor's
/// socket, its own wake-up and the session bus at once.</summary>
/// <remarks>poll rather than select, which the X11 loop uses: a descriptor past 1024
/// is no problem for it.</remarks>
internal static unsafe class Poll
{
    public const short In = 0x001;
    public const short Error = 0x008;
    public const short HangUp = 0x010;

    [StructLayout(LayoutKind.Sequential)]
    public struct Entry
    {
        public int Fd;
        public short Events;
        public short Returned;
    }

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int poll(Entry* fds, nuint count, int timeout);

    /// <summary>Wait until one of <paramref name="entries"/> is readable or the time is
    /// up; -1 waits for ever. An interrupted wait is no error: the loop just turns again.</summary>
    public static void Wait(Span<Entry> entries, int timeoutMs)
    {
        fixed (Entry* first = entries)
            poll(first, (nuint)entries.Length, timeoutMs);
    }
}

/// <summary>An eventfd: how another thread wakes the platform's loop out of its poll —
/// <c>Invoke</c> from a worker, a <c>Dispatcher</c> post.</summary>
internal sealed unsafe class EventFd : IDisposable
{
    private const int EFD_NONBLOCK = 0x800;
    private const int EFD_CLOEXEC = 0x80000;

    [DllImport("libc", SetLastError = true)] private static extern int eventfd(uint initial, int flags);
    [DllImport("libc")] private static extern nint read(int fd, void* buffer, nint count);
    [DllImport("libc")] private static extern nint write(int fd, void* buffer, nint count);
    [DllImport("libc")] private static extern int close(int fd);

    public EventFd()
    {
        FileDescriptor = eventfd(0, EFD_NONBLOCK | EFD_CLOEXEC);

        if (FileDescriptor < 0)
            throw new ZfPlatformException($"eventfd failed (errno {Marshal.GetLastPInvokeError()}).");
    }

    public int FileDescriptor { get; private set; }

    /// <summary>Wake the loop: any thread.</summary>
    public void Signal()
    {
        ulong one = 1;
        write(FileDescriptor, &one, sizeof(ulong));
    }

    /// <summary>Take the wake-ups that came: the counter back to zero.</summary>
    public void Drain()
    {
        ulong count;
        read(FileDescriptor, &count, sizeof(ulong));
    }

    public void Dispose()
    {
        if (FileDescriptor < 0) return;

        close(FileDescriptor);
        FileDescriptor = -1;
    }
}