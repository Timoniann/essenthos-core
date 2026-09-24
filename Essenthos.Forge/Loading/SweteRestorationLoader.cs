using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Swete;
using Essenthos.Core.Corpus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading;

internal sealed record SweteRestorationOutcome(int Verses, int Words, TimeSpan Elapsed)
{
    public override string ToString() => Verses == 0
        ? "Swete's text already holds the words its transcription lost"
        : $"{Verses} verses of Swete's text rewritten with the words its transcription lost, {Words} words " +
          $"added, in {Elapsed}";
}

/// <summary>
/// Makes <see cref="SweteRestorations"/> on a corpus that loaded Swete before them.
///
/// <para>
/// A cold load reads the restored words straight from the reader, and this finds every verse
/// already right. A warm one holds the verses as the transcription had them, and the corpus loader
/// does not load a text twice, so the verses are rewritten here in place: the words the two readings
/// share keep their rows, and with them every link and annotation already standing on them; a
/// digitised token the restoration replaces goes, with the links it stood in alone; and the printed
/// words are inserted with no link, which is what they are until the two Septuagints are linked again.
/// </para>
///
/// <para>
/// It is guarded on what the verse says. A verse reading as the transcription is rewritten, one
/// reading as the restoration is left, and one reading as neither stops the pass, because then the
/// corpus holds a Swete nobody here has read.
/// </para>
/// </summary>
internal sealed class SweteRestorationLoader(AppDbContext db, ILogger<SweteRestorationLoader> logger)
{
    /// <summary>
    /// Moves the words after the rewritten stretch out of the way before they are placed again.
    /// Negative first, as the psalm openings do, so the unique index on (verse, position) holds
    /// after each statement.
    /// </summary>
    private const string FreeTheTail =
        """
        UPDATE word SET "position" = -"position" WHERE verse_id = @verseId AND "position" > @after;
        """;

    private const string Place =
        """
        UPDATE word SET "position" = @position WHERE id = @id;
        """;

    /// <summary>
    /// The links a word about to go stands in alone on its side. The row cascades out of every link,
    /// and a link left with words on one side only would read as a statement that those words have
    /// no counterpart, which nobody made.
    /// </summary>
    private const string Unlink =
        """
        DELETE FROM link l
        USING link_word lw
        WHERE lw.link_id = l.id AND lw.word_id = @id
          AND NOT EXISTS (SELECT 1 FROM link_word other
                          WHERE other.link_id = l.id AND other.side = lw.side AND other.word_id <> @id);
        """;

    private const string Remove =
        """
        DELETE FROM word WHERE id = @id;
        """;

    private const string Rebuild =
        """
        SELECT string_agg("text" || trailer, '' ORDER BY "position") FROM word WHERE verse_id = @verseId
        """;

    public async Task<SweteRestorationOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == SweteTextSource.Slug, cancellationToken);
        if (text is null)
        {
            return new SweteRestorationOutcome(0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var verses = 0;
        var added = 0;

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var book in SweteRestorations.Books)
        {
            var path = Path.Combine(folder, SweteTextSource.FileName(book));
            var digitised = SweteReader.Read(File.ReadLines(path));
            var restored = SweteReader.Read(SweteRestorations.Apply(book, File.ReadLines(path)));
            var canonical = SweteTextSource.Canonical(book);

            foreach (var (chapter, verse) in SweteRestorations.All
                         .Where(r => r.Book == book)
                         .Select(r => (r.Chapter, r.Verse))
                         .Distinct())
            {
                var before = Words(digitised, chapter, verse);
                var after = Words(restored, chapter, verse);
                var stored = await db.Words
                    .Where(w => w.TextId == text.Id
                                && w.Verse!.Book!.CanonicalOrdinal == canonical
                                && w.Verse.ChapterNumber == chapter
                                && w.Verse.Number == verse
                                && w.Verse.Label == string.Empty)
                    .OrderBy(w => w.Position)
                    .Select(w => new StoredWord(w.Id, w.VerseId, w.Position, w.Surface, w.Trailer))
                    .ToListAsync(cancellationToken);

                if (Same(stored, after))
                {
                    continue;
                }

                if (!Same(stored, before))
                {
                    throw new InvalidOperationException(
                        $"{text.Slug} {BookReferences.Name(canonical)} {chapter}:{verse} reads neither as the " +
                        $"transcription nor as its restoration: \"{string.Concat(stored.Select(w => w.Surface + w.Trailer))}\". " +
                        "The corpus holds a Swete loaded from other files; load the text again from these before " +
                        "restoring anything in it. Nothing was changed.");
                }

                added += await Write(text, stored, after, cancellationToken);
                await EnsureRebuilds(stored[0].VerseId, after, chapter, verse, cancellationToken);
                verses++;
            }
        }

        if (verses > 0 && text.RightsNote?.Contains(SweteRestorations.Note, StringComparison.Ordinal) != true)
        {
            text.RightsNote = text.RightsNote is { Length: > 0 } existing
                ? $"{existing} {SweteRestorations.Note}"
                : SweteRestorations.Note;
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = new SweteRestorationOutcome(verses, added, started.Elapsed);
        logger.LogInformation("Swete restorations: {Outcome}", outcome);
        return outcome;
    }

    private sealed record StoredWord(long Id, int VerseId, int Position, string Surface, string Trailer);

    private static IReadOnlyList<SweteWord> Words(SweteBook book, int chapter, int verse) =>
        book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse && v.Label.Length == 0).Words;

    private static bool Same(IReadOnlyList<StoredWord> stored, IReadOnlyList<SweteWord> words) =>
        stored.Count == words.Count
        && stored.Zip(words).All(pair => pair.First.Surface == pair.Second.Surface
                                         && pair.First.Trailer == pair.Second.Trailer);

    /// <summary>
    /// The stretch between what the two readings share at the head and at the tail is what differs.
    /// Its digitised rows go — a misread token is not a word the edition prints, so nothing standing
    /// on it is kept — and the printed words are written in their place. Returns how many words the
    /// verse gained.
    /// </summary>
    private async Task<int> Write(
        Text text,
        List<StoredWord> stored,
        IReadOnlyList<SweteWord> after,
        CancellationToken cancellationToken)
    {
        var head = 0;
        while (head < stored.Count && head < after.Count
               && stored[head].Surface == after[head].Surface && stored[head].Trailer == after[head].Trailer)
        {
            head++;
        }

        var tail = 0;
        while (tail < stored.Count - head && tail < after.Count - head
               && stored[^(tail + 1)].Surface == after[^(tail + 1)].Surface
               && stored[^(tail + 1)].Trailer == after[^(tail + 1)].Trailer)
        {
            tail++;
        }

        var old = stored.Count - head - tail;
        var now = after.Count - head - tail;
        var verseId = stored[0].VerseId;

        for (var i = 0; i < old; i++)
        {
            await Execute(Unlink, cancellationToken, ("id", stored[head + i].Id));
            await Execute(Remove, cancellationToken, ("id", stored[head + i].Id));
        }

        if (now != old)
        {
            await Execute(FreeTheTail, cancellationToken, ("verseId", verseId), ("after", head + old));
            var tailRows = stored.Skip(stored.Count - tail).ToList();
            for (var i = 0; i < tailRows.Count; i++)
            {
                await Execute(Place, cancellationToken, ("id", tailRows[i].Id), ("position", head + now + i + 1));
            }
        }

        for (var i = 0; i < now; i++)
        {
            var word = after[head + i];
            db.Words.Add(new Word
            {
                TextId = text.Id,
                VerseId = verseId,
                Position = head + i + 1,
                Surface = word.Surface,
                Trailer = word.Trailer,
                NormalisedText = WordFolding.Fold(word.Surface, text.Language),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return now - old;
    }

    /// <summary>The words in order must give back the restored verse, checked inside the transaction.</summary>
    private async Task EnsureRebuilds(
        int verseId,
        IReadOnlyList<SweteWord> after,
        int chapter,
        int verse,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(Rebuild, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddWithValue("verseId", verseId);

        var rebuilt = (await command.ExecuteScalarAsync(cancellationToken)) as string ?? string.Empty;
        var expected = VerseRoundTrip.Rebuild(after, w => w.Surface, w => w.Trailer);
        if (rebuilt != expected)
        {
            throw new InvalidOperationException(
                $"Swete {chapter}:{verse} reads \"{rebuilt}\" after its restoration where \"{expected}\" was " +
                "written. The words went to the wrong positions; the transaction is rolled back, so nothing " +
                "was changed.");
        }
    }

    private async Task Execute(string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(sql, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
