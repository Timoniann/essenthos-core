using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Read">Lines in the files, and how many of them are marked to be written.</param>
/// <param name="Missing">Records a line names that the encyclopedia does not hold.</param>
/// <param name="Moved">Lines whose word is not where it was read: no word at the position, or another number on it.</param>
/// <param name="Named">Words that already name somebody, left as they are.</param>
/// <param name="Contested">Words the links reach that already name somebody else, left as they are.</param>
/// <param name="Written">Annotations written under the readings' sources, the carried ones included.</param>
internal sealed record PassageReadingOutcome(
    bool AlreadyLoaded,
    int Read,
    int Marked,
    IReadOnlyList<string> Missing,
    int Moved,
    int Named,
    int Contested,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the words a reading of the passage found are already named"
            : $"{Marked} of {Read} passage readings to be written: {Moved} words no longer where they were read, " +
              $"{Named} already naming somebody, {Contested} carried words already naming somebody else; " +
              $"{Written} words written in {Elapsed}" +
              (Missing.Count > 0 ? $"; no record for {string.Join(", ", Missing)}" : "") +
              ". Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// The word by which a verse speaks of a person the dataset lists there without the verse printing the
/// name — <em>the king</em> of Babylon, <em>the man</em> of God, <em>him</em> — as two readings of the
/// passage agreed on it (<c>scripts/references.py</c>), written on the original word and carried across
/// the links.
///
/// <para>
/// A model's reading, so it carries the reading's own confidence and a source naming the model, the
/// prompt and the day, or the one reading that decided it and on whose word. A word that names anybody
/// already is never touched, whoever settled it, and a carried word that already names somebody else
/// keeps its answer. Idempotent on each of its sources, so a file of readings added later is written
/// beside the ones already there.
/// </para>
/// </summary>
internal sealed class PassageReadingLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<PassageReadingLoader> logger)
{
    private const string SourceShape =
        "Essenthos, a reading of the passage by {0}, prompt {1}, upheld by a second reading by {2}, asked {3}";

    private const string Words =
        """
        SELECT x.n, w.id, coalesce(w.strong_number, ''),
               EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = w.id)
        FROM unnest(@texts, @books, @chapters, @verses, @positions) WITH ORDINALITY AS x(slug, b, c, v, p, n)
        JOIN text t ON t.slug = x.slug
        JOIN verse_reference r ON r.canonical_book = x.b AND r.canonical_chapter = x.c
                              AND r.canonical_verse = x.v AND r.is_primary
        JOIN verse ve ON ve.id = r.verse_id AND ve.text_id = t.id
        JOIN word w ON w.verse_id = ve.id AND w.position = x.p
        """;

    /// <summary>A carried word that already names another entity is not given a second answer.</summary>
    private const string Contest =
        """
        DELETE FROM pending_annotation a
        USING word_entity already
        WHERE already.word_id = a.word_id AND already.entity_id <> a.entity_id
        """;

    private const string DecidedShape = "Essenthos, {0}";

    public static string SourceOf(PassageReadingRecord line) =>
        line.DecidedBy is { Length: > 0 } decided
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, DecidedShape, decided)
            : string.Format(
                System.Globalization.CultureInfo.InvariantCulture, SourceShape,
                line.Reading?.Model, line.Reading?.PromptVersion, line.Check?.Model, line.Reading?.AskedAt);

    public async Task<PassageReadingOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        var directory = configuration[PassageReadingFiles.ConfigurationKey] is { Length: > 0 } set
            ? set
            : Path.Combine(resources, PassageReadingFiles.DefaultFolder);
        if (!Directory.Exists(directory))
        {
            logger.LogWarning(
                "No passage readings at {Directory}, so the words only a reading of the passage finds stay "
                + "unnamed. Produce them with \"python scripts/references.py publish\", or point {Key} at a "
                + "folder that holds them",
                directory, PassageReadingFiles.ConfigurationKey);
            return new PassageReadingOutcome(false, 0, 0, [], 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        return await Load(PassageReadingFiles.Read(directory), cancellationToken);
    }

    internal async Task<PassageReadingOutcome> Load(
        IReadOnlyList<PassageReadingRecord> lines,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var readable = lines
            .Where(line => line.Write && line.Confidence is not null && line.Reading?.Model is not null)
            .ToList();
        var sources = readable.Select(SourceOf).Distinct().ToList();
        var written = sources.Count == 0
            ? []
            : (await db.WordEntities
                .Where(a => sources.Contains(a.Source))
                .Select(a => a.Source)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var marked = readable.Where(line => !written.Contains(SourceOf(line))).ToList();
        if (marked.Count == 0 && written.Count > 0)
        {
            logger.LogInformation("The words a reading of the passage found are already named; nothing to do");
            return new PassageReadingOutcome(true, lines.Count, readable.Count, [], 0, 0, 0, 0, [], started.Elapsed);
        }

        var slugs = marked.Select(line => line.Record).Distinct().ToList();
        var records = await db.Entities.AsNoTracking()
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, cancellationToken);
        var missing = slugs.Where(slug => !records.ContainsKey(slug)).Order(StringComparer.Ordinal).ToList();

        var held = marked
            .Select(line => (Line: line, At: Place(line.Reference)))
            .Where(x => records.ContainsKey(x.Line.Record))
            .ToList();
        var placed = held.Where(x => x.At is not null).ToList();

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var found = await Find(connection, placed.Select(x => (x.Line.Text, x.At!.Value, x.Line.Position)).ToList(),
            cancellationToken);

        var moved = held.Count - found.Count;
        var named = 0;
        var bySource = new Dictionary<string, List<(long, int, double?, bool, string)>>(StringComparer.Ordinal);
        for (var index = 0; index < placed.Count; index++)
        {
            if (!found.TryGetValue(index + 1, out var word))
            {
                continue;
            }

            var line = placed[index].Line;
            if (!string.Equals(word.Strong, line.Strong ?? "", StringComparison.Ordinal))
            {
                moved++;
                continue;
            }

            if (word.Annotated)
            {
                named++;
                continue;
            }

            var source = SourceOf(line);
            (bySource.TryGetValue(source, out var seed) ? seed : bySource[source] = [])
                .Add((word.Id, records[line.Record], Math.Clamp(line.Confidence!.Value, 0, 1), true,
                    line.Reason ?? line.Class ?? ""));
        }

        var contested = 0;
        var byText = new Dictionary<string, int>(StringComparer.Ordinal);
        var method = EnumSpelling.Of(LinkMethod.ModelReading);
        foreach (var (source, seed) in bySource)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
            await Annotating.Seed(connection, seed, cancellationToken);
            await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
            await using (var contest = new NpgsqlCommand(
                             Contest, connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
            {
                contest.CommandTimeout = Annotating.Patient;
                contested += await contest.ExecuteNonQueryAsync(cancellationToken);
            }

            await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
                ("method", method), ("source", source));
            await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
                ("method", method), ("source", source));
            foreach (var (text, words) in await Annotating.ByText(connection, transaction, source, cancellationToken))
            {
                byText[text] = byText.GetValueOrDefault(text) + words;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var outcome = new PassageReadingOutcome(
            false, lines.Count, marked.Count, missing, moved, named, contested, byText.Values.Sum(),
            [.. byText.OrderByDescending(t => t.Value).Select(t => (t.Key, t.Value))], started.Elapsed);
        logger.LogInformation("Named the words a reading of the passage found: {Outcome}", outcome);
        return outcome;
    }

    /// <summary><c>2KI 25:21</c> as a canonical address, or null where it is not one.</summary>
    internal static CanonicalReference? Place(string reference)
    {
        var space = reference.LastIndexOf(' ');
        var colon = reference.IndexOf(':', Math.Max(space, 0));
        if (space <= 0 || colon < 0
            || !BookCodes.TryGetOrdinal(reference[..space], out var book)
            || !int.TryParse(reference.AsSpan(space + 1, colon - space - 1), out var chapter)
            || !int.TryParse(reference.AsSpan(colon + 1), out var verse))
        {
            return null;
        }

        return new CanonicalReference(book, chapter, verse);
    }

    private static async Task<Dictionary<long, (long Id, string Strong, bool Annotated)>> Find(
        NpgsqlConnection connection,
        IReadOnlyList<(string Text, CanonicalReference At, int Position)> wanted,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Words, connection);
        command.Parameters.AddWithValue("texts", wanted.Select(w => w.Text).ToArray());
        command.Parameters.AddWithValue("books", wanted.Select(w => w.At.Book).ToArray());
        command.Parameters.AddWithValue("chapters", wanted.Select(w => w.At.Chapter).ToArray());
        command.Parameters.AddWithValue("verses", wanted.Select(w => w.At.Verse).ToArray());
        command.Parameters.AddWithValue("positions", wanted.Select(w => w.Position).ToArray());
        command.CommandTimeout = Annotating.Patient;
        var found = new Dictionary<long, (long, string, bool)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found[reader.GetInt64(0)] = (reader.GetInt64(1), reader.GetString(2), reader.GetBoolean(3));
        }

        return found;
    }
}
