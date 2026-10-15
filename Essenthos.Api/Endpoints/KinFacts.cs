using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// How many children a person's verses count, by sex, and whether they say there were more than
/// they name. Counts of the same children give the larger number; counts of different children
/// (Job's before and after his trial) add up.
/// </summary>
internal sealed record ChildrenCount(
    int? Sons,
    int? Daughters,
    int? Children,
    int? Grandsons,
    bool MoreSons,
    bool MoreDaughters,
    IReadOnlyList<VerseRefResponse> Verses)
{
    public bool Counts => Sons is not null || Daughters is not null || Children is not null;

    public bool More => MoreSons || MoreDaughters;
}

/// <summary>A child's place among a parent's children as a verse states it, with every verse that does.</summary>
internal sealed record PlaceAmongChildren(int Ordinal, IReadOnlyList<VerseRefResponse> Verses);

/// <summary>One person a subject's family tree reaches below their children, and how.</summary>
/// <param name="Depth">Generations down, where the tree says; a stated descent or a tribe counts as far.</param>
/// <param name="Tie">The verse of the first step down from the subject: what ties the two.</param>
/// <param name="Named">The verse of the last step, which names the descendant.</param>
internal sealed record Descendant(int Id, int Depth, VerseRefResponse? Tie, VerseRefResponse? Named, double? Confidence);

/// <summary>
/// What a person's line says of their children and their descendants beyond the clauses on the
/// records: the counts and places the verses state, and the descendants the family tree reaches.
/// </summary>
internal static class KinFacts
{
    /// <summary>The counts of children and the places among them the verses state, for these people.</summary>
    public static async Task<(Dictionary<int, ChildrenCount> Counts, Dictionary<(int Child, int Parent), PlaceAmongChildren> Places)> Statements(
        AppDbContext db,
        IReadOnlyCollection<int> people,
        CancellationToken cancellationToken)
    {
        var rows = await db.EntityKinStatements
            .Where(s => people.Contains(s.EntityId))
            .OrderBy(s => s.CanonicalBook).ThenBy(s => s.CanonicalChapter).ThenBy(s => s.CanonicalVerse).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var counts = rows
            .Where(r => r.Kind is KinStatementKinds.Count or KinStatementKinds.More)
            .GroupBy(r => r.EntityId)
            .ToDictionary(person => person.Key, person =>
            {
                var families = person.Where(r => r.Kind == KinStatementKinds.Count).GroupBy(r => r.Family ?? "").ToList();
                int? Summed(Func<EntityKinStatement, int?> field) =>
                    families.Select(family => family.Max(field)).Aggregate((int?)null, (sum, one) => one is null ? sum : (sum ?? 0) + one);
                return new ChildrenCount(
                    Summed(r => r.Sons),
                    Summed(r => r.Daughters),
                    Summed(r => r.Children),
                    Summed(r => r.Grandsons),
                    person.Any(r => r.MoreSons),
                    person.Any(r => r.MoreDaughters),
                    Cited(person));
            });

        var places = rows
            .Where(r => r.Kind == KinStatementKinds.Order && r.ParentEntityId is not null && r.Ordinal is not null)
            .GroupBy(r => (r.EntityId, Parent: r.ParentEntityId!.Value))
            .ToDictionary(place => place.Key, place => new PlaceAmongChildren(place.First().Ordinal!.Value, Cited(place)));

        return (counts, places);
    }

    private static List<VerseRefResponse> Cited(IEnumerable<EntityKinStatement> rows) =>
    [
        .. rows
            .Select(r => BookReferences.At(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse)!)
            .DistinctBy(v => (v.BookOrdinal, v.Chapter, v.Verse)),
    ];

    private static readonly string[] Down =
    [
        DescriptorRelations.SonOf, DescriptorRelations.DaughterOf,
        DescriptorRelations.FatherOf, DescriptorRelations.MotherOf,
        DescriptorRelations.GrandsonOf, DescriptorRelations.GranddaughterOf,
        DescriptorRelations.GrandfatherOf, DescriptorRelations.GrandmotherOf,
        DescriptorRelations.DescendantOf, DescriptorRelations.OfTribe, DescriptorRelations.DescendantsOf,
    ];

    /// <summary>Generations a stated descent or a tribe stands for, where nothing says how many.</summary>
    private const int Far = 3;

    /// <summary>
    /// Everyone the family tree reaches below each of these people: children and grandchildren by
    /// the parent and grandparent relationships, anyone stated to descend from them, and the members
    /// of a tribe whose eponym they are. A tribe is followed only where a record states its member
    /// <em>of the tribe</em>; a people a person belongs to is not their descent.
    /// </summary>
    public static async Task<Dictionary<int, List<Descendant>>> Descendants(
        AppDbContext db,
        IReadOnlyCollection<int> people,
        CancellationToken cancellationToken)
    {
        if (people.Count == 0)
        {
            return [];
        }

        var rows = await db.EntityRelationships
            .Where(r => Down.Contains(r.Type)
                        && !FamilyEndpoints.Deities.Contains(r.From!.Slug)
                        && !FamilyEndpoints.Deities.Contains(r.To!.Slug))
            .Select(r => new
            {
                From = r.FromEntityId,
                To = r.ToEntityId,
                r.Type,
                FromKind = r.From!.Kind,
                ToKind = r.To!.Kind,
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                r.Confidence,
            })
            .ToListAsync(cancellationToken);

        var eponyms = rows
            .Where(r => r.Type == DescriptorRelations.DescendantsOf && r.FromKind == EntityKind.People && r.ToKind == EntityKind.Person)
            .GroupBy(r => r.From)
            .ToDictionary(nation => nation.Key, nation => nation.Select(r => r.To).ToList());

        var children = new Dictionary<int, List<(int Child, int Step, VerseRefResponse? Verse, double? Confidence)>>();
        void Edge(int parent, int child, int step, VerseRefResponse? verse, double? confidence)
        {
            if (parent == child)
            {
                return;
            }

            if (!children.TryGetValue(parent, out var list))
            {
                children[parent] = list = [];
            }

            list.Add((child, step, verse, confidence));
        }

        foreach (var r in rows.Where(r => r.FromKind == EntityKind.Person))
        {
            var verse = BookReferences.At(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse);
            switch (r.Type)
            {
                case DescriptorRelations.SonOf or DescriptorRelations.DaughterOf when r.ToKind == EntityKind.Person:
                    Edge(r.To, r.From, 1, verse, r.Confidence);
                    break;
                case DescriptorRelations.FatherOf or DescriptorRelations.MotherOf when r.ToKind == EntityKind.Person:
                    Edge(r.From, r.To, 1, verse, r.Confidence);
                    break;
                case DescriptorRelations.GrandsonOf or DescriptorRelations.GranddaughterOf when r.ToKind == EntityKind.Person:
                    Edge(r.To, r.From, 2, verse, r.Confidence);
                    break;
                case DescriptorRelations.GrandfatherOf or DescriptorRelations.GrandmotherOf when r.ToKind == EntityKind.Person:
                    Edge(r.From, r.To, 2, verse, r.Confidence);
                    break;
                case DescriptorRelations.DescendantOf when r.ToKind == EntityKind.Person:
                    Edge(r.To, r.From, Far, verse, r.Confidence);
                    break;
                case DescriptorRelations.OfTribe when r.ToKind == EntityKind.Person:
                    Edge(r.To, r.From, Far, verse, r.Confidence);
                    break;
                case DescriptorRelations.OfTribe when eponyms.TryGetValue(r.To, out var ancestors):
                    foreach (var ancestor in ancestors)
                    {
                        Edge(ancestor, r.From, Far, verse, r.Confidence);
                    }

                    break;
            }
        }

        var found = new Dictionary<int, List<Descendant>>();
        foreach (var person in people.Distinct())
        {
            var reached = new Dictionary<int, Descendant>();
            var queue = new PriorityQueue<Descendant, int>();
            queue.Enqueue(new Descendant(person, 0, null, null, null), 0);
            while (queue.TryDequeue(out var at, out _))
            {
                if (reached.ContainsKey(at.Id))
                {
                    continue;
                }

                reached[at.Id] = at;
                foreach (var (child, step, verse, confidence) in children.GetValueOrDefault(at.Id) ?? [])
                {
                    if (reached.ContainsKey(child))
                    {
                        continue;
                    }

                    var sure = (at.Confidence, confidence) switch
                    {
                        (null, var one) => one,
                        (var one, null) => one,
                        var (a, b) => Math.Min(a!.Value, b!.Value),
                    };
                    queue.Enqueue(new Descendant(child, at.Depth + step, at.Tie ?? verse, verse, sure), at.Depth + step);
                }
            }

            reached.Remove(person);
            found[person] = [.. reached.Values];
        }

        return found;
    }

    /// <summary>How many verses name each of these people: what the descendants on a line are chosen by.</summary>
    public static async Task<Dictionary<int, int>> VerseCounts(
        AppDbContext db,
        IReadOnlyCollection<int> people,
        CancellationToken cancellationToken)
    {
        if (people.Count == 0)
        {
            return [];
        }

        var ids = people.Distinct().ToArray();
        return await db.EntityVerses
            .Where(v => ids.Contains(v.EntityId))
            .Select(v => new { v.EntityId, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
            .Distinct()
            .GroupBy(v => v.EntityId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
    }
}
