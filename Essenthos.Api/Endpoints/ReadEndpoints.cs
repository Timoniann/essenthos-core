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

/// <param name="Books">
/// The books it holds, in canonical order, each with this text's own chapter count and its own
/// name for the book where it has one.
/// </param>
/// <param name="LemmasFrom">
/// Whoever supplied this text's lemmas, where that is somebody other than the edition itself.
/// </param>
internal record TextDetailResponse(
    CorpusResponse Text,
    TextCountsResponse Counts,
    TextFeaturesResponse Features,
    IList<TextLinkResponse> Links,
    IList<TextCreditResponse> LemmasFrom,
    IList<BookResponse> Books);

/// <param name="Words">The words the edition prints; a word it prints no letters for is not one.</param>
internal record TextCountsResponse(int Chapters, int Verses, int Words)
{
    public int Books { get; init; }
}

/// <summary>How many of the text's words carry each kind of annotation. Zero is an answer: it does not.</summary>
/// <param name="Transliteration">Words carrying the edition's own pronunciation, which only BHSA has.</param>
/// <param name="Named">Words that say which person or place they name.</param>
/// <param name="SyntaxGroups">Clauses, phrases and sentences the edition's own analysis marks.</param>
/// <param name="Supplied">Spans the edition marks as the translators' own, the italics of a printed Bible.</param>
/// <param name="Notes">Notes the edition prints at a verse.</param>
internal record TextFeaturesResponse(
    int Lemmas,
    int StrongNumbers,
    int Glosses,
    int Morphology,
    int Transliteration,
    int Named,
    int SyntaxGroups,
    int Supplied,
    int Notes);

/// <param name="Text">The other text, by its identifier.</param>
/// <param name="Links">How many word links join the two, in either direction.</param>
/// <param name="Methods">What established them, by method, most first.</param>
/// <param name="StatedBy">The datasets that stated any of them. Empty where every link is this project's own.</param>
internal record TextLinkResponse(
    string Text,
    int Links,
    IList<TextLinkMethodResponse> Methods,
    IList<TextCreditResponse> StatedBy);

internal record TextLinkMethodResponse(string Method, int Links);

internal record TextCreditResponse(string Name, string Author);

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
                    entry.HasWordMapping) with { Summary = TextSummaries.For(text.Slug) })
                .ToList();

            return Results.Ok(new CorpusListResponse(items));
        });

        // One text and everything the corpus can say about it. The counts are the expensive part
        // and are asked of TextFacts, which counts every text once and remembers.
        routes.MapGet("/corpora/{corpus}", async (
            string corpus,
            AppDbContext db,
            ICanonIndex canon,
            TextFacts facts,
            CancellationToken cancellationToken) =>
        {
            if (await canon.Text(corpus, cancellationToken) is not { } entry)
            {
                return ApiResults.NotFound(
                    $"There is no text \"{corpus}\". Ask /v1/corpora for the ones this corpus holds.");
            }

            var text = await db.Texts.SingleAsync(t => t.Id == entry.Id, cancellationToken);
            var tally = await facts.Of(entry.Id, cancellationToken);

            // The books as this text has them: its own chapter count and what it calls each one,
            // under the canonical name and slug every other address uses.
            var native = await db.Books
                .Where(b => b.TextId == entry.Id)
                .ToDictionaryAsync(b => b.CanonicalOrdinal, b => b.NameNative, cancellationToken);
            var books = new List<BookResponse>(entry.Books.Count);
            foreach (var ordinal in entry.Books)
            {
                books.Add(await Book(canon, ordinal, cancellationToken) with
                {
                    ChapterCount = await canon.ChapterCountIn(entry.Id, ordinal, cancellationToken),
                    NameNative = native.GetValueOrDefault(ordinal),
                });
            }

            var lemmasFrom = Datasets.All
                .Where(dataset => string.Equals(dataset.Lemmas, text.Slug, StringComparison.OrdinalIgnoreCase))
                .Select(dataset => new TextCreditResponse(dataset.Name, dataset.Author))
                .ToList();

            return Results.Ok(new TextDetailResponse(
                Texts.Corpus(
                    text,
                    new CoverageResponse(entry.FirstBook, entry.LastBook, entry.Books),
                    entry.HasWordMapping) with { Summary = TextSummaries.For(text.Slug) },
                tally.Counts with { Books = entry.Books.Count },
                tally.Features,
                tally.Links,
                lemmasFrom,
                books));
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
