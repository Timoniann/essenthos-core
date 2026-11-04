using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Essenthos.Core.Endpoints;

/// <param name="Alignment">
/// How this text's verses meet the reference text's at a row: <c>original-verse</c> through the shared frame, or
/// <c>verse-number</c> where only a reading of where each verse falls joins them (as <c>/v1/parallel</c> says).
/// </param>
/// <param name="Verses">By row, how strongly this text's verse is linked to the reference text's; a row with no link is absent.</param>
internal record PaneStrengthResponse(string Alignment, Dictionary<int, LinkStrengthResponse> Verses);

/// <param name="Reference">The text the others are measured against: the first one asked for.</param>
/// <param name="Texts">Every other text asked for that the corpus holds.</param>
internal record StrengthResponse(string Reference, Dictionary<string, PaneStrengthResponse> Texts);

/// <param name="Language">The language the names are in, as the corpus spells it.</param>
/// <param name="Names">Every record's name in that language, by slug: its own name there, or its English headword where it has none.</param>
internal record RecordNamesResponse(string Language, Dictionary<string, string> Names);

/// <summary>
/// The parallel view from precomputed panes (DOC-0224 §12): one text's side of one canonical chapter per request, sent
/// as it is stored; how strongly each text's verses are linked to the reference text's, which depends on the pair and
/// is asked for apart; and the names of every record in a language, which the panes leave out.
/// </summary>
internal static class PaneEndpoints
{
    private const string Gzip = "gzip";

    /// <summary>A pane changes only with a new corpus build; a reader revalidates after this long, by the entity tag.</summary>
    private const string Cached = "public, max-age=300";

    public static void MapPanes(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/panes/{text}/{book}/{chapter:int}", async (
            string text,
            string book,
            int chapter,
            HttpContext context,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            if (BookReferences.ResolveOrdinal(book) is not { } ordinal)
            {
                return ApiResults.NotFound(BookReferences.FormatHint(book));
            }

            if (await canon.Text(text, cancellationToken) is not { } entry)
            {
                return ApiResults.NotFound($"No text is called {text}. Ask /v1/corpora for the ones this corpus holds.");
            }

            var pane = await db.Panes.AsNoTracking()
                .Where(p => p.TextId == entry.Id && p.CanonicalBook == ordinal && p.CanonicalChapter == chapter)
                .Select(p => new { p.Body, p.Hash })
                .FirstOrDefaultAsync(cancellationToken);
            if (pane is null)
            {
                return ApiResults.NotFound($"{entry.Slug} has nothing at {BookReferences.Name(ordinal)} {chapter} in the shared numbering.");
            }

            var tag = $"\"{pane.Hash}\"";
            var response = context.Response;
            response.Headers.ETag = tag;
            response.Headers.CacheControl = Cached;
            response.Headers.Vary = HeaderNames.AcceptEncoding;
            if (context.Request.Headers.IfNoneMatch.Any(asked => asked == tag))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            if (AcceptsGzip(context.Request))
            {
                response.Headers.ContentEncoding = Gzip;
                return Results.Bytes(pane.Body, "application/json");
            }

            return Results.Bytes(Panes.Unpack(pane.Body), "application/json");
        });

        routes.MapGet("/strength/{book}/{chapter:int}", async (
            string book,
            int chapter,
            string? texts,
            ICanonIndex canon,
            IServiceScopeFactory scopes,
            CancellationToken cancellationToken) =>
        {
            if (BookReferences.ResolveOrdinal(book) is not { } ordinal)
            {
                return ApiResults.NotFound(BookReferences.FormatHint(book));
            }

            var requested = new List<TextEntry>();
            foreach (var slug in (texts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (await canon.Text(slug, cancellationToken) is { } entry && requested.All(r => r.Id != entry.Id))
                {
                    requested.Add(entry);
                }
            }

            if (requested.Count == 0)
            {
                return ApiResults.Malformed("Name the texts in the texts query as a comma-separated list, the reference text first.");
            }

            if (requested.Count > ParallelEndpoints.MostCorporaAtOnce)
            {
                return ApiResults.Malformed($"At most {ParallelEndpoints.MostCorporaAtOnce} texts can be read side by side; {requested.Count} were asked for.");
            }

            var reference = requested[0];
            var measured = new ConcurrentDictionary<string, PaneStrengthResponse>();
            // Each text against the reference on a context of its own, all at once: the reader waits for the slowest pair only.
            await Task.WhenAll(requested.Skip(1).Select(async entry =>
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                measured[entry.Slug] = await ParallelEndpoints.Strength(db, entry.Id, reference.Id, ordinal, chapter, cancellationToken);
            }));

            return Results.Ok(new StrengthResponse(
                reference.Slug,
                requested.Skip(1).ToDictionary(entry => entry.Slug, entry => measured[entry.Slug])));
        });

        routes.MapGet("/record-names", async (
            string? language,
            HttpContext context,
            RecordNames names,
            CancellationToken cancellationToken) =>
        {
            var (body, tag) = await names.Of(language, cancellationToken);
            var response = context.Response;
            response.Headers.ETag = tag;
            response.Headers.CacheControl = Cached;
            if (context.Request.Headers.IfNoneMatch.Any(asked => asked == tag))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return Results.Bytes(body, "application/json");
        });
    }

    private static bool AcceptsGzip(HttpRequest request) =>
        request.Headers.AcceptEncoding
            .SelectMany(value => (value ?? "").Split(','))
            .Select(part => part.Split(';'))
            .Any(part => part[0].Trim().Equals(Gzip, StringComparison.OrdinalIgnoreCase)
                         && !part.Skip(1).Any(q => q.Trim().Replace(" ", "") is "q=0" or "q=0.0" or "q=0.00" or "q=0.000"));
}

/// <summary>
/// Every record's name in a language, by slug, made once per language and kept: the corpus does not change under a
/// running API (a new corpus is a restart). The names are <see cref="EntityNames.Localised"/>'s, so a record is called
/// what the encyclopedia calls it in that language.
/// </summary>
internal sealed class RecordNames(IServiceScopeFactory scopes, IOptions<JsonOptions> json)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<(byte[] Body, string Tag)>>> _made = new(StringComparer.Ordinal);

    public Task<(byte[] Body, string Tag)> Of(string? language, CancellationToken cancellationToken)
    {
        var key = EntityNames.Local(language) ?? "eng";
        var made = _made.GetOrAdd(key, _ => new Lazy<Task<(byte[], string)>>(() => Make(key, CancellationToken.None)));
        return Wait(key, made, cancellationToken);
    }

    /// <summary>A dictionary that failed to be made is forgotten, so the next request tries again.</summary>
    private async Task<(byte[] Body, string Tag)> Wait(string key, Lazy<Task<(byte[] Body, string Tag)>> made, CancellationToken cancellationToken)
    {
        try
        {
            return await made.Value.WaitAsync(cancellationToken);
        }
        catch when (made.Value.IsFaulted)
        {
            _made.TryRemove(new KeyValuePair<string, Lazy<Task<(byte[] Body, string Tag)>>>(key, made));
            throw;
        }
    }

    private async Task<(byte[] Body, string Tag)> Make(string language, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var names = await EntityNames.Localised(db, db.Entities.AsNoTracking(), language)
            .Select(e => new { e.Entity.Slug, e.Shown })
            .ToListAsync(cancellationToken);
        var response = new RecordNamesResponse(language, names
            .OrderBy(n => n.Slug, StringComparer.Ordinal)
            .ToDictionary(n => n.Slug, n => n.Shown, StringComparer.Ordinal));
        // With the options every other response is written with, so a name is sent as its letters and not escaped.
        var body = JsonSerializer.SerializeToUtf8Bytes(response, json.Value.SerializerOptions.GetTypeInfo(typeof(RecordNamesResponse)));
        return (body, $"\"{Convert.ToHexStringLower(SHA256.HashData(body))[..16]}\"");
    }
}
