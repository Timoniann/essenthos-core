namespace Essenthos.Core.Descriptions;

/// <summary>
/// The family ties a tree draws and the connected families they make: every relationship of the four family ranks
/// (<see cref="ChapterFamily.Types"/>) that names no deity and not the same person twice, and for every person the family
/// those ties join them to. A person with no family tie is a family of one, so every person has a family and a tree.
///
/// <para>
/// A family is numbered by its first person in ordinal order of slugs, from 1, so the numbers are a function of the ties
/// and not of the order anything was read in.
/// </para>
/// </summary>
internal static class FamilyGraph
{
    public const string TiesProduct = "records/derived/family-ties";

    public const string MembersProduct = "records/derived/family-members";

    /// <summary>One relationship as <c>records/facts/relationships</c> holds it.</summary>
    public sealed record Relationship(string From, string Relation, string To, (int Book, int Chapter, int Verse)? Verse);

    /// <summary>A family tie with the family it is in.</summary>
    public sealed record Tie(string From, string Relation, string To, (int Book, int Chapter, int Verse)? Verse, int Component);

    public sealed record Member(string Slug, int Component);

    public sealed record Families(IReadOnlyList<Tie> Ties, IReadOnlyList<Member> Members);

    public static Families Of(IEnumerable<Relationship> relationships, IEnumerable<string> persons)
    {
        var types = ChapterFamily.Types.ToHashSet(StringComparer.Ordinal);
        var deities = ChapterFamily.Deities.ToHashSet(StringComparer.Ordinal);
        // One row per pair and relation: where two verses state it, the first verse, as a record's side lists it.
        var ties = relationships
            .Where(r => types.Contains(r.Relation) && r.From != r.To && !deities.Contains(r.From) && !deities.Contains(r.To))
            .GroupBy(r => (r.From, r.Relation, r.To))
            .Select(g => g.OrderBy(r => r.Verse is null).ThenBy(r => r.Verse).First())
            .ToList();

        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        string Find(string slug)
        {
            while (parent[slug] != slug)
            {
                parent[slug] = parent[parent[slug]];
                slug = parent[slug];
            }

            return slug;
        }

        foreach (var slug in persons.Concat(ties.SelectMany(t => new[] { t.From, t.To })))
        {
            parent.TryAdd(slug, slug);
        }

        foreach (var tie in ties)
        {
            var (a, b) = (Find(tie.From), Find(tie.To));
            if (a != b)
            {
                // The smaller slug leads, so the root of a family is its first person whatever the order of union.
                if (string.CompareOrdinal(a, b) < 0)
                {
                    parent[b] = a;
                }
                else
                {
                    parent[a] = b;
                }
            }
        }

        var numbers = parent.Keys
            .GroupBy(Find, StringComparer.Ordinal)
            .Select(g => g.Min(StringComparer.Ordinal)!)
            .Order(StringComparer.Ordinal)
            .Select((first, index) => (first, index))
            .ToDictionary(p => p.first, p => p.index + 1, StringComparer.Ordinal);
        var firsts = parent.Keys.GroupBy(Find, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => numbers[g.Min(StringComparer.Ordinal)!], StringComparer.Ordinal);

        return new Families(
            [.. ties.Select(t => new Tie(t.From, t.Relation, t.To, t.Verse, firsts[Find(t.From)]))
                .OrderBy(t => t.From, StringComparer.Ordinal).ThenBy(t => t.Relation, StringComparer.Ordinal).ThenBy(t => t.To, StringComparer.Ordinal)],
            [.. parent.Keys.Order(StringComparer.Ordinal).Select(slug => new Member(slug, firsts[Find(slug)]))]);
    }
}
