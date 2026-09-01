using Essenthos.Core.Database;
using Essenthos.Core.Strong;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Strong's concordance, and the words of the corpus that carry each number.
///
/// The occurrences endpoint is the one worth having. A dictionary entry is a page anyone can find
/// elsewhere; "every place this lexeme stands, in every witness that tags it, and how each
/// translation rendered it there" is the thing this corpus is shaped to answer and almost nowhere
/// else can.
/// </summary>
internal static class StrongEndpoints
{
    /// <summary>
    /// A search that returns everything is a search nobody paged, and it is the whole table over
    /// the wire.
    /// </summary>
    private const int MostPerPage = 200;

    public static void MapStrong(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/strong/{number}", async (
            string number,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (StrongNumbers.Normalize(number) is not { } canonical)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{number}\" is not a Strong number. Write a language letter and digits, as H430 or G26."));
            }

            var entry = await db.StrongEntries
                .Where(e => e.StrongNumber == canonical)
                .Select(e => Response(e))
                .FirstOrDefaultAsync(cancellationToken);

            if (entry is not null)
            {
                return Results.Ok(entry);
            }

            // A prefix morpheme is not a missing entry. ETCBC numbers the conjunction, the article
            // and the inseparable prepositions in the H9000 range, and Strong never catalogued them
            // because a concordance has nothing to say about a letter. Answering 404 would report
            // 121,077 words of this corpus as broken.
            return StrongMorphemeCodes.GetDescription(canonical) is { } morpheme
                ? Results.Ok(new StrongEntryResponse(canonical, null, null, null, morpheme, null, null,
                    null, null, null, null, null, true))
                : Results.NotFound(new ProblemResponse(
                    $"Strong's concordance has no entry {canonical}."));
        });

        routes.MapGet("/strong", async (
            [FromQuery] string? query,
            [FromQuery] string? language,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var entries = db.StrongEntries.AsQueryable();

            if (language is { Length: > 0 })
            {
                var letter = language.StartsWith('g') || language.StartsWith('G') ? "G" : "H";
                entries = entries.Where(e => e.StrongNumber.StartsWith(letter));
            }

            if (query is { Length: > 0 })
            {
                var like = $"%{query}%";
                entries = entries.Where(e =>
                    EF.Functions.ILike(e.Lemma ?? string.Empty, like) ||
                    EF.Functions.ILike(e.Transliteration ?? string.Empty, like) ||
                    EF.Functions.ILike(e.Definition ?? string.Empty, like) ||
                    EF.Functions.ILike(e.KjvDefinition ?? string.Empty, like));
            }

            var total = await entries.CountAsync(cancellationToken);
            var page = await entries
                .OrderBy(e => e.StrongNumber.Substring(0, 1))
                .ThenBy(e => e.StrongNumber.Length)
                .ThenBy(e => e.StrongNumber)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .Select(e => Response(e))
                .ToListAsync(cancellationToken);

            return Results.Ok(new StrongListResponse(total, page));
        });

        routes.MapGet("/strong/{number}/occurrences", async (
            string number,
            [FromQuery] string? corpus,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (StrongNumbers.Normalize(number) is not { } canonical)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{number}\" is not a Strong number. Write a language letter and digits, as H430 or G26."));
            }

            var words = db.Words.Where(w => w.StrongNumber == canonical);
            if (corpus is { Length: > 0 })
            {
                words = words.Where(w => w.Text!.Slug == corpus);
            }

            var total = await words.CountAsync(cancellationToken);
            var page = await words
                .OrderBy(w => w.Text!.Slug)
                .ThenBy(w => w.Verse!.Book!.CanonicalOrdinal)
                .ThenBy(w => w.Verse!.ChapterNumber)
                .ThenBy(w => w.Verse!.Number)
                .ThenBy(w => w.Position)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .Select(w => new StrongOccurrenceResponse(
                    w.Text!.Slug,
                    w.Verse!.Book!.CanonicalOrdinal,
                    w.Verse!.Book!.Name,
                    w.Verse!.ChapterNumber,
                    w.Verse!.Number,
                    w.Position,
                    w.Surface,
                    w.Gloss))
                .ToListAsync(cancellationToken);

            return Results.Ok(new StrongOccurrenceListResponse(canonical, total, page));
        });
    }

    private static StrongEntryResponse Response(Database.Entities.StrongEntry entry) => new(
        entry.StrongNumber,
        entry.Lemma,
        entry.Transliteration,
        entry.Pronunciation,
        entry.Definition,
        entry.Derivation,
        entry.KjvDefinition,
        entry.Morphology,
        entry.DetailedDefinition,
        entry.SeeAlso,
        entry.SourceLanguage,
        entry.TwotReference,
        false);
}

/// <param name="Morpheme">
/// True where the number is not a concordance entry at all but a prefix morpheme ETCBC numbers in
/// the H9000 range. The definition then says which morpheme, and everything else is null — because
/// there is no entry, not because one is missing.
/// </param>
internal record StrongEntryResponse(
    string StrongNumber,
    string? Lemma,
    string? Transliteration,
    string? Pronunciation,
    string? Definition,
    string? Derivation,
    string? KjvDefinition,
    string? Morphology,
    string? DetailedDefinition,
    string? SeeAlso,
    string? SourceLanguage,
    string? TwotReference,
    bool Morpheme);

internal record StrongListResponse(int Total, IList<StrongEntryResponse> Items);

internal record StrongOccurrenceResponse(
    string Corpus,
    int BookOrdinal,
    string Book,
    int Chapter,
    int Verse,
    int Position,
    string Text,
    string? Gloss);

internal record StrongOccurrenceListResponse(
    string StrongNumber,
    int Total,
    IList<StrongOccurrenceResponse> Items);
