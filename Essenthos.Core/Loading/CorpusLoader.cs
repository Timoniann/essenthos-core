using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
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
        COPY word (text_id, verse_id, "position", "text", trailer, lemma, strong_number, gloss, morphology,
                   elided)
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

    /// <summary>
    /// The ids of the words a supplied span covers. Words are written with COPY, which returns no
    /// keys, so the spans are joined back on the address the draft knows: the verse and the
    /// position within it. Only the words in a span are asked for — 5,054 of the Synodal's 565,384.
    /// </summary>
    private const string SuppliedWordIds =
        """
        SELECT w.id FROM unnest(@verseIds, @positions) WITH ORDINALITY AS wanted(verse_id, "position", at)
        JOIN word w ON w.verse_id = wanted.verse_id AND w."position" = wanted."position"
        ORDER BY wanted.at
        """;

    private const string SuppliedGroupImport =
        """
        COPY word_group (id, text_id, kind, "position") FROM STDIN (FORMAT BINARY)
        """;

    private const string SuppliedMembershipImport =
        "COPY word_group_word (word_group_id, word_id) FROM STDIN (FORMAT BINARY)";

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
        await WriteSupplied(text, verses, cancellationToken);

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
        Citation = definition.Citation,
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
                if (word.Elided != (word.Surface.Length == 0))
                {
                    throw new InvalidOperationException(
                        $"{loaded.Reference} word {i + 1} is marked " +
                        $"{(word.Elided ? "elided and has letters" : "written and has none")}. A word with no " +
                        "surface is a claim the reader makes about the source, not a string that came out " +
                        "empty; set Elided where the source prints nothing and nowhere else.");
                }

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
                await writer.WriteAsync(word.Elided, NpgsqlDbType.Boolean, cancellationToken);
                written++;
            }
        }

        await writer.CompleteAsync(cancellationToken);
        return written;
    }

    /// <summary>
    /// The spans the edition marks as words it supplies, as word groups of their own.
    ///
    /// They are groups rather than a flag on the word because the mark is over a span and the
    /// spans abut: the Synodal writes "[для] [управления]" 79 times, two brackets and two claims,
    /// which a per-word flag reports as one. And they are not links, although the schema can say
    /// <c>expands</c> with an empty side — a link is a claim about a pair of texts, and it would
    /// make this edition's mark on its own page into an assertion about a counterpart the edition
    /// never named, for 884 New Testament spans among others.
    /// </summary>
    private async Task WriteSupplied(
        Text text,
        List<LoadedVerse> verses,
        CancellationToken cancellationToken)
    {
        var verseIds = new List<int>();
        var positions = new List<int>();
        var sizes = new List<int>();

        foreach (var loaded in verses)
        {
            var span = 0;
            var size = 0;
            for (var i = 0; i < loaded.Draft.Words.Count; i++)
            {
                if (loaded.Draft.Words[i].SuppliedSpan is not { } number)
                {
                    continue;
                }

                if (number != span)
                {
                    if (size > 0)
                    {
                        sizes.Add(size);
                    }

                    span = number;
                    size = 0;
                }

                verseIds.Add(loaded.Verse.Id);
                positions.Add(i + 1);
                size++;
            }

            if (size > 0)
            {
                sizes.Add(size);
            }
        }

        if (sizes.Count == 0)
        {
            return;
        }

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var words = await SuppliedWords(connection, verseIds, positions, cancellationToken);
        if (words.Count != positions.Count)
        {
            throw new InvalidOperationException(
                $"The load of {text.Slug} marked {positions.Count} supplied words but only {words.Count} of " +
                "them came back from the word table. The spans are addressed by verse and position, so a " +
                "position that does not exist means the words were written from a different draft than the " +
                "one the spans were read from.");
        }

        var firstGroup = await ReserveGroupIds(connection, sizes.Count, cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(SuppliedGroupImport, cancellationToken))
        {
            for (var i = 0; i < sizes.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstGroup + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(text.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(
                    EnumSpelling.Of(WordGroupKind.Supplied), NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(i + 1, NpgsqlDbType.Integer, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer =
                     await connection.BeginBinaryImportAsync(SuppliedMembershipImport, cancellationToken))
        {
            var at = 0;
            for (var i = 0; i < sizes.Count; i++)
            {
                for (var j = 0; j < sizes[i]; j++, at++)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(firstGroup + i, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(words[at], NpgsqlDbType.Bigint, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        logger.LogInformation(
            "{Slug}: {Spans} spans the edition marks as supplied, over {Words} words",
            text.Slug, sizes.Count, words.Count);
    }

    private async Task<List<long>> SuppliedWords(
        NpgsqlConnection connection,
        List<int> verseIds,
        List<int> positions,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SuppliedWordIds, connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.Add(new NpgsqlParameter("verseIds", NpgsqlDbType.Array | NpgsqlDbType.Integer)
        {
            Value = verseIds.ToArray(),
        });
        command.Parameters.Add(new NpgsqlParameter("positions", NpgsqlDbType.Array | NpgsqlDbType.Integer)
        {
            Value = positions.ToArray(),
        });

        var ids = new List<long>(positions.Count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    /// <summary>
    /// Groups are written with COPY, which does not fill a generated key, so the sequence is moved
    /// on by as many as are about to be written and the block it skipped is theirs. The same thing
    /// <see cref="SyntaxLoader"/> does, for the same reason.
    /// </summary>
    private async Task<long> ReserveGroupIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('word_group', 'id'), " +
            "coalesce((SELECT max(id) FROM word_group), 0) + @count) - @count + 1", connection,
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddWithValue("count", count);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
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
