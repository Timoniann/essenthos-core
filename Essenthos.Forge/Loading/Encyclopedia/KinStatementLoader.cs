using System.Diagnostics;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// One thing a verse says of a person's children, as the file writes it: a count
/// (<c>{"entity": "gideon", "kind": "count", "sons": 70, "reference": "JDG 8:30"}</c>), more children
/// than it names (<c>"kind": "more", "moreSons": true</c>), or a child's place among a parent's
/// children (<c>{"entity": "david", "kind": "order", "ordinal": 7, "parent": "jesse"}</c>).
/// </summary>
/// <param name="Family">
/// Which children a count counts, where a person's verses count two sets of them that add up (Job's
/// before and after his trial). Counts with the same family count the same children.
/// </param>
/// <param name="Reference">The verse, or a passage of up to three, that a person named in it states it in.</param>
/// <param name="DecidedBy">Who read it in the original, and when.</param>
internal sealed record KinStatementRecord(
    string Entity,
    string Kind,
    string Reference,
    string DecidedBy,
    string? Family = null,
    int? Sons = null,
    int? Daughters = null,
    int? Children = null,
    int? Grandsons = null,
    bool MoreSons = false,
    bool MoreDaughters = false,
    int? Ordinal = null,
    string? Parent = null,
    string? Witness = null,
    string? Original = null,
    string? Reason = null);

/// <param name="Refused">
/// Statements naming a record the corpus does not hold, or a verse that names neither the person
/// nor the parent, or that say nothing: each is logged with its line.
/// </param>
internal sealed record KinStatementOutcome(bool NoFiles, int Written, int Removed, int Refused, TimeSpan Elapsed)
{
    public override string ToString() =>
        NoFiles ? "no statements of children's counts and places are on this disk, so the ones loaded are kept"
        : Written == 0 && Removed == 0 && Refused == 0
            ? "the counts and places of children the verses state are already there"
            : $"{Written} statements of children's counts and places written and {Removed} removed, " +
              $"{Refused} refused, in {Elapsed}";
}

/// <summary>
/// What the verses say of a person's children beyond naming them, read from
/// <c>Essenthos/kin/*.jsonl</c>: Gideon's seventy sons, Adam's other sons and daughters, David the
/// seventh son of Jesse. Each statement was read in the original and carries the words it rests on.
///
/// <para>
/// The rows are these files' alone. One no line states any more is removed, one already held is
/// left as it is, and all of it is one save, so a failure leaves the rows as they were and a second
/// run writes nothing.
/// </para>
/// </summary>
internal sealed class KinStatementLoader(AppDbContext db, ILogger<KinStatementLoader> logger)
{
    public static readonly string Folder = Path.Combine("Essenthos", "kin");

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<KinStatementOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(resources, Folder);
        if (!Directory.Exists(directory))
        {
            logger.LogInformation("No statements of children's counts and places at {Directory}", directory);
            return new KinStatementOutcome(true, 0, 0, 0, TimeSpan.Zero);
        }

        return await Load(Read(directory), cancellationToken);
    }

    internal async Task<KinStatementOutcome> Load(IReadOnlyList<KinStatementRecord> records, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var slugs = records.Select(r => r.Entity)
            .Concat(records.Select(r => r.Parent).OfType<string>())
            .Distinct()
            .ToList();
        var ids = await db.Entities
            .Where(e => slugs.Contains(e.Slug) && e.Kind == EntityKind.Person)
            .Select(e => new { e.Slug, e.Id })
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
        var people = ids.Values.ToList();
        var named = (await db.EntityVerses
                .Where(v => people.Contains(v.EntityId))
                .Select(v => new { v.EntityId, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
                .ToListAsync(cancellationToken))
            .Select(v => (v.EntityId, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse))
            .ToHashSet();

        var wanted = new List<EntityKinStatement>();
        var refused = 0;
        foreach (var record in records)
        {
            var problem = Problem(record, ids, named, out var row);
            if (problem is not null)
            {
                logger.LogWarning("A statement of {Entity}'s children at {Reference} is not loaded: {Problem}",
                    record.Entity, record.Reference, problem);
                refused++;
                continue;
            }

            wanted.Add(row!);
        }

        var held = await db.EntityKinStatements.ToListAsync(cancellationToken);
        var kept = new HashSet<EntityKinStatement>(ReferenceEqualityComparer.Instance);
        var written = 0;
        foreach (var row in wanted)
        {
            if (held.FirstOrDefault(h => !kept.Contains(h) && Same(h, row)) is { } same)
            {
                kept.Add(same);
                continue;
            }

            db.EntityKinStatements.Add(row);
            written++;
        }

        var removed = 0;
        foreach (var row in held.Where(h => !kept.Contains(h)))
        {
            db.EntityKinStatements.Remove(row);
            removed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        var outcome = new KinStatementOutcome(false, written, removed, refused, started.Elapsed);
        logger.LogInformation("Children's counts and places the verses state: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>What is wrong with a statement, or null, with the row it is otherwise.</summary>
    private static string? Problem(
        KinStatementRecord record,
        IReadOnlyDictionary<string, int> ids,
        HashSet<(int, int, int, int)> named,
        out EntityKinStatement? row)
    {
        row = null;
        if (!ids.TryGetValue(record.Entity, out var entity))
        {
            return "the corpus holds no person of that slug";
        }

        if (!KinStatementKinds.All.Contains(record.Kind))
        {
            return $"\"{record.Kind}\" is not one of {string.Join(", ", KinStatementKinds.All)}";
        }

        int? parent = null;
        if (record.Kind == KinStatementKinds.Order)
        {
            if (record.Parent is null || !ids.TryGetValue(record.Parent, out var found))
            {
                return "a place among the children names no parent the corpus holds";
            }

            if (record.Ordinal is not >= 1)
            {
                return "a place among the children needs an ordinal from 1";
            }

            parent = found;
        }

        if (record.Kind == KinStatementKinds.Count
            && new[] { record.Sons, record.Daughters, record.Children, record.Grandsons }.All(n => n is not > 0))
        {
            return "a count counts nobody";
        }

        if (record.Kind == KinStatementKinds.More && !record.MoreSons && !record.MoreDaughters)
        {
            return "more children of neither sex";
        }

        if (Citation.Parse(record.Reference) is not { } citation)
        {
            return "the reference cannot be read";
        }

        var about = parent is { } p ? new[] { entity, p } : [entity];
        if (!citation.Verses.Any(v => about.Any(person => named.Contains((person, v.Book, v.Chapter, v.Verse)))))
        {
            return "no verse of the reference names the person it is said of";
        }

        if (string.IsNullOrWhiteSpace(record.DecidedBy))
        {
            return "nobody is said to have read it";
        }

        var (book, chapter, verse) = citation.First;
        row = new EntityKinStatement
        {
            EntityId = entity,
            Kind = record.Kind,
            Family = record.Kind == KinStatementKinds.Count ? record.Family : null,
            Sons = record.Kind == KinStatementKinds.Count ? record.Sons : null,
            Daughters = record.Kind == KinStatementKinds.Count ? record.Daughters : null,
            Children = record.Kind == KinStatementKinds.Count ? record.Children : null,
            Grandsons = record.Kind == KinStatementKinds.Count ? record.Grandsons : null,
            MoreSons = record.Kind == KinStatementKinds.More && record.MoreSons,
            MoreDaughters = record.Kind == KinStatementKinds.More && record.MoreDaughters,
            Ordinal = parent is null ? null : record.Ordinal,
            ParentEntityId = parent,
            CanonicalBook = book,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Citation = citation.Single ? null : record.Reference,
            Witness = record.Witness,
            Original = record.Original,
            Method = LinkMethod.Manual,
            Source = "read from Scripture by " + record.DecidedBy,
            Note = record.Reason,
        };
        return null;
    }

    private static bool Same(EntityKinStatement a, EntityKinStatement b) =>
        a.EntityId == b.EntityId && a.Kind == b.Kind && a.Family == b.Family
        && a.Sons == b.Sons && a.Daughters == b.Daughters && a.Children == b.Children && a.Grandsons == b.Grandsons
        && a.MoreSons == b.MoreSons && a.MoreDaughters == b.MoreDaughters
        && a.Ordinal == b.Ordinal && a.ParentEntityId == b.ParentEntityId
        && a.CanonicalBook == b.CanonicalBook && a.CanonicalChapter == b.CanonicalChapter && a.CanonicalVerse == b.CanonicalVerse
        && a.Citation == b.Citation && a.Witness == b.Witness && a.Original == b.Original
        && a.Method == b.Method && a.Confidence == b.Confidence && a.Source == b.Source && a.Note == b.Note;

    internal static IReadOnlyList<KinStatementRecord> Read(string directory)
    {
        var records = new List<KinStatementRecord>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl").Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file).Where(line => line.Length > 0))
            {
                var record = JsonSerializer.Deserialize<KinStatementRecord>(line, Shape);
                if (record is null || string.IsNullOrWhiteSpace(record.Entity) || string.IsNullOrWhiteSpace(record.Kind)
                    || string.IsNullOrWhiteSpace(record.Reference))
                {
                    throw new InvalidDataException(
                        $"A line of {file} is not a statement of a person's children. Each line must be one JSON " +
                        "object with entity, kind (count, more or order), reference and decidedBy.");
                }

                records.Add(record);
            }
        }

        return records;
    }
}
