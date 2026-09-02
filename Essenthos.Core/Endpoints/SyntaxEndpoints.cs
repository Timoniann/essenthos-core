using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The spans a text's own analysis names, and the words in them.
///
/// 986,830 of them have been sitting in the resources parsed and unreachable. The question they
/// answer — <em>every clause where this word is the predicate</em>, <em>what phrase is this word
/// part of and what is its function</em> — is one no free site answers, and it needs no new data.
/// </summary>
internal static class SyntaxEndpoints
{
    private const int MostPerPage = 100;

    private static readonly Dictionary<string, WordGroupKind> Kinds = Enum
        .GetValues<WordGroupKind>()
        .ToDictionary(EnumSpelling.Of, kind => kind);

    public static void MapSyntax(this IEndpointRouteBuilder routes)
    {
        // What a word is part of, innermost first, which is how a reader reads it: this word is the
        // subject of this phrase, in this clause, in this sentence.
        routes.MapGet("/words/{id:long}/syntax", async (
            long id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var groups = await db.WordGroupWords
                .Where(m => m.WordId == id)
                .Select(m => new
                {
                    m.WordGroup!.Id,
                    m.WordGroup.Kind,
                    m.WordGroup.Features,
                    Words = m.WordGroup.Words.Count,
                })
                .ToListAsync(cancellationToken);

            return groups.Count == 0
                ? Results.NotFound(new ProblemResponse(
                    $"Word {id} is in no group. Either it does not exist, or its text has no syntax loaded."))
                : Results.Ok(groups
                    .OrderBy(g => g.Words)
                    .Select(g => new SyntaxGroupResponse(
                        g.Id, EnumSpelling.Of(g.Kind), g.Words, Features(g.Features), null))
                    .ToList());
        });

        routes.MapGet("/syntax/{id:long}", async (
            long id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var group = await db.WordGroups
                .Where(g => g.Id == id)
                .Select(g => new
                {
                    g.Id,
                    g.Kind,
                    g.Features,
                    g.ParentId,
                    ParentKind = g.Parent == null ? (WordGroupKind?)null : g.Parent.Kind,
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (group is null)
            {
                return Results.NotFound(new ProblemResponse($"There is no word group {id}."));
            }

            var words = await db.WordGroupWords
                .Where(m => m.WordGroupId == id)
                .OrderBy(m => m.Word!.Verse!.Book!.Position)
                .ThenBy(m => m.Word!.Verse!.ChapterNumber)
                .ThenBy(m => m.Word!.Verse!.Number)
                .ThenBy(m => m.Word!.Position)
                .Select(m => new SyntaxWordResponse(
                    m.WordId,
                    m.Word!.Verse!.Book!.CanonicalOrdinal,
                    m.Word.Verse!.Book!.Name,
                    m.Word.Verse!.ChapterNumber,
                    m.Word.Verse!.Number,
                    m.Word.Position,
                    m.Word.Surface,
                    m.Word.Trailer,
                    m.Word.Gloss))
                .ToListAsync(cancellationToken);

            return Results.Ok(new SyntaxGroupResponse(
                group.Id, EnumSpelling.Of(group.Kind), words.Count, Features(group.Features), words)
            {
                ParentId = group.ParentId,
                ParentKind = group.ParentKind is { } kind ? EnumSpelling.Of(kind) : null,
            });
        });

        // The search the feature is named for. A feature is asked for as name:value — function:
        // Predicate, domain:Narrative — because the attributes differ per kind and a query
        // parameter per attribute would be a contract that changes whenever a witness does.
        routes.MapGet("/syntax", async (
            [FromQuery] string? kind,
            [FromQuery] string? feature,
            [FromQuery] string? corpus,
            [FromQuery] string? book,
            [FromQuery] int? chapter,
            [FromQuery] int? verse,
            [FromQuery] string? word,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var groups = db.WordGroups.AsQueryable();

            // Where to look, and what to look for in it. A search over 986,830 groups that can
            // only say "every phrase whose function is Predicate" is a search of the whole Bible
            // at once; the questions worth asking are about a passage, or about a word.
            if (book is { Length: > 0 })
            {
                if (BookReferences.ResolveOrdinal(book) is not { } ordinal)
                {
                    return ApiResults.NotFound(BookReferences.FormatHint(book));
                }

                groups = groups.Where(g => g.Words.Any(m =>
                    m.Word!.Verse!.Book!.CanonicalOrdinal == ordinal
                    && (chapter == null || m.Word.Verse.ChapterNumber == chapter)
                    && (verse == null || m.Word.Verse.Number == verse)));
            }
            else if (chapter != null || verse != null)
            {
                return Results.BadRequest(new ProblemResponse(
                    "A chapter or a verse needs a book to be in. Pass ?book= as well."));
            }

            // The word is matched on the same folded form the text search uses, so an unpointed
            // query finds a pointed word here exactly as it does there.
            if (word is { Length: > 0 })
            {
                var folded = WordFolding.Fold(word, "hbo");
                groups = groups.Where(g => g.Words.Any(m => m.Word!.NormalisedText == folded));
            }

            if (kind is { Length: > 0 })
            {
                // The column holds the spelling and the property holds the enum, so the name has to
                // be turned back into one here rather than compared as a string.
                if (!Kinds.TryGetValue(kind, out var wanted))
                {
                    return Results.BadRequest(new ProblemResponse(
                        $"\"{kind}\" is not a kind of word group. Try one of: {string.Join(", ", Kinds.Keys)}."));
                }

                groups = groups.Where(g => g.Kind == wanted);
            }

            if (corpus is { Length: > 0 })
            {
                groups = groups.Where(g => g.Text!.Slug == corpus);
            }

            if (feature is { Length: > 0 })
            {
                var at = feature.IndexOf(':');
                if (at <= 0 || at == feature.Length - 1)
                {
                    return Results.BadRequest(new ProblemResponse(
                        $"\"{feature}\" is not a feature. Write it as name:value, such as function:Predicate."));
                }

                var name = feature[..at];
                var value = feature[(at + 1)..];
                groups = groups.Where(g =>
                    g.Features != null && g.Features.RootElement.GetProperty(name).GetString() == value);
            }

            var total = await groups.CountAsync(cancellationToken);
            var page = await groups
                .OrderBy(g => g.Id)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 25, 1, MostPerPage))
                .Select(g => new { g.Id, g.Kind, g.Features, Words = g.Words.Count })
                .ToListAsync(cancellationToken);

            // A result that says only "phrase, 3 words, predicate" is not a result anybody can
            // read. The words themselves are what a search of a text is for, so the page's groups —
            // never more than a hundred — carry their own text and their own address.
            var ids = page.Select(g => g.Id).ToList();
            var previews = await db.WordGroupWords
                .Where(m => ids.Contains(m.WordGroupId))
                .OrderBy(m => m.Word!.Verse!.Book!.Position)
                .ThenBy(m => m.Word!.Verse!.ChapterNumber)
                .ThenBy(m => m.Word!.Verse!.Number)
                .ThenBy(m => m.Word!.Position)
                .Select(m => new
                {
                    m.WordGroupId,
                    Ordinal = m.Word!.Verse!.Book!.CanonicalOrdinal,
                    Chapter = m.Word.Verse!.ChapterNumber,
                    Verse = m.Word.Verse!.Number,
                    m.Word.Surface,
                    m.Word.Trailer,
                })
                .ToListAsync(cancellationToken);

            var byGroup = previews.GroupBy(row => row.WordGroupId).ToDictionary(g => g.Key, g => g.ToList());

            return Results.Ok(new SyntaxListResponse(total, page
                .Select(g =>
                {
                    var words = byGroup.GetValueOrDefault(g.Id, []);
                    var first = words.FirstOrDefault();
                    return new SyntaxGroupResponse(
                        g.Id, EnumSpelling.Of(g.Kind), g.Words, Features(g.Features), null)
                    {
                        Preview = words.Count == 0
                            ? null
                            : string.Concat(words.Select(w => w.Surface + w.Trailer)).Trim(),
                        Reference = first is null
                            ? null
                            : new VerseRefResponse(
                                first.Ordinal,
                                BookReferences.Name(first.Ordinal),
                                BookReferences.Slug(first.Ordinal),
                                first.Chapter,
                                first.Verse),
                    };
                })
                .ToList()));
        });
    }

    private static Dictionary<string, string>? Features(JsonDocument? features) =>
        features?.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty);
}

/// <param name="Words">
/// The words themselves, on a single group, and null on a list — a phrase is a handful of words and
/// a clause search is a hundred phrases, which is a different amount of Hebrew.
/// </param>
internal record SyntaxGroupResponse(
    long Id,
    string Kind,
    int WordCount,
    Dictionary<string, string>? Features,
    IList<SyntaxWordResponse>? Words)
{
    public long? ParentId { get; init; }

    /// <summary>What the containing group is, so a link to it is a fact rather than a number.</summary>
    public string? ParentKind { get; init; }

    /// <summary>The group's own words, run together, so a search result can be read as text.</summary>
    public string? Preview { get; init; }

    /// <summary>Where the group starts, so a search result can be opened in the reader.</summary>
    public VerseRefResponse? Reference { get; init; }
}

internal record SyntaxWordResponse(
    long Id,
    int BookOrdinal,
    string Book,
    int Chapter,
    int Verse,
    int Position,
    string Text,
    /// <summary>
    /// What follows the word in the text — a space, a maqqef, a verse mark. BHSA's words are
    /// morphemes, so joining them with a space writes בְּ רֵאשִׁית where the text has בְּרֵאשִׁית.
    /// </summary>
    string Trailer,
    string? Gloss);

internal record SyntaxListResponse(int Total, IList<SyntaxGroupResponse> Items);
