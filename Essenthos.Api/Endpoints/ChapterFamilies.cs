using Essenthos.Core.Database;

namespace Essenthos.Core.Endpoints;

/// <summary>The trees beside a chapter (<see cref="ChapterFamily"/>), read for one request.</summary>
internal static class ChapterFamilies
{
    /// <param name="stored">Whether the trees the build stored with the chapter's skeleton are read where it stored them.</param>
    internal static async Task<ChapterFamilyResponse> Of(
        AppDbContext db,
        int book,
        int chapter,
        string? language,
        CancellationToken cancellationToken,
        string? prose = null,
        bool stored = false)
    {
        var held = stored ? await ContextEndpoints.Stored(db, book, chapter, cancellationToken) : null;
        var (trees, members) = held is not null
            ? (held.Trees, held.TreeMembers)
            : await ChapterSkeletons.Trees(
                db, (await ChapterSkeletons.NamedAndHow(db, book, chapter, cancellationToken)).Verses.Keys, cancellationToken);
        if (trees.Count == 0)
        {
            return new ChapterFamilyResponse([], []);
        }

        var memberSlugs = members.ToHashSet(StringComparer.Ordinal);

        // A member's family ties reach only the tree, so the tree draws exactly who is in it. Their
        // other ties stay, because a king's crown on his card is read from them — all but the
        // ancestors and descendants, which a tree never draws and a genealogy has hundreds of.
        var family = await FamilyEndpoints.Family(db, members, language, cancellationToken, prose: prose);
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

        return new ChapterFamilyResponse(shown, [.. trees.Select(t => new ChapterTreeResponse(t.Named, t.Between))]);
    }
}

/// <param name="People">Everyone in any of the trees, as the family tree page draws them; their family ties reach only the trees.</param>
/// <param name="Trees">The trees, the largest first.</param>
internal sealed record ChapterFamilyResponse(IList<FamilyMemberResponse> People, IList<ChapterTreeResponse> Trees);

/// <param name="Named">The people of the chapter in this tree, by slug.</param>
/// <param name="Between">The people the tree needs to join them whom the chapter does not name.</param>
internal sealed record ChapterTreeResponse(IList<string> Named, IList<string> Between);
