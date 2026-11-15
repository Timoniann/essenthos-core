using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>The trees beside a chapter (<see cref="ChapterFamily"/>), read for one request.</summary>
internal static class ChapterFamilies
{
    internal static async Task<ChapterFamilyResponse> Of(
        AppDbContext db,
        int book,
        int chapter,
        string? language,
        CancellationToken cancellationToken,
        string? prose = null)
    {
        var named = (await ContextEndpoints.Named(db, book, chapter, cancellationToken)).Keys.ToList();
        var people = await db.Entities
            .Where(e => named.Contains(e.Slug) && e.Kind == EntityKind.Person)
            .Select(e => new { e.Id, e.Slug })
            .ToListAsync(cancellationToken);
        var chapterIds = people.Select(p => p.Id).ToHashSet();

        var ties = new List<ChapterFamily.Tie>();
        var reached = new HashSet<int>(chapterIds);
        var ring = chapterIds.ToList();
        for (var step = 0; step < 2 && ring.Count > 0; step++)
        {
            var rows = await db.EntityRelationships
                .Where(r => ChapterFamily.Types.Contains(r.Type)
                            && (ring.Contains(r.FromEntityId) || ring.Contains(r.ToEntityId))
                            && r.FromEntityId != r.ToEntityId
                            && !ChapterFamily.Deities.Contains(r.From!.Slug)
                            && !ChapterFamily.Deities.Contains(r.To!.Slug))
                .Select(r => new { r.FromEntityId, r.ToEntityId, r.Type })
                .ToListAsync(cancellationToken);
            var next = new List<int>();
            foreach (var row in rows)
            {
                ties.Add(new ChapterFamily.Tie(row.FromEntityId, row.ToEntityId, ChapterFamily.Descent(row.Type)));
                foreach (var id in (int[])[row.FromEntityId, row.ToEntityId])
                {
                    if (reached.Add(id))
                    {
                        next.Add(id);
                    }
                }
            }

            ring = next;
        }

        var trees = ChapterFamily.Trees(chapterIds, ties);
        if (trees.Count == 0)
        {
            return new ChapterFamilyResponse([], []);
        }

        var members = trees.SelectMany(t => t.Named.Concat(t.Between)).Distinct().ToList();
        var slugOf = await db.Entities
            .Where(e => members.Contains(e.Id))
            .Select(e => new { e.Id, e.Slug })
            .ToDictionaryAsync(e => e.Id, e => e.Slug, cancellationToken);
        var memberSlugs = slugOf.Values.ToHashSet(StringComparer.Ordinal);

        // A member's family ties reach only the tree, so the tree draws exactly who is in it. Their
        // other ties stay, because a king's crown on his card is read from them — all but the
        // ancestors and descendants, which a tree never draws and a genealogy has hundreds of.
        var family = await FamilyEndpoints.Family(db, [.. memberSlugs], language, cancellationToken, prose: prose);
        var familyTypes = ChapterFamily.Types.ToHashSet(StringComparer.Ordinal);
        var shown = family.People
            .Select(p => p with
            {
                Ties =
                [
                    .. p.Ties.Where(t => familyTypes.Contains(t.Type)
                        ? memberSlugs.Contains(t.Slug)
                        : !ChapterFamily.LineageTypes.Contains(t.Type)),
                ],
            })
            .ToList();

        return new ChapterFamilyResponse(
            shown,
            [
                .. trees.Select(t => new ChapterTreeResponse(
                    [.. t.Named.Select(id => slugOf[id]).Order(StringComparer.Ordinal)],
                    [.. t.Between.Select(id => slugOf[id]).Order(StringComparer.Ordinal)])),
            ]);
    }
}

/// <param name="People">Everyone in any of the trees, as the family tree page draws them; their family ties reach only the trees.</param>
/// <param name="Trees">The trees, the largest first.</param>
internal sealed record ChapterFamilyResponse(IList<FamilyMemberResponse> People, IList<ChapterTreeResponse> Trees);

/// <param name="Named">The people of the chapter in this tree, by slug.</param>
/// <param name="Between">The people the tree needs to join them whom the chapter does not name.</param>
internal sealed record ChapterTreeResponse(IList<string> Named, IList<string> Between);
