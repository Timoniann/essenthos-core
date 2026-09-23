using System.Text.Json.Nodes;
using Microsoft.AspNetCore.StaticFiles;

namespace Essenthos.Core.Desk;

/// <param name="Count">How many are waiting, or null where it could not be counted.</param>
/// <param name="Problem">Why it could not be counted, in a sentence.</param>
internal sealed record SummaryCount(string Key, int? Count, string? Problem);

internal sealed record SummaryResponse(IReadOnlyList<SummaryCount> Counts);

internal sealed record ProblemResponse(string Problem);

/// <summary>The console's endpoints, one group per section of the page.</summary>
internal static class DeskEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>
    /// How much is waiting on the owner in each section. Each count stands alone, so one that cannot
    /// be read — avioniq missing, the corpus not loaded — says so without hiding the others.
    /// </summary>
    public static void MapSummary(this RouteGroupBuilder routes) =>
        routes.MapGet("/summary", async (
            QuestionBoard board,
            RelationshipReview relationships,
            ThingReview things,
            PortraitBoard portraits,
            CancellationToken cancellationToken) =>
        {
            var counts = new List<SummaryCount>();
            var questions = await board.Read(cancellationToken);
            counts.Add(questions.Available
                ? new SummaryCount("questions", questions.Questions.Count, null)
                : new SummaryCount("questions", null, questions.Problem));
            counts.Add(questions.Available
                ? new SummaryCount("sign-offs", questions.SignOffs.Count, null)
                : new SummaryCount("sign-offs", null, questions.Problem));

            counts.Add(Count("relationships", () => relationships.Exists ? relationships.Undecided() : null,
                "The relationship list is not in the review folder."));
            counts.Add(Count("occurrences", () => things.Exists ? things.Unanswered() : null,
                "The list of objects and appointed times is not in the review folder."));
            counts.Add(Count("records", () => things.Unreviewed(), null));

            try
            {
                var people = (await portraits.List(cancellationToken)).People;
                var first = people.Where(p => p.Tier == 1 && !p.NeverPictured).ToList();
                counts.Add(new SummaryCount("portraits", first.Count(p => !p.GeneratedListed), null));
                counts.Add(new SummaryCount("briefs", first.Count(p => !p.Brief), null));
            }
            catch (Exception exception) when (exception is Npgsql.NpgsqlException or InvalidOperationException)
            {
                const string problem = "The corpus could not be read. Start core-db, and load the corpus if it is empty.";
                counts.Add(new SummaryCount("portraits", null, problem));
                counts.Add(new SummaryCount("briefs", null, problem));
            }

            return new SummaryResponse(counts);
        });

    public static void MapQuestions(this RouteGroupBuilder routes)
    {
        routes.MapGet("/questions", (QuestionBoard board, CancellationToken cancellationToken) => board.Read(cancellationToken));

        routes.MapPost("/questions/answer", async (AnswerRequest request, QuestionBoard board, CancellationToken cancellationToken) =>
        {
            var answered = await board.Answer(request, cancellationToken);
            return answered.Answered ? Results.Ok(answered) : Results.UnprocessableEntity(answered);
        });
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

        // The pictures themselves, from the corpus's images folder and nowhere above it.
        routes.MapGet("/images/{**file}", (string file, DeskPaths paths) =>
            DeskPaths.Under(paths.Images, file) is { } path && File.Exists(path)
            && ContentTypes.TryGetContentType(path, out var type) && type.StartsWith("image/", StringComparison.Ordinal)
                ? Results.File(path, type)
                : NotThere("There is no such picture in the images folder."));
    }

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
