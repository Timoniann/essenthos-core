using Essenthos.Core.Corpus;
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

    /// <summary>The same, keyed by slug, for rows that name their records by address.</summary>
    public static async Task<Dictionary<string, string>> OfSlugs(
        AppDbContext db,
        IReadOnlyCollection<string> slugs,
        string? language,
        CancellationToken cancellationToken)
    {
        var local = EntityNames.Local(language);
        if (local is null || slugs.Count == 0)
        {
            return [];
        }

        return await db.EntityDistinguishers
            .Where(d => slugs.Contains(d.Entity!.Slug) && d.Language == local && d.English == d.Entity!.Distinguisher)
            .Select(d => new { d.Entity!.Slug, d.Text })
            .ToDictionaryAsync(d => d.Slug, d => d.Text, StringComparer.Ordinal, cancellationToken);
    }

    /// <summary>
    /// A record's rendered line where it may show one: a record of ours that has no line of ours
    /// shows none, whatever was once rendered of the dataset's.
    /// </summary>
    public static string? For(
        IReadOnlyDictionary<string, OursOnlyRecords.OwnFacts> ours,
        IReadOnlyDictionary<string, string> lines,
        string? slug) =>
        slug is null || (ours.TryGetValue(slug, out var own) && own.Line is null)
            ? null
            : lines.GetValueOrDefault(slug);
}
