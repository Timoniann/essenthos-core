using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <summary>A verse as written, kept beside the draft it came from so the load can check itself.</summary>
internal sealed record LoadedVerse(Verse Verse, VerseDraft Draft, string Reference);

internal sealed record LoadOutcome(
    string Slug,
    bool AlreadyLoaded,
    int Books,
    int Chapters,
    int Verses,
    int Words,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"{Slug} was already loaded"
            : $"{Slug}: {Books} books, {Chapters} chapters, {Verses} verses, {Words} words in {Elapsed}";
}

/// <summary>
/// Writes one text and everything under it. There is one of these rather than one per witness,
/// because the shape a witness is stored in no longer depends on what kind of witness it is: BHSA
/// and Nestle are two callers of the same loader, and the Textus Receptus will be a third.
/// </summary>
internal sealed class CorpusLoader(AppDbContext db, ILogger<CorpusLoader> logger)
{
    /// <summary>
    /// Words are written with COPY rather than through the change tracker. Half a million tracked
    /// entities is minutes and a gigabyte; the binary import is seconds and nothing.
    /// </summary>
    private const string WordImport =
        """
        COPY word (text_id, verse_id, "position", "text", trailer, lemma, strong_number, gloss, morphology)
        FROM STDIN (FORMAT BINARY)
        """;

    /// <summary>
    /// The load's own round trip: every verse is read back out of the database and compared against
    /// the words that were handed in. One query, aggregated in Postgres, rather than half a million
    /// rows over the wire.
    /// </summary>
    private const string RebuildVerses =
        """
        SELECT verse_id, string_agg("text" || trailer, '' ORDER BY "position") AS rebuilt
        FROM word WHERE text_id = @textId GROUP BY verse_id
        """;

    public async Task<LoadOutcome> Load(TextSource source, CancellationToken cancellationToken = default)
    {
        source.Definition.Validate();

        var slug = source.Definition.Slug;
        if (await db.Texts.AnyAsync(t => t.Slug == slug, cancellationToken))
        {
            logger.LogInformation("Text {Slug} is already loaded; nothing to do", slug);
            return new LoadOutcome(slug, AlreadyLoaded: true, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var text = NewText(source.Definition);
        db.Texts.Add(text);
        await db.SaveChangesAsync(cancellationToken);

        var verses = await WriteStructure(text, source, cancellationToken);
        var words = await WriteWords(text, verses, cancellationToken);
        await VerifyRoundTrip(text, verses, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var outcome = new LoadOutcome(
            slug,
            AlreadyLoaded: false,
            source.Books.Count,
            source.Books.Sum(b => b.Chapters.Count),
            verses.Count,
            words,
            started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    private static Text NewText(TextDefinition definition) => new()
    {
        Slug = definition.Slug,
        Name = definition.Name,
        NameNative = definition.NameNative,
        Kind = definition.Kind,
        Language = definition.Language,
        Direction = definition.Direction,
        Versification = definition.Versification,
        PublishedYear = definition.PublishedYear,
        SourceUrl = definition.SourceUrl,
        RightsHolder = definition.RightsHolder,
        Licence = definition.Licence,
        LicenceUrl = definition.LicenceUrl,
        Redistribution = definition.Redistribution,
        TextualFamily = definition.TextualFamily,
    };

    /// <summary>
    /// Books, chapters and verses go through the change tracker: there are tens of thousands of
    /// them rather than hundreds of thousands, and their generated keys are what the words hang on.
    /// </summary>
    private async Task<List<LoadedVerse>> WriteStructure(
        Text text,
        TextSource source,
        CancellationToken cancellationToken)
    {
        var verses = new List<LoadedVerse>(32_000);

        foreach (var bookDraft in source.Books)
        {
            var book = new Book
            {
                TextId = text.Id,
                CanonicalOrdinal = bookDraft.CanonicalOrdinal,
                Position = bookDraft.Position,
                Name = bookDraft.Name,
                NameNative = bookDraft.NameNative,
                Abbreviation = bookDraft.Abbreviation,
                Slug = bookDraft.Slug,
            };
            db.Books.Add(book);

            foreach (var chapterDraft in bookDraft.Chapters)
            {
                var chapter = new Chapter { TextId = text.Id, Book = book, Number = chapterDraft.Number };
                db.Chapters.Add(chapter);

                foreach (var verseDraft in chapterDraft.Verses)
                {
                    var verse = new Verse
                    {
                        TextId = text.Id,
                        Book = book,
                        Chapter = chapter,
                        ChapterNumber = chapterDraft.Number,
                        Number = verseDraft.Number,
                        Label = verseDraft.Label,
                    };
                    db.Verses.Add(verse);
                    verses.Add(new LoadedVerse(verse, verseDraft,
                        $"{source.Definition.Slug} {bookDraft.Name} {chapterDraft.Number}:{verseDraft.Number}{verseDraft.Label}"));
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return verses;
    }

    private async Task<int> WriteWords(
        Text text,
        List<LoadedVerse> verses,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var written = 0;

        await using var writer = await connection.BeginBinaryImportAsync(WordImport, cancellationToken);
        foreach (var loaded in verses)
        {
            for (var i = 0; i < loaded.Draft.Words.Count; i++)
            {
                var word = loaded.Draft.Words[i];
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(text.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(loaded.Verse.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(i + 1, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(word.Surface, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(word.Trailer, NpgsqlDbType.Text, cancellationToken);
                await WriteNullable(writer, word.Lemma, NpgsqlDbType.Text, cancellationToken);
                await WriteNullable(writer, word.StrongNumber, NpgsqlDbType.Text, cancellationToken);
                await WriteNullable(writer, word.Gloss, NpgsqlDbType.Text, cancellationToken);
                await WriteNullable(writer, word.Morphology, NpgsqlDbType.Jsonb, cancellationToken);
                written++;
            }
        }

        await writer.CompleteAsync(cancellationToken);
        return written;
    }

    private static async Task WriteNullable(
        NpgsqlBinaryImporter writer,
        string? value,
        NpgsqlDbType type,
        CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await writer.WriteNullAsync(cancellationToken);
            return;
        }

        await writer.WriteAsync(value, type, cancellationToken);
    }

    /// <summary>
    /// The property from DOC-0007, checked here rather than in a test: what the database now holds
    /// must rebuild the verse the parser handed over. It runs before the commit, so a corrupt load
    /// leaves nothing behind — a corpus that is silently wrong is worse than one that is missing,
    /// because only the second is noticed.
    /// </summary>
    private async Task VerifyRoundTrip(
        Text text,
        List<LoadedVerse> verses,
        CancellationToken cancellationToken)
    {
        var expected = verses.ToDictionary(
            v => v.Verse.Id,
            v => (v.Reference, Text: VerseRoundTrip.Rebuild(v.Draft.Words, w => w.Surface, w => w.Trailer)));

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(RebuildVerses, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddWithValue("textId", text.Id);

        var seen = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var verseId = reader.GetInt32(0);
            var rebuilt = reader.GetString(1);
            if (!expected.TryGetValue(verseId, out var source))
            {
                throw new InvalidOperationException(
                    $"The load of {text.Slug} wrote words for verse {verseId}, which it never read. " +
                    "Something other than this loader is writing to the word table.");
            }

            VerseRoundTrip.Ensure(source.Reference, rebuilt, source.Text);
            seen++;
        }

        var withWords = expected.Count(e => e.Value.Text.Length > 0);
        if (seen != withWords)
        {
            throw new InvalidOperationException(
                $"The load of {text.Slug} read {withWords} verses with words but the database holds words " +
                $"for {seen}. Words were dropped between the parser and the import; the transaction is " +
                "rolled back, so nothing was written.");
        }
    }
}
