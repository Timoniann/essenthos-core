using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading;

/// <param name="Verses">Verses rewritten as the edition prints them.</param>
/// <param name="Removed">Words the file printed twice, gone with what stood on them alone.</param>
/// <param name="Added">Words the file lost, written with nothing standing on them yet.</param>
/// <param name="Books">The books the rewritten verses stand in.</param>
internal sealed record BrentonEditOutcome(int Verses, int Removed, int Added, IReadOnlyList<int> Books, TimeSpan Elapsed)
{
    public override string ToString() => Verses == 0
        ? $"{SeptuagintTextSource.Slug} already reads its repeated and lost words as the edition prints them"
        : $"{SeptuagintTextSource.Slug}: {Verses} verses read as the edition prints them, {Removed} repeated words "
          + $"removed and {Added} lost words written, in {Elapsed}";
}

/// <summary>
/// Makes <see cref="BrentonEdits"/> on a corpus that loaded Brenton's Greek before them.
///
/// <para>
/// A cold load reads the verses edited from the reader and this finds every one already right. A warm
/// one holds them as the file prints them: the words the two readings share keep their rows, and with
/// them their lemmas, names and links; a word the file printed twice goes, with what
/// <see cref="RemovedWordEvidence"/> allows to go with it; and the words it lost are written with no
/// link, which is what they are until the pairs are drawn again.
/// </para>
///
/// <para>
/// It is guarded on what the verse says: one reading as neither the file nor the edition stops the
/// pass, and the transaction leaves nothing behind.
/// </para>
/// </summary>
internal sealed class BrentonEditLoader(AppDbContext db, ILogger<BrentonEditLoader> logger)
{
    private const string Rebuild =
        """
        SELECT coalesce(string_agg("text" || trailer, '' ORDER BY "position"), '') FROM word WHERE verse_id = @verseId
        """;

    public async Task<BrentonEditOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == SeptuagintTextSource.Slug, cancellationToken);
        if (text is null)
        {
            return new BrentonEditOutcome(0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var file = SeptuagintTextSource.Read(folder, edited: false);
        var edition = SeptuagintTextSource.Read(folder);

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        int verses = 0, removed = 0, added = 0;
        var books = new HashSet<int>();
        foreach (var (book, chapter, number) in BrentonEdits.All.Select(e => (e.Book, e.Chapter, e.Verse)).Distinct())
        {
            var verseId = await db.Verses
                .Where(v => v.TextId == text.Id && v.Book!.CanonicalOrdinal == book && v.ChapterNumber == chapter
                            && v.Number == number && v.Label == "")
                .Select(v => (int?)v.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (verseId is not { } id)
            {
                // A corpus loaded without the book, which only a test builds.
                continue;
            }

            var address = $"{text.Slug} {BookReferences.Name(book)} {chapter}:{number}";
            var stored = await db.Words.AsNoTracking().Where(w => w.VerseId == id).OrderBy(w => w.Position)
                .ToListAsync(cancellationToken);
            var after = Words(edition, book, chapter, number);
            if (Same(stored, after))
            {
                continue;
            }

            if (!Same(stored, Words(file, book, chapter, number)))
            {
                throw new InvalidOperationException(
                    $"{address} reads \"{Printed(stored)}\", which is neither the file's verse nor the edition's. The "
                    + "corpus holds a text loaded from another file; load it again from this one before editing "
                    + "anything in it. Nothing was changed.");
            }

            var kept = SharedWords.Of([.. stored.Select(w => w.Surface)], [.. after.Select(w => w.Surface)]);
            var keptRows = kept.Where(k => k >= 0).ToHashSet();
            var gone = stored.Where((_, index) => !keptRows.Contains(index)).Select(w => w.Id).ToArray();
            await RemovedWordEvidence.Remove(db, gone, cancellationToken);
            await db.Words.Where(w => w.VerseId == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(w => w.Position, w => -w.Position), cancellationToken);

            for (var at = 0; at < after.Count; at++)
            {
                var word = after[at];
                if (kept[at] >= 0)
                {
                    var row = stored[kept[at]].Id;
                    var position = at + 1;
                    await db.Words.Where(w => w.Id == row).ExecuteUpdateAsync(setters => setters
                        .SetProperty(w => w.Position, position)
                        .SetProperty(w => w.Trailer, word.Trailer)
                        .SetProperty(w => w.Break, word.Break), cancellationToken);
                    continue;
                }

                db.Words.Add(new Word
                {
                    TextId = text.Id,
                    VerseId = id,
                    Position = at + 1,
                    Surface = word.Surface,
                    Trailer = word.Trailer,
                    Break = word.Break,
                    NormalisedText = WordFolding.Fold(word.Surface, text.Language),
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            await EnsureRebuilds(address, id, BrentonDivisions.Printed(after), cancellationToken);
            verses++;
            removed += gone.Length;
            added += after.Count - kept.Count(k => k >= 0);
            books.Add(book);
        }

        if (verses > 0 && text.RightsNote?.Contains(BrentonEdits.Note, StringComparison.Ordinal) != true)
        {
            text.RightsNote = text.RightsNote is { Length: > 0 } existing ? $"{existing} {BrentonEdits.Note}" : BrentonEdits.Note;
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = new BrentonEditOutcome(verses, removed, added, [.. books.Order()], started.Elapsed);
        logger.LogInformation("Edits: {Outcome}", outcome);
        return outcome;
    }

    private static IReadOnlyList<WordDraft> Words(TextSource source, int book, int chapter, int verse) =>
        source.Books.Single(b => b.CanonicalOrdinal == book).Chapters.Single(c => c.Number == chapter)
            .Verses.Single(v => v.Number == verse && v.Label.Length == 0).Words;

    private static bool Same(IReadOnlyList<Word> stored, IReadOnlyList<WordDraft> words) =>
        stored.Count == words.Count
        && stored.Zip(words).All(pair => pair.First.Surface == pair.Second.Surface && pair.First.Trailer == pair.Second.Trailer);

    private static string Printed(IEnumerable<Word> words) =>
        string.Concat(words.Select(w => w.Surface + w.Trailer)).TrimEnd();

    /// <summary>A verse must read as the edition prints it, checked inside the transaction.</summary>
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
                $"{address} reads \"{rebuilt}\" after its edit, where \"{expected}\" was meant. The words went to the "
                + "wrong places; the transaction is rolled back, so nothing was changed.");
        }
    }
}
