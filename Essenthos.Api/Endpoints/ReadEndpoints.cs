using Essenthos.Core.Corpus;
﻿using Essenthos.Core.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal record CorpusListResponse(IList<CorpusResponse> Items);

/// <param name="Testament">
/// <c>old</c> or <c>new</c>, kept because the contract has it. It is the Protestant canon's answer
/// and only ever that: <c>Section</c> is the one that changes with the canon asked for.
/// </param>
/// <param name="Section">
/// The heading this book sits under in the canon that was asked for — <c>ketuvim</c> in the
/// Tanakh, <c>old-testament</c> in the Protestant canon, both for Ruth.
/// </param>
internal record BookResponse(
    int Ordinal,
    string Name,
    string Abbreviation,
    string Slug,
    string Testament,
    int ChapterCount)
{
    public string? Section { get; init; }

    /// <summary>
    /// What a given text calls this book, in its own language, present only when a text was asked
    /// for. Null where that text names no book at that ordinal — six of the Reina-Valera's, for
    /// instance — and null on the plain canon listing, which is about the canon and not about any
    /// one witness.
    ///
    /// It is served because a client that has to match what a reader typed — *Йов* for Job — cannot
    /// do it from the English name, and the alternative is a copy of the names inside the client
    /// that drifts from the corpus the moment a text is loaded or corrected (PRB-0381, PRB-0396).
    /// </summary>
    public string? NameNative { get; init; }
}

/// <param name="Canon">Which canon these books are, in which order.</param>
internal record BookListResponse(IList<BookResponse> Items)
{
    public CanonResponse? Canon { get; init; }
}

/// <param name="Collection">
/// What to call the whole thing in this canon: "Bible", or "Scripture" for the Tanakh.
/// </param>
internal record CanonResponse(
    string Slug,
    string Name,
    string Collection,
    string Description,
    int BookCount,
    IList<CanonSectionResponse> Sections);

internal record CanonSectionResponse(string Slug, string Name, int BookCount);

internal record CanonListResponse(IList<CanonResponse> Items);

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
                    new CoverageResponse(entry.FirstBook, entry.LastBook, entry.Books),
                    entry.HasWordMapping))
                .ToList();

            return Results.Ok(new CorpusListResponse(items));
        });

        // Which canon, in which order, under what headings — and what the collection is called.
        // Answered without touching the database, because it is a statement about traditions and
        // not about what happens to be loaded.
        routes.MapGet("/canons", () => Results.Ok(new CanonListResponse(
            [.. Canons.List.Select(Canon)])));

        routes.MapGet("/books", async (
            [FromQuery] string? canon,
            [FromQuery] string? corpus,
            AppDbContext db,
            ICanonIndex index,
            CancellationToken cancellationToken) =>
        {
            if (Canons.Find(canon) is not { } wanted)
            {
                return ApiResults.NotFound($"There is no canon \"{canon}\". Try one of: {Canons.Names}.");
            }

            // Asked for a text, the listing also says what that text calls each book. An unknown
            // identifier is refused rather than ignored: a client that misspells it would otherwise
            // get the English names back and no sign that its question was dropped.
            var native = new Dictionary<int, string>();
            if (!string.IsNullOrWhiteSpace(corpus))
            {
                if (await index.Text(corpus, cancellationToken) is not { } text)
                {
                    return ApiResults.NotFound($"There is no text \"{corpus}\".");
                }

                native = await db.Books
                    .Where(b => b.TextId == text.Id && b.NameNative != null)
                    .ToDictionaryAsync(b => b.CanonicalOrdinal, b => b.NameNative!, cancellationToken);
            }

            var items = new List<BookResponse>(wanted.BookCount);
            foreach (var ordinal in wanted.Ordinals)
            {
                items.Add(await Book(index, ordinal, cancellationToken) with
                {
                    Section = Canons.SectionOf(wanted, ordinal),
                    NameNative = native.GetValueOrDefault(ordinal),
                });
            }

            return Results.Ok(new BookListResponse(items) { Canon = Canon(wanted) });
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

    private static CanonResponse Canon(CanonDefinition canon) => new(
        canon.Slug,
        canon.Name,
        canon.Collection,
        canon.Description,
        canon.BookCount,
        [.. canon.Sections.Select(section =>
            new CanonSectionResponse(section.Slug, section.Name, section.Ordinals.Count))]);

    private static BookRefResponse BookReference(int ordinal) =>
        new(ordinal, BookReferences.Name(ordinal), BookReferences.Slug(ordinal));
}
