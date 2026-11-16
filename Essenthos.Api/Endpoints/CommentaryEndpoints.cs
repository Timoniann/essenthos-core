using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <param name="Language">The language the commentary is written in, ISO 639-1; it is shown in it, whatever the reader's.</param>
/// <param name="Books">The canonical books it comments on.</param>
internal record CommentaryResponse(
    string Id,
    string Name,
    string Author,
    string Language,
    string? Licence,
    string? LicenceUrl,
    string Url,
    IList<int> Books);

internal record CommentaryListResponse(IList<CommentaryResponse> Items);

/// <param name="Kind"><c>verses</c> for a comment on a passage, <c>chapter</c> or <c>book</c> for an introduction.</param>
/// <param name="First">Where the passage starts; null for an introduction.</param>
/// <param name="Last">Where it ends, the same verse when it is one.</param>
/// <param name="Body">
/// The comment as blocks — <c>{"h": […]}</c> a heading, <c>{"p": […]}</c> a paragraph, <c>{"l": […]}</c> a line of verse,
/// <c>{"q": […]}</c> a quotation — each a list of pieces: a plain string, or <c>{"t": text}</c> with <c>i</c> italic,
/// <c>b</c> bold, <c>sc</c> small capitals, <c>sup</c> a raised verse number, <c>u</c> underlined, <c>n</c> the author's
/// note, <c>r</c> the verse it cites as [book, chapter(, verse(, last chapter, last verse))].
/// </param>
internal record CommentaryEntryResponse(
    string Kind,
    VerseRefResponse? First,
    VerseRefResponse? Last,
    JsonElement Body);

/// <param name="Verse">The first verse asked for; null when the introductions were.</param>
/// <param name="Through">The last verse asked for, the same as <paramref name="Verse"/> for one.</param>
internal record CommentaryPassageResponse(
    string Commentary,
    BookRefResponse Book,
    int Chapter,
    int? Verse,
    int? Through,
    IList<CommentaryEntryResponse> Entries);

/// <summary>
/// What a commentary wrote beside a verse, from one commentary at a time: each comment covers the run of verses it
/// was written on, so the one beside a verse may begin before it and end after it, and says where it does.
/// </summary>
internal static class CommentaryEndpoints
{
    private const string Verses = "verses";
    private const string Chapter = "chapter";
    private const string Book = "book";

    public static void MapCommentaries(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/commentaries", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var books = await db.CommentaryEntries
                .Select(e => new { e.Commentary, e.Book })
                .Distinct()
                .ToListAsync(cancellationToken);
            return Results.Ok(new CommentaryListResponse(
            [
                .. CommentaryWorks.All
                    .Where(work => books.Any(b => b.Commentary == work.Id))
                    .Select(work =>
                    {
                        var dataset = Array.Find(Datasets.All, d => d.Id == work.Dataset)!;
                        return new CommentaryResponse(
                            work.Id, dataset.Name, dataset.Author, work.Language, dataset.Licence, dataset.LicenceUrl, dataset.Url,
                            [.. books.Where(b => b.Commentary == work.Id).Select(b => b.Book).Order()]);
                    }),
            ]));
        }).KeptUntilRelease();

        routes.MapGet("/commentaries/{commentary}/{book}/{chapter:int}", async (
            string commentary,
            string book,
            int chapter,
            [FromQuery] int? verse,
            [FromQuery] int? through,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            if (CommentaryWorks.Find(commentary) is null)
            {
                return ApiResults.NotFound(
                    $"There is no commentary \"{commentary}\". Ask /v1/commentaries for the ones there are.");
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
                    $"{BookReferences.Name(ordinal.Value)} has {chapters} chapters in the shared numbering, so there is no chapter {chapter}.");
            }

            if (verse is < 1 || through is not null && (verse is null || through < verse))
            {
                return Results.BadRequest(new ProblemResponse(
                    "Ask for a verse from 1, or for verse=…&through=… with through at or after verse; leave both out for the introductions."));
            }

            return Results.Ok(await Passage(db, commentary, ordinal.Value, chapter, verse, through ?? verse, cancellationToken));
        });
    }

    /// <summary>
    /// The comments of one commentary whose passage holds any of the verses, in the commentary's order; without a verse,
    /// the introductions to the chapter and to its book.
    /// </summary>
    internal static async Task<CommentaryPassageResponse> Passage(
        AppDbContext db,
        string commentary,
        int book,
        int chapter,
        int? verse,
        int? through,
        CancellationToken cancellationToken)
    {
        var inBook = db.CommentaryEntries.Where(e => e.Commentary == commentary && e.Book == book);
        var last = through ?? verse;
        var found = verse is { } v
            ? inBook.Where(e => e.FirstVerse != null
                                && (e.FirstChapter < chapter || e.FirstChapter == chapter && e.FirstVerse <= last)
                                && (e.LastChapter > chapter || e.LastChapter == chapter && e.LastVerse >= v))
            : inBook.Where(e => e.FirstChapter == null || e.FirstChapter == chapter && e.FirstVerse == null);
        var rows = await found
            .OrderBy(e => e.Sequence)
            .Select(e => new { e.FirstChapter, e.FirstVerse, e.LastChapter, e.LastVerse, e.Body })
            .ToListAsync(cancellationToken);

        return new CommentaryPassageResponse(
            commentary,
            new BookRefResponse(book, BookReferences.Name(book), BookReferences.Slug(book)),
            chapter,
            verse,
            verse is null ? null : last,
            [
                .. rows.Select(row =>
                {
                    using var body = JsonDocument.Parse(row.Body);
                    return new CommentaryEntryResponse(
                        row.FirstChapter is null ? Book : row.FirstVerse is null ? Chapter : Verses,
                        row.FirstVerse is { } first ? BookReferences.At(book, row.FirstChapter!.Value, first) : null,
                        row.LastVerse is { } last ? BookReferences.At(book, row.LastChapter!.Value, last) : null,
                        body.RootElement.Clone());
                }),
            ]);
    }
}
