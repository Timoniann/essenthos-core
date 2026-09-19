using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Corpus;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Refused">Why each rejected clause was rejected, so a bad pass is visible as a shape rather than a number.</param>
internal sealed record DescriptorRefusals(
    int UnknownEntity,
    int UnknownRelation,
    int UnresolvedTarget,
    int UnmatchedReference,
    int WithoutConfidence,
    int Unaccompanied,
    int Misplaced)
{
    public int Total =>
        UnknownEntity + UnknownRelation + UnresolvedTarget + UnmatchedReference + WithoutConfidence
        + Unaccompanied + Misplaced;

    public override string ToString() =>
        $"{UnknownEntity} for an entity the encyclopedia does not hold, " +
        $"{UnknownRelation} stating a relation that is not in the vocabulary, " +
        $"{UnresolvedTarget} naming a target no entity answers to, " +
        $"{UnmatchedReference} whose reference names no verse that entity is named in, and " +
        $"{WithoutConfidence} carrying no confidence, and " +
        $"{Unaccompanied} reading company out of a verse that speaks of none, and " +
        $"{Misplaced} placing something somewhere that is not a place";
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
    /// <summary>
    /// Entities whose loaded description was replaced because a later pass answered about them
    /// again. Distinct from <c>Replaced</c>, which counts records one file beat on disk: this one
    /// counts what a reader was already being shown and is not any more.
    /// </summary>
    public int Superseded { get; init; }

    /// <summary>Rows removed to make room for them, clauses only — forms and relationships go too.</summary>
    public int Forgotten { get; init; }

    public override string ToString() =>
        AlreadyLoaded ? "every descriptor on this disk is already loaded"
        : NoDescriptors ? "no descriptor files are on this disk, so nothing was loaded from them"
        : $"{Described} entities describe themselves in {Clauses} clauses with {Forms} name forms, " +
          $"read from {Records} records over {Files} files in {Elapsed}. " +
          $"{Skipped} entities were already described under the same pass and were left alone, " +
          $"{Replaced} records were superseded by a later file, {Superseded} entities were " +
          $"described again by a later pass and their {Forgotten} loaded clauses were dropped for " +
          $"the new answer, and the passes report {Unresolved} targets the encyclopedia does not " +
          $"hold. Refused: {Refused}.";
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
/// started writing into the same table a step earlier. Per entity rather than per table because
/// the passes arrive in batches over days: a second batch must load beside the first, and an
/// entity already described is left exactly as it is.
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
    public const string SourcePrefix = Sources.DescriptorReadingPrefix;

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
                + "\"{Key}\" at a directory of {Pattern} files, one newline-delimited JSON object "
                + "per entity, to load them",
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

        // A record for an entity this loader already described, published in a file it has not
        // read, is a re-ask: the vocabulary widened or the prompt changed and the pass was asked
        // again. The file is what tells them apart -- both passes ran as claude-sonnet-5 on
        // 2026-09-09, so the credit a reader sees is the same string on both.
        // The file set settles that by taking the file that sorts last, and the database has to
        // settle it the same way or the two disagree — the later file wins on disk and the reader
        // keeps the first answer for ever. Same source, and there is nothing to do: that
        // is an ordinary restart over files already loaded.
        var superseded = records
            .Where(record => entities.TryGetValue(record.Entity, out var id)
                && described.TryGetValue(id, out var loaded)
                && !loaded.Contains(record.File))
            .Select(record => entities[record.Entity])
            .ToHashSet();

        // In one transaction with the writes below, and this is not a precaution. `Forget` deletes
        // through the database rather than through the change tracker, so it lands the moment it
        // runs: the first time this ran without one, every entity was superseded -- the run column
        // was new and null on every row -- the removal committed, one insert hit a unique index and
        // the whole corpus's own descriptions were gone until the next boot rebuilt them from the
        // files. Nothing was lost that the files do not hold, and that is luck rather than design.
        await using var replacing = superseded.Count == 0
            ? null
            : await db.Database.BeginTransactionAsync(cancellationToken);

        var forgotten = superseded.Count == 0
            ? 0
            : await Forget(superseded, cancellationToken);
        foreach (var id in superseded)
        {
            described.Remove(id);
        }

        // Which name forms the corpus already holds for these entities, whoever wrote them, so a
        // record cannot collide with the name-forms pass on the one row a language and a case may
        // have. Read after the removal above, so a form this loader has just dropped is free again.
        var taken = await Taken(entities.Values, cancellationToken);

        var occurrences = await Occurrences(entities.Values, cancellationToken);
        var refiled = await Refiled(cancellationToken);
        var accompanied = await Accompanied(records, cancellationToken);

        // What kind each target is, so a clause that places something can be asked to point at a
        // place. Every entity the records name, not only the described ones, because a target is
        // as often somebody else's entity as it is one of these.
        var kinds = await db.Entities
            .Where(e => entities.Values.Contains(e.Id))
            .Select(e => new { e.Id, e.Kind })
            .ToDictionaryAsync(e => e.Id, e => e.Kind, cancellationToken);

        int unknownEntity = 0, unknownRelation = 0, unresolvedTarget = 0;
        int unmatchedReference = 0, withoutConfidence = 0, unaccompanied = 0, misplaced = 0;
        int skipped = 0, unresolved = 0, clauses = 0, forms = 0, wrote = 0;

        foreach (var record in records)
        {
            unresolved += record.Unresolved?.Count ?? 0;

            if (!entities.TryGetValue(record.Entity, out var entityId))
            {
                unknownEntity += record.Claims?.Count ?? 1;
                continue;
            }

            var source = Source(record);
            if (described.TryGetValue(entityId, out var loaded) && loaded.Contains(record.File))
            {
                skipped++;
                continue;
            }

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

                // Somewhere that is not a place. A tribe is a person, a people and a territory at
                // once, and a pass reading *Bethlehem, a city in Judah* means the territory while
                // the name it reaches for is most often the patriarch's. A reader who follows that
                // link arrives at Jacob's son, which is a false statement made by a link rather
                // than by a sentence. The right record usually exists, and choosing it
                // here would be this loader naming a target the pass did not.
                if (PlacingRelations.All.Contains(claim.Relation)
                    && kinds.GetValueOrDefault(targetId) != EntityKind.Place)
                {
                    misplaced++;
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

                // A clause read while the dataset still filed this verse under the wrong man of the
                // name points at him; the verse is his namesake's, and so is the clause's target.
                if (refiled.TryGetValue((targetId, verse.Book, verse.Chapter, verse.Verse), out var namesake))
                {
                    targetId = namesake;
                }

                // A person's decision is not a model's reading: it is stored as manual, credited to
                // whoever decided it, and outranks a dataset's row. It carries a confidence only
                // where the person said the verse does not settle it.
                var decided = !string.IsNullOrWhiteSpace(claim.DecidedBy);
                double? confidence = claim.Confidence;
                if (!decided && (confidence is null or < 0 or > 1))
                {
                    withoutConfidence++;
                    continue;
                }

                var method = decided ? LinkMethod.Manual : LinkMethod.ModelReading;
                var credit = decided ? $"{SourcePrefix} {claim.DecidedBy}" : source;
                if (decided && confidence is < 0 or > 1)
                {
                    confidence = null;
                }

                // A companionship the cited verse does not mention. This one relation is read out
                // of adjacency in a list of names more than any other -- 109 of the 229 clauses the
                // widened vocabulary produced -- and every wrong one sampled was a list: *Shallum,
                // Amariah, and Joseph*, *Hodijah, Hashum, Bezai*, *Daniel, Hananiah, Mishael, and
                // Azariah*. Standing beside somebody in a register is not keeping company with
                // them, and a reader cannot check a claim the verse does not make.
                //
                // A necessary condition and not a sufficient one: it removes the list-shaped
                // reading, which is the whole of the measured failure, and leaves the judgement of
                // a verse that does speak of company to the pass and to its confidence.
                if (claim.Relation == DescriptorRelations.CompanionOf
                    && accompanied is not null
                    && !accompanied.Contains((verse.Book, verse.Chapter, verse.Verse)))
                {
                    unaccompanied++;
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
                    Method = method,
                    Confidence = confidence,
                    Source = credit,
                    Run = record.File,
                    Note = claim.Reason,
                    Claims =
                    [
                        new EntityDescriptorClaim
                        {
                            Method = method,
                            Confidence = confidence,
                            Source = credit,
                            Note = claim.Reason,
                        },
                    ],
                });

                clauses++;
            }

            forms += Forms(entityId, record, source, taken);
            if (ordinal > 0)
            {
                wrote++;
            }
        }

        var refused = new DescriptorRefusals(
            unknownEntity, unknownRelation, unresolvedTarget, unmatchedReference, withoutConfidence,
            unaccompanied, misplaced);

        if (clauses > 0 || forms > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (replacing is not null)
        {
            await replacing.CommitAsync(cancellationToken);
        }

        var outcome = new DescriptorOutcome(
            AlreadyLoaded: clauses == 0 && forms == 0 && skipped == records.Count,
            NoDescriptors: false,
            files, records.Count, replaced, skipped, wrote, clauses, forms, unresolved, refused,
            started.Elapsed)
        {
            Superseded = superseded.Count,
            Forgotten = forgotten,
        };

        if (refused.Total > 0)
        {
            logger.LogWarning(
                "{Count} descriptor clauses were refused and are not in the corpus: {Refused}. "
                + "Each is a claim the generation pass made that this corpus cannot render or "
                + "cannot let a reader check: a clause needs a relation from the closed "
                + "vocabulary, a target this corpus holds, and a reference among the entity's own "
                + "verses",
                refused.Total,
                refused);
        }

        logger.LogInformation("Described: {Outcome}", outcome);
        return outcome;
    }

    private static DescriptorOutcome Nothing(bool alreadyLoaded) =>
        new(alreadyLoaded, !alreadyLoaded, 0, 0, 0, 0, 0, 0, 0, 0,
            new DescriptorRefusals(0, 0, 0, 0, 0, 0, 0), TimeSpan.Zero);

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
    /// not stored, and a preposition the phrase supplies for itself is taken off.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Dictionary<string, string>> NoNames =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

    private int Forms(
        int entityId,
        DescriptorRecord record,
        string source,
        HashSet<(int Entity, string Language, string Case)> taken)
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

                var bare = NameForms.Bare(language, form);
                if (bare.Length == 0)
                {
                    continue;
                }

                // A name has one form per language and case, and two loaders write them: this one
                // from the record's own `names`, and the name-forms pass from its own files. Where
                // the other already holds this form, it stays -- it was asked for the name alone
                // and this pass produced it in passing. Only reachable since a re-ask can arrive
                // for an entity already declined; before that the whole entity was skipped.
                if (!taken.Add((entityId, language, grammaticalCase)))
                {
                    continue;
                }

                db.EntityNameForms.Add(new EntityNameForm
                {
                    EntityId = entityId,
                    Language = language,
                    GrammaticalCase = grammaticalCase,
                    Form = bare,
                    Method = LinkMethod.ModelReading,
                    Confidence = 1,
                    Source = source,
                    Run = record.File,
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
    /// <summary>
    /// What this loader already wrote about these entities, and which file of which pass each row
    /// came from. Per entity, because the passes arrive in batches over days and a second batch has
    /// to load beside the first; per file, because a re-ask is the same model on the same date and
    /// there is nothing else to tell it from the batch it corrects.
    /// </summary>
    private async Task<Dictionary<int, HashSet<string>>> Described(
        IReadOnlyCollection<int> entities,
        CancellationToken cancellationToken)
    {
        var clauses = await db.EntityDescriptors
            .Where(d => entities.Contains(d.EntityId) && d.Source.StartsWith(SourcePrefix))
            .Select(d => new { d.EntityId, d.Run })
            .Distinct()
            .ToListAsync(cancellationToken);

        var forms = await db.EntityNameForms
            .Where(f => entities.Contains(f.EntityId) && f.Source.StartsWith(SourcePrefix))
            .Select(f => new { f.EntityId, f.Run })
            .Distinct()
            .ToListAsync(cancellationToken);

        var described = new Dictionary<int, HashSet<string>>();
        foreach (var row in clauses.Concat(forms))
        {
            if (!described.TryGetValue(row.EntityId, out var runs))
            {
                described[row.EntityId] = runs = new HashSet<string>(StringComparer.Ordinal);
            }

            // A row loaded before the run was recorded answers for no file, so no file matches it
            // and the first load after this one reads it again. That is the intent: a blank cannot
            // be compared, and trusting it would keep exactly the stale answers this fixes.
            runs.Add(row.Run ?? string.Empty);
        }

        return described;
    }

    /// <summary>
    /// Everything this loader wrote about these entities, removed so the newer answer can take its
    /// place. The relationships go with the clauses because they are read off them and their own
    /// loader writes only for an entity it has no rows for: leaving them would keep the old reading
    /// on the page while the new one sat in the descriptor table underneath it.
    /// </summary>
    private async Task<int> Forget(
        IReadOnlyCollection<int> entities,
        CancellationToken cancellationToken)
    {
        await db.EntityRelationships
            .Where(r => entities.Contains(r.FromEntityId) && r.Source.StartsWith(SourcePrefix))
            .ExecuteDeleteAsync(cancellationToken);

        await db.EntityNameForms
            .Where(f => entities.Contains(f.EntityId) && f.Source.StartsWith(SourcePrefix))
            .ExecuteDeleteAsync(cancellationToken);

        return await db.EntityDescriptors
            .Where(d => entities.Contains(d.EntityId) && d.Source.StartsWith(SourcePrefix))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Which of the verses these records cite say anything about accompaniment, read in the English
    /// the pass was shown.
    ///
    /// <para>
    /// The words are the ones a translation of this period uses for it, and <em>with</em> is among
    /// them although it is the commonest word in the language — the test is whether the verse
    /// speaks of company at all, and a verse that is nothing but a list of names contains none of
    /// them. That is the case this exists to refuse.
    /// </para>
    /// </summary>
    private static readonly string[] Accompaniment =
        ["with", "companion", "companions", "fellow", "fellows", "together",
         "beside", "accompanied", "accompanying", "along"];

    private async Task<HashSet<(int Book, int Chapter, int Verse)>?> Accompanied(
        IReadOnlyList<DescriptorRecord> records,
        CancellationToken cancellationToken)
    {
        var wanted = records
            .SelectMany(record => record.Claims ?? [])
            .Where(claim => claim.Relation == DescriptorRelations.CompanionOf)
            .Select(claim => Reference(claim.Reference))
            .OfType<(int Book, int Chapter, int Verse)>()
            .Distinct()
            .ToList();

        if (wanted.Count == 0)
        {
            return [];
        }

        var books = wanted.Select(v => v.Book).Distinct().ToList();
        var chapters = wanted.Select(v => v.Chapter).Distinct().ToList();

        var words = await db.Words
            .Where(w => w.Text!.Slug == Shown)
            .SelectMany(w => w.Verse!.References.Where(r => r.IsPrimary), (w, r) => new
            {
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                // The normalised word where the text has one and the printed word where it does
                // not: a text loaded without normalising would otherwise have every verse read as
                // silent, and every companion clause on it refused.
                Word = w.NormalisedText ?? w.Surface,
            })
            .Where(row => books.Contains(row.CanonicalBook)
                && chapters.Contains(row.CanonicalChapter))
            .ToListAsync(cancellationToken);

        // A check that cannot be made is not a check that failed. Where the text the pass was shown
        // is not in this corpus at all -- a test fixture, a partial load -- the verses say nothing
        // either way and refusing every clause on that silence would be the loader inventing a
        // verdict.
        if (words.Count == 0)
        {
            // Said out loud, because this silence has two causes that look identical from outside:
            // a corpus without the text, and a rule looking for it under the wrong name. The second
            // one shipped once and refused nothing for a day before anyone noticed.
            logger.LogWarning(
                "{Count} companion-of clauses could not be checked against their verses: no words of "
                + "the text {Text} were found for any of them. Either that text is not loaded, or the "
                + "loader is asking for it under a name the corpus does not use; nothing was refused",
                wanted.Count,
                Shown);
            return null;
        }

        var asked = wanted.ToHashSet();
        return [.. words
            .Where(row => row.Word != null
                && Accompaniment.Contains(row.Word.ToLowerInvariant()))
            .Select(row => (row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse))
            .Where(asked.Contains)];
    }

    /// <summary>
    /// The text the generation pass was shown, and so the one a clause was read from. The loader's
    /// own constant and not a spelling of it: this was the string "kjv" until 2026-09-10, the corpus
    /// writes "KJV", and the rule it serves found no text and refused nothing for a day.
    /// </summary>
    private const string Shown = Bible4uTextSource.KingJames;

    /// <summary>
    /// The language and case each of these entities already has a form in, from any source. The
    /// column is unique on the three together, and the name-forms pass writes the same table.
    /// </summary>
    private async Task<HashSet<(int Entity, string Language, string Case)>> Taken(
        IReadOnlyCollection<int> entities,
        CancellationToken cancellationToken) =>
        [.. (await db.EntityNameForms
            .Where(f => entities.Contains(f.EntityId))
            .Select(f => new { f.EntityId, f.Language, f.GrammaticalCase })
            .ToListAsync(cancellationToken))
            .Select(f => (f.EntityId, f.Language, f.GrammaticalCase))];

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
    /// Where each of <see cref="PersonRegisterLoader.MisfiledVerses"/> took a verse from and whom it
    /// gave it to, by the record's id and the verse.
    /// </summary>
    private async Task<Dictionary<(int Entity, int Book, int Chapter, int Verse), int>> Refiled(
        CancellationToken cancellationToken)
    {
        var misfiled = PersonRegisterLoader.MisfiledVerses;
        var ids = misfiled
            .SelectMany(m => new[] { m.Held, PersonRegisterLoader.SourceIdOf(m.Bearer) })
            .ToList();
        var bySource = (await db.Entities
                .Where(e => ids.Contains(e.SourceId))
                .Select(e => new { e.Id, e.SourceId })
                .ToListAsync(cancellationToken))
            .GroupBy(e => e.SourceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        var refiled = new Dictionary<(int Entity, int Book, int Chapter, int Verse), int>();
        foreach (var m in misfiled)
        {
            if (bySource.TryGetValue(m.Held, out var from)
                && bySource.TryGetValue(PersonRegisterLoader.SourceIdOf(m.Bearer), out var to)
                && Reference(m.Reference) is { } verse)
            {
                refiled[(from, verse.Book, verse.Chapter, verse.Verse)] = to;
            }
        }

        return refiled;
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
