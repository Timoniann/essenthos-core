using System.Diagnostics;
using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Created">Records written because a verse names somebody no dataset holds.</param>
/// <param name="Unsettled">
/// Records that say out loud who else they might be. A record with alternatives is not a weaker
/// record — it is one that has stopped pretending the question is closed.
/// </param>
/// <param name="Withheld">
/// Records the bulk pass would have written and did not, because the switch is off. It is counted
/// so the size of what is being withheld is visible rather than implied.
/// </param>
/// <param name="Labelled">
/// Records given the name row they should have had. Counted apart from <see cref="Created"/>
/// because it is what a corpus written before this pass named anything gains on its next boot.
/// </param>
/// <param name="Restored">
/// Ruled words of a file already recorded that had lost the ruling's answer, and were given it again.
/// </param>
internal sealed record OwnRecordOutcome(
    bool AlreadyLoaded,
    int Created,
    int Unsettled,
    int Named,
    int Annotated,
    int Withheld,
    int Labelled,
    TimeSpan Elapsed,
    int Restored = 0)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"the records this corpus writes for itself are already there; {Labelled} of them were " +
              $"given the name row they should have had, and {Restored} ruled words their ruling's answer again"
            : $"{Created} records written for referents no dataset holds and {Named} words annotated " +
              $"to them or to a record the ruling named, {Annotated} words in all once the links " +
              $"carried them, in {Elapsed}. {Unsettled} of the records name who else they might be, " +
              $"{Labelled} carry a name row this pass wrote, and {Restored} ruled words of files " +
              "already recorded were given their ruling's answer again. " +
              $"{Withheld} further records the readings ask for are withheld: the bulk pass is off.";
}

/// <summary>
/// The records this corpus writes for itself, where a verse names somebody no dataset holds.
///
/// The encyclopedia is 3,010 people and 1,351 places and the Bible names more than that. Until now
/// the only answers available for an occurrence were the records somebody else compiled, so a word
/// whose referent is missing had two possible fates and both are wrong: no annotation at all, which
/// tells the reader nothing and hides a real gap, or the nearest listed candidate, which is a
/// confident wrong answer they cannot tell from scholarship.
///
/// <para>
/// **The third answer is a record of our own, and the owner has ruled that this is normal rather
/// than exceptional.** It carries that it is ours, the verse it rests on, and what established it,
/// so nothing about it can be mistaken for a dataset's testimony. And where the identification is
/// open it says so and names who else it might be, which is the thing no other Bible dataset
/// offers: <em>this might be the same man as that one, and here is why nobody can tell</em> is a
/// better answer than a guess or a silence.
/// </para>
///
/// <para>
/// **The rulings arrive in files rather than in code, and there is more than one of them.** The
/// owner's own decisions are one; the review of the model's readings is another, and it is the
/// larger — where a second pass overturned a reading it also said whom the verse names, and that
/// answer is a decision about one word in exactly the shape the owner's are. Each file says who
/// decided and under what method, so what reaches the reader distinguishes a person's ruling from a
/// review's without either of them being hidden. A reading that was overturned is never stored as
/// an answer, and it is not thrown away either: it travels in the note beside the answer that
/// replaced it, so a reader meeting the word learns what was considered and why it did not stand.
/// </para>
///
/// <para>
/// **The bulk pass is built and switched off, and the reason is a measurement rather than caution.**
/// The readings name 1,403 occurrences whose referent the encyclopedia does not hold, in 306 groups
/// — and 175 of them read <em>the kingdom of Judah</em>, 88 <em>kingdom of Judah</em>, 73 <em>the
/// territory of Judah</em> and 44 <em>tribe of Judah</em>. Written as they stand those are four
/// records for one or two things, and most of the largest groups are peoples and kingdoms, which
/// this encyclopedia has no kind for at all. Beneath that, the candidate lists the readings were
/// answered against are themselves being repaired, so part of what looks like a gap is a record
/// that exists and could not be offered. Creating 306 records on that basis would be filling the
/// encyclopedia with duplicates of each other under our own name, which is a worse failure than the
/// gap.
/// </para>
/// </summary>
internal sealed class OwnRecordLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<OwnRecordLoader> logger)
{
    /// <summary>
    /// Whether to write a record for every referent the readings say nobody holds. Off, and the
    /// summary above says why. Turning it on is a decision about the shape of the encyclopedia, not
    /// a deployment setting.
    /// </summary>
    public const string BulkConfigurationKey = "Dataset:CreateRecordsForUnheldReferents";

    /// <summary>
    /// What every record written here says about itself. It is the same prefix the corpus already
    /// uses for the one entity it separated out of a dataset by hand, so a reader filtering on it
    /// gets everything this project asserts and nothing it merely carries.
    /// </summary>
    private const string Ours = "Essenthos";

    private const string ReadingSource =
        "Essenthos, from a model's reading naming a referent no dataset holds";

    private const string SourceIdPrefix = "essenthos:";

    /// <summary>The kind of label a record's own name is, and the kind a namesake group is built of.</summary>
    private const string ProperName = "proper name";

    /// <summary>The longest a generated slug's descriptive part may be, so a page's address stays typeable.</summary>
    private const int SlugRoom = 60;

    public async Task<OwnRecordOutcome> Load(
        string resources,
        CancellationToken cancellationToken = default)
    {
        var files = SenseReadingFiles.AllRulings();
        await ValidateCompanions(files, cancellationToken);

        var started = Stopwatch.StartNew();
        int created = 0, unsettled = 0, named = 0, annotated = 0;

        await db.Database.OpenConnectionAsync(cancellationToken);
        var words = await RuledWords.Resolve(
            (NpgsqlConnection)db.Database.GetDbConnection(),
            files.SelectMany(file => file.Rulings).Select(ruling => ruling.Word),
            cancellationToken);

        var pending = new List<OwnRecordRulings>(files.Count);
        foreach (var file in files)
        {
            if (!await Recorded(file, cancellationToken))
            {
                pending.Add(file);
            }
        }

        var restored = await Restore(files.Except(pending).ToList(), files, words, cancellationToken);

        foreach (var file in pending)
        {
            var (wrote, open, settled) = await Apply(file, words, cancellationToken);
            created += wrote;
            unsettled += open;
            named += settled.Count;
            annotated += settled.Count == 0
                ? 0
                : await Annotate(
                    settled, await Corrections(file, words, cancellationToken), EnumSpelling.ToLinkMethod(file.Method),
                    file.Source, cancellationToken, file.Carry);
        }

        var labelled = await Label(files, words, cancellationToken);
        await Rehead(files, cancellationToken);

        if (pending.Count == 0)
        {
            logger.LogInformation(
                "The rulings are already recorded; {Labelled} of their records were given the name "
                + "row they should have had, and {Restored} ruled words their ruling's answer again",
                labelled, restored);
            return new OwnRecordOutcome(true, 0, 0, 0, 0, 0, labelled, started.Elapsed, restored);
        }

        // The bulk pass belongs to the first boot and to no later one: a rulings file arriving on a
        // corpus that already has the others is that file's work alone.
        var withheld = pending.Count == files.Count ? await Bulk(resources, cancellationToken) : 0;
        var outcome = new OwnRecordOutcome(
            false, created, unsettled, named, annotated, withheld, labelled, started.Elapsed, restored);
        logger.LogInformation("Wrote: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// A ruling naming a person beside a title stands on the title's own ruling, in a manual file of
    /// this edition, and on a held person. The title is held as one, or is one of the owner's
    /// decided titles: on a corpus built from nothing those are written by a later step, and the
    /// record may not exist yet or still be held as the person it was.
    /// </summary>
    private async Task ValidateCompanions(IReadOnlyList<OwnRecordRulings> files, CancellationToken cancellationToken)
    {
        var decided = SenseReadingFiles.Titles().Titles.Select(title => title.Slug).ToHashSet(StringComparer.Ordinal);
        foreach (var file in files)
        foreach (var ruling in file.Rulings.Where(r => r.Alongside is not null))
        {
            var companion = files.Where(f => !f.Carry && f.Method == "manual")
                .SelectMany(f => f.Rulings).SingleOrDefault(r => r.Word == ruling.Word && r.Existing == ruling.Alongside);
            if (file.Carry || file.Method != "manual" || ruling.Corrects is not null
                || companion is null || ruling.Existing is null
                || (!decided.Contains(ruling.Alongside!)
                    && !await db.Entities.AnyAsync(e => e.Slug == ruling.Alongside && e.Kind == EntityKind.Title, cancellationToken))
                || !await db.Entities.AnyAsync(e => e.Slug == ruling.Existing && e.Kind == EntityKind.Person, cancellationToken))
                throw new InvalidDataException($"The companion ruling on {ruling.Word} must name a held person beside an explicitly ruled, edition-local title.");
        }
    }

    /// <summary>
    /// Whether one file's rulings are already in the corpus, asked per file because the files arrive
    /// separately. A file that only names records the encyclopedia holds writes no claim on a record,
    /// so its annotations are asked about as well.
    /// </summary>
    private async Task<bool> Recorded(OwnRecordRulings file, CancellationToken cancellationToken) =>
        await db.EntityClaims.AnyAsync(c => c.Source == file.Source, cancellationToken)
        || await db.WordEntities.AnyAsync(a => a.Source == file.Source, cancellationToken);

    /// <summary>
    /// Whether a claim of the source stands on an annotation of the word naming the record, asked of
    /// every candidate at once. A ruling that named a record the word already named lands as a claim
    /// on that annotation rather than as a row of its own, so the claim is what is asked about.
    /// </summary>
    private const string Answered =
        """
        SELECT x.n
        FROM unnest(@words, @entities, @sources) WITH ORDINALITY AS x(word_id, entity_id, source, n)
        WHERE EXISTS (
            SELECT 1 FROM word_entity a
            JOIN word_entity_claim c ON c.word_entity_id = a.id
            WHERE a.word_id = x.word_id AND a.entity_id = x.entity_id AND c.source = x.source)
        """;

    /// <summary>
    /// The rulings of files already recorded whose word no longer carries the ruling's answer, given
    /// it again.
    ///
    /// <para>
    /// A file is applied once and then skipped whole, so a word that loses its answer afterwards —
    /// as every word of a text does when a reload numbers its words afresh and the annotations go
    /// with the old rows — would stay unanswered for good. An answer a later ruling takes back is not
    /// given again, and neither is one naming a record that has since been folded or withdrawn: the
    /// record is gone, and that is the fold's decision to keep.
    /// </para>
    /// </summary>
    private async Task<int> Restore(
        IReadOnlyList<OwnRecordRulings> recorded,
        IReadOnlyList<OwnRecordRulings> files,
        IReadOnlyDictionary<RuledWord, long> words,
        CancellationToken cancellationToken)
    {
        var takenBack = files
            .SelectMany(file => file.Rulings)
            .Where(ruling => ruling.Corrects is not null)
            .Select(ruling => (words[ruling.Word], ruling.Corrects!))
            .ToHashSet();

        var candidates = recorded
            .SelectMany(file => file.Rulings.Select(ruling => (File: file, Ruling: ruling,
                Word: words[ruling.Word], Slug: ruling.Create?.Slug ?? ruling.Existing!)))
            .Where(c => !takenBack.Contains((c.Word, c.Slug)))
            .ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        var slugs = candidates.Select(c => c.Slug).Distinct().ToList();
        var ids = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
        var held = candidates.Where(c => ids.ContainsKey(c.Slug)).ToList();

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var answered = new HashSet<long>();
        await using (var command = new NpgsqlCommand(Answered, connection))
        {
            command.Parameters.AddWithValue("words", held.Select(c => c.Word).ToArray());
            command.Parameters.AddWithValue("entities", held.Select(c => ids[c.Slug]).ToArray());
            command.Parameters.AddWithValue("sources", held.Select(c => c.File.Source).ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                answered.Add(reader.GetInt64(0));
            }
        }

        var restored = 0;
        foreach (var file in held.Where((_, index) => !answered.Contains(index + 1)).GroupBy(c => c.File))
        {
            var seed = file
                .Select(c => ((long, int, double?, bool, string))(c.Word, ids[c.Slug], null, false, c.Ruling.Why))
                .ToList();
            await Annotate(seed, [], EnumSpelling.ToLinkMethod(file.Key.Method), file.Key.Source, cancellationToken, file.Key.Carry);
            logger.LogInformation(
                "{Count} words ruled on in a recorded file had lost the ruling's answer and were given it again: {Words}",
                seed.Count, string.Join("; ", file.Select(c => c.Ruling.Word)));
            restored += seed.Count;
        }

        return restored;
    }

    /// <summary>
    /// A record a ruling re-heads is ours: its heading, its line and its note are what the ruling
    /// says, and its verses are the words the ruling gives it, so it is credited to the ruling and no
    /// longer to the dataset that first listed the person. The dataset's row is kept as a claim,
    /// which is how the two loaders that separated a record out of a dataset before this one did it.
    ///
    /// <para>
    /// Asked per record and outside the guard that skips the rulings, like the name rows, because the
    /// file is already recorded on a database that loaded it before the credit moved.
    /// </para>
    /// </summary>
    private async Task<int> Rehead(
        IReadOnlyList<OwnRecordRulings> files,
        CancellationToken cancellationToken)
    {
        var reheaded = files
            .SelectMany(file => file.Rulings
                .Where(ruling => ruling.Existing is not null && ruling.Says?.Name is not null)
                .Select(ruling => (Slug: ruling.Existing!, file.Source)))
            .DistinctBy(record => record.Slug)
            .ToList();
        if (reheaded.Count == 0)
        {
            return 0;
        }

        var slugs = reheaded.Select(record => record.Slug).ToList();
        var records = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Include(e => e.Claims)
            .ToDictionaryAsync(e => e.Slug, cancellationToken);

        var moved = 0;
        foreach (var (slug, source) in reheaded)
        {
            if (!records.TryGetValue(slug, out var record) || record.Source.StartsWith(Ours, StringComparison.Ordinal))
            {
                continue;
            }

            if (record.Claims.All(claim => claim.Method != LinkMethod.StatedBySource || claim.Source != record.Source))
            {
                record.Claims.Add(new EntityClaim
                {
                    Method = LinkMethod.StatedBySource,
                    Confidence = null,
                    Source = record.Source,
                    Note = "listed this person under another heading, which is where this record's "
                           + "relationships come from and whose they stay",
                });
            }

            record.Source = source;
            moved++;
        }

        if (moved > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Moved} re-headed records are credited to the ruling that re-headed them", moved);
        }

        return moved;
    }

    /// <summary>
    /// The name row every record these rulings ask for should have had.
    ///
    /// <c>entity_name</c> is where the encyclopedia meets the rest of the corpus: a label carries a
    /// Strong number and a Strong number reaches words. A record with no row there is reachable by
    /// its slug and by nothing else — it stands in no namesake group, the pass that declines a name
    /// into Ukrainian and Russian reads that table and cannot see it, and no resolution by number
    /// arrives at it however plainly the word carries the number. Every record written here was in
    /// that position.
    ///
    /// <para>
    /// Asked per record and outside the guard that skips the rulings, because the records it is
    /// about are already written: a step that named only what it created on this boot would never
    /// reach one of them, and the guard is what would keep it away.
    /// </para>
    ///
    /// <para>
    /// The number is read off the word the ruling rests on rather than off the ruling's own
    /// <c>strongNumber</c>, for the reason the address is read off it. Where the word carries none
    /// the row is written without one: a label alone still puts the record in the namesake group,
    /// which is most of what is missing.
    /// </para>
    /// </summary>
    private async Task<int> Label(
        IReadOnlyList<OwnRecordRulings> files,
        IReadOnlyDictionary<RuledWord, long> words,
        CancellationToken cancellationToken)
    {
        var wanted = files
            .SelectMany(file => file.Rulings)
            .Where(ruling => ruling.Create is not null)
            .ToList();

        if (wanted.Count == 0)
        {
            return 0;
        }

        var slugs = wanted.Select(ruling => ruling.Create!.Slug).ToList();
        var records = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Include(e => e.Names)
            .ToDictionaryAsync(e => e.Slug, cancellationToken);

        var occurrences = await Verses(
            wanted.Select(ruling => words[ruling.Word]).ToList(), cancellationToken);

        var labelled = 0;
        foreach (var ruling in wanted)
        {
            if (!records.TryGetValue(ruling.Create!.Slug, out var record)
                || record.Names.Any(name => name.Kind == ProperName))
            {
                continue;
            }

            var number = occurrences.TryGetValue(words[ruling.Word], out var at) ? at.StrongNumber : null;
            record.Names.Add(new EntityName
            {
                Label = ruling.Create.Name,
                Kind = ProperName,
                HebrewStrongNumber = number is not null && number.StartsWith('H') ? number : null,
                GreekStrongNumber = number is not null && number.StartsWith('G') ? number : null,
            });
            labelled++;
        }

        if (labelled > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return labelled;
    }

    /// <summary>
    /// One file of rulings: the records it asks for, the doubts it records beside them, and the seed
    /// of words it settles.
    ///
    /// The owner's rulings and the review's are the same shape on purpose. What differs is who
    /// decided and therefore what the claim says, and both of those come off the file's own header
    /// rather than out of this code, so a third reviewer needs a file and not a branch here.
    /// </summary>
    private async Task<(int Created, int Unsettled, List<(long, int, double?, bool, string)> Settled)> Apply(
        OwnRecordRulings file,
        IReadOnlyDictionary<RuledWord, long> words,
        CancellationToken cancellationToken)
    {
        var settled = new List<(OwnRecordRuling Ruling, Entity Referent)>(file.Rulings.Count);
        int created = 0, unsettled = 0;

        var verses = await Verses(file.Rulings.Select(r => words[r.Word]).ToList(), cancellationToken);

        foreach (var ruling in file.Rulings)
        {
            var referent = ruling.Create is { } record
                ? await Write(record, verses.GetValueOrDefault(words[ruling.Word]), ruling.Why, file.Source, cancellationToken)
                : await Existing(ruling.Existing!, cancellationToken);

            if (referent is null)
            {
                logger.LogWarning(
                    "The ruling on {Reference} names the record \"{Slug}\", which the encyclopedia does "
                    + "not hold, so the word was left unannotated. Either the record was renamed or the "
                    + "encyclopedia has not been loaded yet",
                    ruling.Reference,
                    ruling.Existing);
                continue;
            }

            if (ruling.Create is not null)
            {
                created++;
            }

            Amend(referent, ruling, file.Source);

            if (await Alternatives(referent, ruling, file.Source, cancellationToken))
            {
                unsettled++;
            }

            settled.Add((ruling, referent));
        }

        // Saved before the seed is built: a record written a moment ago has no id until it is, and
        // an annotation pointing at entity 0 is refused by the foreign key rather than stored.
        await db.SaveChangesAsync(cancellationToken);

        var seed = settled
            .Select(s => ((long, int, double?, bool, string))(
                words[s.Ruling.Word], s.Referent.Id, null, false, s.Ruling.Why))
            .ToList();

        return (created, unsettled, seed);
    }

    /// <summary>
    /// A record of our own: what it is, the verse it rests on, and what established it — the last of
    /// those as a claim, because a method and a person are two facts and a source string is one.
    /// </summary>
    private async Task<Entity> Write(
        OwnRecord record,
        Occurrence? at,
        string why,
        string source,
        CancellationToken cancellationToken)
    {
        var already = await db.Entities.FirstOrDefaultAsync(e => e.Slug == record.Slug, cancellationToken);
        if (already is not null)
        {
            return already;
        }

        var entity = new Entity
        {
            Kind = EnumSpelling.ToEntityKind(record.Kind),
            Slug = record.Slug,
            Name = record.Name,
            Distinguisher = record.Distinguisher,
            Notes = record.Notes,
            SourceId = SourceIdPrefix + record.Slug,
            Source = source,
        };
        db.Entities.Add(entity);

        if (at is { } address)
        {
            entity.Verses.Add(new EntityVerse
            {
                CanonicalBook = address.Book,
                CanonicalChapter = address.Chapter,
                CanonicalVerse = address.Verse,
                Label = record.Name,
                Source = source,
            });
        }

        entity.Claims.Add(new EntityClaim
        {
            Method = LinkMethod.Manual,
            Confidence = null,
            Source = source,
            Note = why,
        });

        return entity;
    }

    /// <summary>
    /// The record a ruling names, with the claims already standing on it, so a second ruling on the
    /// same record adds the file's claim once rather than twice. A record an earlier ruling of the
    /// same file wrote is not saved yet, so it is looked for among the tracked entities first.
    /// </summary>
    private async Task<Entity?> Existing(string slug, CancellationToken cancellationToken) =>
        db.Entities.Local.FirstOrDefault(e => e.Slug == slug)
        ?? await db.Entities.Include(e => e.Claims).FirstOrDefaultAsync(e => e.Slug == slug, cancellationToken);

    /// <summary>
    /// What the ruling makes the record say about itself, and the claim that it was a person who
    /// said so. A record the encyclopedia compiled says what its compiler concluded, and a ruling
    /// that the conclusion is one reading of several has to reach the sentence a reader is shown.
    /// </summary>
    private static void Amend(Entity referent, OwnRecordRuling ruling, string source)
    {
        if (ruling.Says is not { } says)
        {
            return;
        }

        referent.Name = says.Name ?? referent.Name;
        referent.Distinguisher = says.Distinguisher ?? referent.Distinguisher;
        referent.Notes = says.Notes ?? referent.Notes;

        if (referent.Claims.All(claim => claim.Source != source))
        {
            referent.Claims.Add(new EntityClaim
            {
                Method = LinkMethod.Manual,
                Confidence = null,
                Source = source,
                Note = ruling.Why,
            });
        }
    }

    /// <summary>
    /// Who else the record might be. Written on the record rather than on the occurrence, because
    /// it is a statement about the person and stays true wherever he is named.
    /// </summary>
    private async Task<bool> Alternatives(
        Entity referent,
        OwnRecordRuling ruling,
        string source,
        CancellationToken cancellationToken)
    {
        if (ruling.Alternatives is not { Count: > 0 } alternatives)
        {
            return false;
        }

        foreach (var alternative in alternatives)
        {
            var other = alternative.Slug is null
                ? null
                : await db.Entities.FirstOrDefaultAsync(e => e.Slug == alternative.Slug, cancellationToken);

            referent.Alternatives.Add(new EntityAlternative
            {
                Alternative = other,
                Describes = other is null ? alternative.Describes ?? alternative.Slug : null,
                Reason = alternative.Reason,
                Source = source,
            });
        }

        return true;
    }

    /// <summary>
    /// What each ruling's word is: the canonical address, which is the verse the record rests on, and
    /// the Strong number, which is the name the record will be reached by. Both read from the word
    /// rather than transcribed into the file beside it — a fact written down twice is a fact that
    /// can disagree with itself.
    /// </summary>
    private async Task<Dictionary<long, Occurrence>> Verses(
        List<long> words,
        CancellationToken cancellationToken)
    {
        var found = await db.Words
            .Where(w => words.Contains(w.Id))
            .SelectMany(
                w => db.VerseReferences.Where(r => r.VerseId == w.VerseId && r.IsPrimary),
                (w, r) => new
                {
                    w.Id,
                    w.StrongNumber,
                    r.CanonicalBook,
                    r.CanonicalChapter,
                    r.CanonicalVerse,
                })
            .ToListAsync(cancellationToken);

        return found.ToDictionary(
            row => row.Id,
            row => new Occurrence(
                row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse, row.StrongNumber));
    }

    /// <summary>Where a ruling's word stands, and what name it carries there.</summary>
    private sealed record Occurrence(int Book, int Chapter, int Verse, string? StrongNumber);

    /// <summary>
    /// What a ruling overrules: every other answer a pass wrote at the word it rules, and the copies
    /// those answers were carried to. A person decided who the word names, so a reading or a
    /// resolution that named somebody else there is not a second opinion to show beside it — it is
    /// the answer the ruling was written against, and left standing it puts two records on one word.
    /// Another person's ruling is not overruled, and the answer the ruling gives is not touched.
    /// </summary>
    private const string Overruled =
        """
        WITH overruled AS (
            SELECT a.id, a.word_id, a.entity_id, a.source
            FROM word_entity a
            JOIN pending_annotation ruled ON ruled.word_id = a.word_id AND ruled.through IS NULL
            WHERE a.entity_id <> ruled.entity_id AND a.method <> @manual
        )
        DELETE FROM word_entity a
        USING overruled o
        WHERE a.id = o.id
           OR (a.entity_id = o.entity_id AND a.source = o.source
               AND a.note LIKE 'through % word ' || o.word_id || ',%')
        """;

    /// <summary>
    /// What a correcting ruling takes back: the earlier ruling's answer at the word, whatever wrote
    /// it, and the copies it was carried to. Only the record the correction names is taken back.
    /// </summary>
    private const string Corrected =
        """
        DELETE FROM word_entity a
        USING unnest(@words, @entities) AS c(word_id, entity_id)
        WHERE a.entity_id = c.entity_id
          AND (a.word_id = c.word_id OR a.note LIKE 'through % word ' || c.word_id || ',%')
        """;

    /// <summary>The word and the record each correcting ruling of a file takes back.</summary>
    private async Task<IReadOnlyList<(long Word, int Entity)>> Corrections(
        OwnRecordRulings file,
        IReadOnlyDictionary<RuledWord, long> words,
        CancellationToken cancellationToken)
    {
        var corrects = file.Rulings.Where(r => r.Corrects is not null).ToList();
        if (corrects.Count == 0)
        {
            return [];
        }

        var slugs = corrects.Select(r => r.Corrects!).Distinct().ToList();
        var ids = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
        return corrects
            .Where(r => ids.ContainsKey(r.Corrects!))
            .Select(r => (words[r.Word], ids[r.Corrects!]))
            .ToList();
    }

    /// <summary>
    /// The annotations the rulings settle, carried into every text the links reach exactly as every
    /// other annotation is. A person decided who is named, so the seed carries no confidence; a
    /// word reached across a link that is itself a guess does carry one, because the reach is what
    /// is uncertain there and not the decision.
    /// </summary>
    private async Task<int> Annotate(
        List<(long, int, double?, bool, string)> seed,
        IReadOnlyList<(long Word, int Entity)> corrected,
        LinkMethod method,
        string source,
        CancellationToken cancellationToken,
        bool carry = true)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (corrected.Count > 0)
        {
            await Annotating.Run(connection, transaction, Corrected, cancellationToken,
                ("words", corrected.Select(c => c.Word).ToArray()),
                ("entities", corrected.Select(c => c.Entity).ToArray()));
        }

        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Seed(connection, seed, cancellationToken);
        await Annotating.Run(connection, transaction, Overruled, cancellationToken,
            ("manual", EnumSpelling.Of(LinkMethod.Manual)));
        await Annotating.Run(connection, transaction, Annotating.MarkCorroboration, cancellationToken);
        if (carry)
            await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

        var spelled = EnumSpelling.Of(method);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", spelled), ("source", source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", spelled), ("source", source));

        var byText = await Annotating.ByText(connection, transaction, source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return byText.Sum(t => t.Words);
    }

    /// <summary>
    /// Every referent the readings say nobody holds, as records of our own — or, with the switch
    /// off, a count of what that would have been.
    ///
    /// The grouping is by Strong number and the model's own description of the referent, and the
    /// kind comes from BHSA's marking of the word rather than from the description, so a group the
    /// witness will not classify is not written at all. Even so this is what the summary above
    /// warns about, and the count it returns is the argument for leaving it off.
    /// </summary>
    private async Task<int> Bulk(string resources, CancellationToken cancellationToken)
    {
        var directory = configuration[SenseReadingFiles.ConfigurationKey] is { Length: > 0 } configured
            ? configured
            : Path.Combine(resources, SenseReadingFiles.DefaultFolder);

        if (!Directory.Exists(directory))
        {
            return 0;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var readings = (await SenseReadingFiles.Place(
            (NpgsqlConnection)db.Database.GetDbConnection(), directory, cancellationToken)).Readings;
        var groups = readings
            .Where(r => r.Referent == SenseReading.Unlisted && !string.IsNullOrWhiteSpace(r.Names))
            .GroupBy(r => (r.StrongNumber, Description: r.Names!.Trim()), TupleComparer.Instance)
            .ToList();

        if (!configuration.GetValue(BulkConfigurationKey, false))
        {
            logger.LogInformation(
                "{Groups} referents the readings say nobody holds are not being written: "
                + "\"{Key}\" is off. Most of the largest groups are peoples and territories, which "
                + "this encyclopedia has no kind for, and several of them describe the same thing in "
                + "different words",
                groups.Count,
                BulkConfigurationKey);
            return groups.Count;
        }

        var kinds = await Kinds(readings, cancellationToken);
        var verses = await Verses(groups.Select(g => g.First().WordId).ToList(), cancellationToken);
        var seed = new List<(long, int, double?, bool, string)>();

        foreach (var group in groups)
        {
            var first = group.First();
            if (!kinds.TryGetValue(first.WordId, out var kind))
            {
                continue;
            }

            var record = new OwnRecord(
                Slug(group.Key.Description, group.Key.StrongNumber),
                EnumSpelling.Of(kind),
                group.Key.Description,
                null,
                $"Written because {group.Count()} occurrences of {group.Key.StrongNumber} were read as " +
                "naming somebody no dataset here holds.");

            var entity = await Write(
                record, verses.GetValueOrDefault(first.WordId), ReadingSource, ReadingSource, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var reading in group)
            {
                seed.Add((reading.WordId, entity.Id, ReadingConfidence, false, group.Key.Description));
            }
        }

        if (seed.Count > 0)
        {
            await Annotate(seed, [], LinkMethod.ModelReading, ReadingSource, cancellationToken);
        }

        return 0;
    }

    /// <summary>
    /// What a record written from a reading alone is worth. It is the measured survival of the
    /// model's own high band and nothing better, because nothing has reviewed these: the readings
    /// that were read a second time are the ones the encyclopedia could answer, and by definition
    /// these are not.
    /// </summary>
    private const double ReadingConfidence = 0.9;

    /// <summary>
    /// Whether BHSA commits to a person or a place at each word, which is what decides the kind of
    /// a record written from a reading. A word it marks with several kinds at once decides nothing
    /// — every occurrence of Israel is marked person, people and place — so it is left out and no
    /// record is written for it.
    /// </summary>
    private async Task<Dictionary<long, EntityKind>> Kinds(
        IReadOnlyList<SenseReading> readings,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var command = new NpgsqlCommand(
            "SELECT id, morphology->>'nameType' FROM word WHERE id = ANY(@ids) "
            + "AND morphology->>'nameType' IN ('pers', 'topo')",
            connection);
        command.Parameters.AddWithValue("ids", readings.Select(r => r.WordId).ToArray());

        var kinds = new Dictionary<long, EntityKind>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            kinds[reader.GetInt64(0)] =
                reader.GetString(1) == "topo" ? EntityKind.Place : EntityKind.Person;
        }

        return kinds;
    }

    private static string Slug(string description, string number)
    {
        var slug = new StringBuilder(SlugRoom);
        foreach (var c in description)
        {
            if (char.IsLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }

            if (slug.Length >= SlugRoom)
            {
                break;
            }
        }

        return $"{slug.ToString().Trim('-')}-{number.ToLowerInvariant()}";
    }

    private sealed class TupleComparer : IEqualityComparer<(string StrongNumber, string Description)>
    {
        public static readonly TupleComparer Instance = new();

        public bool Equals(
            (string StrongNumber, string Description) x,
            (string StrongNumber, string Description) y) =>
            string.Equals(x.StrongNumber, y.StrongNumber, StringComparison.Ordinal)
            && string.Equals(x.Description, y.Description, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string StrongNumber, string Description) value) =>
            HashCode.Combine(value.StrongNumber, value.Description.ToLowerInvariant());
    }
}
