using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

/// <summary>A pipe and the reads and writes over it with a deadline: how Wayland hands
/// data between clients — the clipboard's, a drop's. One end goes to the other client
/// through the compositor; nothing waits for ever on a client that doesn't answer.</summary>
internal static unsafe class UnixPipe
{
    private const int O_NONBLOCK = 0x800;
    private const int O_CLOEXEC = 0x80000;
    private const int F_GETFL = 3;
    private const int F_SETFL = 4;
    private const short POLLOUT = 0x004;
    private const int EAGAIN = 11;
    private const int EINTR = 4;

    [DllImport("libc", SetLastError = true)] private static extern int pipe2(int* fds, int flags);
    [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, byte* buffer, nint count);
    [DllImport("libc", SetLastError = true)] private static extern nint write(int fd, byte* buffer, nint count);
    [DllImport("libc")] private static extern int close(int fd);
    [DllImport("libc")] private static extern int fcntl(int fd, int command, int argument);

    /// <summary>A new pipe, or null when the process is out of descriptors.</summary>
    public static (int Read, int Write)? Create()
    {
        int* fds = stackalloc int[2];

        if (pipe2(fds, O_CLOEXEC) != 0) return null;

        return (fds[0], fds[1]);
    }

    public static void Close(int fd)
    {
        if (fd >= 0) close(fd);
    }

    /// <summary>Everything the other end writes until it closes, or null when it doesn't
    /// finish within <paramref name="timeoutMs"/>. The descriptor is closed either way.</summary>
    public static byte[]? ReadToEnd(int fd, int timeoutMs)
    {
        try
        {
            SetNonBlocking(fd);

            var result = new MemoryStream();
            byte* buffer = stackalloc byte[16384];
            long deadline = Environment.TickCount64 + timeoutMs;

            while (true)
            {
                nint count = read(fd, buffer, 16384);

                if (count > 0)
                {
                    result.Write(new ReadOnlySpan<byte>(buffer, (int)count));
                    continue;
                }

                // the writer closed its end: all there is came
                if (count == 0) return result.ToArray();

                int errno = Marshal.GetLastPInvokeError();
                if (errno == EINTR) continue;
                if (errno != EAGAIN) return null;

                int left = (int)(deadline - Environment.TickCount64);
                if (left <= 0) return null;

                Span<Poll.Entry> entry = [new Poll.Entry { Fd = fd, Events = Poll.In }];
                Poll.Wait(entry, left);
            }
        }
        finally
        {
            close(fd);
        }
    }

    /// <summary>Write all of <paramref name="data"/>, giving up after
    /// <paramref name="timeoutMs"/> — a reader that stopped reading must not hang the
    /// loop. The descriptor is closed either way: the reader sees the end of the data.</summary>
    public static void WriteAll(int fd, ReadOnlySpan<byte> data, int timeoutMs)
    {
        try
        {
            SetNonBlocking(fd);

            long deadline = Environment.TickCount64 + timeoutMs;

            fixed (byte* start = data)
            {
                int written = 0;

                while (written < data.Length)
                {
                    nint count = write(fd, start + written, data.Length - written);

                    if (count > 0)
                    {
                        written += (int)count;
                        continue;
                    }

                    int errno = Marshal.GetLastPInvokeError();
                    if (errno == EINTR) continue;

                    // the reader went away (EPIPE — .NET ignores SIGPIPE) or failed
                    if (errno != EAGAIN) return;

                    int left = (int)(deadline - Environment.TickCount64);
                    if (left <= 0) return;

                    Span<Poll.Entry> entry = [new Poll.Entry { Fd = fd, Events = POLLOUT }];
                    Poll.Wait(entry, left);
                }
            }
        }
        finally
        {
            close(fd);
        }
    }

    private static void SetNonBlocking(int fd)
    {
        int flags = fcntl(fd, F_GETFL, 0);
        if (flags >= 0) fcntl(fd, F_SETFL, flags | O_NONBLOCK);
    }
}