using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Every verse of one edition where a Strong number stands, a page at a time and as the books run,
/// each with the words that carry it and, on the first page, how many verses of each book there are.
/// The occurrences list counts words; this counts verses, which is what a reader reading them asks for.
/// </summary>
internal static class StrongVerseEndpoints
{
    public static void MapStrongVerses(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/strong/{number}/verses", async (
            string number,
            [FromQuery] string? corpus,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            if (StrongNumbers.Normalize(number) is not { } canonical)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{number}\" is not a Strong number. Write a language letter and digits, as H430 or G26."));
            }

            if (corpus is { Length: > 0 } && await canon.Text(corpus, cancellationToken) is null)
            {
                return ApiResults.NotFound(
                    $"There is no text \"{corpus}\". Ask /v1/corpora for the ones this corpus holds.");
            }

            return Results.Ok(await Page(db, canon, canonical, corpus, skip ?? 0, take ?? 20, cancellationToken));
        });

    /// <summary>
    /// One page of the verses, in the edition named or, where none is, the original holding the number most
    /// often; an empty page where no text holds it.
    /// </summary>
    internal static async Task<StrongVerseListResponse> Page(
        AppDbContext db,
        ICanonIndex canon,
        string canonical,
        string? corpus,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var byText = await db.Words
            .Where(w => w.StrongNumber == canonical)
            .GroupBy(w => w.Text!.Slug)
            .Select(g => new StrongTextCountResponse(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
        byText = [.. byText.OrderByDescending(text => text.Count).ThenBy(text => text.Corpus, StringComparer.Ordinal)];

        var slug = corpus is { Length: > 0 }
            ? (await canon.Text(corpus, cancellationToken))?.Slug
            : await Edition(db, byText, cancellationToken);
        var textId = slug is null ? 0 : (await canon.Text(slug, cancellationToken))?.Id ?? 0;

        const int bookStride = EncyclopediaEndpoints.BookStride;
        const int chapterStride = EncyclopediaEndpoints.ChapterStride;
        var placed =
            from w in db.Words
            join r in db.VerseReferences on w.VerseId equals r.VerseId
            where w.StrongNumber == canonical && w.TextId == textId && r.IsPrimary
            select new
            {
                Address = (r.CanonicalBook * bookStride) + (r.CanonicalChapter * chapterStride) + r.CanonicalVerse,
                w.Position,
                w.Surface,
            };
        var addresses = placed.Select(p => p.Address).Distinct();

        var total = await addresses.CountAsync(cancellationToken);
        var wanted = await addresses
            .OrderBy(address => address)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, EncyclopediaEndpoints.MostReferencesPerPage))
            .ToListAsync(cancellationToken);
        var words = await placed
            .Where(p => wanted.Contains(p.Address))
            .OrderBy(p => p.Address).ThenBy(p => p.Position)
            .Select(p => new { p.Address, p.Surface })
            .ToListAsync(cancellationToken);

        List<EntityReferenceBookResponse>? books = null;
        if (skip <= 0)
        {
            books =
            [
                .. (await addresses
                        .GroupBy(address => address / bookStride)
                        .Select(g => new { Book = g.Key, Count = g.Count(), First = g.Min() })
                        .ToListAsync(cancellationToken))
                    .OrderBy(book => book.Book)
                    .Select(book => new EntityReferenceBookResponse(
                        Book(book.Book), book.Count, book.First % bookStride / chapterStride, book.First % chapterStride)),
            ];
        }

        var byAddress = words.GroupBy(w => w.Address).ToDictionary(g => g.Key, g => g.Select(w => w.Surface).ToList());
        return new StrongVerseListResponse(
            canonical,
            slug,
            total,
            byText,
            [
                .. wanted.Select(address => new StrongVerseResponse(
                    Book(address / bookStride),
                    address % bookStride / chapterStride,
                    address % chapterStride,
                    byAddress.GetValueOrDefault(address) ?? [])),
            ])
        {
            Books = books,
        };
    }

    /// <summary>The original edition holding the number most often, or the text holding it most where no original does.</summary>
    private static async Task<string?> Edition(
        AppDbContext db, IReadOnlyList<StrongTextCountResponse> byText, CancellationToken cancellationToken)
    {
        var slugs = byText.Select(text => text.Corpus).ToList();
        var originals = await db.Texts
            .Where(t => slugs.Contains(t.Slug) && t.Kind != TextKind.Translation)
            .Select(t => t.Slug)
            .ToListAsync(cancellationToken);
        return byText.FirstOrDefault(text => originals.Contains(text.Corpus))?.Corpus ?? byText.FirstOrDefault()?.Corpus;
    }

    private static BookRefResponse Book(int ordinal) =>
        new(ordinal, BookReferences.Name(ordinal), BookReferences.Slug(ordinal));
}

/// <param name="Corpus">The edition the verses are counted in, as its slug; null where no text holds the number.</param>
/// <param name="Total">How many verses of that edition hold the number.</param>
/// <param name="ByText">How many words carry the number in each text, most first, to choose another edition by.</param>
internal record StrongVerseListResponse(
    string StrongNumber,
    string? Corpus,
    int Total,
    IList<StrongTextCountResponse> ByText,
    IList<StrongVerseResponse> Items)
{
    /// <summary>Every book the verses are in, in canonical order, with how many and the first. On the first page only.</summary>
    public IList<EntityReferenceBookResponse>? Books { get; init; }
}

/// <param name="Words">The words of the verse that carry the number, as the edition prints them.</param>
internal record StrongVerseResponse(BookRefResponse Book, int Chapter, int Verse, IList<string> Words);
