using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <param name="Kind">
/// <c>references</c> for verses a reader might turn to, <c>parallels</c> for passages that tell the
/// same thing in the same words, which open side by side.
/// </param>
/// <param name="Default">Whether the reader opens on this set once it shows cross references at all.</param>
internal record CrossReferenceSetResponse(
    string Id,
    string Kind,
    string Name,
    string Author,
    string? Licence,
    string? LicenceUrl,
    string Url,
    bool Default);

internal record CrossReferenceSetListResponse(IList<CrossReferenceSetResponse> Items);

/// <param name="End">Where the reference stops, when it is more than one verse.</param>
/// <param name="Votes">What readers of the set have voted it, where the set is ranked by votes.</param>
/// <param name="Note">The word of the verse the set files it under, as the set prints it.</param>
/// <param name="Shared">For a parallel, how many words the two verses share.</param>
/// <param name="Passage">For a parallel, the two passages it belongs to.</param>
internal record CrossReferenceResponse(
    VerseRefResponse To,
    VerseRefResponse? End,
    int? Votes,
    string? Note,
    int? Shared,
    ParallelPassageRangeResponse? Passage);

/// <param name="References">In the set's order: by votes, as the set prints them, or by words shared.</param>
internal record CrossReferenceVerseResponse(int Verse, IList<CrossReferenceResponse> References);

internal record CrossReferenceChapterResponse(
    string Set,
    BookRefResponse Book,
    int Chapter,
    IList<CrossReferenceVerseResponse> Verses);

/// <summary>A run of verses, both ends included.</summary>
internal record VerseRangeResponse(VerseRefResponse First, VerseRefResponse Last);

/// <param name="From">The passage on this side: the one the reader is reading.</param>
/// <param name="To">The passage it runs parallel to.</param>
internal record ParallelPassageRangeResponse(VerseRangeResponse From, VerseRangeResponse To);

/// <param name="Words">The words the two verses share, as positions in each verse of the passage's text, this side's first.</param>
internal record ParallelPairResponse(VerseRefResponse From, VerseRefResponse To, IList<IList<int>> Words);

/// <param name="Text">The original the words were matched in, whose word positions the pairs give.</param>
/// <param name="Pairs">Verse against verse, in the order of this side.</param>
internal record ParallelPassageResponse(
    ParallelPassageRangeResponse Passage,
    string Text,
    IList<ParallelPairResponse> Pairs);

/// <summary>
/// The cross references of a chapter, from one set at a time, and the parallel passages this
/// project detected, with the words each pair of verses shares.
///
/// <para>
/// A set is chosen by the reader, not by the corpus: OpenBible's is ranked by its readers' votes,
/// the Treasury's is in its printed order, and neither is more right than the other. So every
/// answer is from the one set asked for, and <c>/sets</c> says which there are and under whose terms.
/// A reference readers voted below zero is left out.
/// </para>
/// </summary>
internal static class CrossReferenceEndpoints
{
    private const string References = "references";

    private const string Parallels = "parallels";

    private const char WithinAddress = ':';

    public static void MapCrossReferences(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/cross-references/sets", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var loaded = await db.CrossReferences.Select(r => r.Set).Distinct().ToListAsync(cancellationToken);
            return Results.Ok(new CrossReferenceSetListResponse(
            [
                .. CrossReferenceSets.All
                    .Where(set => loaded.Contains(set.Id))
                    .Select(set =>
                    {
                        var dataset = Array.Find(Datasets.All, d => d.Id == set.Dataset)!;
                        return new CrossReferenceSetResponse(
                            set.Id,
                            set.What == CrossReferenceSets.Kind.Parallels ? Parallels : References,
                            set.Name ?? dataset.Name,
                            dataset.Author,
                            dataset.Licence,
                            dataset.LicenceUrl,
                            dataset.Url,
                            set.Id == CrossReferenceSets.Default);
                    }),
            ]));
        });

        routes.MapGet("/cross-references/{book}/{chapter:int}", async (
            string book,
            int chapter,
            [FromQuery] string? set,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var chosen = CrossReferenceSets.Find(set ?? CrossReferenceSets.Default);
            if (chosen is null)
            {
                return ApiResults.NotFound(
                    $"There is no set of cross references \"{set}\". Ask /v1/cross-references/sets for the ones there are.");
            }

            var ordinal = BookReferences.ResolveOrdinal(book);
            if (ordinal is null)
            {
                return ApiResults.NotFound(BookReferences.FormatHint(book));
            }

            var chapters = await canon.ChapterCount(ordinal.Value, cancellationToken);
            if (chapter < 1 || chapter > chapters)
            {
                return ApiResults.NotFound(
                    $"{BookReferences.Name(ordinal.Value)} has {chapters} chapters in the shared numbering, " +
                    $"so there is no chapter {chapter}.");
            }

            return Results.Ok(await InChapter(db, chosen, ordinal.Value, chapter, cancellationToken));
        });

        // A detected passage, verse against verse, found by any one pair of its verses: the passage
        // number is renumbered by every load, and an address is not.
        routes.MapGet("/cross-references/passage", async (
            [FromQuery] string? from,
            [FromQuery] string? to,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (Verse(from) is not { } a || Verse(to) is not { } b)
            {
                return Results.BadRequest(new ProblemResponse(
                    "Ask for a passage by one pair of its verses, from=book:chapter:verse&to=book:chapter:verse — "
                    + "from=2-samuel:22:2&to=psalms:18:2."));
            }

            return await Passage(db, a, b, cancellationToken) is { } found
                ? Results.Ok(found)
                : ApiResults.NotFound(
                    $"No detected passage pairs {from} with {to}. The parallels of a chapter are at "
                    + "/v1/cross-references/{book}/{chapter}?set=parallels.");
        });
    }

    /// <summary>One set's references from each verse of a chapter, in the set's order, without those readers voted down.</summary>
    internal static async Task<CrossReferenceChapterResponse> InChapter(
        AppDbContext db,
        CrossReferenceSets.Set chosen,
        int book,
        int chapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.CrossReferences
            .Where(r => r.Set == chosen.Id && r.Book == book && r.Chapter == chapter
                        && (r.Votes == null || r.Votes >= 0))
            .OrderBy(r => r.Verse)
            .ThenBy(r => r.Rank)
            .Select(r => new
            {
                r.Verse, r.ToBook, r.ToChapter, r.ToVerse, r.ToEndBook, r.ToEndChapter, r.ToEndVerse,
                r.Votes, r.Note, r.Passage, r.Matched,
            })
            .ToListAsync(cancellationToken);

        var passages = await Ranges(
            db, [.. rows.Where(r => r.Passage != null).Select(r => r.Passage!.Value).Distinct()], cancellationToken);

        return new CrossReferenceChapterResponse(
            chosen.Id,
            new BookRefResponse(book, BookReferences.Name(book), BookReferences.Slug(book)),
            chapter,
            [
                .. rows.GroupBy(r => r.Verse).Select(verse => new CrossReferenceVerseResponse(
                    verse.Key,
                    [
                        .. verse.Select(r => new CrossReferenceResponse(
                            BookReferences.At(r.ToBook, r.ToChapter, r.ToVerse)!,
                            BookReferences.At(r.ToEndBook, r.ToEndChapter, r.ToEndVerse),
                            r.Votes,
                            r.Note,
                            r.Matched is null ? null : Pairs(r.Matched).Count,
                            r.Passage is { } passage ? passages.GetValueOrDefault(passage) : null)),
                    ])),
            ]);
    }

    /// <summary>The detected passage one pair of verses belongs to, verse against verse; null where no passage pairs them.</summary>
    internal static async Task<ParallelPassageResponse?> Passage(
        AppDbContext db,
        (int Book, int Chapter, int Verse) a,
        (int Book, int Chapter, int Verse) b,
        CancellationToken cancellationToken)
    {
        var passage = await db.CrossReferences
            .Where(r => r.Set == CrossReferenceSets.Parallels
                        && r.Book == a.Book && r.Chapter == a.Chapter && r.Verse == a.Verse
                        && r.ToBook == b.Book && r.ToChapter == b.Chapter && r.ToVerse == b.Verse)
            .Select(r => r.Passage)
            .FirstOrDefaultAsync(cancellationToken);
        if (passage is not { } number)
        {
            return null;
        }

        var pairs = await db.CrossReferences
            .Where(r => r.Set == CrossReferenceSets.Parallels && r.Passage == number)
            .OrderBy(r => r.Book).ThenBy(r => r.Chapter).ThenBy(r => r.Verse)
            .ThenBy(r => r.ToBook).ThenBy(r => r.ToChapter).ThenBy(r => r.ToVerse)
            .Select(r => new { r.Book, r.Chapter, r.Verse, r.ToBook, r.ToChapter, r.ToVerse, r.MatchedIn, r.Matched })
            .ToListAsync(cancellationToken);
        var ranges = await Ranges(db, [number], cancellationToken);

        return new ParallelPassageResponse(
            ranges[number],
            pairs[0].MatchedIn ?? string.Empty,
            [
                .. pairs.Select(pair => new ParallelPairResponse(
                    BookReferences.At(pair.Book, pair.Chapter, pair.Verse)!,
                    BookReferences.At(pair.ToBook, pair.ToChapter, pair.ToVerse)!,
                    [.. Pairs(pair.Matched ?? string.Empty).Select(w => (IList<int>)[w.From, w.To])])),
            ]);
    }

    /// <summary>Where each passage starts and ends on both sides, from the rows it is made of.</summary>
    private static async Task<Dictionary<int, ParallelPassageRangeResponse>> Ranges(
        AppDbContext db,
        List<int> passages,
        CancellationToken cancellationToken)
    {
        if (passages.Count == 0)
        {
            return [];
        }

        var rows = await db.CrossReferences
            .Where(r => r.Set == CrossReferenceSets.Parallels && r.Passage != null && passages.Contains(r.Passage.Value))
            .Select(r => new { Passage = r.Passage!.Value, r.Book, r.Chapter, r.Verse, r.ToBook, r.ToChapter, r.ToVerse })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.Passage).ToDictionary(
            passage => passage.Key,
            passage =>
            {
                var from = passage.Select(r => (r.Book, r.Chapter, r.Verse)).Order().ToList();
                var to = passage.Select(r => (Book: r.ToBook, Chapter: r.ToChapter, Verse: r.ToVerse)).Order().ToList();
                return new ParallelPassageRangeResponse(Range(from[0], from[^1]), Range(to[0], to[^1]));
            });
    }

    private static VerseRangeResponse Range((int Book, int Chapter, int Verse) first, (int Book, int Chapter, int Verse) last) =>
        new(BookReferences.At(first.Book, first.Chapter, first.Verse)!, BookReferences.At(last.Book, last.Chapter, last.Verse)!);

    /// <summary>The shared words as the row writes them — <c>1-1 2-2 4-3</c> — as positions in each verse.</summary>
    private static List<(int From, int To)> Pairs(string matched) =>
    [
        .. matched.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('-'))
            .Where(pair => pair.Length == 2)
            .Select(pair => (int.Parse(pair[0]), int.Parse(pair[1]))),
    ];

    private static (int Book, int Chapter, int Verse)? Verse(string? address)
    {
        var parts = (address ?? string.Empty).Split(WithinAddress);
        return parts.Length == 3
               && BookReferences.ResolveOrdinal(parts[0]) is { } book
               && int.TryParse(parts[1], out var chapter)
               && int.TryParse(parts[2], out var verse)
            ? (book, chapter, verse)
            : null;
    }
}
