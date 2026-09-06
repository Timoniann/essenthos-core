using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Refused">Why each rejected clause was rejected, so a bad pass is visible as a shape rather than a number.</param>
internal sealed record DescriptorRefusals(
    int UnknownEntity,
    int UnknownRelation,
    int UnresolvedTarget,
    int UnmatchedReference,
    int WithoutConfidence)
{
    public int Total =>
        UnknownEntity + UnknownRelation + UnresolvedTarget + UnmatchedReference + WithoutConfidence;

    public override string ToString() =>
        $"{UnknownEntity} for an entity the encyclopedia does not hold, " +
        $"{UnknownRelation} stating a relation that is not in the vocabulary, " +
        $"{UnresolvedTarget} naming a target no entity answers to, " +
        $"{UnmatchedReference} whose reference names no verse that entity is named in, and " +
        $"{WithoutConfidence} carrying no confidence";
}

internal sealed record DescriptorOutcome(
    bool AlreadyLoaded,
    bool NoDescriptors,
    int Files,
    int Records,
    int Replaced,
    int Skipped,
    int Described,
    int Clauses,
    int Forms,
    int Unresolved,
    DescriptorRefusals Refused,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded ? "every descriptor on this disk is already loaded"
        : NoDescriptors ? "no descriptor files are on this disk, so nothing was loaded from them"
        : $"{Described} entities describe themselves in {Clauses} clauses with {Forms} name forms, " +
          $"read from {Records} records over {Files} files in {Elapsed}. " +
          $"{Skipped} entities were already described and were left alone, {Replaced} records were " +
          $"superseded by a later file, and the passes report {Unresolved} targets the encyclopedia " +
          $"does not hold. Refused: {Refused}.";
}

/// <summary>
/// The description this corpus writes for itself, read from what a generation pass produced.
///
/// What a reader is shown under a name today is <see cref="Entity.Distinguisher"/> — one English
/// sentence per entity, imported whole. It is somebody else's prose, so translating it makes a
/// derivative; it is prose, so the Moses inside it cannot be clicked; and it is English, so every
/// reader of the Ukrainian text meets an English sentence about a Hebrew name. What this loads is
/// what that sentence would be made of: ordered clauses, each naming an entity the encyclopedia
/// holds and the verse it was read from, out of which a line is rendered per language.
///
/// <para>
/// **It refuses three things, loudly and with a count for each, and refusing is the point.** A
/// relation outside the closed vocabulary has no phrasing in any language and would print its own
/// identifier at a reader. A target no entity answers to cannot be a link, and a clause that cannot
/// be a link is prose, which is the thing being replaced. A reference naming no verse the entity is
/// named in is a citation a reader cannot follow, which is worse than none. Each is counted rather
/// than merely skipped, because a number that quietly went to zero is how a pass stops being read.
/// </para>
///
/// <para>
/// **The guard is on this loader's own rows, and per entity.** Guarding on "does the table hold
/// anything" is what left a cold database with no name resolutions at all when another loader
/// started writing into the same table a step earlier (PRB-0343). Per entity rather than per table
/// because the passes arrive in batches over days: a second batch must load beside the first, and
/// an entity already described is left exactly as it is.
/// </para>
/// </summary>
internal sealed class EntityDescriptorLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<EntityDescriptorLoader> logger)
{
    /// <summary>
    /// How every source string written here begins. It is what tells this loader's rows from any
    /// other row carrying the same method, and what the per-entity guard matches on.
    /// </summary>
    public const string SourcePrefix = "read from Scripture by";

    public async Task<DescriptorOutcome> Load(
        string resources,
        CancellationToken cancellationToken = default)
    {
        var directory = Where(resources);
        if (!Directory.Exists(directory))
        {
            logger.LogInformation(
                "No descriptor files at {Directory}, so no entity describes itself yet. They are "
                + "what a generation pass writes and they stay out of the repository; point "
                + "\"{Key}\" at a directory of {Pattern} files, in the shape DOC-0191 states, to "
                + "load them",
                directory,
                DescriptorFiles.ConfigurationKey,
                DescriptorFiles.FilePattern);
            return Nothing(alreadyLoaded: false);
        }

        var started = Stopwatch.StartNew();
        var (records, files, replaced) = DescriptorFiles.Read(directory);
        if (records.Count == 0)
        {
            return Nothing(alreadyLoaded: false) with { Files = files, Elapsed = started.Elapsed };
        }

        var entities = await Slugs(records, cancellationToken);
        var described = await Described(entities.Values, cancellationToken);
        var occurrences = await Occurrences(entities.Values, cancellationToken);

        int unknownEntity = 0, unknownRelation = 0, unresolvedTarget = 0;
        int unmatchedReference = 0, withoutConfidence = 0;
        int skipped = 0, unresolved = 0, clauses = 0, forms = 0, wrote = 0;

        foreach (var record in records)
        {
            unresolved += record.Unresolved?.Count ?? 0;

            if (!entities.TryGetValue(record.Entity, out var entityId))
            {
                unknownEntity += record.Claims?.Count ?? 1;
                continue;
            }

            if (described.Contains(entityId))
            {
                skipped++;
                continue;
            }

            var source = Source(record);
            var ordinal = 0;
            foreach (var claim in record.Claims ?? [])
            {
                if (!DescriptorRelations.All.Contains(claim.Relation))
                {
                    unknownRelation++;
                    continue;
                }

                if (!entities.TryGetValue(claim.Target, out var targetId))
                {
                    unresolvedTarget++;
                    continue;
                }

                if (Reference(claim.Reference) is not { } verse)
                {
                    unmatchedReference++;
                    continue;
                }

                if (!occurrences.Contains((entityId, verse.Book, verse.Chapter, verse.Verse)))
                {
                    unmatchedReference++;
                    continue;
                }

                if (claim.Confidence is not { } confidence || confidence is < 0 or > 1)
                {
                    withoutConfidence++;
                    continue;
                }

                db.EntityDescriptors.Add(new EntityDescriptor
                {
                    EntityId = entityId,
                    Ordinal = ++ordinal,
                    Relation = claim.Relation,
                    TargetEntityId = targetId,
                    CanonicalBook = verse.Book,
                    CanonicalChapter = verse.Chapter,
                    CanonicalVerse = verse.Verse,
                    Method = LinkMethod.ModelReading,
                    Confidence = confidence,
                    Source = source,
                    Note = claim.Reason,
                    Claims =
                    [
                        new EntityDescriptorClaim
                        {
                            Method = LinkMethod.ModelReading,
                            Confidence = confidence,
                            Source = source,
                            Note = claim.Reason,
                        },
                    ],
                });

                clauses++;
            }

            forms += Forms(entityId, record, source);
            if (ordinal > 0)
            {
                wrote++;
            }
        }

        var refused = new DescriptorRefusals(
            unknownEntity, unknownRelation, unresolvedTarget, unmatchedReference, withoutConfidence);

        if (clauses > 0 || forms > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var outcome = new DescriptorOutcome(
            AlreadyLoaded: clauses == 0 && forms == 0 && skipped == records.Count,
            NoDescriptors: false,
            files, records.Count, replaced, skipped, wrote, clauses, forms, unresolved, refused,
            started.Elapsed);

        if (refused.Total > 0)
        {
            logger.LogWarning(
                "{Count} descriptor clauses were refused and are not in the corpus: {Refused}. "
                + "Each is a claim the generation pass made that this corpus cannot render or "
                + "cannot let a reader check; DOC-0191 states what a clause has to be",
                refused.Total,
                refused);
        }

        logger.LogInformation("Described: {Outcome}", outcome);
        return outcome;
    }

    private static DescriptorOutcome Nothing(bool alreadyLoaded) =>
        new(alreadyLoaded, !alreadyLoaded, 0, 0, 0, 0, 0, 0, 0, 0,
            new DescriptorRefusals(0, 0, 0, 0, 0), TimeSpan.Zero);

    /// <summary>
    /// Where the files are. Under the corpus sources by default, in this project's own folder:
    /// they are the one part of that tree nobody else made.
    /// </summary>
    private string Where(string resources)
    {
        var configured = configuration[DescriptorFiles.ConfigurationKey];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(resources, DescriptorFiles.DefaultFolder)
            : configured;
    }

    /// <summary>
    /// Who said it, in the words a reader gets: which model, and when it was asked. Taken from the
    /// record rather than declared here, so a second pass under a different model cannot be stored
    /// under the first one's name.
    /// </summary>
    private static string Source(DescriptorRecord record) =>
        $"{SourcePrefix} {record.Model}, asked {record.AskedAt}";

    /// <summary>
    /// The name forms this record carries, as rows. A language with no form for an entity is a gap
    /// the rendering fills with the English name; an empty form is not a gap but a mistake, and is
    /// not stored.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Dictionary<string, string>> NoNames =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

    private int Forms(int entityId, DescriptorRecord record, string source)
    {
        var written = 0;
        foreach (var (language, cases) in record.Names ?? NoNames)
        {
            foreach (var (grammaticalCase, form) in cases)
            {
                if (!GrammaticalCases.All.Contains(grammaticalCase) || string.IsNullOrWhiteSpace(form))
                {
                    continue;
                }

                db.EntityNameForms.Add(new EntityNameForm
                {
                    EntityId = entityId,
                    Language = language,
                    GrammaticalCase = grammaticalCase,
                    Form = form,
                    Method = LinkMethod.ModelReading,
                    Confidence = 1,
                    Source = source,
                });

                written++;
            }
        }

        return written;
    }

    /// <summary>
    /// Which entity each slug the files mention is — the entities described and every target they
    /// name — in one query rather than one per claim.
    /// </summary>
    private async Task<Dictionary<string, int>> Slugs(
        IReadOnlyCollection<DescriptorRecord> records,
        CancellationToken cancellationToken)
    {
        var slugs = records
            .SelectMany(r => new[] { r.Entity }.Concat(r.Claims?.Select(c => c.Target) ?? []))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
    }

    /// <summary>
    /// The entities this loader has already written for. On this loader's own source and not on the
    /// table, so a second batch loads beside the first and so nothing another writer puts in the
    /// table can make this one believe its work is done.
    ///
    /// <para>
    /// Both tables, because a record can produce name forms and no clauses at all — which is what
    /// every record naming somebody else's genitive does — and asking only about the clauses would
    /// make those records look untouched and write their forms again on every boot.
    /// </para>
    /// </summary>
    private async Task<HashSet<int>> Described(
        IReadOnlyCollection<int> entities,
        CancellationToken cancellationToken)
    {
        var clauses = await db.EntityDescriptors
            .Where(d => entities.Contains(d.EntityId) && d.Source.StartsWith(SourcePrefix))
            .Select(d => d.EntityId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var forms = await db.EntityNameForms
            .Where(f => entities.Contains(f.EntityId) && f.Source.StartsWith(SourcePrefix))
            .Select(f => f.EntityId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. clauses, .. forms];
    }

    /// <summary>
    /// Every verse each of these entities is named in, so a reference can be checked against the
    /// encyclopedia's own record rather than believed.
    /// </summary>
    private async Task<HashSet<(int Entity, int Book, int Chapter, int Verse)>> Occurrences(
        IReadOnlyCollection<int> entities,
        CancellationToken cancellationToken)
    {
        var rows = await db.EntityVerses
            .Where(v => entities.Contains(v.EntityId))
            .Select(v => new { v.EntityId, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => (r.EntityId, r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))];
    }

    /// <summary>
    /// <c>NUM 10:29</c> as three numbers. The book is whatever the corpus already answers to — a
    /// USFM code, an abbreviation or a name — so a pass writing <c>1SA 1:1</c> and one writing
    /// <c>1 Samuel 1:1</c> both resolve, and neither is silently dropped for spelling.
    /// </summary>
    internal static (int Book, int Chapter, int Verse)? Reference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var split = reference.Trim().LastIndexOf(' ');
        if (split <= 0)
        {
            return null;
        }

        var address = reference.Trim()[(split + 1)..].Split(':');
        if (address.Length != 2
            || !int.TryParse(address[0], out var chapter)
            || !int.TryParse(address[1], out var verse))
        {
            return null;
        }

        var book = BookReferences.ResolveOrdinal(reference.Trim()[..split]);
        return book is { } ordinal ? (ordinal, chapter, verse) : null;
    }
}
