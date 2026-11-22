using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>A lexicon beside Strong's, and whose it is.</summary>
/// <param name="Id">How the articles name it: <c>bdb</c>, <c>abbott-smith</c>, <c>lsj</c>.</param>
internal record LexiconResponse(
    string Id,
    string Name,
    string Author,
    string? Licence,
    string? LicenceUrl,
    string Url);

/// <param name="Ordinal">Its place among this lexicon's articles under the number, from 1.</param>
/// <param name="Entry">The lexicon's own identifier for the article.</param>
/// <param name="Status">How far the digital edition has got with it, in the edition's own word, where it says (BDB: <c>base</c>, <c>ref</c>, <c>done</c>).</param>
/// <param name="Body">The article as blocks of marked text, in the shape a commentary's body has.</param>
internal record LexiconArticleResponse(
    string Dictionary,
    int Ordinal,
    string Entry,
    string Headword,
    string? Status,
    JsonElement Body);

/// <param name="Dictionaries">The lexicons the articles come from, in the order a page shows them.</param>
internal record StrongDictionariesResponse(
    string StrongNumber,
    IList<LexiconResponse> Dictionaries,
    IList<LexiconArticleResponse> Articles);

/// <summary>
/// The scholarly lexicons' articles on a Strong number. Asked apart from the entry: an article of BDB or LSJ runs to tens
/// of kilobytes, which the entry and the page composed from it should not carry for every reader who never opens it.
/// </summary>
internal static class LexiconEndpoints
{
    public static void MapLexicons(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/strong/{number}/dictionaries", async (string number, AppDbContext db, CancellationToken cancellationToken) =>
        {
            if (StrongNumbers.Normalize(number) is not { } canonical)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{number}\" is not a Strong number. Write a language letter and digits, as H430 or G26."));
            }

            return Results.Ok(await Articles(db, canonical, cancellationToken));
        });
    }

    internal static async Task<StrongDictionariesResponse> Articles(AppDbContext db, string canonical, CancellationToken cancellationToken)
    {
        var rows = await db.LexiconArticles
            .Where(a => a.StrongNumber == canonical)
            .Select(a => new { a.Dictionary, a.Ordinal, a.Entry, a.Headword, a.Status, a.Body })
            .ToListAsync(cancellationToken);

        var order = LexiconWorks.All.Select((work, index) => (work.Id, index)).ToDictionary(w => w.Id, w => w.index, StringComparer.Ordinal);
        var articles = rows
            .Where(row => order.ContainsKey(row.Dictionary))
            .OrderBy(row => order[row.Dictionary])
            .ThenBy(row => row.Ordinal)
            .Select(row =>
            {
                using var body = JsonDocument.Parse(row.Body);
                return new LexiconArticleResponse(row.Dictionary, row.Ordinal, row.Entry, row.Headword, row.Status, body.RootElement.Clone());
            })
            .ToList();

        return new StrongDictionariesResponse(
            canonical,
            [
                .. LexiconWorks.All
                    .Where(work => articles.Any(a => a.Dictionary == work.Id))
                    .Select(work =>
                    {
                        var dataset = Array.Find(Datasets.All, d => d.Id == work.Dataset)!;
                        return new LexiconResponse(work.Id, dataset.Name, dataset.Author, dataset.Licence, dataset.LicenceUrl, dataset.Url);
                    }),
            ],
            articles);
    }
}
