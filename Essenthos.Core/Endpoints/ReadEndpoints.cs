using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal record CorpusListResponse(IList<CorpusResponse> Items);

internal record BookResponse(
    int Ordinal,
    string Name,
    string Abbreviation,
    string Slug,
    string Testament,
    int ChapterCount);

internal record BookListResponse(IList<BookResponse> Items);

internal record ChapterTextResponse(
    string Corpus,
    BookRefResponse Book,
    int Chapter,
    int ChapterCount,
    string Direction,
    IList<TextVerseResponse> Verses);

internal static class ReadEndpoints
{
    public static void MapRead(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/corpora", async (AppDbContext db, ICanonIndex canon, CancellationToken cancellationToken) =>
        {
            var entries = await canon.Texts(cancellationToken);
            var texts = await db.Texts.OrderBy(t => t.Slug).ToListAsync(cancellationToken);

            var items = texts
                .Join(entries, t => t.Id, e => e.Id, (text, entry) => Texts.Corpus(
                    text,
                    new CoverageResponse(entry.FirstBook, entry.LastBook),
                    entry.HasWordMapping))
                .ToList();

            return Results.Ok(new CorpusListResponse(items));
        });

        routes.MapGet("/books", async (ICanonIndex canon, CancellationToken cancellationToken) =>
        {
            var items = new List<BookResponse>(BookReferences.CanonBookCount);
            foreach (var ordinal in BookReferences.Ordinals)
            {
                items.Add(await Book(canon, ordinal, cancellationToken));
            }

            return Results.Ok(new BookListResponse(items));
        });

        routes.MapGet("/books/{book}", async (string book, ICanonIndex canon, CancellationToken cancellationToken) =>
        {
            var ordinal = BookReferences.ResolveOrdinal(book);
            return ordinal is null
                ? ApiResults.NotFound(BookReferences.FormatHint(book))
                : Results.Ok(await Book(canon, ordinal.Value, cancellationToken));
        });

        routes.MapGet("/text/{corpus}/{book}/{chapter:int}", async (
            string corpus,
            string book,
            int chapter,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var entry = await canon.Text(corpus, cancellationToken);
            if (entry is null)
            {
                return ApiResults.NotFound(
                    $"There is no text \"{corpus}\". Ask /v1/corpora for the ones this corpus holds.");
            }

            var ordinal = BookReferences.ResolveOrdinal(book);
            if (ordinal is null)
            {
                return ApiResults.NotFound(BookReferences.FormatHint(book));
            }

            var chapterCount = await canon.ChapterCountIn(entry.Id, ordinal.Value, cancellationToken);
            if (chapterCount == 0)
            {
                return ApiResults.NotFound(
                    $"The text \"{corpus}\" does not contain {BookReferences.Name(ordinal.Value)}. That is an " +
                    "absence of the book, not an absence of text.");
            }

            if (chapter < 1 || chapter > chapterCount)
            {
                return ApiResults.NotFound(
                    $"{BookReferences.Name(ordinal.Value)} has {chapterCount} chapters in \"{corpus}\", so there " +
                    $"is no chapter {chapter}.");
            }

            var verses = await Texts.ReadChapter(db, entry.Id, ordinal.Value, chapter, cancellationToken);
            var text = await db.Texts.SingleAsync(t => t.Id == entry.Id, cancellationToken);

            return Results.Ok(new ChapterTextResponse(
                entry.Slug,
                BookReference(ordinal.Value),
                chapter,
                chapterCount,
                text.Direction == Database.Entities.Enums.TextDirection.RightToLeft ? "rtl" : "ltr",
                verses));
        });
    }

    private static async Task<BookResponse> Book(ICanonIndex canon, int ordinal, CancellationToken cancellationToken)
    {
        return new BookResponse(
            ordinal,
            BookReferences.Name(ordinal),
            BookReferences.Abbreviation(ordinal),
            BookReferences.Slug(ordinal),
            BookReferences.Testament(ordinal),
            await canon.ChapterCount(ordinal, cancellationToken));
    }

    private static BookRefResponse BookReference(int ordinal) =>
        new(ordinal, BookReferences.Name(ordinal), BookReferences.Slug(ordinal));
}
