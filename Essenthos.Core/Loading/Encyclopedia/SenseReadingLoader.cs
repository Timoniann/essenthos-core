using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Superseded">
/// Answers a later run replaced, because the name was asked again on a repaired candidate list.
/// Counted because a zero here would mean the second campaign was silently not being applied, and
/// the whole point of asking again is that the later answer is the one that stands.
/// </param>
/// <param name="Refused">
/// Readings a second pass read again and contradicted, whose answer has not changed since. Counted
/// rather than merely skipped, because a number that quietly went to zero would mean the review
/// files had stopped being read.
/// </param>
/// <param name="Decided">
/// Words a ruling settles — the owner's, or the review naming the referent it found instead. The
/// reading is not loaded for them and the word is not left blank: a ruling annotates it, with what
/// was ruled out recorded beside the answer.
/// </param>
/// <param name="Unlisted">
/// Occurrences where the model said the encyclopedia holds nobody who fits. Not a failure — it is
/// the answer a tie-break could never give — but not an annotation either, because there is no
/// record to point at.
/// </param>
/// <param name="Corroborated">
/// Annotations the encyclopedia's own list of verses independently agrees with. A second claim, not
/// a higher number: that list is known to put verses on the wrong man.
/// </param>
internal sealed record SenseReadingOutcome(
    bool AlreadyLoaded,
    bool NoReadings,
    int Runs,
    int Answers,
    int Occurrences,
    int Superseded,
    int Refused,
    int Decided,
    int Contradicted,
    int Unlisted,
    int Unclear,
    int Unmeasured,
    int Unresolvable,
    int Annotated,
    int Corroborated,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded ? "the model's readings are already loaded"
        : NoReadings ? "no model readings are on this disk, so nothing was loaded from them"
        : $"{Annotated} words name a person or a place on a model's reading, from {Occurrences} " +
          $"occurrences answered over {Runs} runs, in {Elapsed}: {Corroborated} of them in a verse " +
          $"the encyclopedia independently says that entity is named in, and {Superseded} of them " +
          "answered again on a repaired candidate list. Not loaded: " +
          $"{Refused} readings a second pass found wrong, {Decided} a ruling settles instead, " +
          $"{Contradicted} a run answered two ways, " +
          $"{Unlisted} naming somebody the encyclopedia does not hold, {Unclear} the model would not " +
          $"answer, {Unmeasured} in a confidence band nobody has measured, and {Unresolvable} naming " +
          "a record that is no longer there. Per text: " +
          string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// The readings a model gave for the names the lexicon cannot settle, written as claims.
///
/// <see cref="EntityAnnotationLoader"/> answers the occurrences where nobody has to choose: one
/// Strong number, one entity that bears it. It stops at 515 numbers that answer with several people
/// — the twenty-three men called Zechariah — and those are most of the names a reader actually
/// wants disambiguated. Nothing in the corpus decides them, and only reading the verse does.
///
/// <para>
/// **So this is the one place where a guess becomes a row a reader is shown, and everything here is
/// arranged so that it cannot arrive looking like scholarship.** The method is its own —
/// <c>model-reading</c>, standing below every resolution the lexicon made, so a reading can add an
/// answer where there was none and can never displace one. The confidence is the measured share of
/// readings in that band that survived a second pass, not a number chosen to look right. The model,
/// the prompt version and the date of the run travel into the claim, so a reader who wants to know
/// who said this gets a model and a date rather than the corpus's own voice. And an answer a second
/// pass read again and contradicted is never loaded as the answer, because a reading known to be
/// wrong is evidence about the method and not data about the text.
/// </para>
///
/// <para>
/// **What is refused here is the answer and never the question.** The word still names somebody, so
/// a verdict hands the word on rather than dropping it: where the review named the referent it
/// found instead, that referent is a ruling and the overturned reading travels with it as the thing
/// that was ruled out; where it named somebody no dataset holds, the record is written; and where
/// nobody could tell, the record says so and names the candidates. A verdict is also about a
/// reading and not about a word, so a name asked again on a repaired candidate list is judged on
/// the answer it gives now.
/// </para>
///
/// <para>
/// Idempotent on its own rows, and on its own method rather than on the annotation table: the
/// resolutions are already there when this runs, so asking whether anything is annotated would
/// answer yes on every boot and this would never run at all.
/// </para>
/// </summary>
internal sealed class SenseReadingLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<SenseReadingLoader> logger)
{
    /// <summary>
    /// The text the readings were asked about. Every other text is reached from it along the links
    /// that already exist, exactly as the resolutions are.
    /// </summary>
    public const string Witness = EntityCandidates.Witness;

    /// <summary>
    /// How often a reading the model called <c>high</c> survived being read again.
    ///
    /// Measured, not chosen: of 8,059 high-confidence readings that a second pass reviewed — the
    /// audit of the ones the encyclopedia's verse lists agree with, and the adjudication of the ones
    /// they contradict — 61 were found wrong. That is 0.9924, kept at two figures because the
    /// sample cannot support a third.
    /// </summary>
    private const double HighBand = 0.99;

    /// <summary>
    /// The same for <c>medium</c>: 186 reviewed, 13 found wrong, 0.9301. The band is doing real
    /// work — it is nine times likelier to be wrong than the one above it — which is the argument
    /// for storing the model's own word as a number rather than flattening every reading to one
    /// figure.
    /// </summary>
    private const double MediumBand = 0.93;

    /// <summary>
    /// A band nobody could measure. The model used <c>low</c> on seventeen answers in the whole
    /// corpus and only one of them names an entity, so there is no rate to state and no honest
    /// number to store — and a made-up one on a row the model itself was unsure of is the exact
    /// shape of the failure this loader exists to avoid. Those readings are not loaded.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, double> Measured =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["high"] = HighBand,
            ["medium"] = MediumBand,
        };

    private const string VerseList =
        "the encyclopedia's own list of the verses each entity is named in";

    public async Task<SenseReadingOutcome> Load(
        string resources,
        CancellationToken cancellationToken = default)
    {
        var method = LinkMethod.ModelReading;
        if (await db.WordEntities.AnyAsync(
                a => a.Method == method && a.Source.StartsWith(SourcePrefix), cancellationToken))
        {
            logger.LogInformation("The model's readings are already loaded; nothing to do");
            return Nothing(alreadyLoaded: true);
        }

        var directory = Where(resources);
        if (!Directory.Exists(directory))
        {
            logger.LogInformation(
                "No model readings at {Directory}, so none were loaded. They are a by-product of "
                + "running the model over the corpus and stay out of the repository; point "
                + "\"{Key}\" at a directory of run folders, each holding {File}, to load them",
                directory,
                SenseReadingFiles.ConfigurationKey,
                SenseReadingFiles.AnswersFileName);
            return Nothing(alreadyLoaded: false);
        }

        var started = Stopwatch.StartNew();
        var (readings, contradicted, answers, runs, superseded) = SenseReadingFiles.Read(directory);
        var refused = SenseReadingFiles.Refused().Readings.ToDictionary(r => r.WordId);
        var ruled = Ruled();

        var wanted = new List<SenseReading>(readings.Count);
        int unlisted = 0, unclear = 0, unmeasured = 0, blocked = 0, decided = 0;
        foreach (var reading in readings)
        {
            if (reading.Referent == SenseReading.Unlisted)
            {
                unlisted++;
            }
            else if (reading.Referent == SenseReading.Unclear)
            {
                unclear++;
            }
            else if (ruled.Contains(reading.WordId))
            {
                decided++;
            }
            else if (Overturned(refused, reading) || contradicted.Contains(reading.WordId))
            {
                blocked++;
            }
            else if (!Measured.ContainsKey(reading.Confidence))
            {
                unmeasured++;
            }
            else
            {
                wanted.Add(reading);
            }
        }

        var entities = await Referents(wanted, cancellationToken);
        var seed = new List<(long, int, double?, bool, string)>(wanted.Count);
        foreach (var reading in wanted)
        {
            if (entities.TryGetValue(reading.Referent, out var entity))
            {
                seed.Add((reading.WordId, entity, Measured[reading.Confidence], false, Note(reading)));
            }
        }

        var unresolvable = wanted.Count - seed.Count;
        if (unresolvable > 0)
        {
            logger.LogWarning(
                "{Count} readings name a record the encyclopedia no longer holds and were not loaded. "
                + "That happens when a record is merged or renamed after a run; re-running the model "
                + "against the current encyclopedia is what closes it",
                unresolvable);
        }

        if (seed.Count == 0)
        {
            logger.LogWarning(
                "No reading survived the refusals and the referent lookup, so nothing was annotated. "
                + "Either the readings are for a different corpus or the encyclopedia has not been "
                + "loaded yet; both are earlier steps of the same pipeline");
            return Nothing(alreadyLoaded: false) with
            {
                Runs = runs, Answers = answers, Occurrences = readings.Count,
                Superseded = superseded, Refused = blocked, Decided = decided,
                Contradicted = contradicted.Count, Unlisted = unlisted, Unclear = unclear,
                Unmeasured = unmeasured, Unresolvable = unresolvable, Elapsed = started.Elapsed,
            };
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Seed(connection, seed, cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.MarkCorroboration, cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Carry, cancellationToken,
            ("witness", Witness));

        var spelled = EnumSpelling.Of(method);
        var source = Source(wanted);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", spelled), ("source", source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", spelled), ("source", source));
        await Annotating.Run(connection, transaction, Annotating.Corroboration, cancellationToken,
            ("method", EnumSpelling.Of(LinkMethod.StatedBySource)), ("source", VerseList),
            ("note", "the encyclopedia states that this entity is named in this verse"));

        var byText = await Annotating.ByText(connection, transaction, source, cancellationToken);
        var corroborated = await Count(
            connection, transaction, "SELECT count(*) FROM pending_annotation WHERE corroborated",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new SenseReadingOutcome(
            false, false, runs, answers, readings.Count, superseded, blocked, decided,
            contradicted.Count, unlisted, unclear, unmeasured, unresolvable,
            byText.Sum(t => t.Words), corroborated, byText, started.Elapsed);
        logger.LogInformation("Read: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Whether a second pass overturned the answer this reading actually gives.
    ///
    /// The verdict names the reading it overturned, and it is compared rather than assumed, because
    /// a word that has since been asked again on a repaired candidate list gives a different answer
    /// and nobody has reviewed that one. Refusing it on the strength of a verdict about the answer
    /// it no longer gives would throw away the second campaign without saying so — and where the
    /// re-ask arrived at what the review said, refusing would drop the very answer the review asked
    /// for.
    /// </summary>
    private static bool Overturned(
        IReadOnlyDictionary<long, RefusedReading> refused,
        SenseReading reading) =>
        refused.TryGetValue(reading.WordId, out var verdict)
        && string.Equals(verdict.Reading, reading.Referent, StringComparison.Ordinal);

    /// <summary>
    /// Every word a ruling has settled, whether the owner's or the review's. A decision beats a
    /// reading, and the two loaders would otherwise both annotate the word — the ruling with the
    /// referent somebody decided on and this with the one that was overturned.
    /// </summary>
    private static HashSet<long> Ruled() =>
    [
        .. SenseReadingFiles.Rulings().Rulings.Select(r => r.WordId),
        .. SenseReadingFiles.ReviewRulings().Rulings.Select(r => r.WordId),
    ];

    private static SenseReadingOutcome Nothing(bool alreadyLoaded) =>
        new(alreadyLoaded, !alreadyLoaded, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], TimeSpan.Zero);

    /// <summary>
    /// Where the run folders are. Under the corpus sources by default, because that is what they
    /// are: a large by-product of a run, kept out of the repository like every other one.
    /// </summary>
    private string Where(string resources)
    {
        var configured = configuration[SenseReadingFiles.ConfigurationKey];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(resources, SenseReadingFiles.DefaultFolder)
            : configured;
    }

    /// <summary>
    /// Which entity each answered slug is, in one query rather than one per reading.
    /// </summary>
    private async Task<Dictionary<string, int>> Referents(
        IReadOnlyCollection<SenseReading> readings,
        CancellationToken cancellationToken)
    {
        var slugs = readings.Select(r => r.Referent).Distinct().ToList();
        return await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
    }

    /// <summary>
    /// What the reader is shown as the reason: the model's own sentence, and the confidence it
    /// attached to it in its own word rather than only as the number the band was measured at.
    /// </summary>
    private static string Note(SenseReading reading) =>
        $"{reading.StrongNumber}, read as {reading.Referent} with {reading.Confidence} confidence" +
        (string.IsNullOrWhiteSpace(reading.Reason) ? string.Empty : $": {reading.Reason}");

    /// <summary>
    /// Who said it, in the words a reader gets on the card: which model, under which prompt, and
    /// when. Taken from the answers themselves rather than declared here, so a second run under a
    /// different model or a revised prompt cannot be stored under the first one's name.
    /// </summary>
    /// <summary>
    /// How every source string written here begins, which is what tells this loader's rows from
    /// every other row that carries the same method — the encyclopedia can also hold records
    /// written from a reading, and those are the reading's work rather than this pass's.
    /// </summary>
    private const string SourcePrefix = "a reading of the verse by";

    private static string Source(IReadOnlyCollection<SenseReading> readings)
    {
        var models = readings.Select(r => r.Model).Distinct().Order(StringComparer.Ordinal);
        var prompts = readings.Select(r => r.PromptVersion).Distinct().Order(StringComparer.Ordinal);
        var latest = readings.Select(r => r.Run).Max(StringComparer.Ordinal) ?? string.Empty;

        return $"{SourcePrefix} {string.Join(" and ", models)}, prompt " +
               $"{string.Join(" and ", prompts)}, run to {latest[..Math.Min(DayLength, latest.Length)]}";
    }

    /// <summary>The date out of the run's timestamp, which is the part a reader can use.</summary>
    private const int DayLength = 10;

    private static async Task<int> Count(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.CommandTimeout = Annotating.Patient;
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
