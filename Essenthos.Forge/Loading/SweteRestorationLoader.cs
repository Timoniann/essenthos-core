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
        ? "Swete's text already holds the words its transcription lost and the letters it corrects"
        : $"{Verses} verses of Swete's text rewritten with the words its transcription lost and the letters it " +
          $"corrects, {Words} words added, in {Elapsed}";
}

/// <summary>
/// Makes <see cref="SweteRestorations"/> on a corpus that loaded Swete before them.
///
/// <para>
/// A cold load reads the restored words straight from the reader, and this finds every verse
/// already right. A warm one holds the verses as the transcription had them, and the corpus loader
/// does not load a text twice, so the verses are rewritten here in place: the words the two readings
/// share keep their rows, and with them every link and annotation already standing on them; a
/// digitised token the restoration replaces goes, with the links a matcher drew on it alone, and
/// anything else standing on it stops the pass (<see cref="RemovedWordEvidence"/>); and the printed
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
    /// Moves the verse's words out of the way before they are placed again. Negative first, as the
    /// psalm openings do, so the unique index on (verse, position) holds after each statement.
    /// </summary>
    private const string FreeTheVerse =
        """
        UPDATE word SET "position" = -"position" WHERE verse_id = @verseId;
        """;

    private const string Place =
        """
        UPDATE word SET "position" = @position WHERE id = @id;
        """;

    private const string Rewrite =
        """
        UPDATE word SET "text" = @surface, trailer = @trailer, normalised_text = @normalised WHERE id = @id;
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

        var held = (await db.Books.Where(b => b.TextId == text.Id).Select(b => b.CanonicalOrdinal)
            .ToListAsync(cancellationToken)).ToHashSet();

        foreach (var book in SweteRestorations.Books.Where(SweteTextSource.Reads))
        {
            var path = Path.Combine(folder, SweteTextSource.FileName(book));
            // With the chapter numbers kept: they are taken out of a warm corpus by the edition's
            // boundary repair, and a verse here is compared with what such a corpus holds.
            var digitised = SweteReader.Read(Lines(book, path), keepChapterMarkers: true);
            var restored = SweteReader.Read(SweteRestorations.Apply(book, Lines(book, path)), keepChapterMarkers: true);
            var cold = SweteReader.Read(SweteRestorations.Apply(book, Lines(book, path)));
            // What a warm corpus may hold: the transcription, or an earlier pass's restorations, with the
            // chapter numbers or, once the boundary pass has taken them out, without.
            var numbered = Readings(book, path, keepChapterMarkers: true);
            var unnumbered = Readings(book, path, keepChapterMarkers: false);

            foreach (var here in SweteRestorations.All
                         .Where(r => r.Book == book)
                         .GroupBy(r => (r.Chapter, r.Verse, r.Label)))
            {
                var (chapter, verse, label) = here.Key;
                var (canonical, placed) = SweteTextSource.Placed(book, chapter);
                if (!held.Contains(canonical))
                {
                    // A corpus loaded without the book, which only a test builds.
                    continue;
                }

                var before = Words(digitised, chapter, verse, label);
                var after = Words(restored, chapter, verse, label);
                var stored = await db.Words
                    .Where(w => w.TextId == text.Id
                                && w.Verse!.Book!.CanonicalOrdinal == canonical
                                && w.Verse.ChapterNumber == placed
                                && w.Verse.Number == verse
                                && w.Verse.Label == label)
                    .OrderBy(w => w.Position)
                    .Select(w => new StoredWord(w.Id, w.VerseId, w.Position, w.Surface, w.Trailer))
                    .ToListAsync(cancellationToken);

                if (Same(stored, after) || Same(stored, Words(cold, chapter, verse, label)))
                {
                    continue;
                }

                var verseId = stored.Count > 0
                    ? stored[0].VerseId
                    : await db.Verses
                          .Where(v => v.TextId == text.Id && v.Book!.CanonicalOrdinal == canonical
                                      && v.ChapterNumber == placed && v.Number == verse && v.Label == label)
                          .Select(v => (int?)v.Id)
                          .SingleOrDefaultAsync(cancellationToken)
                      ?? throw new InvalidOperationException(
                          $"{text.Slug} {BookReferences.Name(canonical)} {placed}:{verse}{label} is not a verse of the corpus, "
                          + "which every reading of the edition numbers. Nothing was changed.");

                // A verse the boundary pass has already taken the numbers out of is brought to the
                // restored reading without them, so that none is written back only to be taken out again.
                var target = Same(stored, before) || numbered.Any(pass => Same(stored, Words(pass, chapter, verse, label)))
                    ? after
                    : unnumbered.Any(pass => Same(stored, Words(pass, chapter, verse, label)))
                        ? Words(cold, chapter, verse, label)
                        : throw new InvalidOperationException(
                            $"{text.Slug} {BookReferences.Name(canonical)} {placed}:{verse}{label} reads neither as the " +
                            $"transcription nor as its restoration: \"{string.Concat(stored.Select(w => w.Surface + w.Trailer))}\". " +
                            "The corpus holds a Swete loaded from other files; load the text again from these before " +
                            "restoring anything in it. Nothing was changed.");

                added += await Write(text, verseId, stored, target, here.All(SweteCorrections.KeepsTheWord), cancellationToken);
                await EnsureRebuilds(verseId, target, $"{placed}:{verse}{label}", cancellationToken);
                verses++;
            }
        }

        if (verses > 0)
        {
            foreach (var note in new[]
                     {
                         SweteRestorations.Note, SweteCorrections.Note, SwetePage.Note, SweteCorrections.FiguresNote,
                         SweteSettled.Note,
                     })
            {
                if (text.RightsNote?.Contains(note, StringComparison.Ordinal) != true)
                {
                    text.RightsNote = text.RightsNote is { Length: > 0 } existing ? $"{existing} {note}" : note;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = new SweteRestorationOutcome(verses, added, started.Elapsed);
        logger.LogInformation("Swete restorations: {Outcome}", outcome);
        return outcome;
    }

    private sealed record StoredWord(long Id, int VerseId, int Position, string Surface, string Trailer);

    /// <summary>
    /// A file's lines with the verses the transcription ran together divided, which a cold load reads
    /// and every restoration is addressed against. A corpus that holds them run together reads as
    /// none of its readings at such a verse, and the pass stops there and asks for the text to be
    /// loaded again.
    /// </summary>
    private static IEnumerable<string> Lines(string book, string path) => SweteDivisions.Lines(book, File.ReadLines(path));

    /// <summary>The transcription and each earlier pass's restorations of it, read with or without the chapter numbers.</summary>
    private static List<SweteBook> Readings(string book, string path, bool keepChapterMarkers) =>
    [
        SweteReader.Read(Lines(book, path), keepChapterMarkers),
        .. SweteRestorations.Earlier.Select(set =>
            SweteReader.Read(SweteRestorations.Apply(book, Lines(book, path), set), keepChapterMarkers)),
    ];

    private static IReadOnlyList<SweteWord> Words(SweteBook book, int chapter, int verse, string label) =>
        book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse && v.Label == label).Words;

    private static bool Same(IReadOnlyList<StoredWord> stored, IReadOnlyList<SweteWord> words) =>
        stored.Count == words.Count
        && stored.Zip(words).All(pair => pair.First.Surface == pair.Second.Surface
                                         && pair.First.Trailer == pair.Second.Trailer);

    /// <summary>
    /// Where every change to the verse corrects a word's letters — a Latin letter, a margin number —
    /// each row is rewritten in place and keeps its links, since it is the same word. Otherwise the
    /// words the two readings share, in order, keep their rows and with them every link and annotation,
    /// taking the printed trailer where it differs; every other digitised row goes — a misread token is
    /// not a word the edition prints, so a matcher's link on it alone goes with it, and anything more
    /// refuses — and the printed words are written in their places. Returns how many words the verse
    /// gained.
    /// </summary>
    private async Task<int> Write(
        Text text,
        int verseId,
        List<StoredWord> stored,
        IReadOnlyList<SweteWord> after,
        bool inPlace,
        CancellationToken cancellationToken)
    {
        if (inPlace && stored.Count == after.Count)
        {
            for (var i = 0; i < stored.Count; i++)
            {
                var word = after[i];
                if (stored[i].Surface == word.Surface && stored[i].Trailer == word.Trailer)
                {
                    continue;
                }

                await Execute(Rewrite, cancellationToken,
                    ("id", stored[i].Id),
                    ("surface", word.Surface),
                    ("trailer", word.Trailer),
                    ("normalised", WordFolding.Fold(word.Surface, text.Language)));
            }

            return 0;
        }

        var kept = SharedWords.Of([.. stored.Select(w => w.Surface)], [.. after.Select(w => w.Surface)]);
        var keptRows = kept.Where(k => k >= 0).ToHashSet();
        await RemovedWordEvidence.Remove(db,
            [.. stored.Where((_, index) => !keptRows.Contains(index)).Select(w => w.Id)], cancellationToken);
        await Execute(FreeTheVerse, cancellationToken, ("verseId", verseId));

        for (var i = 0; i < after.Count; i++)
        {
            var word = after[i];
            if (kept[i] >= 0)
            {
                var row = stored[kept[i]];
                await Execute(Place, cancellationToken, ("id", row.Id), ("position", i + 1));
                if (row.Trailer != word.Trailer)
                {
                    await Execute(Rewrite, cancellationToken,
                        ("id", row.Id),
                        ("surface", word.Surface),
                        ("trailer", word.Trailer),
                        ("normalised", WordFolding.Fold(word.Surface, text.Language)));
                }

                continue;
            }

            db.Words.Add(new Word
            {
                TextId = text.Id,
                VerseId = verseId,
                Position = i + 1,
                Surface = word.Surface,
                Trailer = word.Trailer,
                NormalisedText = WordFolding.Fold(word.Surface, text.Language),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return after.Count - stored.Count;
    }

    /// <summary>The words in order must give back the restored verse, checked inside the transaction.</summary>
    private async Task EnsureRebuilds(
        int verseId,
        IReadOnlyList<SweteWord> after,
        string address,
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
                $"Swete {address} reads \"{rebuilt}\" after its restoration where \"{expected}\" was " +
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
