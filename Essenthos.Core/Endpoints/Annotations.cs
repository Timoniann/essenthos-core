using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Who a word names, as the reader is told it.
///
/// A word may carry several annotations, because several methods may speak about it and the schema
/// keeps them apart rather than letting the first one win. What a reader is shown is one answer, so
/// the rule for picking it lives here and nowhere else: a word read in a chapter and the same word
/// opened in the panel must not name two different people.
///
/// <para>
/// The rule is the one <see cref="ClaimStanding"/> already states for links — testimony outranks
/// inference and a person outranks both — with one addition. Where two methods of the same standing
/// name two different entities, nothing is shown. That is not a gap to be filled later by picking
/// the more frequent or the nearer one; it is the corpus saying it does not know, which is the
/// answer for the twenty-three men called Zechariah and the reason the reader can trust the card
/// when it does appear.
/// </para>
/// </summary>
internal static class Annotations
{
    /// <summary>
    /// The annotations on a set of words, in one query. A chapter is a thousand words and this is
    /// asked for every one of them, so it is a single indexed read rather than a lookup per word.
    /// </summary>
    public static async Task<Dictionary<long, EntityRefResponse>> Of(
        AppDbContext db,
        IEnumerable<long> wordIds,
        CancellationToken cancellationToken)
    {
        var ids = wordIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.WordEntities
            .Where(a => ids.Contains(a.WordId))
            .Select(a => new Claimed(
                a.WordId, a.Method, a.Confidence, a.Entity!.Kind, a.Entity.Slug, a.Entity.Name))
            .ToListAsync(cancellationToken);

        return Settle(rows);
    }

    /// <summary>The same for one word, which is what the word panel asks.</summary>
    public static async Task<EntityRefResponse?> Of(
        AppDbContext db,
        long wordId,
        CancellationToken cancellationToken)
    {
        var found = await Of(db, [wordId], cancellationToken);
        return found.GetValueOrDefault(wordId);
    }

    /// <summary>
    /// One answer per word, or none where the strongest two disagree. Ordered by standing first and
    /// by confidence within it, so a hand correction beats a resolution however sure the resolution
    /// was — the standing is what the method knew before it started, and no confidence can make a
    /// guess into a reading.
    /// </summary>
    private static Dictionary<long, EntityRefResponse> Settle(List<Claimed> rows) =>
        rows.GroupBy(row => row.WordId)
            .Select(group => new
            {
                group.Key,
                Ranked = group
                    .OrderByDescending(row => ClaimStanding.Of(row.Method))
                    .ThenByDescending(row => row.Confidence ?? 1)
                    .ToList(),
            })
            .Where(word => word.Ranked.Count == 1 || !Disputed(word.Ranked[0], word.Ranked[1]))
            .ToDictionary(word => word.Key, word => Show(word.Ranked[0]));

    private static bool Disputed(Claimed best, Claimed next) =>
        best.Slug != next.Slug
        && ClaimStanding.Of(best.Method) == ClaimStanding.Of(next.Method)
        && (best.Confidence ?? 1) == (next.Confidence ?? 1);

    private static EntityRefResponse Show(Claimed claimed) =>
        new(EnumSpelling.Of(claimed.Kind), claimed.Slug, claimed.Name)
        {
            Method = EnumSpelling.Of(claimed.Method),
            Confidence = claimed.Confidence,
        };

    /// <summary>What a claim about one word says, flattened for the pick.</summary>
    private sealed record Claimed(
        long WordId, LinkMethod Method, double? Confidence, EntityKind Kind, string Slug, string Name);
}
