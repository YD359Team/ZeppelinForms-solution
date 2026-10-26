using System.Runtime.InteropServices;
using ZeppelinForms.Core;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>Anonymous memory the compositor maps too: what a wl_shm pool is made of.
/// memfd_create gives it a file descriptor without a file anywhere.</summary>
internal sealed unsafe class SharedMemory : IDisposable
{
    private const int MFD_CLOEXEC = 0x0001;
    private const int PROT_READ = 0x1;
    private const int PROT_WRITE = 0x2;
    private const int MAP_SHARED = 0x01;

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create([MarshalAs(UnmanagedType.LPStr)] string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc", SetLastError = true)]
    private static extern nint mmap(nint address, nuint length, int protection, int flags, int fd, long offset);

    [DllImport("libc")] private static extern int munmap(nint address, nuint length);

    [DllImport("libc")] private static extern int close(int fd);

    private SharedMemory(int fd, nint pointer, int size)
    {
        FileDescriptor = fd;
        Pointer = pointer;
        Size = size;
    }

    public int FileDescriptor { get; private set; }

    public nint Pointer { get; private set; }

    public int Size { get; }

    public static SharedMemory Create(int size)
    {
        int fd = memfd_create("zeppelinforms-shm", MFD_CLOEXEC);

        if (fd < 0)
            throw new ZfPlatformException($"memfd_create failed (errno {Marshal.GetLastPInvokeError()}).");

        if (ftruncate(fd, size) < 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            close(fd);
            throw new ZfPlatformException($"ftruncate to {size} bytes failed (errno {errno}).");
        }

        nint pointer = mmap(0, (nuint)size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);

        // MAP_FAILED is (void*)-1
        if (pointer == -1)
        {
            int errno = Marshal.GetLastPInvokeError();
            close(fd);
            throw new ZfPlatformException($"mmap of {size} bytes failed (errno {errno}).");
        }

        return new SharedMemory(fd, pointer, size);
    }

    public void Dispose()
    {
        if (Pointer != 0)
        {
            munmap(Pointer, (nuint)Size);
            Pointer = 0;
        }

        if (FileDescriptor >= 0)
        {
            close(FileDescriptor);
            FileDescriptor = -1;
        }
    }
}