using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Read">Readings in the file.</param>
/// <param name="Missing">Records a reading names that the encyclopedia does not hold.</param>
/// <param name="Listed">Verse references these readings stand behind after the pass.</param>
/// <param name="Added">Of those, the ones this run wrote.</param>
/// <param name="Withdrawn">References an earlier run wrote that the file no longer holds, taken back.</param>
/// <param name="Moved">Words no longer where they were read: no word at the position, or another number on it.</param>
/// <param name="Named">Words that already name another record, left as they are.</param>
/// <param name="Contested">Words the links reach that already name another record, left as they are.</param>
/// <param name="Seeded">Original words annotated to the record a reading found them standing for.</param>
internal sealed record VerseReadingOutcome(
    bool AlreadyLoaded,
    int Read,
    IReadOnlyList<string> Missing,
    int Listed,
    int Added,
    int Withdrawn,
    int Moved,
    int Named,
    int Contested,
    int Seeded,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        (AlreadyLoaded ? "the verses read for the records they speak of are already kept: " : "") +
        $"{Listed} verse references from {Read} readings, {Added} added and {Withdrawn} withdrawn; {Seeded} " +
        $"original words annotated to the record they stand for, {Moved} no longer where they were read, " +
        $"{Named} already naming another record, {Contested} carried words already naming another, in {Elapsed}" +
        (Missing.Count > 0 ? $"; no record for {string.Join(", ", Missing)}" : "") +
        (ByText.Count > 0 ? ". Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}")) : "");
}

/// <summary>
/// The verses a dataset lists for a record and no word of ours named it in, kept as this project's own
/// where two readings of the verse agree that it speaks of the record.
///
/// <para>
/// **The reference is of the verse, and says which kind it is.** A verse that says <em>the king of
/// Babylon</em> of Nebuchadnezzar, <em>the Philistine</em> of Goliath or <em>him</em> of Jesus does not
/// name him, and the row says so in its source (<see cref="Corpus.ReferenceKinds.SpokenOfMark"/>) and
/// carries the words that stand for him as its label. A verse where a name stands and means the
/// record, <em>the children of Ammon</em> for the Ammonites, is kept as naming it.
/// </para>
///
/// <para>
/// **Where a noun or a name stands for the record, the word is annotated too**, on the original and
/// across the links, as a reading with its confidence. A word that already names another record is
/// never touched, with one exception the corpus already makes everywhere: a people stands beside the
/// person it is named after, so <em>the Philistine</em> keeps the Philistines and gains Goliath. A
/// tribe's name the man-or-people pass has answered (<see cref="EponymReadingLoader"/>) is not given
/// the other of the two beside it.
/// </para>
///
/// <para>
/// Idempotent on the file: the references and the seeded words are compared with what an earlier run
/// wrote, and what the file no longer holds is taken back. After every pass that names a word, before
/// the verses are read off the words.
/// </para>
/// </summary>
internal sealed class VerseReadingLoader(AppDbContext db, ILogger<VerseReadingLoader> logger)
{
    /// <summary>
    /// Each word a reading points at, with every record it already names, that record's kind, and
    /// whether the man-or-people pass stands behind the answer.
    /// </summary>
    private const string Words =
        """
        SELECT x.n, w.id, coalesce(w.strong_number, ''), a.entity_id, e.kind, a.source,
               a.source = ANY(@decided) OR EXISTS (SELECT 1 FROM word_entity_claim c
                                                   WHERE c.word_entity_id = a.id AND c.source = ANY(@decided))
        FROM unnest(@texts, @books, @chapters, @verses, @positions) WITH ORDINALITY AS x(slug, b, c, v, p, n)
        JOIN text t ON t.slug = x.slug
        JOIN verse_reference r ON r.canonical_book = x.b AND r.canonical_chapter = x.c
                              AND r.canonical_verse = x.v AND r.is_primary
        JOIN verse ve ON ve.id = r.verse_id AND ve.text_id = t.id
        JOIN word w ON w.verse_id = ve.id AND w.position = x.p
        LEFT JOIN word_entity a ON a.word_id = w.id
        LEFT JOIN entity e ON e.id = a.entity_id
        """;

    /// <summary>The seeds written last time: a claim of these sources that no link produced.</summary>
    private const string Seeded =
        """
        SELECT a.word_id, a.entity_id
        FROM word_entity_claim c
        JOIN word_entity a ON a.id = c.word_entity_id
        WHERE c.source = ANY(@sources) AND coalesce(c.note, '') NOT LIKE @carried
        """;

    /// <summary>
    /// A carried word that already names another record is not given a second answer, unless the two
    /// are a person and a people, which stand beside each other.
    /// </summary>
    private const string Contest =
        """
        DELETE FROM pending_annotation a
        USING word_entity already, entity whom, entity mine
        WHERE a.through IS NOT NULL
          AND already.word_id = a.word_id AND already.entity_id <> a.entity_id
          AND whom.id = already.entity_id AND mine.id = a.entity_id
          AND NOT ((mine.kind = @people AND whom.kind = @person) OR (mine.kind = @person AND whom.kind = @people))
        """;

    private static readonly string Person = EnumSpelling.Of(EntityKind.Person);

    private static readonly string People = EnumSpelling.Of(EntityKind.People);

    public Task<VerseReadingOutcome> Load(CancellationToken cancellationToken = default) =>
        Load(VerseReadingFiles.Read(), cancellationToken);

    internal async Task<VerseReadingOutcome> Load(VerseReadings file, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        var slugs = file.Readings.Select(r => r.Record).Distinct().ToList();
        var records = await db.Entities.AsNoTracking()
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new { e.Slug, e.Id, e.Kind })
            .ToDictionaryAsync(e => e.Slug, e => (e.Id, Kind: EnumSpelling.Of(e.Kind)), StringComparer.Ordinal,
                cancellationToken);
        var missing = slugs.Where(slug => !records.ContainsKey(slug)).Order(StringComparer.Ordinal).ToList();

        var placed = file.Readings
            .Select(reading => (Reading: reading, At: PassageReadingLoader.Place(reading.Reference)))
            .Where(x => x.At is not null && records.ContainsKey(x.Reading.Record))
            .Select(x => (x.Reading, At: x.At!.Value, Record: records[x.Reading.Record]))
            .ToList();
        foreach (var unplaced in file.Readings.Where(r => PassageReadingLoader.Place(r.Reference) is null))
        {
            logger.LogWarning(
                "The reading of {Reference} for {Record} names no verse. Write the reference as a book code, a " +
                "chapter and a verse in canonical numbering, as 2KI 25:6",
                unplaced.Reference, unplaced.Record);
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var (seeds, moved, named) = await Seeds(connection, file, placed, cancellationToken);
        var before = await SeededBefore(connection, file, cancellationToken);
        var wordsStand = before.SetEquals(seeds.Select(s => (s.Word, s.Entity)));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (listed, added, withdrawn) = await List(file, placed, cancellationToken);

        var contested = 0;
        IReadOnlyList<(string Text, int Words)> byText = [];
        if (!wordsStand)
        {
            (contested, byText) = await Annotate(connection, transaction, file, seeds, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = new VerseReadingOutcome(
            wordsStand && added == 0 && withdrawn == 0, file.Readings.Count, missing, listed, added, withdrawn,
            moved, named, contested, seeds.Count, byText, started.Elapsed);
        logger.LogInformation("Kept the verses read for the records they speak of: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The verse references the readings stand behind, written where they are not yet and taken back
    /// where the file no longer holds them. A row is a verse once per record and source, so two
    /// readings of one verse for one record are one row, under the first one's words.
    /// </summary>
    private async Task<(int Listed, int Added, int Withdrawn)> List(
        VerseReadings file,
        IReadOnlyList<(VerseReading Reading, CanonicalReference At, (int Id, string Kind) Record)> placed,
        CancellationToken cancellationToken)
    {
        var wanted = placed
            .Select(x => (Key: (x.Record.Id, x.At.Book, x.At.Chapter, x.At.Verse, Source: file.ReferenceSource(x.Reading)),
                Label: x.Reading.Words))
            .DistinctBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Label);

        var sources = file.ReferenceSources.ToList();
        var held = await db.EntityVerses.Where(v => sources.Contains(v.Source)).ToListAsync(cancellationToken);
        var withdrawn = 0;
        foreach (var row in held)
        {
            var key = (row.EntityId, row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse, row.Source);
            if (wanted.Remove(key, out var label))
            {
                row.Label = label;
            }
            else
            {
                db.EntityVerses.Remove(row);
                withdrawn++;
            }
        }

        foreach (var (key, label) in wanted)
        {
            db.EntityVerses.Add(new EntityVerse
            {
                EntityId = key.Id,
                CanonicalBook = key.Book,
                CanonicalChapter = key.Chapter,
                CanonicalVerse = key.Verse,
                Label = label,
                Source = key.Source,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return (held.Count - withdrawn + wanted.Count, wanted.Count, withdrawn);
    }

    /// <summary>
    /// The words to annotate: each word a reading points at that is where it was read and names no
    /// other record, or only one that stands beside the reading's. One answer to a word: a second
    /// reading pointing at a word already taken is counted with the words that name another.
    /// </summary>
    private async Task<(List<(long Word, int Entity, double Confidence, string Source, string Note)> Seeds, int Moved, int Named)>
        Seeds(
            NpgsqlConnection connection,
            VerseReadings file,
            IReadOnlyList<(VerseReading Reading, CanonicalReference At, (int Id, string Kind) Record)> placed,
            CancellationToken cancellationToken)
    {
        var pointed = placed
            .SelectMany(x => (x.Reading.Named ?? []).Select(word => (x.Reading, x.At, x.Record, Word: word)))
            .ToList();
        var seeds = new List<(long, int, double, string, string)>();
        if (pointed.Count == 0)
        {
            return (seeds, 0, 0);
        }

        var found = new Dictionary<long, (long Id, string Strong, List<(int Entity, string Kind, bool Decided)> Names)>();
        var ours = file.WordSources.ToHashSet(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(Words, connection))
        {
            command.Parameters.AddWithValue("texts", pointed.Select(p => p.Word.Text).ToArray());
            command.Parameters.AddWithValue("books", pointed.Select(p => p.At.Book).ToArray());
            command.Parameters.AddWithValue("chapters", pointed.Select(p => p.At.Chapter).ToArray());
            command.Parameters.AddWithValue("verses", pointed.Select(p => p.At.Verse).ToArray());
            command.Parameters.AddWithValue("positions", pointed.Select(p => p.Word.Position).ToArray());
            command.Parameters.AddWithValue("decided", EponymReadingLoader.Sources);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var n = reader.GetInt64(0);
                if (!found.TryGetValue(n, out var word))
                {
                    found[n] = word = (reader.GetInt64(1), reader.GetString(2), []);
                }

                if (!reader.IsDBNull(3) && !ours.Contains(reader.GetString(5)))
                {
                    word.Names.Add((reader.GetInt32(3), reader.GetString(4), reader.GetBoolean(6)));
                }
            }
        }

        int moved = 0, named = 0;
        var taken = new HashSet<long>();
        for (var index = 0; index < pointed.Count; index++)
        {
            var (reading, _, record, at) = pointed[index];
            if (!found.TryGetValue(index + 1, out var word)
                || !string.Equals(word.Strong, at.Strong ?? "", StringComparison.Ordinal))
            {
                moved++;
                continue;
            }

            if (word.Names.Any(other => other.Entity != record.Id && (other.Decided || !Beside(record.Kind, other.Kind)))
                || !taken.Add(word.Id))
            {
                named++;
                continue;
            }

            seeds.Add((word.Id, record.Id, Math.Clamp(reading.Confidence, 0, 1), file.WordSource(reading),
                $"{reading.Reference}, {reading.How ?? reading.Kind}: '{reading.Words}'. {reading.First}"));
        }

        return (seeds, moved, named);
    }

    private static bool Beside(string kind, string other) =>
        (kind == People && other == Person) || (kind == Person && other == People);

    private static async Task<HashSet<(long, int)>> SeededBefore(
        NpgsqlConnection connection,
        VerseReadings file,
        CancellationToken cancellationToken)
    {
        var before = new HashSet<(long, int)>();
        await using var command = new NpgsqlCommand(Seeded, connection);
        command.Parameters.AddWithValue("sources", file.WordSources.ToArray());
        command.Parameters.AddWithValue("carried", Annotating.CarriedNote);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            before.Add((reader.GetInt64(0), reader.GetInt32(1)));
        }

        return before;
    }

    /// <summary>
    /// What an earlier run wrote on the words is taken back and the seeds are written again, each
    /// source's in its own pass, carried across the links.
    /// </summary>
    private static async Task<(int Contested, IReadOnlyList<(string Text, int Words)> ByText)> Annotate(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        VerseReadings file,
        IReadOnlyList<(long Word, int Entity, double Confidence, string Source, string Note)> seeds,
        CancellationToken cancellationToken)
    {
        var method = EnumSpelling.Of(LinkMethod.ModelReading);
        var contested = 0;
        var byText = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in file.WordSources)
        {
            await Annotating.Run(connection, transaction,
                "DELETE FROM word_entity_claim WHERE source = @source; DELETE FROM word_entity WHERE source = @source",
                cancellationToken, ("source", source));

            var seed = seeds.Where(s => s.Source == source).ToList();
            if (seed.Count == 0)
            {
                continue;
            }

            await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
            await Annotating.Seed(
                connection, seed.Select(s => (s.Word, s.Entity, (double?)s.Confidence, false, s.Note)), cancellationToken);
            await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
            await using (var contest = new NpgsqlCommand(
                             Contest, connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
            {
                contest.Parameters.AddWithValue("person", Person);
                contest.Parameters.AddWithValue("people", People);
                contested += await contest.ExecuteNonQueryAsync(cancellationToken);
            }

            await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
                ("method", method), ("source", source));
            await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
                ("method", method), ("source", source));
            await Annotating.Run(connection, transaction, "DROP TABLE pending_annotation", cancellationToken);
            foreach (var (text, words) in await Annotating.ByText(connection, transaction, source, cancellationToken))
            {
                byText[text] = byText.GetValueOrDefault(text) + words;
            }
        }

        return (contested, [.. byText.OrderByDescending(t => t.Value).Select(t => (t.Key, t.Value))]);
    }
}
