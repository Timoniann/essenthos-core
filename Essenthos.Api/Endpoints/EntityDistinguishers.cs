using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The line under a record's name in a reader's language, where this corpus wrote the line and has
/// rendered it into that language. A dataset's own line has none and stays in its own words.
/// </summary>
internal static class EntityDistinguishers
{
    /// <summary>
    /// The rendered lines of a set of entities, keyed by entity id, holding only those that have one
    /// and whose English is still the line it renders. One indexed read.
    /// </summary>
    public static async Task<Dictionary<int, string>> Of(
        AppDbContext db,
        IReadOnlyCollection<int> entities,
        string? language,
        CancellationToken cancellationToken)
    {
        var local = EntityNames.Local(language);
        if (local is null || entities.Count == 0)
        {
            return [];
        }

        return await db.EntityDistinguishers
            .Where(d => entities.Contains(d.EntityId) && d.Language == local && d.English == d.Entity!.Distinguisher)
            .ToDictionaryAsync(d => d.EntityId, d => d.Text, cancellationToken);
    }
}
