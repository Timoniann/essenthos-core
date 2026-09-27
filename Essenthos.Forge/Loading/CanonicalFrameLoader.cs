using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
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

/// <summary>A verse as the frame needs it: its own address, its letter and how much text it holds.</summary>
internal sealed record PlacedVerse(int Id, int Book, int Chapter, int Number, string Label, int Length);

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

        var expected = Expected(rules, text.Versification, [
            .. verses.Select(v => new PlacedVerse(v.Id, v.Book, v.ChapterNumber, v.Number, v.Label, v.Length)),
        ], BookTraditions.For(text.Slug));

        var versesById = verses.ToDictionary(verse => verse.Id);

        if (Unchanged(existing, expected))
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

    /// <summary>
    /// Whether the text already stands where the frame puts it. A psalm's first verse that holds its
    /// title also covers the title's row, and that further row is written after the frame by the passes
    /// that read the title in (<see cref="PsalmOpeningLoader"/>, <see cref="SuperscriptionFrameLoader"/>);
    /// it is theirs, so it does not count as a difference, or every load would place those texts again
    /// only for the passes to write the same rows back.
    /// </summary>
    internal static bool Unchanged(IReadOnlyCollection<ReferenceDraft> existing, IReadOnlyCollection<ReferenceDraft> expected)
    {
        var wanted = expected.ToHashSet();
        var framed = existing
            .Where(reference => wanted.Contains(reference)
                                || reference.IsPrimary
                                || reference.Verse != CanonicalReference.TitleVerse)
            .ToHashSet();
        return framed.Count == wanted.Count && framed.SetEquals(wanted);
    }

    /// <summary>
    /// Where each verse of an edition stands, the first reference of a verse its primary place.
    /// Which scheme of its tradition the edition follows is a question only the edition can answer,
    /// and the versification data states the tests that ask it.
    /// </summary>
    /// <param name="books">Books the edition numbers in another tradition, placed by that tradition's rules.</param>
    public static List<ReferenceDraft> Expected(
        VersificationRules rules,
        Versification tradition,
        IReadOnlyList<PlacedVerse> verses,
        IReadOnlyDictionary<int, Versification>? books = null)
    {
        books ??= new Dictionary<int, Versification>();
        Versification TraditionOf(PlacedVerse verse) => books.GetValueOrDefault(verse.Book, tradition);

        // A book numbered in no tradition the data describes stands at its own numbers.
        var frames = verses
            .GroupBy(TraditionOf)
            .Where(group => rules.Covers(group.Key))
            .ToDictionary(group => group.Key, group => rules.Frame(group.Key, EditionShape.Of(group.Select(v =>
                (v.Book, v.Chapter, v.Number, v.Label, v.Length)))));

        // The addresses this text prints as lettered verses, which the frame resolves differently.
        var lettered = verses
            .Where(v => v.Label.Length > 0)
            .Select(v => new CanonicalReference(v.Book, v.Chapter, v.Number))
            .ToHashSet();

        return verses
            .SelectMany(verse => AtBothNames(verse.Book, frames.TryGetValue(TraditionOf(verse), out var frame)
                    ? frame.Resolve(
                        verse.Book,
                        verse.Chapter,
                        verse.Number,
                        lettered.Contains(new CanonicalReference(verse.Book, verse.Chapter, verse.Number)),
                        verse.Label)
                    : [new CanonicalReference(verse.Book, verse.Chapter, verse.Number)])
                .Select((placement, index) => new ReferenceDraft(
                    verse.Id,
                    placement.Book,
                    placement.Chapter,
                    placement.Verse,
                    index == 0)))
            .ToList();
    }

    /// <summary>
    /// A verse of the Letter of Jeremiah under the name its own edition prints it, and then under the
    /// other: the data places the Greek letter at the standard's Baruch 6, and an edition that prints
    /// it as a book stands in that book and covers Baruch 6, as one that prints Baruch 6 covers the
    /// book. Every other verse stands where the frame puts it.
    /// </summary>
    private static IReadOnlyList<CanonicalReference> AtBothNames(int book, IReadOnlyList<CanonicalReference> placed)
    {
        if (book is not (LetterOfJeremiah.Book or LetterOfJeremiah.Baruch) ||
            !placed.Any(place => LetterOfJeremiah.Twin(place.Book, place.Chapter) is not null))
        {
            return placed;
        }

        var own = placed
            .Select(place => LetterOfJeremiah.Twin(place.Book, place.Chapter) is { } twin && place.Book != book
                ? new CanonicalReference(twin.Book, twin.Chapter, place.Verse)
                : place)
            .ToList();
        var other = own
            .Select(place => LetterOfJeremiah.Twin(place.Book, place.Chapter) is { } twin
                ? new CanonicalReference(twin.Book, twin.Chapter, place.Verse)
                : (CanonicalReference?)null)
            .OfType<CanonicalReference>();
        return [.. own.Concat(other).Distinct()];
    }
}
