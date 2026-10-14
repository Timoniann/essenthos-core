using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Which parent two half-siblings share, read from the parents the corpus records for them.
///
/// <para>
/// A half-sibling relation does not say which parent it runs through, and Ukrainian cannot say it
/// without choosing: <em>єдинокровний</em> by the father, <em>єдиноутробний</em> by the mother. The
/// owner's rule: a parent both are recorded the child of is the shared one; failing that, a father
/// either is recorded the child of, and failing that a mother. Abram and Sarai are both Terah's
/// children, so Abram is Sarah's <em>єдинокровний брат</em>, as Genesis 20:12 has it.
/// </para>
/// </summary>
internal static class SharedParents
{
    public const string Father = "father";

    public const string Mother = "mother";

    private const string Male = "male";

    private const string Female = "female";

    /// <summary>
    /// The parent each pair shares, keyed by the first's slug and the second's id, holding only the
    /// pairs where one is known.
    /// </summary>
    public static async Task<Dictionary<(string Slug, int Other), string>> Of(
        AppDbContext db,
        IReadOnlyCollection<(string Slug, int Other)> pairs,
        CancellationToken cancellationToken)
    {
        if (pairs.Count == 0)
        {
            return [];
        }

        var slugs = pairs.Select(p => p.Slug).Distinct().ToList();
        var ids = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new { e.Slug, e.Id })
            .ToDictionaryAsync(e => e.Slug, e => e.Id, cancellationToken);
        var people = ids.Values.Concat(pairs.Select(p => p.Other)).Distinct().ToList();

        var asChild = await db.EntityRelationships
            .Where(r => people.Contains(r.FromEntityId)
                        && (r.Type == DescriptorRelations.SonOf || r.Type == DescriptorRelations.DaughterOf))
            .Select(r => new { Child = r.FromEntityId, Parent = r.ToEntityId, r.To!.Sex })
            .ToListAsync(cancellationToken);
        var asParent = await db.EntityRelationships
            .Where(r => people.Contains(r.ToEntityId)
                        && (r.Type == DescriptorRelations.FatherOf || r.Type == DescriptorRelations.MotherOf))
            .Select(r => new
            {
                Child = r.ToEntityId,
                Parent = r.FromEntityId,
                Sex = r.Type == DescriptorRelations.FatherOf ? Male : Female,
            })
            .ToListAsync(cancellationToken);

        var parents = asChild.Select(r => (r.Child, r.Parent, r.Sex))
            .Concat(asParent.Select(r => (r.Child, r.Parent, Sex: (string?)r.Sex)))
            .GroupBy(r => r.Child)
            .ToDictionary(g => g.Key, g => g.Select(r => (r.Parent, r.Sex)).Distinct().ToList());

        var shared = new Dictionary<(string, int), string>();
        foreach (var pair in pairs)
        {
            if (!ids.TryGetValue(pair.Slug, out var one))
            {
                continue;
            }

            var mine = parents.GetValueOrDefault(one) ?? [];
            var theirs = parents.GetValueOrDefault(pair.Other) ?? [];
            var common = mine.Where(m => theirs.Any(t => t.Parent == m.Parent)).ToList();
            var parent = Through(common) ?? Through([.. mine, .. theirs]);
            if (parent is not null)
            {
                shared[pair] = parent;
            }
        }

        return shared;
    }

    /// <summary>A father among these parents, else a mother, else nothing: a parent of unknown sex says neither.</summary>
    private static string? Through(IReadOnlyCollection<(int Parent, string? Sex)> parents) =>
        parents.Any(p => p.Sex == Male) ? Father
        : parents.Any(p => p.Sex == Female) ? Mother
        : null;
}
