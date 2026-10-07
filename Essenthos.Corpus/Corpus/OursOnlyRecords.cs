using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The records a dataset supplied that this project has since read for itself. Such a record is
/// credited as ours, and nothing only the dataset states of it is shown: not its line under the name,
/// not its notes, not its sex or tribe.
///
/// <para>
/// A record qualifies when a dataset supplied it and the text has it by our own reading: at least one
/// of its verses is a row of ours, or it stands in a relationship of ours, every one of which was read
/// from a verse by this project. Its line under the name is its descriptor clauses where it has them,
/// and otherwise the line this project wrote for it from its verses, which is its English line only
/// where this project rendered that line into the readers' languages; where it has neither, it has no
/// line. Its sex and tribe are what our own rows say: the gendered word of a relationship it is the
/// subject of, and the tribe it is of, is the patriarch of or descends from, however many generations
/// up. Where our rows say nothing, or say two things, they are empty.
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

    /// <summary>
    /// What our own rows say of a qualifying record; null where they say nothing or disagree. The line
    /// is this project's own English line, and null where the record has descriptor clauses or no line
    /// of ours.
    /// </summary>
    public sealed record OwnFacts(string? Sex, string? Tribe, string? Line = null);

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
        var ours = (await db.EntityVerses
                .Where(v => ids.Contains(v.EntityId))
                .Select(v => new { v.EntityId, v.Source })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Where(v => Datasets.Of(v.Source) == Datasets.Own)
            .Select(v => v.EntityId)
            .ToHashSet();
        var tied = (await db.EntityRelationships
                .Where(r => (ids.Contains(r.FromEntityId) || ids.Contains(r.ToEntityId))
                            && !r.Source.StartsWith(ShownVerses.Witness))
                .Select(r => new { r.FromEntityId, r.ToEntityId })
                .ToListAsync(cancellationToken))
            .SelectMany(r => new[] { r.FromEntityId, r.ToEntityId })
            .ToHashSet();
        var qualifying = ids.Where(id => ours.Contains(id) || tied.Contains(id)).ToList();
        if (qualifying.Count == 0)
        {
            return [];
        }

        var described = (await db.EntityDescriptors
                .Where(d => qualifying.Contains(d.EntityId))
                .Select(d => d.EntityId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var bare = qualifying.Where(id => !described.Contains(id)).ToList();
        var lines = bare.Count == 0
            ? []
            : await db.EntityDistinguishers
                .Where(d => bare.Contains(d.EntityId) && d.English == d.Entity!.Distinguisher)
                .Select(d => new { d.EntityId, d.English })
                .Distinct()
                .ToDictionaryAsync(d => d.EntityId, d => d.English, cancellationToken);

        var clauses = await OurClauses(db, qualifying, cancellationToken);
        var tribes = await TribeNames(db, cancellationToken);
        var unnamed = qualifying
            .Where(id => !tribes.ContainsKey(id) && Named([.. clauses.Where(c => c.EntityId == id)], tribes).Count == 0)
            .ToList();
        var descent = unnamed.Count == 0 ? [] : await Ancestry(db, unnamed, tribes, paternal: false, cancellationToken);
        var both = unnamed.Where(id => !descent.ContainsKey(id)).ToList();
        var fathers = both.Count == 0 ? [] : await Ancestry(db, both, tribes, paternal: true, cancellationToken);
        var silent = qualifying.Where(id => Sex([.. clauses.Where(c => c.EntityId == id)]) is null).ToList();
        var grammar = await GrammaticalSexes(db, silent, cancellationToken);
        return qualifying.ToDictionary(
            id => supplied[id],
            id =>
            {
                var own = clauses.Where(c => c.EntityId == id).ToList();
                var named = Named(own, tribes);
                return new OwnFacts(
                    Sex(own) ?? grammar.GetValueOrDefault(id),
                    tribes.GetValueOrDefault(id)
                    ?? (named.Count == 1
                        ? named[0]
                        : named.Count == 0 ? descent.GetValueOrDefault(id) ?? fathers.GetValueOrDefault(id) : null),
                    lines.GetValueOrDefault(id));
            });
    }

    /// <summary>A dataset's line, or our own where the record is ours; never the dataset's for a record of ours.</summary>
    public static string? Line(IReadOnlyDictionary<string, OwnFacts> ours, string? slug, string? line) =>
        slug is not null && ours.TryGetValue(slug, out var own) ? own.Line : line;

    /// <summary>
    /// The sex our own rows state of each of these records, for a record no dataset supplied: the
    /// gendered word of a relationship it is the subject of, or else the grammar of the name the
    /// Greek prints for it. A record neither says anything of is left out.
    /// </summary>
    public static async Task<Dictionary<int, string>> SexesOf(
        AppDbContext db,
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var clauses = await OurClauses(db, ids, cancellationToken);
        var stated = ids
            .Select(id => (Id: id, Sex: Sex([.. clauses.Where(c => c.EntityId == id)])))
            .Where(x => x.Sex is not null)
            .ToDictionary(x => x.Id, x => x.Sex!);
        foreach (var (id, sex) in await GrammaticalSexes(db, [.. ids.Where(id => !stated.ContainsKey(id))], cancellationToken))
        {
            stated[id] = sex;
        }

        return stated;
    }

    /// <summary>The witness whose morphology states the gender of the words that name a record.</summary>
    private const string GreekWitness = "NESTLE1904";

    /// <summary>
    /// The sex the Greek's grammar gives a name: the gender Nestle's morphology states of every word of
    /// its own that names the record, where they agree, and only for a word whose lexicon headword is a
    /// name. Bernice is Βερνίκη, feminine. Hebrew grammar is not asked, because it is the form's and not
    /// the bearer's: Hagabah, a man's house, is feminine in BHSA. And a common noun used as a name is not
    /// asked either, so Legion, Λεγιών, is no woman.
    /// </summary>
    private static async Task<Dictionary<int, string>> GrammaticalSexes(
        AppDbContext db,
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.Database
            .SqlQueryRaw<GrammaticalSex>(
                """
                SELECT a.entity_id, min(w.morphology->>'gender') AS gender,
                       count(DISTINCT w.morphology->>'gender')::int AS genders
                FROM word_entity a
                JOIN word w ON w.id = a.word_id
                JOIN text t ON t.id = w.text_id AND t.slug = {0}
                JOIN strong_entry s ON s.strong_number = w.strong_number
                WHERE a.entity_id = ANY({1})
                  AND (a.note IS NULL OR a.note NOT LIKE 'through %')
                  AND w.morphology->>'gender' IN ('masculine', 'feminine')
                  AND lower(left(s.lemma, 1)) <> left(s.lemma, 1)
                GROUP BY a.entity_id
                """,
                GreekWitness, ids.ToArray())
            .ToListAsync(cancellationToken);
        return rows
            .Where(row => row.Genders == 1)
            .ToDictionary(row => row.EntityId, row => row.Gender == "feminine" ? "female" : "male");
    }

    private sealed record GrammaticalSex(int EntityId, string Gender, int Genders);

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

    /// <summary>The descriptor clauses and the relationship rows of which each record is the subject.</summary>
    private static async Task<List<Clause>> OurClauses(
        AppDbContext db,
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        var clauses = await db.EntityDescriptors
            .Where(d => ids.Contains(d.EntityId))
            .Select(d => new Clause(d.EntityId, d.Relation, d.TargetEntityId))
            .ToListAsync(cancellationToken);
        clauses.AddRange(await db.EntityRelationships
            .Where(r => ids.Contains(r.FromEntityId))
            .Select(r => new Clause(r.FromEntityId, r.Type, r.ToEntityId))
            .ToListAsync(cancellationToken));
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
    /// The one tribe a record's line of descent reaches through the relationships, for a record with no
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
                .Concat(await db.EntityRelationships
                    .Where(r => asked.Contains(r.FromEntityId) && ascent.Contains(r.Type))
                    .Select(r => new { From = r.FromEntityId, To = r.ToEntityId })
                    .ToListAsync(cancellationToken))
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
