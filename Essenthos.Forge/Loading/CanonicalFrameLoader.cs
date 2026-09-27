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

internal sealed record ReferenceDraft(int VerseId, int Book, int Chapter, int Verse, bool IsPrimary);

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
        if (!rules.Covers(text.Versification))
        {
            throw new InvalidOperationException(
                $"The text \"{text.Slug}\" follows {text.Versification} numbering, which the versification " +
                "data does not describe. Placing it by another tradition's rules would put every verse of it " +
                "at a plausible and wrong address; leave it out of the frame instead.");
        }

        var started = Stopwatch.StartNew();
        // A corpus imported before a new layer can gain a verse afterwards: a source note can sit
        // on a printed \v marker with no words (ASV Matthew 17:21). Do not make an existing frame
        // an all-or-nothing gate, or that new anchor can never reach the parallel reader. The
        // edition's shape still comes from every verse, because that is what decides its scheme.
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

        // The frame is derived data. A rule fixed after a corpus was loaded must replace the old
        // placements rather than leave them behind just because every verse already has a row.
        var existing = await db.VerseReferences
            .Where(reference => reference.Verse!.TextId == text.Id)
            .Select(reference => new ReferenceDraft(
                reference.VerseId,
                reference.CanonicalBook,
                reference.CanonicalChapter,
                reference.CanonicalVerse,
                reference.IsPrimary))
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

        var expected = verses
            .SelectMany(verse => frame.Resolve(
                    verse.Book,
                    verse.ChapterNumber,
                    verse.Number,
                    lettered.Contains(new CanonicalReference(verse.Book, verse.ChapterNumber, verse.Number)),
                    verse.Label)
                .Select((placement, index) => new ReferenceDraft(
                    verse.Id,
                    placement.Book,
                    placement.Chapter,
                    placement.Verse,
                    index == 0)))
            .ToList();

        var versesById = verses.ToDictionary(verse => verse.Id);

        if (existing.Count == expected.Count && existing.ToHashSet().SetEquals(expected))
        {
            logger.LogInformation("Text {Slug} is already placed in the frame", text.Slug);
            return new FrameOutcome(text.Slug, AlreadyPlaced: true, 0, 0, 0);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await db.VerseReferences
            .Where(reference => reference.Verse!.TextId == text.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(ReferenceImport, cancellationToken))
        {
            foreach (var reference in expected)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(reference.VerseId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(reference.Book, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(reference.Chapter, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(reference.Verse, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(reference.IsPrimary, NpgsqlDbType.Boolean, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var moved = expected
            .Where(reference => reference.IsPrimary)
            .Count(reference => reference.Chapter != versesById[reference.VerseId].ChapterNumber ||
                                reference.Verse != versesById[reference.VerseId].Number);
        var outcome = new FrameOutcome(text.Slug, AlreadyPlaced: false, verses.Count, expected.Count, moved);
        logger.LogInformation("Placed {Outcome} in {Elapsed}", outcome, started.Elapsed);
        return outcome;
    }
}
