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
internal sealed record OwnRecordOutcome(
    bool AlreadyLoaded,
    int Created,
    int Unsettled,
    int Named,
    int Annotated,
    int Withheld,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the records this corpus writes for itself are already there"
            : $"{Created} records written for referents no dataset holds and {Named} words annotated " +
              $"to them or to a record the ruling named, {Annotated} words in all once the links " +
              $"carried them, in {Elapsed}. {Unsettled} of the records name who else they might be. " +
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

    /// <summary>The longest a generated slug's descriptive part may be, so a page's address stays typeable.</summary>
    private const int SlugRoom = 60;

    public async Task<OwnRecordOutcome> Load(
        string resources,
        CancellationToken cancellationToken = default)
    {
        var files = new[] { SenseReadingFiles.Rulings(), SenseReadingFiles.ReviewRulings() };
        var sources = files.Select(f => f.Source).ToList();
        if (await db.EntityClaims.AnyAsync(c => sources.Contains(c.Source), cancellationToken))
        {
            logger.LogInformation("The rulings are already recorded; nothing to do");
            return new OwnRecordOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        int created = 0, unsettled = 0, named = 0, annotated = 0;

        foreach (var file in files)
        {
            var (wrote, open, settled) = await Apply(file, cancellationToken);
            created += wrote;
            unsettled += open;
            named += settled.Count;
            annotated += settled.Count == 0
                ? 0
                : await Annotate(
                    settled, EnumSpelling.ToLinkMethod(file.Method), file.Source, cancellationToken);
        }

        var withheld = await Bulk(resources, cancellationToken);
        var outcome = new OwnRecordOutcome(
            false, created, unsettled, named, annotated, withheld, started.Elapsed);
        logger.LogInformation("Wrote: {Outcome}", outcome);
        return outcome;
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
        CancellationToken cancellationToken)
    {
        var settled = new List<(OwnRecordRuling Ruling, Entity Referent)>(file.Rulings.Count);
        int created = 0, unsettled = 0;

        var verses = await Verses(file.Rulings.Select(r => r.WordId).ToList(), cancellationToken);

        foreach (var ruling in file.Rulings)
        {
            var referent = ruling.Create is { } record
                ? await Write(record, verses.GetValueOrDefault(ruling.WordId), ruling.Why, file.Source, cancellationToken)
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
                s.Ruling.WordId, s.Referent.Id, null, false, s.Ruling.Why))
            .ToList();

        return (created, unsettled, seed);
    }

    /// <summary>
    /// A record of our own: what it is, the verse it rests on, and what established it — the last of
    /// those as a claim, because a method and a person are two facts and a source string is one.
    /// </summary>
    private async Task<Entity> Write(
        OwnRecord record,
        (int Book, int Chapter, int Verse)? at,
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

    private async Task<Entity?> Existing(string slug, CancellationToken cancellationToken) =>
        await db.Entities.FirstOrDefaultAsync(e => e.Slug == slug, cancellationToken);

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
    /// The canonical address of each ruling's word, which is the verse the record rests on. Read
    /// from the word rather than transcribed into the file beside it: a reference typed twice is a
    /// reference that can disagree with itself.
    /// </summary>
    private async Task<Dictionary<long, (int Book, int Chapter, int Verse)>> Verses(
        List<long> words,
        CancellationToken cancellationToken)
    {
        var found = await db.Words
            .Where(w => words.Contains(w.Id))
            .SelectMany(
                w => db.VerseReferences.Where(r => r.VerseId == w.VerseId && r.IsPrimary),
                (w, r) => new { w.Id, r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse })
            .ToListAsync(cancellationToken);

        return found.ToDictionary(
            row => row.Id, row => (row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse));
    }

    /// <summary>
    /// The annotations the rulings settle, carried into every text the links reach exactly as every
    /// other annotation is. A person decided who is named, so the seed carries no confidence; a
    /// word reached across a link that is itself a guess does carry one, because the reach is what
    /// is uncertain there and not the decision.
    /// </summary>
    private async Task<int> Annotate(
        List<(long, int, double?, bool, string)> seed,
        LinkMethod method,
        string source,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Seed(connection, seed, cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.MarkCorroboration, cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Carry, cancellationToken,
            ("witness", SenseReadingLoader.Witness));

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

        var (readings, _, _, _, _) = SenseReadingFiles.Read(directory);
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
            await Annotate(seed, LinkMethod.ModelReading, ReadingSource, cancellationToken);
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
        command.CommandTimeout = Annotating.Patient;

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
