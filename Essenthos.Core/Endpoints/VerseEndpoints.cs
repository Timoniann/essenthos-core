using Essenthos.Core.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <param name="Text">The verse as it reads, its words joined by their own trailers.</param>
internal record VerseTextResponse(BookRefResponse Book, int Chapter, int Verse, string Text)
{
    /// <summary>The verse's own label where the text gives one, as the chapter reading does.</summary>
    public string? Label { get; init; }
}

/// <param name="Missing">
/// The addresses this text has nothing at, in the spelling they were asked for. A reference that
/// resolves to no verse is an answer — the Septuagint numbers Jeremiah differently and the King
/// James has no Sirach — and dropping it silently would leave a caller unable to tell an empty
/// verse from one it never asked about.
/// </param>
internal record VerseTextListResponse(string Corpus, IList<VerseTextResponse> Items, IList<string> Missing);

/// <summary>
/// The words of a handful of verses, addressed one by one, with nothing else attached to them.
///
/// The reading endpoints answer a chapter at a time and answer it in full — every word with its
/// links, its morphology and its annotations, which is 200KB for Genesis 11 and is the right answer
/// for a reader. It is the wrong answer for a page that shows forty references and wants to say
/// what each of them says: forty chapters at that size is megabytes, fetched to render a tooltip.
///
/// So this endpoint takes the addresses and returns the words joined and nothing more. One request
/// for a page rather than one per reference, because a page of a hundred references is a hundred
/// round trips otherwise and the client cannot batch what the API will not take in a batch.
/// </summary>
internal static class VerseEndpoints
{
    /// <summary>
    /// More addresses than a page shows. The entity page pages its references at a hundred, and a
    /// request past this is a download of the text under another name.
    /// </summary>
    private const int MostAddresses = 120;

    /// <summary>What separates one address from the next, and the parts of one: `genesis:11:27`.</summary>
    private const char BetweenAddresses = ',';

    private const char WithinAddress = ':';

    public static void MapVerses(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/verses", async (
            [FromQuery] string? corpus,
            [FromQuery] string? refs,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var text = corpus is { Length: > 0 }
                ? await canon.Text(corpus, cancellationToken)
                : (await canon.Texts(cancellationToken)).FirstOrDefault();

            if (text is null)
            {
                return ApiResults.NotFound(
                    $"There is no text \"{corpus}\". Ask /v1/corpora for the ones this corpus holds.");
            }

            var asked = (refs ?? string.Empty)
                .Split(BetweenAddresses, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (asked.Count == 0)
            {
                return Results.BadRequest(new ProblemResponse(
                    "Ask for verses with refs=book:chapter:verse, separated by commas — "
                    + "refs=genesis:11:27,exodus:4:14. The book is its name, slug or abbreviation."));
            }

            if (asked.Count > MostAddresses)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"{asked.Count} addresses is more than this endpoint answers at once. Ask for at most "
                    + $"{MostAddresses}, or read the chapter with /v1/text/{{corpus}}/{{book}}/{{chapter}}."));
            }

            var wanted = new List<Address>(asked.Count);
            var unreadable = new List<string>();
            foreach (var one in asked)
            {
                if (Address.Parse(one) is { } address)
                {
                    wanted.Add(address);
                }
                else
                {
                    unreadable.Add(one);
                }
            }

            if (unreadable.Count > 0)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{unreadable[0]}\" is not an address. Write it as book:chapter:verse, as in "
                    + "genesis:11:27."));
            }

            var keys = wanted.Select(address => address.Key).Distinct().ToList();

            // One query for every address asked for, keyed on the same arithmetic the encyclopedia
            // orders verses by: a book never reaches a thousand chapters and a chapter never a
            // thousand verses, so the three numbers pack into one the database can match on.
            var rows = await db.Words
                .Where(w => w.TextId == text.Id
                            && keys.Contains((w.Verse!.Book!.CanonicalOrdinal * Address.BookStride)
                                             + (w.Verse.ChapterNumber * Address.ChapterStride)
                                             + w.Verse.Number))
                .OrderBy(w => w.Verse!.Book!.CanonicalOrdinal).ThenBy(w => w.Verse!.ChapterNumber)
                .ThenBy(w => w.Verse!.Number).ThenBy(w => w.Verse!.Label).ThenBy(w => w.Position)
                .Select(w => new
                {
                    Ordinal = w.Verse!.Book!.CanonicalOrdinal,
                    Chapter = w.Verse.ChapterNumber,
                    Verse = w.Verse.Number,
                    w.Verse.Label,
                    w.Surface,
                    w.Trailer,
                })
                .ToListAsync(cancellationToken);

            var found = rows
                .GroupBy(row => new { row.Ordinal, row.Chapter, row.Verse, row.Label })
                .Select(group => new VerseTextResponse(
                    new BookRefResponse(
                        group.Key.Ordinal,
                        BookReferences.Name(group.Key.Ordinal),
                        BookReferences.Slug(group.Key.Ordinal)),
                    group.Key.Chapter,
                    group.Key.Verse,
                    string.Concat(group.Select(row => row.Surface + row.Trailer)).Trim())
                {
                    Label = string.IsNullOrWhiteSpace(group.Key.Label) ? null : group.Key.Label,
                })
                .ToList();

            var answered = found
                .Select(verse => (verse.Book.Ordinal * Address.BookStride)
                                 + (verse.Chapter * Address.ChapterStride) + verse.Verse)
                .ToHashSet();

            return Results.Ok(new VerseTextListResponse(
                text.Slug,
                found,
                [.. wanted.Where(address => !answered.Contains(address.Key)).Select(address => address.Asked)]));
        });
    }

    /// <summary>One address as it was asked for, and as one number the database can match on.</summary>
    internal readonly record struct Address(string Asked, int Key)
    {
        internal const int ChapterStride = 1_000;

        internal const int BookStride = 1_000_000;

        public static Address? Parse(string asked)
        {
            var parts = asked.Split(WithinAddress);
            if (parts.Length != 3
                || BookReferences.ResolveOrdinal(parts[0]) is not { } ordinal
                || !int.TryParse(parts[1], out var chapter)
                || !int.TryParse(parts[2], out var verse)
                || chapter < 1
                || verse < 1)
            {
                return null;
            }

            return new Address(asked, (ordinal * BookStride) + (chapter * ChapterStride) + verse);
        }
    }
}
