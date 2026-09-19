using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

internal sealed record ParagraphOutcome(
    string Slug,
    bool AlreadyMarked,
    int Paragraphs,
    int Lines,
    TimeSpan Elapsed)
{
    public int Marks => Paragraphs + Lines;

    public override string ToString() =>
        AlreadyMarked
            ? $"{Slug} already carries its paragraph marks"
            : $"{Slug}: {Paragraphs} paragraphs and {Lines} lines marked in {Elapsed}";
}

/// <summary>
/// Puts an edition's paragraph and line marks on a text that was loaded before the marks were read.
///
/// A text loaded now carries them from the start, because the loader writes what the source reader
/// hands it. One loaded earlier does not, and loading it again would renumber every word the links
/// point at. So the source is read again and each mark is written onto the word it stands before,
/// found by its verse and its position and checked by its letters: a mark whose word is not the one
/// the source has at that place is a sign the text in the database was read from different bytes,
/// and nothing is written.
///
/// It runs for every text on every load, beside the stated verse numbers and the source notes, and
/// costs nothing for a text whose source marks no paragraphs: the marks are counted in hand before
/// the database is asked anything.
/// </summary>
internal sealed class ParagraphMarkLoader(AppDbContext db, ILogger<ParagraphMarkLoader> logger)
{
    private const string Write =
        """
        UPDATE word w SET "break" = m.kind
        FROM unnest(@ordinals, @chapters, @numbers, @labels, @positions, @surfaces, @kinds)
                 AS m(ordinal, chapter, number, label, "position", surface, kind)
        JOIN book b ON b.text_id = @textId AND b.canonical_ordinal = m.ordinal
        JOIN verse v ON v.book_id = b.id AND v.chapter_number = m.chapter AND v.number = m.number
                    AND v.label = m.label
        WHERE w.verse_id = v.id AND w."position" = m."position" AND w."text" = m.surface
        """;

    public async Task<ParagraphOutcome> Mark(TextSource source, CancellationToken cancellationToken = default)
    {
        var slug = source.Definition.Slug;
        var started = Stopwatch.StartNew();
        var marks = Marks(source).ToList();
        if (marks.Count == 0)
        {
            return new ParagraphOutcome(slug, false, 0, 0, TimeSpan.Zero);
        }

        var text = await db.Texts
            .Where(t => t.Slug.ToUpper() == slug.ToUpper())
            .Select(t => new { t.Id, t.Slug })
            .SingleAsync(cancellationToken);

        if (await db.Words.AnyAsync(w => w.TextId == text.Id && w.Break != null, cancellationToken))
        {
            return new ParagraphOutcome(text.Slug, true, 0, 0, TimeSpan.Zero);
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(Write, connection, transaction);
        command.Parameters.AddWithValue("textId", text.Id);
        Array(command, "ordinals", NpgsqlDbType.Integer, marks.Select(m => m.Ordinal));
        Array(command, "chapters", NpgsqlDbType.Integer, marks.Select(m => m.Chapter));
        Array(command, "numbers", NpgsqlDbType.Integer, marks.Select(m => m.Number));
        Array(command, "labels", NpgsqlDbType.Text, marks.Select(m => m.Label));
        Array(command, "positions", NpgsqlDbType.Integer, marks.Select(m => m.Position));
        Array(command, "surfaces", NpgsqlDbType.Text, marks.Select(m => m.Surface));
        Array(command, "kinds", NpgsqlDbType.Text, marks.Select(m => EnumSpelling.Of(m.Break)));

        var written = await command.ExecuteNonQueryAsync(cancellationToken);
        if (written != marks.Count)
        {
            throw new InvalidOperationException(
                $"{text.Slug}: the source marks {marks.Count} paragraph and line starts but only {written} of them "
                + "fell on the word the source has at that place. The text in the database was read from "
                + "different files than the ones on disk now; nothing was written. Load the text afresh, or put "
                + "back the files it was loaded from.");
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = new ParagraphOutcome(
            text.Slug,
            false,
            marks.Count(m => m.Break == TextBreak.Paragraph),
            marks.Count(m => m.Break == TextBreak.Line),
            started.Elapsed);
        logger.LogInformation("Marked {Outcome}", outcome);
        return outcome;
    }

    private readonly record struct Placed(
        int Ordinal, int Chapter, int Number, string Label, int Position, string Surface, TextBreak Break);

    private static IEnumerable<Placed> Marks(TextSource source) =>
        from book in source.Books
        from chapter in book.Chapters
        from verse in chapter.Verses
        from word in verse.Words.Select((word, at) => (word, Position: at + 1))
        where word.word.Break is not null
        select new Placed(
            book.CanonicalOrdinal, chapter.Number, verse.Number, verse.Label, word.Position,
            word.word.Surface, word.word.Break!.Value);

    private static void Array<T>(NpgsqlCommand command, string name, NpgsqlDbType type, IEnumerable<T> values) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | type) { Value = values.ToArray() });
}
