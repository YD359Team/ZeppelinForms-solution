using ZeppelinForms.Drawing;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// Two wl_shm buffers the backend draws into in turn: while the compositor reads one,
/// the next frame goes into the other.
/// </summary>
/// <remarks>
/// <para>
/// A buffer is the compositor's from the commit until its release event: drawing into
/// it before then tears what is on screen. With both held — a compositor slow to
/// release — the frame waits for the release instead.
/// </para>
/// <para>
/// ARGB8888, the one format every compositor has: blue, green, red, alpha in memory,
/// premultiplied — a translucent form is composited as such.
/// </para>
/// </remarks>
internal sealed class WaylandShmSurface(WlShm shm, IGraphicsBackend graphics) : IWaylandSurface
{
    private sealed class Slot : IDisposable
    {
        public required SharedMemory Memory { get; init; }
        public required WlShmPool Pool { get; init; }
        public required WlBuffer Buffer { get; init; }
        public required IRenderSurface Surface { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }

        /// <summary>Committed and not yet released by the compositor.</summary>
        public bool Busy { get; set; }

        public void Dispose()
        {
            Surface.Dispose();
            Buffer.Destroy();
            Pool.Destroy();
            Memory.Dispose();
        }
    }

    private readonly Slot?[] _slots = new Slot?[2];
    private Slot? _current;

    /// <summary>A frame can begin: a buffer isn't the compositor's, or is yet to be made.</summary>
    public bool CanBeginFrame => Array.Exists(_slots, slot => slot is not { Busy: true });

    /// <summary>The graphics for a frame of this size in pixels, or null while both
    /// buffers are the compositor's.</summary>
    public Graphics? BeginFrame(int width, int height)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            Slot? slot = _slots[i];

            if (slot is { Busy: true }) continue;

            // a buffer of the old size isn't drawn into: it is made anew
            if (slot is not null && (slot.Width != width || slot.Height != height))
            {
                slot.Dispose();
                slot = null;
            }

            _slots[i] = slot ??= MakeSlot(width, height);
            _current = slot;

            return slot.Surface.BeginFrame();
        }

        return null;
    }

    /// <summary>Finish the frame and commit its buffer: the compositor's from here on,
    /// until it releases it.</summary>
    public void EndFrame(WlSurface surface, int width, int height)
    {
        Slot slot = _current ?? throw new InvalidOperationException("No frame was begun.");

        slot.Surface.EndFrame();
        slot.Busy = true;
        _current = null;

        surface.Attach(slot.Buffer, 0, 0);

        // damage in buffer pixels from version 4; before, in the surface's units — the
        // whole surface either way, as large as it may be
        if (surface.Version >= 4) surface.DamageBuffer(0, 0, width, height);
        else surface.Damage(0, 0, int.MaxValue, int.MaxValue);

        surface.Commit();
    }

    private Slot MakeSlot(int width, int height)
    {
        int stride = width * 4;
        var memory = SharedMemory.Create(stride * height);

        WlShmPool pool = shm.CreatePool(memory.FileDescriptor, memory.Size);
        WlBuffer buffer = pool.CreateBuffer(0, width, height, stride, (uint)WlShm.FormatKind.Argb8888);

        IRenderSurface surface = graphics.CreatePixelSurface(
            new PixelBuffer(memory.Pointer, width, height, stride, PixelFormat.Bgra8888))
            ?? throw new Core.ZfPlatformException(
                $"The {graphics.Name} backend draws on the GPU only, and EGL is unavailable for this Wayland window.");

        var slot = new Slot
        {
            Memory = memory,
            Pool = pool,
            Buffer = buffer,
            Surface = surface,
            Width = width,
            Height = height,
        };

        // the loop sees the free buffer through CanBeginFrame on its next turn
        buffer.Release += () => slot.Busy = false;

        return slot;
    }

    public void Dispose()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i]?.Dispose();
            _slots[i] = null;
        }
    }
}