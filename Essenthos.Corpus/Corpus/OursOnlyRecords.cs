using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The records a dataset supplied that this project has since read for itself. Such a record's
/// line under the name and its notes are still the dataset's words, and its verses and descriptor
/// are ours; with the owner's switch on it is credited as ours and leaves out what only the dataset
/// states.
///
/// <para>
/// A record qualifies when a dataset supplied it, it has a descriptor, and at least one of its
/// verses is a row of ours. Sex and tribe are kept only where our own descriptor says the same.
/// </para>
/// </summary>
internal static class OursOnlyRecords
{
    /// <summary>What a qualifying record is credited to; the dataset it belongs to is <see cref="Datasets.Own"/>.</summary>
    public const string Credit = "Essenthos, the line under the name and the verses read from the text";

    private const string SuppliedBy = "bibledata";

    private const string OfTribe = "of-tribe";

    private static readonly HashSet<string> Male =
    [
        "son-of", "father-of", "husband-of", "grandfather-of", "brother-of", "half-brother-of",
        "father-in-law-of", "son-in-law-of", "nephew-of", "uncle-of", "grandson-of",
        "brother-in-law-of", "king-of",
    ];

    private static readonly HashSet<string> Female =
    [
        "daughter-of", "mother-of", "wife-of", "sister-of", "half-sister-of", "grandmother-of",
        "queen-of", "concubine-of", "granddaughter-of", "daughter-in-law-of", "mother-in-law-of",
    ];

    /// <summary>What our own descriptor says of a qualifying record; null where it says nothing.</summary>
    public sealed record OwnFacts(string? Sex, string? Tribe);

    /// <summary>The qualifying records among these slugs, by slug, with what our descriptor states of each.</summary>
    public static async Task<Dictionary<string, OwnFacts>> Among(
        AppDbContext db,
        IReadOnlyCollection<string> slugs,
        CancellationToken cancellationToken)
    {
        if (slugs.Count == 0)
        {
            return [];
        }

        var supplied = (await db.Entities
                .Where(e => slugs.Contains(e.Slug) && e.Source.StartsWith("BibleData"))
                .Select(e => new { e.Id, e.Slug, e.Source })
                .ToListAsync(cancellationToken))
            .Where(e => Datasets.Of(e.Source) == SuppliedBy)
            .ToDictionary(e => e.Id, e => e.Slug);
        if (supplied.Count == 0)
        {
            return [];
        }

        var ids = supplied.Keys.ToList();
        var descriptors = (await db.EntityDescriptors
                .Where(d => ids.Contains(d.EntityId))
                .Select(d => new { d.EntityId, d.Relation, Target = d.Target!.Name })
                .ToListAsync(cancellationToken))
            .ToLookup(d => d.EntityId);
        var ours = (await db.EntityVerses
                .Where(v => ids.Contains(v.EntityId))
                .Select(v => new { v.EntityId, v.Source })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Where(v => Datasets.Of(v.Source) == Datasets.Own)
            .Select(v => v.EntityId)
            .ToHashSet();

        return ids
            .Where(id => ours.Contains(id) && descriptors[id].Any())
            .ToDictionary(
                id => supplied[id],
                id =>
                {
                    var clauses = descriptors[id].ToList();
                    var sexes = clauses
                        .Select(c => Male.Contains(c.Relation) ? "male" : Female.Contains(c.Relation) ? "female" : null)
                        .OfType<string>()
                        .Distinct()
                        .ToList();
                    return new OwnFacts(
                        sexes.Count == 1 ? sexes[0] : null,
                        clauses.FirstOrDefault(c => c.Relation == OfTribe)?.Target);
                });
    }

    /// <summary>A dataset's fact, kept only where our own descriptor states the same.</summary>
    public static string? Agreed(string? dataset, string? ours) =>
        dataset is not null && string.Equals(dataset, ours, StringComparison.OrdinalIgnoreCase) ? dataset : null;
}
