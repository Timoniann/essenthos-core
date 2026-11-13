using Microsoft.Extensions.Caching.Memory;

namespace Essenthos.Core.Pages;

/// <summary>
/// What the page renderer has already worked out, kept for a while and within a budget in bytes: a
/// crawler walks every verse of a chapter one after another, and each of those pages would otherwise
/// read the same chapter again.
///
/// What a page is made from (a chapter's verses, the list of texts, a text's book names) is kept at a
/// higher priority than the pages themselves, so when the budget is full a written page goes first:
/// it is one address, while a chapter serves every verse of it. A page carries the answers it was
/// written from and can weigh a hundred kilobytes, so pages are sized by what they hold, not counted.
///
/// It is its own cache rather than the process's shared one, because a size limit on the shared one
/// would bind every other user of it to declare sizes too.
/// </summary>
internal sealed class PageCache : IDisposable
{
    /// <summary>The budget in megabytes; 64 where unset.</summary>
    public const string BudgetKey = "Site:PageCacheMegabytes";

    /// <summary>A rendered page: long enough to serve a crawl, short enough that an edit shows the same hour.</summary>
    public static readonly TimeSpan Page = TimeSpan.FromMinutes(10);

    /// <summary>What only a new corpus release changes, and a release restarts the process anyway.</summary>
    public static readonly TimeSpan Corpus = TimeSpan.FromHours(6);

    private const long DefaultMegabytes = 64;

    /// <summary>A flat allowance for anything kept without a size of its own.</summary>
    private const long ObjectSize = 4 * 1024;

    private readonly long _budget;

    private readonly MemoryCache _cache;

    public PageCache(IConfiguration? configuration = null)
    {
        _budget = Math.Max(1, configuration?.GetValue(BudgetKey, DefaultMegabytes) ?? DefaultMegabytes) * 1024 * 1024;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = _budget });
    }

    public async Task<T> Remember<T>(
        string key, TimeSpan lifetime, Func<Task<T>> make, Func<T, long>? size = null,
        CacheItemPriority priority = CacheItemPriority.High)
    {
        if (_cache.TryGetValue(key, out T? kept) && kept is not null)
        {
            return kept;
        }

        var made = await make();
        _cache.Set(key, made, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = lifetime,
            Size = Math.Min(size?.Invoke(made) ?? ObjectSize, _budget),
            Priority = priority,
        });
        return made;
    }

    public void Dispose() => _cache.Dispose();
}
