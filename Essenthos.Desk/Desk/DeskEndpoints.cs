using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Desk;

/// <param name="Count">How many are waiting, or null where it could not be counted.</param>
/// <param name="Problem">Why it could not be counted, in a sentence.</param>
internal sealed record SummaryCount(string Key, int? Count, string? Problem);

/// <param name="Waiting">How many changes wait on each step that applies them, from the change log.</param>
internal sealed record SummaryResponse(IReadOnlyList<SummaryCount> Counts, IReadOnlyDictionary<string, int> Waiting);

internal sealed record ProblemResponse(string Problem);

internal sealed record TextReaderRequest(string? Note);

/// <param name="ReaderTranslation">The translation a new reader now opens in.</param>
internal sealed record TextReaderResponse(string ReaderTranslation);

/// <summary>The console's endpoints, one group per section of the page.</summary>
internal static class DeskEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>
    /// How much is waiting on the owner in each section. Each count stands alone, so one that cannot
    /// be read — avioniq missing, the corpus not loaded — says so without hiding the others.
    /// </summary>
    /// <summary>
    /// How much is waiting on the owner in each section, and how many of his changes wait on a step
    /// that applies them. Each count stands alone, so one that cannot be read — the corpus not
    /// loaded, a list missing — says so without hiding the others.
    /// </summary>
    public static void MapSummary(this RouteGroupBuilder routes) =>
        routes.MapGet("/summary", async (
            RelationshipReview relationships,
            ThingReview things,
            PortraitBoard portraits,
            ChangeLog log,
            CancellationToken cancellationToken) =>
        {
            var counts = new List<SummaryCount>
            {
                Count("relationships", () => relationships.Exists ? relationships.Undecided() : null,
                    "The relationship list is not in the review folder."),
                Count("occurrences", () => things.Exists ? things.Unanswered() : null,
                    "There is no list of open questions in the review folder."),
                Count("records", () => things.Unreviewed(), null),
            };

            try
            {
                var people = (await portraits.List(cancellationToken)).People;
                counts.Add(new SummaryCount("portraits", people.Count(p => p.Waiting), null));
            }
            catch (Exception exception) when (exception is Npgsql.NpgsqlException or InvalidOperationException)
            {
                counts.Add(new SummaryCount("portraits", null,
                    "The corpus could not be read. Start core-db, and load the corpus if it is empty."));
            }

            var changes = log.Read();
            counts.Add(new SummaryCount("history", changes.Waiting.Values.Sum(), null));
            return new SummaryResponse(counts, changes.Waiting);
        });

    public static void MapHistory(this RouteGroupBuilder routes) =>
        routes.MapGet("/history", (ChangeLog log) => log.Read());

    public static void MapSettings(this RouteGroupBuilder routes)
    {
        routes.MapGet("/settings", (SiteSwitches switches) => switches.Read());

        routes.MapPut("/settings/licence", async (LicenceRequest request, SiteSwitches switches) =>
            await switches.SetLicence(request) is { } set
                ? Results.Ok(set)
                : Results.UnprocessableEntity(new ProblemResponse(
                    "There is no such licence to choose; pick one of those listed, or undecided.")));

        routes.MapPut("/settings/{key}", async (string key, SiteSwitchRequest request, SiteSwitches switches) =>
            await switches.Set(key, request) is { } set
                ? Results.Ok(set)
                : NotThere("There is no such switch for the site."));
    }

    public static void MapRelationshipReview(this RouteGroupBuilder routes)
    {
        routes.MapGet("/review/relationships", (RelationshipReview review) =>
            review.Exists
                ? Results.Json(review.ReadForPage(), DeskJsonContext.Default.JsonNode)
                : NotThere("The relationship list is not in the review folder."));

        routes.MapPut("/review/relationships/{key}", async (string key, RelationshipDecisionRequest request, RelationshipReview review) =>
            await review.Decide(key, request) is { } decided
                ? Results.Ok(decided)
                : Results.UnprocessableEntity(new ProblemResponse(
                    "That fact is not in the list, or the decision is not one of the four. Reload the list and decide again.")));

        routes.MapPost("/review/relationships/bulk", async (RelationshipBulkRequest request, RelationshipReview review) =>
            await review.DecideAll(request) is { } decided
                ? Results.Ok(decided)
                : Results.UnprocessableEntity(new ProblemResponse("The decision is not one of the four.")));
    }

    public static void MapThingReview(this RouteGroupBuilder routes)
    {
        routes.MapGet("/review/occurrences", (ThingReview review) =>
            review.Exists
                ? Results.Ok(review.ReadQuestions())
                : NotThere("The list of objects and appointed times is not in the review folder."));

        routes.MapPut("/review/occurrences/{key}", async (string key, ThingAnswerRequest request, ThingReview review) =>
            await review.Answer(key, request) is { } answered
                ? Results.Ok(answered)
                : Results.UnprocessableEntity(new ProblemResponse(
                    "That question is not in the list, or the answer is not one of its options. Reload the list and answer again.")));

        routes.MapGet("/review/records", (ThingReview review) => review.ReadRecords());

        routes.MapPut("/review/records/{slug}", async (string slug, ThingRecordRequest request, ThingReview review) =>
            await review.Review(slug, request) is { } reviewed
                ? Results.Ok(reviewed)
                : Results.UnprocessableEntity(new ProblemResponse("There is no such record, or no such kind of review.")));
    }

    public static void MapPortraits(this RouteGroupBuilder routes)
    {
        routes.MapGet("/portraits", (PortraitBoard board, CancellationToken cancellationToken) => board.List(cancellationToken));

        routes.MapGet("/portraits/{slug}", async (string slug, PortraitBoard board, CancellationToken cancellationToken) =>
            await board.Detail(slug, cancellationToken) is { } detail
                ? Results.Ok(detail)
                : NotThere("There is no such person in the corpus."));

        routes.MapPut("/portraits/{slug}/brief/{field}", async (
                string slug, string field, BriefFieldRequest request, PortraitEditor editor, CancellationToken cancellationToken) =>
            Changed(await editor.SetField(slug, field, request, cancellationToken)));

        routes.MapPut("/portraits/{slug}/status", async (
                string slug, PortraitStatusRequest request, PortraitEditor editor, CancellationToken cancellationToken) =>
            Changed(await editor.SetStatus(slug, request, cancellationToken)));

        routes.MapPut("/portraits/{slug}/review", async (
                string slug, PortraitReviewRequest request, PortraitEditor editor, CancellationToken cancellationToken) =>
            Changed(await editor.SetReview(slug, request, cancellationToken)));

        routes.MapPut("/portraits/{slug}/answer", async (
                string slug, PortraitAnswerRequest request, PortraitEditor editor, CancellationToken cancellationToken) =>
            Changed(await editor.Answer(slug, request, cancellationToken)));

        // The picture as the request's body, so nothing but its bytes crosses: no form, no field names.
        routes.MapPost("/portraits/{slug}/upload", async (
                string slug, string? name, bool? glory, string? note, HttpRequest request, PortraitEditor editor,
                CancellationToken cancellationToken) =>
            request.ContentLength > PortraitEditor.LargestUpload
                ? Results.UnprocessableEntity(new ProblemResponse("The picture is larger than 30 MB."))
                : Changed(await editor.Upload(slug, name, glory == true, note, request.Body, cancellationToken)));

        routes.MapGet("/pictures", (PortraitBoard board, CancellationToken cancellationToken) => board.Pictured(cancellationToken));

        routes.MapGet("/pictures/{slug}", async (string slug, PortraitBoard board, CancellationToken cancellationToken) =>
            await board.Pictures(slug, cancellationToken) is { } set
                ? Results.Ok(set)
                : NotThere("There is no such person or place in the corpus."));

        routes.MapPut("/pictures/{slug}/choice", async (
            string slug, PictureChoiceRequest request, PictureChoices choices, PortraitBoard board,
            CancellationToken cancellationToken) =>
        {
            if (await board.Pictures(slug, cancellationToken) is not { } before)
            {
                return NotThere("There is no such person or place in the corpus.");
            }

            return await choices.Choose(slug, request, before.Entity.LocalName ?? before.Entity.Name) is null
                ? Results.UnprocessableEntity(new ProblemResponse("That picture is not under the images folder, or the caption is too long."))
                : Results.Ok(await board.Pictures(slug, cancellationToken));
        });

        // The pictures themselves, from the corpus's images folder and nowhere above it.
        routes.MapGet("/images/{**file}", (string file, DeskPaths paths) =>
            DeskPaths.Under(paths.Images, file) is { } path && File.Exists(path)
            && ContentTypes.TryGetContentType(path, out var type) && type.StartsWith("image/", StringComparison.Ordinal)
                ? Results.File(path, type)
                : NotThere("There is no such picture in the images folder."));
    }

    private static IResult Changed(PortraitChange change) =>
        change.Detail is { } detail ? Results.Ok(detail) : Results.UnprocessableEntity(new ProblemResponse(change.Problem!));

    public static void MapOperations(this RouteGroupBuilder routes)
    {
        routes.MapGet("/operations", (Operations operations, CancellationToken cancellationToken) => operations.Read(cancellationToken));

        routes.MapPost("/operations/{name}/runs", async (string name, Operations operations, CancellationToken cancellationToken) =>
        {
            var started = await operations.Start(name, cancellationToken);
            return started.Run is null ? Results.Conflict(new ProblemResponse(started.Problem!)) : Results.Ok(started);
        });

        routes.MapGet("/operations/runs/{id}", (string id, int? from, Operations operations) =>
            operations.Log(id, from ?? 0) is { } log ? Results.Ok(log) : NotThere("There is no such run; the console remembers the last twenty."));
    }

    public static void MapTexts(this RouteGroupBuilder routes)
    {
        routes.MapGet("/texts", (TextBoard board, AppDbContext db, CancellationToken cancellationToken) =>
            board.List(db, cancellationToken));

        routes.MapPost("/texts/census", async (TextBoard board, AppDbContext db, CancellationToken cancellationToken) =>
        {
            board.Recount();
            return await board.List(db, cancellationToken);
        });

        routes.MapGet("/texts/problems", async (TextProblems problems, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var texts = await db.Texts.AsNoTracking().ToListAsync(cancellationToken);
            return await problems.List([.. texts.Select(TextProblems.Named)], cancellationToken);
        });

        routes.MapGet("/texts/{slug}/verification", async (string slug, AppDbContext db, CancellationToken cancellationToken) =>
            await TextBoard.Verification(db, slug, cancellationToken) is { } verification
                ? Results.Ok(verification)
                : NotThere("There is no such text in the corpus."));

        // The translation a reader opens in: one of the site's choices, which only a translation can be.
        routes.MapPut("/texts/{slug}/reader", async (
            string slug, TextReaderRequest request, SiteSwitches switches, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var text = await db.Texts.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);
            if (text is null)
            {
                return NotThere("There is no such text in the corpus.");
            }

            if (text.Kind != TextKind.Translation)
            {
                return Results.UnprocessableEntity(new ProblemResponse(
                    "The reader opens with a translation beside the original; this text is not a translation."));
            }

            await switches.Choose(SiteSettings.ReaderTranslation, text.Slug, TextsSection, text.Name, request.Note);
            return Results.Ok(new TextReaderResponse(text.Slug));
        });
    }

    /// <summary>
    /// Where the site runs, and the runs that move code and corpus there. A run is started only for
    /// what the page showed and the owner confirmed; production also wants its name typed.
    /// </summary>
    public static void MapDeployment(this RouteGroupBuilder routes)
    {
        routes.MapGet("/deploy", (Deployment deployment, CancellationToken cancellationToken) => deployment.Read(cancellationToken));

        routes.MapPost("/deploy/runs", async (DeployRequest request, Deployment deployment, CancellationToken cancellationToken) =>
        {
            var started = await deployment.Start(request, cancellationToken);
            return started.Run is null ? Results.Conflict(new ProblemResponse(started.Problem!)) : Results.Ok(started);
        });
    }

    /// <summary>The section a change made among the texts is logged under.</summary>
    private const string TextsSection = "texts";

    private static SummaryCount Count(string key, Func<int?> count, string? absent)
    {
        try
        {
            return count() is { } value ? new SummaryCount(key, value, null) : new SummaryCount(key, null, absent);
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            return new SummaryCount(key, null, $"The file could not be read: {exception.Message}");
        }
    }

    private static IResult NotThere(string problem) => Results.NotFound(new ProblemResponse(problem));
}
