using Xunit;
using ZeppelinForms.Core.Caching;

namespace ZeppelinForms.UnitTests;

/// <summary>0.21: files kept on disk within a size, the least recently used going first
/// when it is passed — the map's tile cache emptied its folder whole.</summary>
public sealed class DiskCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "zf-disk-cache-" + Guid.NewGuid().ToString("N"));

    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A cache whose uses are a minute apart, in the order they are made.</summary>
    private DiskCache Create(long maxBytes) => new(_directory, maxBytes)
    {
        Clock = () => _now = _now.AddMinutes(1),
    };

    private static byte[] Bytes(int count, byte value = 1) => Enumerable.Repeat(value, count).ToArray();

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task AnEntryReadsBackAsWritten()
    {
        DiskCache cache = Create(1000);

        Assert.True(await cache.WriteAsync("tile_1_2_3.png", Bytes(10, 7), TestContext.Current.CancellationToken));

        Assert.Equal(Bytes(10, 7), await cache.ReadAsync("tile_1_2_3.png", TestContext.Current.CancellationToken));
        Assert.Equal(10, cache.Size);
    }

    [Fact]
    public async Task AMissingEntryIsNull() =>
        Assert.Null(await Create(1000).ReadAsync("nothing", TestContext.Current.CancellationToken));

    [Fact]
    public async Task PastTheLimitTheLeastRecentlyUsedGoFirst()
    {
        DiskCache cache = Create(250);
        CancellationToken token = TestContext.Current.CancellationToken;

        await cache.WriteAsync("a", Bytes(100), token);
        await cache.WriteAsync("b", Bytes(100), token);

        // a read is a use: "a" is now the newer of the two
        await cache.ReadAsync("a", token);

        // 300 bytes: past the limit, trimmed to nine tenths of it — "b" goes, and only "b"
        await cache.WriteAsync("c", Bytes(100), token);

        Assert.True(cache.Contains("a"));
        Assert.False(cache.Contains("b"));
        Assert.True(cache.Contains("c"));
        Assert.Equal(200, cache.Size);
    }

    [Fact]
    public async Task AnEntryWrittenAgainReplacesItsBytesAndSize()
    {
        DiskCache cache = Create(1000);
        CancellationToken token = TestContext.Current.CancellationToken;

        await cache.WriteAsync("a", Bytes(100), token);
        await cache.WriteAsync("a", Bytes(40, 2), token);

        Assert.Equal(Bytes(40, 2), await cache.ReadAsync("a", token));
        Assert.Equal(40, cache.Size);
    }

    [Fact]
    public async Task AKeyThatIsNoFileNameIsHashed()
    {
        DiskCache cache = Create(1000);
        const string key = "https://tile.example.org/10/5/6.png?x=1";

        Assert.True(await cache.WriteAsync(key, Bytes(5), TestContext.Current.CancellationToken));

        Assert.Equal(Bytes(5), await cache.ReadAsync(key, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Directory.GetFiles(_directory), path => Path.GetFileName(path).Contains('/', StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFolderLeftTooLargeIsTrimmedOnRequest()
    {
        DiskCache first = Create(10_000);
        CancellationToken token = TestContext.Current.CancellationToken;

        for (int i = 0; i < 5; i++)
            await first.WriteAsync($"e{i}", Bytes(100), token);

        // another run, a smaller limit: the oldest go until nine tenths of it is used
        DiskCache second = Create(300);
        second.Trim();

        Assert.Equal(200, second.Size);
        Assert.False(second.Contains("e0"));
        Assert.True(second.Contains("e4"));
    }

    [Fact]
    public async Task RemoveAndClearDropEntries()
    {
        DiskCache cache = Create(1000);
        CancellationToken token = TestContext.Current.CancellationToken;

        await cache.WriteAsync("a", Bytes(10), token);
        await cache.WriteAsync("b", Bytes(10), token);

        Assert.True(cache.Remove("a"));
        Assert.False(cache.Remove("a"));
        Assert.Equal(10, cache.Size);

        cache.Clear();

        Assert.False(cache.Contains("b"));
        Assert.Equal(0, cache.Size);
    }
}
