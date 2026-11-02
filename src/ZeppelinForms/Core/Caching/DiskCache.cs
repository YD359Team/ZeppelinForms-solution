using System.Security.Cryptography;
using System.Text;

namespace ZeppelinForms.Core.Caching;

/// <summary>
/// Files kept on disk between runs, by key, within a size: when the folder grows past
/// <see cref="MaxBytes"/>, the entries used longest ago go first. The map's tiles live in
/// one; anything downloaded or worked out once and asked for again — thumbnails, pictures
/// of a feed, rendered previews — can.
/// </summary>
/// <remarks>
/// <para>
/// An entry is a file in <see cref="Directory"/>, named by its key: a key that is a plain
/// file name is the name itself, any other — an address, a path — is hashed into one.
/// "Used" is read or written: a read marks the file's write time, so the least recently
/// used is the oldest file whatever the file system does with access times, which many
/// mount without.
/// </para>
/// <para>
/// When a write takes the folder past the limit, entries go from the least recently used
/// until a tenth of the limit is free: a cache full to the byte would trim on every write.
/// Until 0.21 the map's cache emptied its folder whole once it passed its limit — the
/// tiles of the town the user looks at every day went with the rest.
/// </para>
/// <para>
/// A write goes to a temporary file first and takes the entry's name at once, so a reader
/// never sees half an entry, and two writers of one key leave one whole entry. Errors of
/// the file system are a miss: a cache that can't read or write is a slower application,
/// not a broken one. Every member may be called from any thread.
/// </para>
/// </remarks>
public sealed class DiskCache
{
    /// <summary>What a trim leaves: nine tenths of the limit.</summary>
    private const double TrimTarget = 0.9;

    /// <summary>The longest key used as a file name as it is.</summary>
    private const int MaxPlainKeyLength = 120;

    private readonly Lock _lock = new();

    /// <summary>The folder's size as last counted and kept since; −1 — not counted yet.</summary>
    private long _size = -1;

    private int _trimming;

    /// <param name="directory">The folder of the entries. Made by the first write.</param>
    /// <param name="maxBytes">How large the folder may grow.</param>
    public DiskCache(string directory, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        Directory = directory;
        MaxBytes = maxBytes;
    }

    /// <summary>The folder of the entries.</summary>
    public string Directory { get; }

    /// <summary>How large the folder may grow, in bytes.</summary>
    public long MaxBytes { get; }

    /// <summary>The time a use is marked with. For tests, which need the uses in order
    /// without waiting between them.</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>How many bytes the entries take now: counted on the first question,
    /// kept up to date by the writes and the trims since.</summary>
    public long Size
    {
        get
        {
            lock (_lock)
                return EnsureSize();
        }
    }

    /// <summary>An entry's bytes, and the entry marked used; null — there is none, or it
    /// couldn't be read.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        string path = PathOf(key);

        try
        {
            byte[] data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            MarkUsed(path);

            return data;
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            return null;
        }
    }

    /// <summary>Whether there is an entry under the key, without reading or marking it.</summary>
    public bool Contains(string key) => File.Exists(PathOf(key));

    /// <summary>Keep bytes under a key, in place of what was there; the folder is trimmed
    /// after it when it grew past the limit. False — the file system refused: the entry
    /// isn't kept, and nothing else changes.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async Task<bool> WriteAsync(string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        string path = PathOf(key);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            global::System.IO.Directory.CreateDirectory(Directory);

            await File.WriteAllBytesAsync(temporary, data, cancellationToken).ConfigureAwait(false);

            long replaced = Math.Max(0, LengthOf(path));

            // counted before the entry takes its name, or the count would hold it already
            // and the line below would add it a second time. Temporaries aren't counted
            lock (_lock)
                EnsureSize();

            File.Move(temporary, path, overwrite: true);
            MarkUsed(path);

            bool full;

            lock (_lock)
            {
                // a Clear between the two locks dropped the count: count again, the entry with it
                _size = _size < 0 ? EnsureSize() : _size + data.Length - replaced;
                full = _size > MaxBytes;
            }

            if (full) Trim();

            return true;
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            TryDelete(temporary);
            return false;
        }
    }

    /// <summary>Drop an entry. False — there was none, or it couldn't be deleted.</summary>
    public bool Remove(string key)
    {
        string path = PathOf(key);
        long length = LengthOf(path);

        if (length < 0 || !TryDelete(path)) return false;

        lock (_lock)
        {
            if (_size >= 0) _size = Math.Max(0, _size - length);
        }

        return true;
    }

    /// <summary>Drop every entry.</summary>
    public void Clear()
    {
        // the temporaries too: those a run that ended mid-write left behind
        foreach (FileInfo file in Files(withTemporaries: true))
            TryDelete(file.FullName);

        lock (_lock)
            _size = -1;
    }

    /// <summary>Drop the entries used longest ago until the folder is within nine tenths
    /// of the limit — if it is past the limit now. A write calls it when it went past;
    /// call it at a start to bring a folder another run left too large back in.</summary>
    public void Trim()
    {
        // one trim at a time: two writers past the limit at once would both delete
        if (Interlocked.Exchange(ref _trimming, 1) == 1) return;

        try
        {
            FileInfo[] files = Files();
            long total = files.Sum(file => file.Length);

            if (total > MaxBytes)
            {
                long target = (long)(MaxBytes * TrimTarget);

                foreach (FileInfo file in files.OrderBy(file => file.LastWriteTimeUtc))
                {
                    if (total <= target) break;

                    if (TryDelete(file.FullName))
                        total -= file.Length;
                }
            }

            lock (_lock)
                _size = total;
        }
        finally
        {
            Volatile.Write(ref _trimming, 0);
        }
    }

    // ===== files =====

    /// <summary>The file of a key: the key itself when it is a plain file name, a hash of
    /// it otherwise — an address or a path has characters no file name may have.</summary>
    private string PathOf(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        return Path.Combine(Directory, IsPlainName(key) ? key : Hash(key));
    }

    private static bool IsPlainName(string key)
    {
        if (key.Length > MaxPlainKeyLength || key[0] == '.' || key.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (char c in key)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
                return false;

        return true;
    }

    private static string Hash(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>Count the folder, once: under the lock.</summary>
    private long EnsureSize()
    {
        if (_size < 0) _size = Files().Sum(file => file.Length);

        return _size;
    }

    /// <summary>The entries' files: without the temporaries of writes going on, which
    /// are neither counted nor trimmed.</summary>
    private FileInfo[] Files(bool withTemporaries = false)
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(Directory).GetFiles();

            return withTemporaries
                ? files
                : Array.FindAll(files, file => !file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            return [];
        }
    }

    private void MarkUsed(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, Clock());
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            // unmarked, the entry only goes a little earlier than it would have
        }
    }

    /// <summary>A file's length; −1 — there is none.</summary>
    private static long LengthOf(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : -1;
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            return -1;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            return false;
        }
    }

    private static bool IsFileSystemError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or global::System.Security.SecurityException;
}
