using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading;

/// <param name="Divisions">Verses begun again at the word Brenton's English begins them.</param>
/// <param name="Words">Words that changed verse, keeping their rows.</param>
/// <param name="Links">Links the moved words stood in, withdrawn.</param>
/// <param name="Verses">The addresses of the verses that lost or gained words, before and after each division.</param>
internal sealed record BrentonDivisionOutcome(
    int Divisions,
    int Words,
    int Links,
    IReadOnlyList<(int Book, int Chapter, int Verse, string Label)> Verses,
    TimeSpan Elapsed)
{
    public override string ToString() => Divisions == 0
        ? $"{SeptuagintTextSource.Slug} already divides its verses as Brenton does"
        : $"{SeptuagintTextSource.Slug}: {Divisions} verses begun where Brenton's English begins them, {Words} words "
          + $"moved with their rows and {Links} links they stood in withdrawn, in {Elapsed}";
}

/// <summary>
/// Makes <see cref="BrentonDivisions"/> on a corpus that loaded Brenton's Greek before them.
///
/// <para>
/// A cold load reads the verses divided from the reader and this finds every division made. A warm
/// one holds them as the file divides them, and the words move here from one verse to the next with
/// their rows, so their lemmas, numbers and the names they carry stay on them. The links they stood in
/// go: every one was drawn verse against verse, the moved words against the verse they were printed
/// in, and a link the move left crossing into the next verse says something its source never said.
/// The words stand in no link until the pairs they belong to are aligned again.
/// </para>
///
/// <para>
/// It is guarded on what the verses say: a division whose words neither end the verse before as the
/// file prints them nor begin the verse as the correction does stops the pass, and the transaction
/// leaves nothing behind.
/// </para>
/// </summary>
internal sealed class BrentonDivisionLoader(AppDbContext db, ILogger<BrentonDivisionLoader> logger)
{
    private const string LinksOfTheMoved =
        """
        DELETE FROM link l
        USING link_word lw
        WHERE lw.link_id = l.id AND lw.word_id = ANY(@ids)
        """;

    private const string BookLinks =
        """
        DELETE FROM link l
        WHERE l.from_text_id = @from AND l.to_text_id = @to AND EXISTS (
            SELECT 1 FROM link_word lw
            JOIN word w ON w.id = lw.word_id
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            WHERE lw.link_id = l.id AND r.canonical_book = ANY(@books))
        """;

    /// <summary>
    /// Both verses' positions out of the way before the words are placed again, negative first so the
    /// unique index on (verse, position) holds after each statement.
    /// </summary>
    private const string FreeThePositions =
        """UPDATE word SET "position" = -"position" WHERE verse_id = ANY(@verses)""";

    private const string Place =
        """
        UPDATE word w SET verse_id = p.verse, "position" = p.position, "break" = p.break
        FROM unnest(@ids, @verses, @positions, @breaks) AS p(id, verse, position, break) WHERE w.id = p.id
        """;

    private const string Rebuild =
        """
        SELECT coalesce(string_agg("text" || trailer, '' ORDER BY "position"), '') FROM word WHERE verse_id = @verseId
        """;

    public async Task<BrentonDivisionOutcome> Load(CancellationToken cancellationToken = default)
    {
        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == SeptuagintTextSource.Slug, cancellationToken);
        if (text is null)
        {
            return new BrentonDivisionOutcome(0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var books = BrentonDivisions.All.Select(d => d.Book).ToHashSet();
        var verseIds = (await db.Verses
                .Where(v => v.TextId == text.Id && books.Contains(v.Book!.CanonicalOrdinal))
                .Select(v => new { v.Id, v.Book!.CanonicalOrdinal, v.ChapterNumber, v.Number, v.Label })
                .ToListAsync(cancellationToken))
            .ToDictionary(v => (v.CanonicalOrdinal, (v.ChapterNumber, v.Number, v.Label)), v => v.Id);

        int divisions = 0, words = 0, links = 0;
        var verses = new List<(int, int, int, string)>();
        foreach (var division in BrentonDivisions.All)
        {
            if (!verseIds.TryGetValue((division.Book, division.Address), out var verseId)
                || !verseIds.TryGetValue((division.Book, division.PreviousAddress), out var previousId))
            {
                // A corpus loaded without the book, which only a test builds.
                continue;
            }

            var previous = await Stored(previousId, cancellationToken);
            var verse = await Stored(verseId, cancellationToken);
            var address = $"{text.Slug} {BookReferences.Name(division.Book)} {division.Chapter}:{division.Verse}";
            var count = division.Count;
            var head = verse[0];
            List<StoredWord> moved;
            if (division.Begins is { } begins)
            {
                if (count < previous.Count && Printed(previous.TakeLast(count)) == begins)
                {
                    moved = previous.GetRange(previous.Count - count, count);
                    previous.RemoveRange(previous.Count - count, count);
                    verse.InsertRange(0, moved);
                }
                else if (count <= verse.Count && Printed(verse.Take(count)) == begins)
                {
                    continue;
                }
                else
                {
                    throw Unread(address, $"the verse before it ending with \"{begins}\"", previous, verse);
                }
            }
            else
            {
                var ends = division.Ends!;
                if (count < verse.Count && Printed(verse.Take(count)) == ends)
                {
                    moved = verse.GetRange(0, count);
                    verse.RemoveRange(0, count);
                    previous.AddRange(moved);
                }
                else if (count <= previous.Count && Printed(previous.TakeLast(count)) == ends)
                {
                    continue;
                }
                else
                {
                    throw Unread(address, $"itself beginning with \"{ends}\"", previous, verse);
                }
            }

            // A paragraph the edition opens before the verse stays at its head, whichever words now begin it.
            TextBreak? Opened(StoredWord word, bool first) =>
                head.Break is null ? word.Break : first ? head.Break : ReferenceEquals(word, head) ? null : word.Break;

            var ids = moved.Select(w => w.Id).ToArray();
            links += await Execute(LinksOfTheMoved, cancellationToken, ("ids", ids));
            await Execute(FreeThePositions, cancellationToken, ("verses", new[] { previousId, verseId }));

            var placed = previous.Select((w, at) => (w.Id, Verse: previousId, Position: at + 1, Break: Opened(w, false)))
                .Concat(verse.Select((w, at) => (w.Id, Verse: verseId, Position: at + 1, Break: Opened(w, at == 0))))
                .ToList();
            await Execute(Place, cancellationToken,
                ("ids", placed.Select(p => p.Id).ToArray()),
                ("verses", placed.Select(p => p.Verse).ToArray()),
                ("positions", placed.Select(p => p.Position).ToArray()),
                ("breaks", placed.Select(p => p.Break is { } b ? EnumSpelling.Of(b) : null).ToArray()));

            await EnsureRebuilds(address, previousId, Printed(previous), cancellationToken);
            await EnsureRebuilds(address, verseId, Printed(verse), cancellationToken);

            divisions++;
            words += moved.Count;
            verses.Add((division.Book, division.PreviousAddress.Chapter, division.PreviousAddress.Number,
                division.PreviousAddress.Label));
            verses.Add((division.Book, division.Chapter, division.Address.Number, division.Address.Label));
        }

        if (divisions > 0 && text.RightsNote?.Contains(BrentonDivisions.Note, StringComparison.Ordinal) != true)
        {
            text.RightsNote = text.RightsNote is { Length: > 0 } existing
                ? $"{existing} {BrentonDivisions.Note}"
                : BrentonDivisions.Note;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new BrentonDivisionOutcome(divisions, words, links, [.. verses.Distinct()], started.Elapsed);
        logger.LogInformation("Divisions: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The links a text holds to Brenton's Greek in the books the divisions touched, which the pass that
    /// wrote them draws again book by book: it pairs the two within each address, and a book it finds
    /// linked it leaves as it is.
    /// </summary>
    public async Task<int> Unlink(int fromTextId, int toTextId, IReadOnlyCollection<int> books, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        return await Execute(BookLinks, cancellationToken,
            ("from", fromTextId), ("to", toTextId), ("books", books.ToArray()));
    }

    /// <summary>
    /// Every verse link the text stands in, which the verse link pass joins again from the addresses
    /// the frame gives its verses now. A pair that already has verse links is not joined again, so a
    /// verse the frame moved would otherwise keep the counterparts of its old address.
    /// </summary>
    public async Task<int> Unjoin(int textId, CancellationToken cancellationToken = default) =>
        await db.VerseLinks
            .Where(link => link.FromTextId == textId || link.ToTextId == textId)
            .ExecuteDeleteAsync(cancellationToken);

    private sealed record StoredWord(long Id, string Surface, string Trailer, TextBreak? Break);

    private async Task<List<StoredWord>> Stored(int verseId, CancellationToken cancellationToken)
    {
        var rows = await db.Words
            .Where(w => w.VerseId == verseId)
            .OrderBy(w => w.Position)
            .Select(w => new { w.Id, w.Surface, w.Trailer, w.Break })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(w => new StoredWord(w.Id, w.Surface, w.Trailer, w.Break))];
    }

    private static string Printed(IEnumerable<StoredWord> words) =>
        string.Concat(words.Select(w => w.Surface + w.Trailer)).TrimEnd();

    private static InvalidOperationException Unread(
        string address,
        string expected,
        IEnumerable<StoredWord> previous,
        IEnumerable<StoredWord> verse) =>
        new($"{address} reads \"{Printed(previous)}\" before it and \"{Printed(verse)}\" in it, which is neither the "
            + $"file's division, {expected}, nor Brenton's. The corpus holds a text loaded from another file; load it "
            + "again from this one before dividing anything in it. Nothing was changed.");

    /// <summary>
    /// A verse must read as its division made it, checked inside the transaction, so a verse that does
    /// not leaves nothing behind.
    /// </summary>
    private async Task EnsureRebuilds(string address, int verseId, string expected, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(Rebuild, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddWithValue("verseId", verseId);
        var rebuilt = ((await command.ExecuteScalarAsync(cancellationToken)) as string ?? string.Empty).TrimEnd();
        if (rebuilt != expected)
        {
            throw new InvalidOperationException(
                $"{address}: a verse reads \"{rebuilt}\" after the division, where \"{expected}\" was meant. The words "
                + "went to the wrong places; the transaction is rolled back, so nothing was changed.");
        }
    }

    private async Task<int> Execute(string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(sql, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
