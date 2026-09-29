using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The records a dataset supplied that this project has since read for itself. Such a record's
/// line under the name and its notes are still the dataset's words, and its verses and descriptor
/// are ours, so it is credited as ours and leaves out what only the dataset states.
///
/// <para>
/// A record qualifies when a dataset supplied it, it has a descriptor, and at least one of its
/// verses is a row of ours. Its sex and tribe are what our own rows say, never the dataset's: the
/// gendered word of a relationship it is the subject of, and the tribe it is of or descends from.
/// Where our rows say nothing, or say two things, they are empty.
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

    private static readonly HashSet<string> Tribal = [OfTribe, "of-people", "descendant-of", "descendants-of"];

    /// <summary>What our own rows say of a qualifying record; null where they say nothing or disagree.</summary>
    public sealed record OwnFacts(string? Sex, string? Tribe);

    private sealed record Clause(int EntityId, string Relation, int TargetId, string Target, EntityKind Kind);

    /// <summary>The qualifying records among these slugs, by slug, with what our rows state of each.</summary>
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
        var described = (await db.EntityDescriptors
                .Where(d => ids.Contains(d.EntityId))
                .Select(d => d.EntityId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var ours = (await db.EntityVerses
                .Where(v => ids.Contains(v.EntityId))
                .Select(v => new { v.EntityId, v.Source })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Where(v => Datasets.Of(v.Source) == Datasets.Own)
            .Select(v => v.EntityId)
            .ToHashSet();
        var qualifying = ids.Where(id => described.Contains(id) && ours.Contains(id)).ToList();
        if (qualifying.Count == 0)
        {
            return [];
        }

        var clauses = await OurClauses(db, qualifying, cancellationToken);
        var tribes = await TribeNames(db, clauses, cancellationToken);
        return qualifying.ToDictionary(
            id => supplied[id],
            id =>
            {
                var own = clauses.Where(c => c.EntityId == id).ToList();
                return new OwnFacts(Sex(own), Tribe(own, tribes));
            });
    }

    /// <summary>A dataset's line, or nothing where the record is ours.</summary>
    public static string? Line(IReadOnlyDictionary<string, OwnFacts> ours, string? slug, string? line) =>
        slug is not null && ours.ContainsKey(slug) ? null : line;

    private static string? Sex(List<Clause> clauses)
    {
        var sexes = clauses
            .Select(c => Male.Contains(c.Relation) ? "male" : Female.Contains(c.Relation) ? "female" : null)
            .OfType<string>()
            .Distinct()
            .ToList();
        return sexes.Count == 1 ? sexes[0] : null;
    }

    private static string? Tribe(List<Clause> clauses, IReadOnlyDictionary<int, string> tribes)
    {
        var named = clauses
            .Where(c => Tribal.Contains(c.Relation))
            .Select(c => tribes.GetValueOrDefault(c.TargetId))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return named.Count == 1 ? named[0] : null;
    }

    /// <summary>Our descriptor clauses and our relationship rows, of which each record is the subject.</summary>
    private static async Task<List<Clause>> OurClauses(
        AppDbContext db,
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        var clauses = await db.EntityDescriptors
            .Where(d => ids.Contains(d.EntityId))
            .Select(d => new Clause(d.EntityId, d.Relation, d.TargetEntityId, d.Target!.Name, d.Target.Kind))
            .ToListAsync(cancellationToken);
        var rows = await db.EntityRelationships
            .Where(r => ids.Contains(r.FromEntityId) && !r.Withdrawn)
            .Select(r => new { r.FromEntityId, r.Type, r.ToEntityId, Target = r.To!.Name, r.To.Kind, r.Source })
            .ToListAsync(cancellationToken);
        clauses.AddRange(rows
            .Where(r => Datasets.Of(r.Source) == Datasets.Own)
            .Select(r => new Clause(r.FromEntityId, r.Type, r.ToEntityId, r.Target, r.Kind)));
        return clauses;
    }

    /// <summary>
    /// The tribe each target stands for. A tribe is what our descriptors say someone is of: its
    /// patriarch is the tribe, and a people that descends from exactly one man is that man's tribe.
    /// Anything else, and a people descending from two, stands for none.
    /// </summary>
    private static async Task<Dictionary<int, string>> TribeNames(
        AppDbContext db,
        List<Clause> clauses,
        CancellationToken cancellationToken)
    {
        var targets = clauses.Where(c => Tribal.Contains(c.Relation)).DistinctBy(c => c.TargetId).ToList();
        if (targets.Count == 0)
        {
            return [];
        }

        var tribal = (await db.EntityDescriptors
                .Where(d => d.Relation == OfTribe)
                .Select(d => d.TargetEntityId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var names = targets
            .Where(t => tribal.Contains(t.TargetId) && t.Kind != EntityKind.People)
            .ToDictionary(t => t.TargetId, t => t.Target);
        var peoples = targets
            .Where(t => tribal.Contains(t.TargetId) && t.Kind == EntityKind.People)
            .Select(t => t.TargetId)
            .ToList();
        if (peoples.Count == 0)
        {
            return names;
        }

        var descent = (await db.EntityDescriptors
                .Where(d => peoples.Contains(d.EntityId) && d.Relation == "descendants-of"
                            && d.Target!.Kind == EntityKind.Person)
                .Select(d => new { d.EntityId, Patriarch = d.Target!.Name })
                .ToListAsync(cancellationToken))
            .GroupBy(d => d.EntityId)
            .Where(group => group.Select(d => d.Patriarch).Distinct().Count() == 1);
        foreach (var people in descent)
        {
            names[people.Key] = people.First().Patriarch;
        }

        return names;
    }
}
