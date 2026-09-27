using Microsoft.Extensions.Caching.Memory;

namespace Essenthos.Core.Pages;

/// <summary>
/// What the page renderer has already worked out, kept for a while and within a budget: a crawler
/// walks every verse of a chapter one after another, and each of those pages would otherwise read
/// the same chapter again.
///
/// It is its own cache rather than the process's shared one, because a size limit on the shared one
/// would bind every other user of it to declare sizes too.
/// </summary>
internal sealed class PageCache : IDisposable
{
    /// <summary>A rendered page: long enough to serve a crawl, short enough that an edit shows the same hour.</summary>
    public static readonly TimeSpan Page = TimeSpan.FromMinutes(10);

    /// <summary>What only a new corpus release changes, and a release restarts the process anyway.</summary>
    public static readonly TimeSpan Corpus = TimeSpan.FromHours(6);

    /// <summary>The budget, in characters of rendered text and a flat allowance for anything else.</summary>
    private const long Budget = 24 * 1024 * 1024;

    private const long ObjectSize = 4 * 1024;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = Budget });

    public async Task<T> Remember<T>(string key, TimeSpan lifetime, Func<Task<T>> make, Func<T, long>? size = null)
    {
        if (_cache.TryGetValue(key, out T? kept) && kept is not null)
        {
            return kept;
        }

        var made = await make();
        _cache.Set(key, made, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = lifetime,
            Size = Math.Min(size?.Invoke(made) ?? ObjectSize, Budget),
        });
        return made;
    }

    public void Dispose() => _cache.Dispose();
}
