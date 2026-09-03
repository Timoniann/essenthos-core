using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Frame;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

internal sealed record FrameOutcome(string Slug, bool AlreadyPlaced, int Verses, int References, int Moved)
{
    public override string ToString() =>
        AlreadyPlaced
            ? $"{Slug} is already placed in the frame"
            : $"{Slug}: {Verses} verses placed as {References} references, {Moved} of them at an address " +
              "other than their own";
}

/// <summary>
/// Puts every verse of a text into the shared address space, so that a chapter asked for by its
/// canonical name resolves in any text.
///
/// This is not a link. A reference is where a verse sits in one frame; a link is a correspondence
/// between the words of two texts. Pairing two texts by verse number instead of through the frame
/// is what made the reader show different passages in its two panes.
/// </summary>
internal sealed class CanonicalFrameLoader(AppDbContext db, ILogger<CanonicalFrameLoader> logger)
{
    private const string ReferenceImport =
        """
        COPY verse_reference (verse_id, canonical_book, canonical_chapter, canonical_verse, is_primary)
        FROM STDIN (FORMAT BINARY)
        """;

    public async Task<FrameOutcome> Place(
        Text text,
        VersificationRules rules,
        CancellationToken cancellationToken = default)
    {
        if (await db.VerseReferences.AnyAsync(r => r.Verse!.TextId == text.Id, cancellationToken))
        {
            logger.LogInformation("Text {Slug} is already placed in the frame; nothing to do", text.Slug);
            return new FrameOutcome(text.Slug, AlreadyPlaced: true, 0, 0, 0);
        }

        if (!rules.Covers(text.Versification))
        {
            throw new InvalidOperationException(
                $"The text \"{text.Slug}\" follows {text.Versification} numbering, which the versification " +
                "data does not describe. Placing it by another tradition's rules would put every verse of it " +
                "at a plausible and wrong address; leave it out of the frame instead.");
        }

        var started = Stopwatch.StartNew();
        var verses = await db.Verses
            .Where(v => v.TextId == text.Id)
            .Select(v => new
            {
                v.Id,
                Book = v.Book!.CanonicalOrdinal,
                v.ChapterNumber,
                v.Number,
                v.Label,
                Length = v.Words.Sum(w => w.Surface.Length),
            })
            .ToListAsync(cancellationToken);

        // Which scheme of its tradition this edition follows is a question only the edition can
        // answer, and the versification data states the tests that ask it.
        var frame = rules.Frame(text.Versification, EditionShape.Of(verses.Select(v =>
            (v.Book, v.ChapterNumber, v.Number, v.Label, v.Length))));

        // The addresses this text prints as lettered verses, which the frame resolves differently.
        var lettered = verses
            .Where(v => v.Label.Length > 0)
            .Select(v => new CanonicalReference(v.Book, v.ChapterNumber, v.Number))
            .ToHashSet();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var references = 0;
        var moved = 0;
        await using (var writer = await connection.BeginBinaryImportAsync(ReferenceImport, cancellationToken))
        {
            foreach (var verse in verses)
            {
                var placements = frame.Resolve(
                    verse.Book,
                    verse.ChapterNumber,
                    verse.Number,
                    lettered.Contains(new CanonicalReference(verse.Book, verse.ChapterNumber, verse.Number)));

                if (placements[0].Chapter != verse.ChapterNumber || placements[0].Verse != verse.Number)
                {
                    moved++;
                }

                for (var i = 0; i < placements.Count; i++)
                {
                    var placement = placements[i];
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(verse.Id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(placement.Book, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(placement.Chapter, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(placement.Verse, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(i == 0, NpgsqlDbType.Boolean, cancellationToken);
                    references++;
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = new FrameOutcome(text.Slug, AlreadyPlaced: false, verses.Count, references, moved);
        logger.LogInformation("Placed {Outcome} in {Elapsed}", outcome, started.Elapsed);
        return outcome;
    }
}
