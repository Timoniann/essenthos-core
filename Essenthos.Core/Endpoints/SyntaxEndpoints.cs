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
                .Select(g => new { g.Id, g.Kind, g.Features, g.ParentId })
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
                    m.Word.Gloss))
                .ToListAsync(cancellationToken);

            return Results.Ok(new SyntaxGroupResponse(
                group.Id, EnumSpelling.Of(group.Kind), words.Count, Features(group.Features), words)
            {
                ParentId = group.ParentId,
            });
        });

        // The search the feature is named for. A feature is asked for as name:value — function:
        // Predicate, domain:Narrative — because the attributes differ per kind and a query
        // parameter per attribute would be a contract that changes whenever a witness does.
        routes.MapGet("/syntax", async (
            [FromQuery] string? kind,
            [FromQuery] string? feature,
            [FromQuery] string? corpus,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var groups = db.WordGroups.AsQueryable();

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

            return Results.Ok(new SyntaxListResponse(total, page
                .Select(g => new SyntaxGroupResponse(
                    g.Id, EnumSpelling.Of(g.Kind), g.Words, Features(g.Features), null))
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
}

internal record SyntaxWordResponse(
    long Id,
    int BookOrdinal,
    string Book,
    int Chapter,
    int Verse,
    int Position,
    string Text,
    string? Gloss);

internal record SyntaxListResponse(int Total, IList<SyntaxGroupResponse> Items);
