using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Essenthos.Core.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Pages;

/// <summary>
/// When each sitemap list last changed, judged by a hash of the addresses it lists rather than by when the
/// release was built: a release changes one list in a hundred, and a date that moves on every list with
/// every release is one a search engine learns to ignore. The hash and the moment it was first seen are
/// kept in the accounts database (<see cref="SitemapList"/>), the one store that outlives a release.
///
/// <para>
/// Where that store cannot be reached, or is behind its migrations, a list carries no date rather than a
/// wrong one, and the next request tries again.
/// </para>
/// </summary>
internal sealed class SitemapDates(IServiceScopeFactory scopes, ILogger<SitemapDates> logger)
{
    private readonly ConcurrentDictionary<string, (string Hash, DateTimeOffset At)> _known = new(StringComparer.Ordinal);

    public static string Hash(IReadOnlyList<string> addresses)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var address in addresses)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(address));
            sha.AppendData("\n"u8);
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset())[..32];
    }

    /// <summary>When the list last changed: when this hash of it was first seen; null where that cannot be said.</summary>
    public async Task<DateTimeOffset?> ChangedAt(string name, string hash, CancellationToken cancellationToken)
    {
        if (_known.TryGetValue(name, out var known) && known.Hash == hash)
        {
            return known.At;
        }

        try
        {
            using var scope = scopes.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var now = DateTimeOffset.UtcNow;
            // The first process to see a new hash dates it; another seeing the same hash keeps that date.
            await accounts.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO sitemap_list (name, hash, changed_at) VALUES ({name}, {hash}, {now})
                 ON CONFLICT (name) DO UPDATE SET hash = excluded.hash, changed_at = excluded.changed_at
                 WHERE sitemap_list.hash <> excluded.hash
                 """, cancellationToken);
            var at = await accounts.SitemapLists.AsNoTracking()
                .Where(list => list.Name == name && list.Hash == hash)
                .Select(list => (DateTimeOffset?)list.ChangedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (at is { } dated)
            {
                _known[name] = (hash, dated);
            }

            return at;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "When the sitemap list {Name} last changed could not be read or kept; it is listed without a date", name);
            return null;
        }
    }
}
