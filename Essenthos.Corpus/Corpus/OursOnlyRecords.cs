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
/// gendered word of a relationship it is the subject of, and the tribe it is of, is the patriarch of
/// or descends from, however many generations up. Where our rows say nothing, or say two things,
/// they are empty.
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

    private static readonly HashSet<string> Ascent = ["son-of", "daughter-of", "descendant-of", "descendants-of"];

    /// <summary>
    /// The father's line: a descent row states no line through the fathers, so only a son or a
    /// daughter of a parent whose sex is male by our rows is followed.
    /// </summary>
    private static readonly HashSet<string> Fathers = ["son-of", "daughter-of"];

    /// <summary>How many generations up a line of descent is followed before it is given up.</summary>
    private const int MostGenerations = 40;

    private static readonly HashSet<string> Tribal = [OfTribe, "of-people", "descendant-of", "descendants-of"];

    /// <summary>What our own rows say of a qualifying record; null where they say nothing or disagree.</summary>
    public sealed record OwnFacts(string? Sex, string? Tribe);

    private sealed record Clause(int EntityId, string Relation, int TargetId);

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
        var tribes = await TribeNames(db, cancellationToken);
        var unnamed = qualifying
            .Where(id => !tribes.ContainsKey(id) && Named([.. clauses.Where(c => c.EntityId == id)], tribes).Count == 0)
            .ToList();
        var descent = unnamed.Count == 0 ? [] : await Ancestry(db, unnamed, tribes, paternal: false, cancellationToken);
        var both = unnamed.Where(id => !descent.ContainsKey(id)).ToList();
        var fathers = both.Count == 0 ? [] : await Ancestry(db, both, tribes, paternal: true, cancellationToken);
        return qualifying.ToDictionary(
            id => supplied[id],
            id =>
            {
                var own = clauses.Where(c => c.EntityId == id).ToList();
                var named = Named(own, tribes);
                return new OwnFacts(
                    Sex(own),
                    tribes.GetValueOrDefault(id)
                    ?? (named.Count == 1
                        ? named[0]
                        : named.Count == 0 ? descent.GetValueOrDefault(id) ?? fathers.GetValueOrDefault(id) : null));
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

    /// <summary>The tribes a record's own clauses name.</summary>
    private static List<string> Named(List<Clause> clauses, IReadOnlyDictionary<int, string> tribes) =>
        clauses
            .Where(c => Tribal.Contains(c.Relation))
            .Select(c => tribes.GetValueOrDefault(c.TargetId))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Our descriptor clauses and our relationship rows, of which each record is the subject.</summary>
    private static async Task<List<Clause>> OurClauses(
        AppDbContext db,
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        var clauses = await db.EntityDescriptors
            .Where(d => ids.Contains(d.EntityId))
            .Select(d => new Clause(d.EntityId, d.Relation, d.TargetEntityId))
            .ToListAsync(cancellationToken);
        var rows = await db.EntityRelationships
            .Where(r => ids.Contains(r.FromEntityId) && !r.Withdrawn)
            .Select(r => new { r.FromEntityId, r.Type, r.ToEntityId, r.Source })
            .ToListAsync(cancellationToken);
        clauses.AddRange(rows
            .Where(r => Datasets.Of(r.Source) == Datasets.Own)
            .Select(r => new Clause(r.FromEntityId, r.Type, r.ToEntityId)));
        return clauses;
    }

    /// <summary>
    /// The tribe each tribal record stands for. A tribe is what our descriptors say someone is of:
    /// its patriarch is the tribe, and a people that descends from exactly one man is that man's
    /// tribe. A people descending from two stands for none.
    /// </summary>
    private static async Task<Dictionary<int, string>> TribeNames(AppDbContext db, CancellationToken cancellationToken)
    {
        var tribal = await db.EntityDescriptors
            .Where(d => d.Relation == OfTribe)
            .Select(d => d.TargetEntityId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var records = await db.Entities
            .Where(e => tribal.Contains(e.Id))
            .Select(e => new { e.Id, e.Name, e.Kind })
            .ToListAsync(cancellationToken);
        var names = records.Where(e => e.Kind != EntityKind.People).ToDictionary(e => e.Id, e => e.Name);
        var peoples = records.Where(e => e.Kind == EntityKind.People).Select(e => e.Id).ToList();
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

    /// <summary>
    /// The one tribe a record's line of descent reaches through our rows, for a record with no
    /// clause of its own naming one: up its fathers and ancestors until a tribe's patriarch (or a
    /// people that is a tribe) is met, each path stopping where it meets one. Empty where two
    /// different tribes are reached, where none is, and past <see cref="MostGenerations"/>. With
    /// <paramref name="paternal"/> it follows the father's line only.
    /// </summary>
    private static async Task<Dictionary<int, string>> Ancestry(
        AppDbContext db,
        IReadOnlyCollection<int> ids,
        IReadOnlyDictionary<int, string> tribes,
        bool paternal,
        CancellationToken cancellationToken)
    {
        var ascent = paternal ? Fathers : Ascent;
        var reached = ids.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var seen = ids.ToDictionary(id => id, id => new HashSet<int> { id });
        var frontier = ids.ToDictionary(id => id, id => new List<int> { id });
        for (var generation = 0; generation < MostGenerations && frontier.Count > 0; generation++)
        {
            var asked = frontier.Values.SelectMany(nodes => nodes).Distinct().ToList();
            var edges = (await db.EntityDescriptors
                    .Where(d => asked.Contains(d.EntityId) && ascent.Contains(d.Relation))
                    .Select(d => new { From = d.EntityId, To = d.TargetEntityId })
                    .ToListAsync(cancellationToken))
                .Concat((await db.EntityRelationships
                        .Where(r => asked.Contains(r.FromEntityId) && !r.Withdrawn && ascent.Contains(r.Type))
                        .Select(r => new { From = r.FromEntityId, To = r.ToEntityId, r.Source })
                        .ToListAsync(cancellationToken))
                    .Where(r => Datasets.Of(r.Source) == Datasets.Own)
                    .Select(r => new { r.From, r.To }))
                .Distinct()
                .ToLookup(edge => edge.From, edge => edge.To);

            var male = paternal
                ? (await OurClauses(db, [.. edges.SelectMany(edge => edge).Distinct()], cancellationToken))
                    .GroupBy(c => c.EntityId)
                    .Where(group => Sex([.. group]) == "male")
                    .Select(group => group.Key)
                    .ToHashSet()
                : null;
            foreach (var id in frontier.Keys.ToList())
            {
                var next = new List<int>();
                foreach (var parent in frontier[id].SelectMany(node => edges[node]).Distinct())
                {
                    if (male is not null && !tribes.ContainsKey(parent) && !male.Contains(parent))
                    {
                        continue;
                    }

                    if (tribes.TryGetValue(parent, out var tribe))
                    {
                        reached[id].Add(tribe);
                    }
                    else if (seen[id].Add(parent))
                    {
                        next.Add(parent);
                    }
                }

                if (next.Count == 0)
                {
                    frontier.Remove(id);
                }
                else
                {
                    frontier[id] = next;
                }
            }
        }

        return reached.Where(one => one.Value.Count == 1).ToDictionary(one => one.Key, one => one.Value.First());
    }
}
