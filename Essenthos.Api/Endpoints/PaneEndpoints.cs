using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
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
            AppDbContext db,
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
            var others = requested.Skip(1).Select(entry => entry.Id).ToList();
            var held = ChapterReading.Held(ordinal, chapter).ToList();
            var books = held.Select(h => h.Book).Distinct().ToList();
            var chapters = held.Select(h => h.Chapter).Distinct().ToList();
            var stored = (await db.VerseStrengths.AsNoTracking()
                    .Where(s => s.ReferenceId == reference.Id && others.Contains(s.TextId)
                                && books.Contains(s.CanonicalBook) && chapters.Contains(s.CanonicalChapter))
                    .Select(s => new { s.TextId, s.CanonicalBook, s.CanonicalChapter, s.ByReading, s.Verses })
                    .ToListAsync(cancellationToken))
                .ToDictionary(s => (s.TextId, s.CanonicalBook, s.CanonicalChapter));

            return Results.Ok(new StrengthResponse(
                reference.Slug,
                requested.Skip(1).ToDictionary(entry => entry.Slug, entry =>
                {
                    var verses = new Dictionary<int, LinkStrengthResponse>();
                    var byReading = false;
                    foreach (var (heldBook, heldChapter, first, last, shift) in held)
                    {
                        if (stored.GetValueOrDefault((entry.Id, heldBook, heldChapter)) is { } row)
                        {
                            ChapterReading.Merge(verses, VerseStrengths.Unpack(row.Verses).ToDictionary(
                                v => v.Key, v => new LinkStrengthResponse(v.Value.Links, v.Value.Stated, v.Value.Confidence)), first, last, shift);
                            byReading |= row.ByReading;
                        }
                    }

                    return new PaneStrengthResponse(byReading ? ParallelEndpoints.PairedByItsOwnNumber : ParallelEndpoints.PairedThroughTheFrame, verses);
                })));
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
/// running API (a new corpus is a restart). The names are <see cref="EntityNames.Localised"/>'s with a renamed person's
/// heading (<see cref="RenamedHeadings"/>), so a record is called what the encyclopedia calls it in that language.
/// </summary>
internal sealed class RecordNames(IServiceScopeFactory scopes, IOptions<JsonOptions> json)
{
    private readonly PerRelease<string, (byte[] Body, string Tag)> _made = new((language, token) => Make(scopes, json, language, token), StringComparer.Ordinal);

    public Task<(byte[] Body, string Tag)> Of(string? language, CancellationToken cancellationToken) =>
        _made.Get(EntityNames.Local(language) ?? "eng", cancellationToken);

    private static async Task<(byte[] Body, string Tag)> Make(
        IServiceScopeFactory scopes, IOptions<JsonOptions> json, string language, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var names = await EntityNames.Localised(db, db.Entities.AsNoTracking(), language)
            .Select(e => new { e.Entity.Id, e.Entity.Slug, e.Shown })
            .ToListAsync(cancellationToken);

        // A person Scripture renames is called what his page heads him with (PRB-1770): Abraham's «Вказує на» line said
        // Аврам in Genesis 22, which prints Авраам.
        var persons = await db.Entities.AsNoTracking().Where(e => e.Kind == EntityKind.Person).Select(e => e.Id).ToListAsync(cancellationToken);
        var renamed = await RenamedHeadings.Of(db, persons, language, cancellationToken);
        var response = new RecordNamesResponse(language, names
            .OrderBy(n => n.Slug, StringComparer.Ordinal)
            .ToDictionary(
                n => n.Slug,
                n => renamed.TryGetValue(n.Id, out var heading) ? heading.LocalName ?? heading.Name : n.Shown,
                StringComparer.Ordinal));
        // With the options every other response is written with, so a name is sent as its letters and not escaped.
        var body = JsonSerializer.SerializeToUtf8Bytes(response, json.Value.SerializerOptions.GetTypeInfo(typeof(RecordNamesResponse)));
        return (body, $"\"{Convert.ToHexStringLower(SHA256.HashData(body))[..16]}\"");
    }
}
