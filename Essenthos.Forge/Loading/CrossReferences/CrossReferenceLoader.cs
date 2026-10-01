using System.Diagnostics;
using System.Text;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.CrossReferences;

/// <param name="Set">The set, as <see cref="CrossReferenceSets"/> names it.</param>
/// <param name="Rows">References written; for the parallels, pairs of verses, each both ways round.</param>
/// <param name="Unread">Pieces of the source that did not read as a verse, and were dropped.</param>
/// <param name="Passages">For the parallels, the passages the pairs belong to.</param>
internal sealed record CrossReferenceOutcome(
    string Set,
    bool AlreadyLoaded,
    int Rows,
    int Unread,
    int Passages,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"The {Set} set of cross references is already loaded"
            : Set == CrossReferenceSets.Parallels
                ? $"{Passages} parallel passages as {Rows} rows of verse pairs, in {Elapsed}"
                : $"{Rows} {Set} cross references, with {Unread} pieces that did not read as a verse, in {Elapsed}";
}

/// <summary>
/// The sets of cross references a reader can choose between beside a verse: OpenBible.info's,
/// ranked by its readers' votes; the Treasury of Scripture Knowledge's, in its own order; and the
/// parallel passages this project finds in the originals, each with the words the two share.
///
/// <para>
/// Idempotent set by set: a set with rows is left as it is, so adding a set to a corpus that has the
/// others is a run of this and nothing else. The parallels read the loaded Hebrew and Greek, so they
/// come after the texts and their lemmas.
/// </para>
/// </summary>
internal sealed class CrossReferenceLoader(AppDbContext db, ILogger<CrossReferenceLoader> logger)
{
    private const string Import =
        """
        COPY cross_reference (set, source, book, chapter, verse, to_book, to_chapter, to_verse,
                              to_end_book, to_end_chapter, to_end_verse, rank, votes, note, passage,
                              matched_in, matched)
        FROM STDIN (FORMAT BINARY)
        """;

    /// <summary>The words of one original in the order they are read, with the frame's address of each.</summary>
    private const string Lemmas =
        """
        SELECT r.canonical_book, r.canonical_chapter, r.canonical_verse, w.position, coalesce(w.lemma, w.text)
        FROM word w
        JOIN text t ON t.id = w.text_id
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        WHERE t.slug = @slug AND NOT w.elided
        ORDER BY r.canonical_book, r.canonical_chapter, r.canonical_verse, w.position
        """;

    public async Task<List<CrossReferenceOutcome>> Load(string resources, CancellationToken cancellationToken = default) =>
    [
        await LoadOpenBible(resources, cancellationToken),
        await LoadTreasury(resources, cancellationToken),
        await LoadParallels(cancellationToken),
    ];

    public Task<CrossReferenceOutcome> LoadOpenBible(string resources, CancellationToken cancellationToken = default) =>
        LoadSet(CrossReferenceSets.OpenBible, () =>
        {
            var (rows, unread) = OpenBibleCrossReferences.Read(
                Path.Combine(resources, OpenBibleCrossReferences.Folder, OpenBibleCrossReferences.FileName));
            return Task.FromResult((rows, unread, 0));
        }, cancellationToken);

    public Task<CrossReferenceOutcome> LoadTreasury(string resources, CancellationToken cancellationToken = default) =>
        LoadSet(CrossReferenceSets.Treasury, () =>
        {
            var (rows, unread) = TreasuryReferences.Read(Path.Combine(resources, TreasuryReferences.Folder));
            return Task.FromResult((rows, unread, 0));
        }, cancellationToken);

    public Task<CrossReferenceOutcome> LoadParallels(CancellationToken cancellationToken = default) =>
        LoadSet(CrossReferenceSets.Parallels, () => Detect(cancellationToken), cancellationToken);

    /// <summary>The parallel passages as they would be written, one line each, for reading before they are; writes nothing.</summary>
    public async Task<List<string>> MeasureParallels(CancellationToken cancellationToken = default)
    {
        var (rows, _, _) = await Detect(cancellationToken);
        return
        [
            .. rows.Where(row => row.Passage % 2 == 1)
                .GroupBy(row => row.Passage)
                .Select(passage =>
                {
                    var from = passage.Select(row => row.From).ToList();
                    var to = passage.Select(row => row.To).ToList();
                    return $"{passage.Sum(Shared),4} {from.Min()}-{from.Max()} = {to.Min()}-{to.Max()} ({passage.First().MatchedIn})";
                }),
        ];
    }

    private async Task<CrossReferenceOutcome> LoadSet(
        string set,
        Func<Task<(List<CrossReferenceRow> Rows, int Unread, int Passages)>> read,
        CancellationToken cancellationToken)
    {
        if (await db.CrossReferences.AnyAsync(r => r.Set == set, cancellationToken))
        {
            logger.LogInformation("The {Set} set of cross references is already loaded", set);
            return new CrossReferenceOutcome(set, true, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var (rows, unread, passages) = await read();
        var source = CrossReferenceSets.Find(set)!.Source;

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using (var writer = await connection.BeginBinaryImportAsync(Import, cancellationToken))
        {
            foreach (var row in rows)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(set, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(source, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(row.From.Book, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.From.Chapter, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.From.Verse, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.To.Book, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.To.Chapter, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.To.Verse, NpgsqlDbType.Integer, cancellationToken);
                await Nullable(writer, row.End?.Book, cancellationToken);
                await Nullable(writer, row.End?.Chapter, cancellationToken);
                await Nullable(writer, row.End?.Verse, cancellationToken);
                await writer.WriteAsync(row.Rank, NpgsqlDbType.Integer, cancellationToken);
                await Nullable(writer, row.Votes, cancellationToken);
                await Nullable(writer, row.Note, cancellationToken);
                await Nullable(writer, row.Passage, cancellationToken);
                await Nullable(writer, row.MatchedIn, cancellationToken);
                await Nullable(writer, row.Matched, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = new CrossReferenceOutcome(set, false, rows.Count, unread, passages, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The parallel passages of the Hebrew Old Testament and of the Greek New, each pair of verses
    /// written both ways round so the reader finds it from either side.
    /// </summary>
    private async Task<(List<CrossReferenceRow> Rows, int Unread, int Passages)> Detect(CancellationToken cancellationToken)
    {
        var passages = new List<(ParallelPassage Passage, string Text)>();
        foreach (var (slug, settings) in new[]
                 {
                     (BhsaTextSource.Slug, ParallelSettings.Hebrew),
                     (Sources.NestleSlug, ParallelSettings.Greek),
                 })
        {
            var tokens = await Tokens(slug, cancellationToken);
            if (tokens.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The parallels are found in {slug}'s lemmas, and the corpus holds no words of {slug}. Load " +
                    "the texts before the cross references.");
            }

            var found = ParallelDetector.Detect(tokens, settings);
            logger.LogInformation("{Passages} parallel passages in {Text}", found.Count, slug);
            passages.AddRange(found.Select(passage => (passage, slug)));
        }

        var rows = new Dictionary<(VerseAddress From, VerseAddress To), CrossReferenceRow>();
        for (var p = 0; p < passages.Count; p++)
        {
            var (passage, text) = passages[p];
            foreach (var pair in passage.Verses)
            {
                Keep(rows, new CrossReferenceRow(pair.A, pair.B, null, 0, Passage: (2 * p) + 1,
                    MatchedIn: text, Matched: Words(pair.Words.Select(w => (w.A, w.B)))));
                Keep(rows, new CrossReferenceRow(pair.B, pair.A, null, 0, Passage: (2 * p) + 2,
                    MatchedIn: text, Matched: Words(pair.Words.Select(w => (w.B, w.A)))));
            }
        }

        var ranked = rows.Values
            .GroupBy(row => row.From)
            .SelectMany(verse => verse
                .OrderByDescending(Shared)
                .ThenBy(row => row.To)
                .Select((row, rank) => row with { Rank = rank + 1 }))
            .ToList();
        return (ranked, 0, passages.Count);
    }

    /// <summary>A pair found by two passages is kept once, with the more words.</summary>
    private static void Keep(Dictionary<(VerseAddress, VerseAddress), CrossReferenceRow> rows, CrossReferenceRow row)
    {
        if (!rows.TryGetValue((row.From, row.To), out var kept) || Shared(kept) < Shared(row))
        {
            rows[(row.From, row.To)] = row;
        }
    }

    private static int Shared(CrossReferenceRow row) =>
        row.Matched is null ? 0 : row.Matched.Count(c => c == ' ') + 1;

    internal static string Words(IEnumerable<(int From, int To)> words)
    {
        var written = new StringBuilder();
        foreach (var (from, to) in words)
        {
            if (written.Length > 0)
            {
                written.Append(' ');
            }

            written.Append(from).Append('-').Append(to);
        }

        return written.ToString();
    }

    private async Task<List<LemmaToken>> Tokens(string slug, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(Lemmas, connection);
        command.Parameters.AddWithValue("slug", slug);

        var tokens = new List<LemmaToken>(450_000);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tokens.Add(new LemmaToken(
                new VerseAddress(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
                reader.GetInt32(3),
                reader.GetString(4)));
        }

        return tokens;
    }

    private static async Task Nullable(NpgsqlBinaryImporter writer, int? value, CancellationToken cancellationToken)
    {
        if (value is { } number)
        {
            await writer.WriteAsync(number, NpgsqlDbType.Integer, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }
    }

    private static async Task Nullable(NpgsqlBinaryImporter writer, string? value, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await writer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await writer.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);
        }
    }
}
