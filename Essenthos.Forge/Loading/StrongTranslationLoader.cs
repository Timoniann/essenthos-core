using Essenthos.Core.Corpus;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <param name="Refused">Why each rejected row was rejected, so a bad run is visible as a shape rather than a number.</param>
internal sealed record TranslationRefusals(
    int UnknownNumber,
    int UnknownMethod,
    int NothingSaid,
    int LostReference,
    int LostSense)
{
    public int Total => UnknownNumber + UnknownMethod + NothingSaid + LostReference + LostSense;

    public override string ToString() =>
        $"{UnknownNumber} for a number the dictionary does not hold, " +
        $"{UnknownMethod} produced by something this loader was not told about, " +
        $"{NothingSaid} translating no field at all, " +
        $"{LostReference} dropping a Strong reference the English carries, and " +
        $"{LostSense} whose sense numbering does not match the English";
}

internal sealed record TranslationOutcome(
    bool AlreadyLoaded,
    bool NoTranslations,
    int Files,
    int Records,
    int Replaced,
    int Skipped,
    int Written,
    IReadOnlyList<string> Languages,
    TranslationRefusals Refused,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded ? "every translated lexicon entry on this disk is already loaded"
        : NoTranslations ? "no translated lexicon files are on this disk, so nothing was loaded from them"
        : $"{Written} translated entries in {string.Join(", ", Languages)}, read from {Records} " +
          $"records over {Files} files in {Elapsed}. {Skipped} were already loaded and were left " +
          $"alone, {Replaced} records were superseded by a later file. Refused: {Refused}.";
}

/// <summary>
/// Strong's dictionary in a language other than English, as a translation run published it.
///
/// The reader of the Ukrainian text meets an English gloss the moment he asks what a word means,
/// and there is no Ukrainian Strong to load: every dictionary that circulates under his name is
/// either somebody else's lexicon keyed to his numbers or a real translation somebody owns.
/// Strong's own English is public domain, so this corpus translates it and says on every row that
/// it did.
///
/// <para>
/// **It never touches <see cref="StrongEntry"/>.** The English stays exactly where it was, and a
/// row here stands beside it. That is not tidiness: a machine's rendering of <em>of uncertain
/// affinity</em> is a new claim, and a claim written over the thing it renders cannot be checked
/// by the reader it is shown to.
/// </para>
///
/// <para>
/// **It refuses four things and counts each.** A number the dictionary does not hold has nothing to
/// stand beside. A row translating no field at all is an empty row wearing a provenance. A
/// translation that dropped a Strong reference or a sense number has lost an address rather than a
/// word, and that is the failure this whole exercise is measured on — the harness scores it at
/// 99.7% and the loader is where a run that did worse stops.
/// </para>
///
/// <para>
/// **The guard is per number and per language.** Guarding on "does the table hold anything" would
/// mean the German run could never be loaded beside the Ukrainian one, and a corrected batch could
/// never be loaded beside an uncorrected one. A row already present is left exactly as it is.
/// </para>
/// </summary>
internal sealed partial class StrongTranslationLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<StrongTranslationLoader> logger)
{
    /// <summary>
    /// How every source string written here begins. It is what tells this loader's rows from any
    /// other row carrying the same method.
    /// </summary>
    public const string SourcePrefix = Sources.StrongTranslationPrefix;

    private const string Import =
        """
        COPY strong_entry_translation (strong_number, language, definition, derivation,
                                       kjv_definition, detailed_definition, method, confidence,
                                       source, note)
        FROM STDIN (FORMAT BINARY)
        """;

    /// <summary>The date out of the run's timestamp, which is the part a reader can use.</summary>
    private const int DayLength = 10;

    public async Task<TranslationOutcome> Load(
        string resources,
        CancellationToken cancellationToken = default)
    {
        var directory = Where(resources);
        if (!Directory.Exists(directory))
        {
            logger.LogInformation(
                "No translated lexicon files at {Directory}, so Strong's dictionary is English "
                + "only. They are what scripts/lexicon.py publish writes and they stay out of the "
                + "repository; point \"{Key}\" at a directory of {Pattern} files to load them",
                directory,
                StrongTranslationFiles.ConfigurationKey,
                StrongTranslationFiles.FilePattern);
            return Nothing(alreadyLoaded: false);
        }

        var started = Stopwatch.StartNew();
        var (records, files, replaced) = StrongTranslationFiles.Read(directory);
        if (records.Count == 0)
        {
            return Nothing(alreadyLoaded: false) with { Files = files, Elapsed = started.Elapsed };
        }

        var english = await English(cancellationToken);
        var loaded = await Loaded(records, cancellationToken);

        int unknownNumber = 0, unknownMethod = 0, nothingSaid = 0, lostReference = 0, lostSense = 0;
        var skipped = 0;
        var rows = new List<StrongEntryTranslation>(records.Count);

        foreach (var record in records)
        {
            if (!string.Equals(
                    record.Method, StrongTranslationFiles.ModelTranslation, StringComparison.Ordinal))
            {
                unknownMethod++;
                continue;
            }

            if (!english.TryGetValue(record.StrongNumber, out var source))
            {
                unknownNumber++;
                continue;
            }

            if (loaded.Contains((record.StrongNumber, record.Language)))
            {
                skipped++;
                continue;
            }

            if (Empty(record))
            {
                nothingSaid++;
                continue;
            }

            var kept = Kept(source, record);
            if (kept is Skeleton.LostReference)
            {
                lostReference++;
                continue;
            }

            if (kept is Skeleton.LostSense)
            {
                lostSense++;
                continue;
            }

            rows.Add(new StrongEntryTranslation
            {
                StrongNumber = record.StrongNumber,
                Language = record.Language,
                Definition = Text(record.Definition),
                Derivation = Text(record.Derivation),
                KjvDefinition = Text(record.KjvDefinition),
                DetailedDefinition = Text(record.DetailedDefinition),
                Method = LinkMethod.ModelReading,
                Confidence = null,
                Source = Source(record),
                Note = Doubt(record),
            });
        }

        if (rows.Count > 0)
        {
            await Write(rows, cancellationToken);
        }

        var refused = new TranslationRefusals(
            unknownNumber, unknownMethod, nothingSaid, lostReference, lostSense);

        var outcome = new TranslationOutcome(
            AlreadyLoaded: rows.Count == 0 && skipped == records.Count,
            NoTranslations: false,
            files,
            records.Count,
            replaced,
            skipped,
            rows.Count,
            [.. rows.Select(r => r.Language).Distinct().Order(StringComparer.Ordinal)],
            refused,
            started.Elapsed);

        if (refused.Total > 0)
        {
            logger.LogWarning(
                "{Count} translated lexicon entries were refused and are not in the corpus: "
                + "{Refused}. A translation that lost a Strong reference or a sense number lost an "
                + "address rather than a word, and nothing downstream could tell",
                refused.Total,
                refused);
        }

        logger.LogInformation("Translated: {Outcome}", outcome);
        return outcome;
    }

    private static TranslationOutcome Nothing(bool alreadyLoaded) =>
        new(alreadyLoaded, !alreadyLoaded, 0, 0, 0, 0, 0, [],
            new TranslationRefusals(0, 0, 0, 0, 0), TimeSpan.Zero);

    /// <summary>
    /// Where the files are. Under the corpus sources by default, in this project's own folder:
    /// they are the one part of that tree nobody else made.
    /// </summary>
    private string Where(string resources)
    {
        var configured = configuration[StrongTranslationFiles.ConfigurationKey];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(resources, StrongTranslationFiles.DefaultFolder)
            : configured;
    }

    /// <summary>The English a row claims to render, which is what the skeleton is checked against.</summary>
    private async Task<Dictionary<string, StrongEntry>> English(CancellationToken cancellationToken) =>
        await db.StrongEntries.AsNoTracking().ToDictionaryAsync(
            entry => entry.StrongNumber, StringComparer.Ordinal, cancellationToken);

    private async Task<HashSet<(string Number, string Language)>> Loaded(
        IReadOnlyList<StrongTranslationRecord> records,
        CancellationToken cancellationToken)
    {
        var languages = records.Select(r => r.Language).Distinct().ToList();
        var already = await db.StrongEntryTranslations
            .AsNoTracking()
            .Where(t => languages.Contains(t.Language))
            .Select(t => new { t.StrongNumber, t.Language })
            .ToListAsync(cancellationToken);

        return [.. already.Select(t => (t.StrongNumber, t.Language))];
    }

    /// <summary>
    /// Who said it, in the words a reader gets: which model, under which prompt, and on which day.
    /// Taken from the record rather than declared here, so a second run under a different model or
    /// a revised prompt cannot be stored under the first one's name.
    /// </summary>
    private static string Source(StrongTranslationRecord record) =>
        $"{SourcePrefix} {record.Model}, prompt {record.PromptVersion}, run "
        + record.TranslatedAt[..Math.Min(DayLength, record.TranslatedAt.Length)];

    private static string? Doubt(StrongTranslationRecord record) =>
        record.Uncertain is { Count: > 0 } terms
            ? "the translator was unsure of: " + string.Join(", ", terms)
            : null;

    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool Empty(StrongTranslationRecord record) =>
        Text(record.Definition) is null
        && Text(record.Derivation) is null
        && Text(record.KjvDefinition) is null
        && Text(record.DetailedDefinition) is null;

    private enum Skeleton
    {
        Kept,
        LostReference,
        LostSense,
    }

    /// <summary>
    /// Whether the addresses inside the prose survived being translated.
    ///
    /// A Strong reference and a sense number are not words. <c>from G25;</c> points at another
    /// entry and <c>1a)</c> points at a sense, and a translation that dropped one reads perfectly
    /// and answers a different question. The run's own harness checks this too; it is checked again
    /// here because this is the last place before the corpus, and a run made by something other
    /// than that harness would otherwise arrive unchecked.
    /// </summary>
    private static Skeleton Kept(StrongEntry source, StrongTranslationRecord record)
    {
        foreach (var (english, translated) in new[]
                 {
                     (source.Definition, record.Definition),
                     (source.Derivation, record.Derivation),
                     (source.KjvDefinition, record.KjvDefinition),
                     (source.DetailedDefinition, record.DetailedDefinition),
                 })
        {
            if (Text(english) is not { } wanted || Text(translated) is not { } got)
            {
                continue;
            }

            if (References(wanted).Except(References(got), StringComparer.Ordinal).Any())
            {
                return Skeleton.LostReference;
            }
        }

        if (Text(source.DetailedDefinition) is { } senses
            && Text(record.DetailedDefinition) is { } rendered
            && !Senses(senses).SequenceEqual(Senses(rendered), StringComparer.Ordinal))
        {
            return Skeleton.LostSense;
        }

        return Skeleton.Kept;
    }

    private static IEnumerable<string> References(string text) =>
        StrongReference().Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal);

    private static IEnumerable<string> Senses(string text) =>
        SenseNumber().Matches(text).Select(match => match.Groups[1].Value);

    [GeneratedRegex(@"\b[GH]\d{1,4}\b")]
    private static partial Regex StrongReference();

    [GeneratedRegex(@"^\s*(\d+[a-z]?)\)", RegexOptions.Multiline)]
    private static partial Regex SenseNumber();

    private async Task Write(
        IReadOnlyList<StrongEntryTranslation> rows,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var method = EnumSpelling.Of(LinkMethod.ModelReading);

        await using var writer = await connection.BeginBinaryImportAsync(Import, cancellationToken);
        foreach (var row in rows)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(row.StrongNumber, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(row.Language, NpgsqlDbType.Text, cancellationToken);
            await Optional(writer, row.Definition, cancellationToken);
            await Optional(writer, row.Derivation, cancellationToken);
            await Optional(writer, row.KjvDefinition, cancellationToken);
            await Optional(writer, row.DetailedDefinition, cancellationToken);
            await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteNullAsync(cancellationToken);
            await writer.WriteAsync(row.Source, NpgsqlDbType.Text, cancellationToken);
            await Optional(writer, row.Note, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }

    private static async Task Optional(
        NpgsqlBinaryImporter writer,
        string? value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            await writer.WriteNullAsync(cancellationToken);
            return;
        }

        await writer.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);
    }
}
