using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.XmlBible;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading;

internal sealed record VerseEndingOutcome(string Slug, int Verses, int Words, TimeSpan Elapsed)
{
    public override string ToString() => Verses == 0
        ? $"{Slug} already ends every verse as its edition prints it"
        : $"{Slug}: {Words} words written onto the end of {Verses} verses its file cut short, in {Elapsed}";
}

/// <summary>
/// Writes the end of a verse the file a text was loaded from cut short, from a complete copy of the
/// same edition.
///
/// <para>
/// The words are appended to the verse and nothing else is touched: the words the verse already
/// held keep their rows, and with them every link and annotation standing on them. The new words
/// carry no Strong number and no link, because nothing has said what they render; the aligner
/// reaches them the next time the text is aligned, as it reaches any other word.
/// </para>
///
/// <para>
/// It is guarded on what the verse says. A verse that already reads as the edition prints it is
/// left, one whose words are the head of it gains the rest, and one reading as neither stops the
/// pass before anything is written, because then either the edition or the loaded text is not the
/// one these words were checked against.
/// </para>
/// </summary>
internal sealed class VerseEndingLoader(AppDbContext db, ILogger<VerseEndingLoader> logger)
{
    /// <summary>The verse's last word gains the space that separated it from the lost words on the page.</summary>
    private const string SeparateTheTail =
        """
        UPDATE word SET trailer = trailer || ' '
        WHERE verse_id = @verseId
          AND "position" = (SELECT max("position") FROM word WHERE verse_id = @verseId)
        """;

    private const string Rebuild =
        """
        SELECT string_agg("text" || trailer, '' ORDER BY "position") FROM word WHERE verse_id = @verseId
        """;

    public async Task<VerseEndingOutcome> Load(
        string slug,
        IReadOnlyList<VerseEnding> endings,
        string rightsNote,
        TextPartSource? source = null,
        CancellationToken cancellationToken = default)
    {
        if (endings.Count == 0)
        {
            return new VerseEndingOutcome(slug, 0, 0, TimeSpan.Zero);
        }

        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"The text \"{slug}\" has {endings.Count} verses to complete but is not loaded. Run this " +
                       "after the corpus loader, which is what writes the verses these words go onto.");

        var started = Stopwatch.StartNew();
        var verses = 0;
        var written = 0;

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var ending in endings)
        {
            var address = $"{slug} {BookReferences.Name(ending.Book)} {ending.Chapter}:{ending.Verse}";
            var verse = await db.Verses
                .Where(v => v.TextId == text.Id
                            && v.Book!.CanonicalOrdinal == ending.Book
                            && v.ChapterNumber == ending.Chapter
                            && v.Number == ending.Verse
                            && v.Label == string.Empty)
                .Select(v => new
                {
                    v.Id,
                    Words = v.Words.OrderBy(w => w.Position).Select(w => w.Surface + w.Trailer).ToList(),
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException(
                    $"{address} is not a verse of the loaded text, so there is nothing to complete. The two " +
                    "editions number the verse differently; settle which verse is meant before loading anything.");

            var stored = string.Concat(verse.Words).TrimEnd();
            if (stored == ending.Complete)
            {
                continue;
            }

            if (stored.Length == 0
                || !ending.Complete.StartsWith(stored, StringComparison.Ordinal)
                || !char.IsWhiteSpace(ending.Complete[stored.Length]))
            {
                throw new InvalidOperationException(
                    $"{address} reads \"{stored}\", which is neither the verse the edition prints nor the head of " +
                    $"it: \"{ending.Complete}\". The loaded text or the edition is not the one these words were " +
                    "checked against; nothing was changed.");
            }

            var tail = VerseWords.Parse(ending.Complete[stored.Length..].Trim());
            if (!char.IsWhiteSpace(verse.Words[^1][^1]))
            {
                await Execute(SeparateTheTail, verse.Id, cancellationToken);
            }

            for (var at = 0; at < tail.Count; at++)
            {
                db.Words.Add(new Word
                {
                    TextId = text.Id,
                    VerseId = verse.Id,
                    Position = verse.Words.Count + at + 1,
                    Surface = tail[at].Word,
                    Trailer = tail[at].Trailer,
                    Elided = tail[at].Word.Length == 0,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            await EnsureRebuilds(verse.Id, address, ending.Complete, cancellationToken);
            verses++;
            written += tail.Count;
        }

        if (verses > 0 && rightsNote.Length > 0 && text.RightsNote?.Contains(rightsNote, StringComparison.Ordinal) != true)
        {
            text.RightsNote = text.RightsNote is { Length: > 0 } existing ? $"{existing} {rightsNote}" : rightsNote;
            await db.SaveChangesAsync(cancellationToken);
        }

        if (verses > 0 && source is not null && TextPartSources.Add(text, source))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = new VerseEndingOutcome(slug, verses, written, started.Elapsed);
        logger.LogInformation("Verse endings: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The words in order must give back the verse the edition prints, checked inside the
    /// transaction, so a verse that does not leaves nothing behind.
    /// </summary>
    private async Task EnsureRebuilds(int verseId, string address, string complete, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(Rebuild, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddWithValue("verseId", verseId);

        var rebuilt = ((await command.ExecuteScalarAsync(cancellationToken)) as string ?? string.Empty).TrimEnd();
        if (rebuilt != complete)
        {
            throw new InvalidOperationException(
                $"{address} reads \"{rebuilt}\" after completing it where \"{complete}\" was written. The words " +
                "went to the wrong positions; the transaction is rolled back, so nothing was changed.");
        }
    }

    private async Task Execute(string sql, int verseId, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(sql, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddWithValue("verseId", verseId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
