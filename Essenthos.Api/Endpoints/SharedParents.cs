using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
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

    public const string Male = "male";

    public const string Female = "female";

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

    /// <summary>
    /// The pairs called brother or sister whom the parents the records give show to be half-siblings:
    /// they share a parent and each has a different one of the other sex. A model reading Genesis 42:36
    /// called Benjamin Simeon's brother, while both records give Jacob as their father and Rachel and
    /// Leah as their mothers. A pair that shares no recorded parent is left as it is: its records
    /// disagree about something else, and calling them half-siblings would assert a parent nobody gave.
    /// </summary>
    public static async Task<HashSet<(int One, int Other)>> Halved(
        AppDbContext db,
        IReadOnlyCollection<(int One, int Other)> pairs,
        CancellationToken cancellationToken)
    {
        if (pairs.Count == 0)
        {
            return [];
        }

        var people = pairs.SelectMany(p => new[] { p.One, p.Other }).Distinct().ToList();
        var asChild = await db.EntityDescriptors
            .Where(d => people.Contains(d.EntityId)
                        && (d.Relation == DescriptorRelations.SonOf || d.Relation == DescriptorRelations.DaughterOf))
            .Select(d => new { Child = d.EntityId, Parent = d.TargetEntityId, d.Target!.Sex })
            .ToListAsync(cancellationToken);
        var asParent = await db.EntityDescriptors
            .Where(d => people.Contains(d.TargetEntityId)
                        && (d.Relation == DescriptorRelations.FatherOf || d.Relation == DescriptorRelations.MotherOf))
            .Select(d => new
            {
                Child = d.TargetEntityId,
                Parent = d.EntityId,
                Sex = d.Relation == DescriptorRelations.FatherOf ? Male : Female,
            })
            .ToListAsync(cancellationToken);
        var parents = asChild.Select(r => (r.Child, r.Parent, r.Sex))
            .Concat(asParent.Select(r => (r.Child, r.Parent, Sex: (string?)r.Sex)))
            .Where(r => r.Sex is Male or Female)
            .GroupBy(r => r.Child)
            .ToDictionary(g => g.Key, g => g.Select(r => (r.Parent, Sex: r.Sex!)).Distinct().ToList());

        var halved = new HashSet<(int, int)>();
        foreach (var pair in pairs)
        {
            var mine = parents.GetValueOrDefault(pair.One) ?? [];
            var theirs = parents.GetValueOrDefault(pair.Other) ?? [];
            var shares = mine.Any(m => theirs.Any(t => t.Parent == m.Parent));
            var differ = new[] { Male, Female }.Any(sex =>
                mine.Any(m => m.Sex == sex) && theirs.Any(t => t.Sex == sex)
                && !mine.Any(m => m.Sex == sex && theirs.Any(t => t.Parent == m.Parent)));
            if (shares && differ)
            {
                halved.Add(pair);
            }
        }

        return halved;
    }

    /// <summary>
    /// The brothers and sisters the recorded parents give each of these people: every other child of
    /// a parent the person is recorded the child of. Jesse's record and his sons' give David six
    /// brothers whether or not any record writes <em>brother of</em>. Each comes with the two parent
    /// clauses that make it, the sibling's and the person's own.
    /// </summary>
    public static async Task<List<SiblingByParents>> Siblings(
        AppDbContext db,
        IReadOnlyCollection<int> people,
        CancellationToken cancellationToken)
    {
        if (people.Count == 0)
        {
            return [];
        }

        var mine = await Parenthood(db, d => people.Contains(d.EntityId), d => people.Contains(d.TargetEntityId), cancellationToken);
        var parents = mine.Select(p => p.Parent).Distinct().ToList();
        if (parents.Count == 0)
        {
            return [];
        }

        var theirs = await Parenthood(db, d => parents.Contains(d.TargetEntityId), d => parents.Contains(d.EntityId), cancellationToken);
        var byParent = theirs.GroupBy(p => p.Parent).ToDictionary(g => g.Key, g => g.ToList());

        var siblings = new List<SiblingByParents>();
        foreach (var person in mine.GroupBy(p => p.Child))
        {
            var seen = new HashSet<int> { person.Key };
            foreach (var own in person)
            {
                foreach (var other in byParent.GetValueOrDefault(own.Parent) ?? [])
                {
                    if (seen.Add(other.Child))
                    {
                        var confidence = own.Confidence is { } a && other.Confidence is { } b ? Math.Min(a, b) : own.Confidence ?? other.Confidence;
                        siblings.Add(new SiblingByParents(person.Key, other.Child, other.Verse, own.Verse, confidence));
                    }
                }
            }
        }

        return siblings;
    }

    /// <summary>
    /// Child and parent pairs the descriptor clauses state from either end: the child's <em>son of</em>
    /// and the parent's <em>father of</em>. God is nobody's parent on a family line.
    /// </summary>
    private static async Task<List<(int Child, int Parent, VerseRefResponse Verse, double? Confidence)>> Parenthood(
        AppDbContext db,
        System.Linq.Expressions.Expression<Func<EntityDescriptor, bool>> asChild,
        System.Linq.Expressions.Expression<Func<EntityDescriptor, bool>> asParent,
        CancellationToken cancellationToken)
    {
        var rows = await db.EntityDescriptors
            .Where(asChild)
            .Where(d => (d.Relation == DescriptorRelations.SonOf || d.Relation == DescriptorRelations.DaughterOf)
                        && d.Entity!.Kind == EntityKind.Person && d.Target!.Kind == EntityKind.Person
                        && !FamilyEndpoints.Deities.Contains(d.Target.Slug))
            .Select(d => new { Child = d.EntityId, Parent = d.TargetEntityId, d.CanonicalBook, d.CanonicalChapter, d.CanonicalVerse, d.Confidence })
            .Concat(db.EntityDescriptors
                .Where(asParent)
                .Where(d => (d.Relation == DescriptorRelations.FatherOf || d.Relation == DescriptorRelations.MotherOf)
                            && d.Entity!.Kind == EntityKind.Person && d.Target!.Kind == EntityKind.Person
                            && !FamilyEndpoints.Deities.Contains(d.Entity.Slug))
                .Select(d => new { Child = d.TargetEntityId, Parent = d.EntityId, d.CanonicalBook, d.CanonicalChapter, d.CanonicalVerse, d.Confidence }))
            .ToListAsync(cancellationToken);
        return
        [
            .. rows
                .Where(r => r.Child != r.Parent)
                .Select(r => (r.Child, r.Parent, BookReferences.At(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse)!, r.Confidence)),
        ];
    }

    /// <summary>A father among these parents, else a mother, else nothing: a parent of unknown sex says neither.</summary>
    private static string? Through(IReadOnlyCollection<(int Parent, string? Sex)> parents) =>
        parents.Any(p => p.Sex == Male) ? Father
        : parents.Any(p => p.Sex == Female) ? Mother
        : null;
}

/// <summary>A brother or sister two recorded parent clauses give a person, with the verse of each.</summary>
internal sealed record SiblingByParents(int Subject, int Sibling, VerseRefResponse Theirs, VerseRefResponse Mine, double? Confidence);
