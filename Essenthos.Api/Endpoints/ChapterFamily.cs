using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The families among the people a chapter names, for trees drawn beside it.
///
/// <para>
/// Two people of the chapter belong to one tree where a path of at most <see cref="MostSteps"/>
/// family ties joins them — parent, child, spouse, brother or sister, read from either end — and
/// the people on the shortest such paths are drawn with them, though the chapter never names them:
/// Noah's grandsons are one tree with Noah because their fathers join them. A parent two members of a
/// tree share is drawn too, so brothers stand under their father rather than side by side with
/// nothing over them. Somebody with nobody of the chapter that near has no tree at all.
/// </para>
///
/// <para>
/// Everything the paths need is two rings of ties out from the chapter's people: a path of three
/// steps between two of them runs through a neighbour of each, and the tie between those neighbours
/// is on the second ring.
/// </para>
/// </summary>
internal static class ChapterFamily
{
    /// <summary>How many family ties apart two of the chapter's people may be and still share a tree.</summary>
    public const int MostSteps = 3;

    /// <summary>Ties naming the other person as a parent, read from the row's first person.</summary>
    private static readonly HashSet<string> ParentTypes =
        new(["son-of", "daughter-of", "son", "daughter", "bearer", "born by"], StringComparer.Ordinal);

    /// <summary>Ties naming the other person as a child, read from the row's first person.</summary>
    private static readonly HashSet<string> ChildTypes =
        new(["father-of", "mother-of", "father", "mother"], StringComparer.Ordinal);

    private static readonly HashSet<string> SideTypes = new(
    [
        "husband-of", "wife-of", "concubine-of", "husband", "wife", "concubine", "concubinator",
        "brother-of", "sister-of", "half-brother-of", "half-sister-of", "brother", "sister", "half-brother",
        "half-sister",
    ], StringComparer.Ordinal);

    /// <summary>Descent over many generations, which a tree draws as the generations themselves.</summary>
    private static readonly HashSet<string> LineageTypes = new(
        ["ancestor", "descendant", "ancestor-of", "descendant-of", "descendants-of"], StringComparer.Ordinal);

    /// <summary>Every tie a family tree draws: the reader's family section's four ranks, and nothing else.</summary>
    internal static readonly string[] Types = [.. ParentTypes, .. ChildTypes, .. SideTypes];

    internal static async Task<ChapterFamilyResponse> Of(
        AppDbContext db,
        int book,
        int chapter,
        string? language,
        CancellationToken cancellationToken)
    {
        var named = (await ContextEndpoints.Named(db, book, chapter, cancellationToken)).Keys.ToList();
        var people = await db.Entities
            .Where(e => named.Contains(e.Slug) && e.Kind == EntityKind.Person)
            .Select(e => new { e.Id, e.Slug })
            .ToListAsync(cancellationToken);
        var chapterIds = people.Select(p => p.Id).ToHashSet();

        var ties = new List<Tie>();
        var reached = new HashSet<int>(chapterIds);
        var ring = chapterIds.ToList();
        for (var step = 0; step < 2 && ring.Count > 0; step++)
        {
            var rows = await db.EntityRelationships
                .Where(r => Types.Contains(r.Type)
                            && (ring.Contains(r.FromEntityId) || ring.Contains(r.ToEntityId))
                            && r.FromEntityId != r.ToEntityId)
                .Select(r => new { r.FromEntityId, r.ToEntityId, r.Type })
                .ToListAsync(cancellationToken);
            var next = new List<int>();
            foreach (var row in rows)
            {
                ties.Add(new Tie(row.FromEntityId, row.ToEntityId, Descent(row.Type)));
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

        var trees = Trees(chapterIds, ties);
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
        var family = await FamilyEndpoints.Family(db, [.. memberSlugs], language, cancellationToken);
        var familyTypes = Types.ToHashSet(StringComparer.Ordinal);
        var shown = family.People
            .Select(p => p with
            {
                Ties =
                [
                    .. p.Ties.Where(t => familyTypes.Contains(t.Type)
                        ? memberSlugs.Contains(t.Slug)
                        : !LineageTypes.Contains(t.Type)),
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

    /// <summary>Which way a tie runs down the generations: 1 where the first person is the parent, −1 the child, 0 neither.</summary>
    private static int Descent(string type) =>
        ChildTypes.Contains(type) ? 1 : ParentTypes.Contains(type) ? -1 : 0;

    /// <param name="Descent">1 where <paramref name="A"/> is the parent of <paramref name="B"/>, −1 where the child, 0 for a spouse or a sibling.</param>
    internal readonly record struct Tie(int A, int B, int Descent);

    /// <param name="Named">The chapter's own people in the tree.</param>
    /// <param name="Between">Everyone drawn to join them whom the chapter does not name.</param>
    internal sealed record Tree(IReadOnlyList<int> Named, IReadOnlyList<int> Between);

    /// <summary>
    /// The chapter's people in trees, the largest first. A person with nobody of the chapter within
    /// <see cref="MostSteps"/> ties is in none.
    /// </summary>
    internal static IReadOnlyList<Tree> Trees(IReadOnlySet<int> chapter, IEnumerable<Tie> ties)
    {
        var next = new Dictionary<int, HashSet<int>>();
        var parents = new Dictionary<int, HashSet<int>>();
        foreach (var tie in ties)
        {
            if (tie.A == tie.B)
            {
                continue;
            }

            Neighbours(next, tie.A).Add(tie.B);
            Neighbours(next, tie.B).Add(tie.A);
            if (tie.Descent != 0)
            {
                var (parent, child) = tie.Descent > 0 ? (tie.A, tie.B) : (tie.B, tie.A);
                Neighbours(parents, child).Add(parent);
            }
        }

        var root = chapter.ToDictionary(id => id, id => id);
        int Find(int id)
        {
            while (root[id] != id)
            {
                id = root[id] = root[root[id]];
            }

            return id;
        }

        var joining = new List<(int From, int Through)>();
        foreach (var from in chapter.Order())
        {
            var (distance, before) = Paths(next, from);
            foreach (var (to, steps) in distance)
            {
                if (to == from || steps == 0 || !chapter.Contains(to))
                {
                    continue;
                }

                var (a, b) = (Find(from), Find(to));
                if (a != b)
                {
                    root[Math.Max(a, b)] = Math.Min(a, b);
                }

                foreach (var through in OnTheWay(before, from, to))
                {
                    joining.Add((from, through));
                }
            }
        }

        var trees = new Dictionary<int, (SortedSet<int> Named, SortedSet<int> Between)>();
        foreach (var id in chapter)
        {
            var tree = Find(id);
            if (!trees.TryGetValue(tree, out var members))
            {
                trees[tree] = members = ([], []);
            }

            members.Named.Add(id);
        }

        foreach (var (from, through) in joining)
        {
            var members = trees[Find(from)];
            if (!chapter.Contains(through))
            {
                members.Between.Add(through);
            }
        }

        foreach (var (named, between) in trees.Values)
        {
            var drawn = named.Concat(between).ToHashSet();
            var shared = drawn
                .SelectMany(member => parents.GetValueOrDefault(member) ?? [])
                .Where(parent => !drawn.Contains(parent))
                .GroupBy(parent => parent)
                .Where(children => children.Count() >= 2)
                .Select(children => children.Key);
            between.UnionWith(shared);
        }

        return
        [
            .. trees.Values
                .Where(t => t.Named.Count >= 2)
                .OrderByDescending(t => t.Named.Count + t.Between.Count)
                .ThenBy(t => t.Named.Min)
                .Select(t => new Tree([.. t.Named], [.. t.Between])),
        ];
    }

    private static HashSet<int> Neighbours(Dictionary<int, HashSet<int>> map, int id)
    {
        if (!map.TryGetValue(id, out var found))
        {
            map[id] = found = [];
        }

        return found;
    }

    /// <summary>How many ties from one person everybody within <see cref="MostSteps"/> is, and who comes just before them on each shortest way.</summary>
    private static (Dictionary<int, int> Distance, Dictionary<int, List<int>> Before) Paths(
        Dictionary<int, HashSet<int>> next,
        int from)
    {
        var distance = new Dictionary<int, int> { [from] = 0 };
        var before = new Dictionary<int, List<int>>();
        var ring = new List<int> { from };
        for (var steps = 1; steps <= MostSteps && ring.Count > 0; steps++)
        {
            var outer = new List<int>();
            foreach (var at in ring)
            {
                foreach (var to in next.GetValueOrDefault(at) ?? [])
                {
                    if (!distance.TryGetValue(to, out var known))
                    {
                        distance[to] = steps;
                        before[to] = [at];
                        outer.Add(to);
                    }
                    else if (known == steps)
                    {
                        before[to].Add(at);
                    }
                }
            }

            ring = outer;
        }

        return (distance, before);
    }

    /// <summary>Everyone on a shortest way between two people, the two left out.</summary>
    private static IEnumerable<int> OnTheWay(Dictionary<int, List<int>> before, int from, int to)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>(before.GetValueOrDefault(to) ?? []);
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            if (at == from || !seen.Add(at))
            {
                continue;
            }

            yield return at;
            foreach (var earlier in before.GetValueOrDefault(at) ?? [])
            {
                stack.Push(earlier);
            }
        }
    }
}

/// <param name="People">Everyone in any of the trees, as the family tree page draws them; their family ties reach only the trees.</param>
/// <param name="Trees">The trees, the largest first.</param>
internal sealed record ChapterFamilyResponse(IList<FamilyMemberResponse> People, IList<ChapterTreeResponse> Trees);

/// <param name="Named">The people of the chapter in this tree, by slug.</param>
/// <param name="Between">The people the tree needs to join them whom the chapter does not name.</param>
internal sealed record ChapterTreeResponse(IList<string> Named, IList<string> Between);
