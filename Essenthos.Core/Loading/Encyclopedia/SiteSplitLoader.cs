using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Occurrences">Every occurrence the register carries, settled or not.</param>
/// <param name="Agreed">
/// Occurrences the gazetteer's verse list and a reading of the verse, made without seeing it, name
/// the same place at.
/// </param>
/// <param name="Stated">The gazetteer alone, the reading having declined to choose.</param>
/// <param name="Read">A reading upheld on a second reading, where the gazetteer says nothing.</param>
/// <param name="Unsettled">
/// Occurrences nothing settled. Not a gap in the pass — it is the answer, and a page that names
/// forty of its sixty-three verses is worth more than one that names sixty-three with a guess in it.
/// </param>
/// <param name="Spoken">
/// Occurrences a word already named somebody else at, which are left exactly as they are. A word
/// with two referents is worse than a word with none.
/// </param>
/// <param name="Unresolvable">Answers naming a record the encyclopedia no longer holds.</param>
/// <param name="Written">Annotations written under this pass's own source.</param>
/// <param name="Corroborated">
/// Of those, how many stand in a verse the encyclopedia's own list independently names that place
/// in. A second claim rather than a higher number: the list is known to put a verse on the wrong
/// record.
/// </param>
/// <param name="Reached">
/// Words the pass reached, the seeds and the translations the links carried them into alike. Larger
/// than <paramref name="Written"/> by the words something else had already given this very answer,
/// which gain a claim rather than a second row.
/// </param>
internal sealed record SiteSplitOutcome(
    bool AlreadyLoaded,
    int Occurrences,
    int Agreed,
    int Stated,
    int Read,
    int Unsettled,
    int Spoken,
    int Unresolvable,
    int Written,
    int Corroborated,
    int Reached,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the namesake places are already told apart"
            : $"{Written} words name one of the places that share a name, from {Occurrences} " +
              $"occurrences over the numbers the gazetteer puts at two sites, in {Elapsed}: " +
              $"{Agreed} where the gazetteer and a reading agree, {Stated} on the gazetteer alone " +
              $"and {Read} on a reading upheld twice. {Unsettled} are left unsettled and say so, " +
              $"{Spoken} were already named by something else, and {Unresolvable} name a record " +
              $"the encyclopedia no longer holds. It reached {Reached} words in all, the rest of " +
              $"which already carried this answer from something else and gained a claim rather " +
              $"than a second row, and {Corroborated} of what it wrote stand in a verse the " +
              "encyclopedia independently says that place is named in. Per text: " +
              string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// Which Jericho this word means.
///
/// The place register gave every place of the lexicon the Strong number its name is, and
/// <see cref="PlaceRegisterLoader.Aspects"/> then asked the gazetteer whether the records bearing one
/// number are one place seen twice or two places. Where it answers *two sites* the number resolves
/// to neither, which is right and leaves the pages empty: Jericho at Tell es Sultan and Jericho at
/// Tell el Alayiq are four kilometres apart, and between them they had sixty-three references and
/// not one annotated word. Eighty-seven numbers are in that state and 1,188 occurrences stand at
/// them.
///
/// <para>
/// <strong>Two accounts, and they were arrived at separately.</strong> The gazetteer does not only
/// place each record at a site; it says, verse by verse, which of them is named there, and that
/// settles 1,115 of the 1,188 on its own. A reading of the verse is the second account, and the
/// harness that produced it never showed it the first: every verse being asked about is struck out
/// of each candidate's attestation before the payload is written, so an agreement between them is a
/// measurement rather than an echo. What the reading is shown instead is the King James verse and
/// its neighbours, the site each record sits at, this corpus's own clauses about each — <em>Mizpah
/// is a city in Gilead</em>, <em>Mizpah 3 is near Ebenezer</em> — and what other places the
/// gazetteer puts in the same verse.
/// </para>
///
/// <para>
/// <strong>Three standings, three claims, and never one dressed as another.</strong> Where both
/// accounts name the same place the annotation rests on two statements and says so. Where only the
/// gazetteer speaks it rests on one, and the method is the inference it is: a verse list is a
/// statement about the verse, and reaching from there to the word is BHSA's marking and the number,
/// not the dataset's testimony. Where only a reading speaks — the gazetteer places nobody, or places
/// two of them in one verse and cannot say which word is which — the row carries
/// <see cref="LinkMethod.ModelReading"/> with the model, the prompt and the date on it, and it got
/// there only by being read a second time.
/// </para>
///
/// <para>
/// <strong>What nothing settles is left alone and counted.</strong> An occurrence the gazetteer does
/// not place and the readings will not uphold writes nothing, and the register carries it with the
/// reason. That is the same discipline the sense pass keeps with <c>unclear</c>, and it is the whole
/// argument for the pass: a confident wrong site is worse than a silent one, because a reader cannot
/// tell it from scholarship.
/// </para>
///
/// <para>
/// <strong>A word that already names somebody is not answered again.</strong> Two hundred and eighty
/// of the occurrences carry an annotation an earlier pass wrote, and <c>word_entity</c> is unique on
/// the word and the entity rather than on the word — so a second referent would land beside the
/// first and the reader would meet a word that names two places. Where the earlier answer is the one
/// this register reaches, the row stays and this pass's claim joins it; where it is not, nothing is
/// written and the occurrence is counted.
/// </para>
///
/// <para>
/// Idempotent on its own rows, and on its own source rather than on the annotation table: the
/// resolutions and the readings are already there when this runs, so asking whether anything is
/// annotated would answer yes on every boot and this would never run at all.
/// </para>
/// </summary>
internal sealed class SiteSplitLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<SiteSplitLoader> logger)
{
    /// <summary>
    /// The text an annotation is carried from, where the register does not say which. Every
    /// translation is reached from the seeded word along the links that already exist, and the note
    /// on a carried row names the text it came from — so the carrying is done once per witness
    /// rather than once for all of them under the Hebrew's name.
    /// </summary>
    private const string DefaultWitness = EntityCandidates.Witness;

    /// <summary>
    /// How every source string written here begins, which is what tells this pass's rows from every
    /// other row carrying the same method.
    /// </summary>
    public const string SourcePrefix = "Essenthos, telling apart the places that share a name";

    /// <summary>
    /// What puts the name on the word, which is not the same statement in the two languages. BHSA
    /// marks a Hebrew word a proper noun and gives it the number; nothing marks a Greek one, so
    /// there the gate is the lexicon writing the headword with a capital and the witness's own
    /// parsing calling the word a noun — the pair <see cref="EntityAnnotationLoader"/> puts every
    /// Greek annotation behind, and a row that credited BHSA for a word of Acts would be naming a
    /// text that does not hold the verse.
    /// </summary>
    private static string Marking(string witness) =>
        string.Equals(witness, DefaultWitness, StringComparison.Ordinal)
            ? "BHSA marks the word a name"
            : $"the lexicon writes the name with a capital and {witness} parses the word as a noun";

    private static string Agreeing(string witness) =>
        SourcePrefix + $": the gazetteer states this verse names it, {Marking(witness)}, and a "
        + "reading of the verse made without seeing the gazetteer says the same";

    private static string Stating(string witness) =>
        SourcePrefix + $": the gazetteer states this verse names it, and {Marking(witness)}";

    /// <summary>
    /// The reading's own source, in the shape the sense pass writes it, because a reader meeting
    /// either should be told the same three things: which model, under which prompt, and when.
    /// </summary>
    private const string ReadingPrefix = "a reading of the verse by";

    /// <summary>The date out of a run's timestamp, which is the part a reader can use.</summary>
    private const int DayLength = 10;

    /// <summary>
    /// How sure the corpus is where both accounts name the same place.
    ///
    /// It is not certainty, and what is left open is what neither of them can see. The gazetteer's
    /// list is keyed to the verse, so it cannot say which word of a verse naming two of these places
    /// is which; the reading read the sentence and not the word. Beside that stands the gazetteer's
    /// known habit of listing a verse the place is not actually named in (PRB-0458), and the
    /// register's own dependence on records the place register minted, two of which can still turn
    /// out to be one place. Two accounts that never met, agreeing about the verse, leave nothing
    /// else open.
    /// </summary>
    private const double Agreed = 0.95;

    /// <summary>
    /// The same where only one of the two speaks, whichever one it is.
    ///
    /// Measured rather than chosen: over the occurrences both accounts settle they disagree on a
    /// share <em>d</em>, and in each of those at least one of them is wrong — so a single account is
    /// wrong at least <em>d</em>/2 of the time and at most <em>d</em>. The run this register was
    /// built from put <em>d</em> at 3.6%: the reading and the gazetteer name the same record for
    /// 1,064 of the 1,104 occurrences both settle. A lone account is stored at the pessimistic end
    /// of that bound, and the same number is used for both, because the bound does not say which of
    /// the two is the worse.
    /// </summary>
    private const double Alone = 0.96;

    /// <summary>
    /// What established each standing, as a method and a number. Only the third is a reading, and
    /// the other two are inferences from a source's statement about a verse — the distinction
    /// PRB-0340 settled for the gentilics and <see cref="EntityAnnotationLoader"/> keeps for the
    /// resolutions. The source is not here because it names the witness, and the witness is a
    /// property of the occurrence rather than of the standing.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (LinkMethod Method, double Confidence)>
        Standings = new Dictionary<string, (LinkMethod, double)>(StringComparer.Ordinal)
        {
            [SiteRegisterFiles.ByBoth] = (LinkMethod.Lexical, Agreed),
            [SiteRegisterFiles.ByTheGazetteer] = (LinkMethod.Lexical, Alone),
            [SiteRegisterFiles.ByTheReading] = (LinkMethod.ModelReading, Alone),
        };

    /// <summary>The source string one standing writes for one witness.</summary>
    private static string Source(string standing, string witness, string reading) => standing switch
    {
        SiteRegisterFiles.ByBoth => Agreeing(witness),
        SiteRegisterFiles.ByTheGazetteer => Stating(witness),
        _ => reading,
    };

    private const string VerseList =
        "the encyclopedia's own list of the verses each entity is named in";

    public async Task<SiteSplitOutcome> Load(
        string resources,
        CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(
                a => a.Source.StartsWith(SourcePrefix), cancellationToken))
        {
            logger.LogInformation("The namesake places are already told apart; nothing to do");
            return Nothing(alreadyLoaded: true);
        }

        var directory = configuration[SiteRegisterFiles.ConfigurationKey] is { Length: > 0 } set
            ? set
            : Path.Combine(resources, SiteRegisterFiles.DefaultFolder);

        if (!Directory.Exists(directory))
        {
            logger.LogWarning(
                "No site register at {Directory}, so the places that share a name stay unannotated. "
                + "Produce it with \"python scripts/sites.py register\" and \"publish\", or point "
                + "{Key} at a folder that holds it",
                directory,
                SiteRegisterFiles.ConfigurationKey);
            return Nothing(alreadyLoaded: false);
        }

        var started = Stopwatch.StartNew();
        var records = SiteRegisterFiles.Read(directory);
        var settled = records.Where(r => r.Referent is { Length: > 0 } && r.Standing is not null)
            .ToList();
        var unsettled = records.Count - settled.Count;

        var referents = await Referents(settled, cancellationToken);
        var spoken = await AlreadySpokenFor(settled, cancellationToken);

        var seeds = new Dictionary<(string Standing, string Witness),
            List<(long, int, double?, bool, string)>>();
        var unresolvable = 0;
        foreach (var record in settled)
        {
            if (!Standings.ContainsKey(record.Standing!))
            {
                unresolvable++;
                continue;
            }

            if (!referents.TryGetValue(record.Referent!, out var entity))
            {
                unresolvable++;
                continue;
            }

            if (spoken.TryGetValue(record.WordId, out var already) && !already.Contains(entity))
            {
                continue;
            }

            var key = (record.Standing!, record.Witness is { Length: > 0 } text
                ? text
                : DefaultWitness);
            if (!seeds.TryGetValue(key, out var rows))
            {
                seeds[key] = rows = [];
            }

            rows.Add((record.WordId, entity, Standings[record.Standing!].Confidence, false,
                Note(record)));
        }

        if (unresolvable > 0)
        {
            logger.LogWarning(
                "{Count} of the register's occurrences name a record the encyclopedia no longer "
                + "holds, or a standing this loader does not know, and were not annotated. That "
                + "happens when a record is merged or renamed after a run; re-running "
                + "\"python scripts/sites.py register\" against the current encyclopedia closes it",
                unresolvable);
        }

        var counted = Standings.Keys.ToDictionary(
            standing => standing,
            standing => seeds
                .Where(pair => pair.Key.Standing == standing)
                .Sum(pair => pair.Value.Count),
            StringComparer.Ordinal);
        var held = settled.Count - seeds.Values.Sum(rows => rows.Count) - unresolvable;

        if (seeds.Count == 0)
        {
            logger.LogWarning(
                "No occurrence of the site register survived the referent lookup, so nothing was "
                + "annotated. Either the register is for a different corpus or the place register "
                + "has not been loaded yet; both are earlier steps of the same pipeline");
            return Nothing(alreadyLoaded: false) with
            {
                Occurrences = records.Count, Unsettled = unsettled, Spoken = held,
                Unresolvable = unresolvable, Elapsed = started.Elapsed,
            };
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);

        var reading = Reading(settled);
        int corroborated = 0, reached = 0;
        foreach (var ((standing, witness), rows) in seeds
                     .OrderBy(pair => pair.Key.Standing, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.Witness, StringComparer.Ordinal))
        {
            var (found, agreed) = await Apply(
                connection, transaction, standing, witness, rows, reading, cancellationToken);
            reached += found;
            corroborated += agreed;
        }

        var counts = new List<(string Text, int Words)>();
        foreach (var source in seeds.Keys
                     .Select(key => Source(key.Standing, key.Witness, reading))
                     .Distinct(StringComparer.Ordinal))
        {
            counts.AddRange(
                await Annotating.ByText(connection, transaction, source, cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        var perText = counts
            .GroupBy(row => row.Text, StringComparer.Ordinal)
            .Select(group => (group.Key, group.Sum(row => row.Words)))
            .OrderByDescending(row => row.Item2)
            .ToList();

        var outcome = new SiteSplitOutcome(
            false,
            records.Count,
            counted[SiteRegisterFiles.ByBoth],
            counted[SiteRegisterFiles.ByTheGazetteer],
            counted[SiteRegisterFiles.ByTheReading],
            unsettled,
            held,
            unresolvable,
            perText.Sum(row => row.Item2),
            corroborated,
            reached,
            perText,
            started.Elapsed);

        logger.LogInformation("Told the namesake places apart: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// One standing's annotations: seeded, carried into every text the links reach, and written with
    /// the method and the source that standing earns.
    ///
    /// They run in one transaction over one workspace, emptied between them. The standings cannot
    /// share a pass, because what distinguishes them is exactly the method and the source, and a pass
    /// that wrote all three under one of those would be the loader claiming for the gazetteer what a
    /// model read, or the other way about. Nor can two witnesses, because the note on a carried row
    /// names the text the answer came from, and the Greek occurrences did not come from the Hebrew.
    ///
    /// <para>
    /// Where the gazetteer and a reading agree, the reading's own claim is written beside the
    /// conclusion as well. It is the same row and the same answer; what the second claim carries is
    /// the model, the prompt and the date, which is the one thing a reading must never be stored
    /// without.
    /// </para>
    /// </summary>
    private static async Task<(int Reached, int Corroborated)> Apply(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string standing,
        string witness,
        IReadOnlyCollection<(long, int, double?, bool, string)> rows,
        string reading,
        CancellationToken cancellationToken)
    {
        var spelled = EnumSpelling.Of(Standings[standing].Method);
        var said = Source(standing, witness, reading);

        await Annotating.Run(connection, transaction, Empty, cancellationToken);
        await Annotating.Seed(connection, rows, cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.MarkCorroboration, cancellationToken);
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", spelled), ("source", said));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", spelled), ("source", said));

        if (standing == SiteRegisterFiles.ByBoth)
        {
            await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
                ("method", EnumSpelling.Of(LinkMethod.ModelReading)), ("source", reading));
        }

        await Annotating.Run(connection, transaction, Annotating.Corroboration, cancellationToken,
            ("method", EnumSpelling.Of(LinkMethod.StatedBySource)), ("source", VerseList),
            ("note", "the encyclopedia states that this entity is named in this verse"));

        var reached = await Count(
            connection, transaction, "SELECT count(*) FROM pending_annotation", cancellationToken);

        // Counted over the rows this pass wrote rather than over the workspace. A word it reached
        // that something else had already given this very answer keeps that source's row, so a
        // corroboration taken off the workspace would be larger than the number of annotations it
        // is supposed to be a share of.
        var agreed = await Count(
            connection,
            transaction,
            "SELECT count(*) FROM word_entity a "
            + "JOIN pending_annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id "
            + "WHERE w.corroborated AND a.source = @source",
            cancellationToken,
            ("source", said));

        return (reached, agreed);
    }

    /// <summary>
    /// The workspace between two standings. It is created once for the transaction and dropped at
    /// commit, so the second standing has to clear it rather than make it again.
    /// </summary>
    private const string Empty = "DELETE FROM pending_annotation";

    /// <summary>
    /// What the reader is shown as the reason: what settled this occurrence, in the register's own
    /// words, and the model's sentence where a model had one.
    /// </summary>
    private static string Note(SiteRegisterRecord record)
    {
        var said = record.Check?.Why ?? record.Reading?.Reason;
        return $"{record.Number}, read as {record.Referent} — {record.Why}"
               + (string.IsNullOrWhiteSpace(said) || record.Why.Contains(said, StringComparison.Ordinal)
                   ? string.Empty
                   : $" ({said})");
    }

    /// <summary>
    /// Who read the verses, in the words a reader gets on the card. Taken from the register itself
    /// rather than declared here, so a second run under a different model or a revised prompt cannot
    /// be stored under the first one's name.
    /// </summary>
    private static string Reading(IReadOnlyCollection<SiteRegisterRecord> records)
    {
        var readings = records.Select(r => r.Reading).OfType<SiteReadingRecord>().ToList();
        if (readings.Count == 0)
        {
            return ReadingPrefix + " an unnamed model";
        }

        var models = readings.Select(r => r.Model).OfType<string>().Distinct()
            .Order(StringComparer.Ordinal);
        var prompts = readings.Select(r => r.PromptVersion).OfType<string>().Distinct()
            .Order(StringComparer.Ordinal);
        var latest = readings.Select(r => r.AskedAt).OfType<string>().Max(StringComparer.Ordinal)
                     ?? string.Empty;

        return $"{ReadingPrefix} {string.Join(" and ", models)}, prompt "
               + $"{string.Join(" and ", prompts)}, run to "
               + latest[..Math.Min(DayLength, latest.Length)];
    }

    /// <summary>Which entity each answered slug is, in one query rather than one per occurrence.</summary>
    private async Task<Dictionary<string, int>> Referents(
        IReadOnlyCollection<SiteRegisterRecord> records,
        CancellationToken cancellationToken)
    {
        var slugs = records.Select(r => r.Referent!).Distinct(StringComparer.Ordinal).ToList();
        return await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
    }

    /// <summary>
    /// What already names each of the register's words. Read from the database rather than from the
    /// register, because the register was written against the corpus as it stood when the pass ran
    /// and this is the corpus as it stands now.
    /// </summary>
    private async Task<Dictionary<long, HashSet<int>>> AlreadySpokenFor(
        IReadOnlyCollection<SiteRegisterRecord> records,
        CancellationToken cancellationToken)
    {
        var words = records.Select(r => r.WordId).Distinct().ToList();
        var rows = await db.WordEntities
            .Where(a => words.Contains(a.WordId))
            .Select(a => new { a.WordId, a.EntityId })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.WordId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.EntityId).ToHashSet());
    }

    private static SiteSplitOutcome Nothing(bool alreadyLoaded) =>
        new(alreadyLoaded, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], TimeSpan.Zero);

    private static async Task<int> Count(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.CommandTimeout = Annotating.Patient;
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
