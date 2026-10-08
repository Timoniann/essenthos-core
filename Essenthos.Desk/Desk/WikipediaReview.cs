using System.Text.Json.Nodes;

namespace Essenthos.Core.Desk;

/// <param name="Answer">One of the entry's items, or <c>none</c>; null to take the answer back.</param>
internal sealed record WikipediaAnswerRequest(string? Answer, string? Note);

/// <param name="Articles">The item's article titles by two-letter language.</param>
/// <param name="Evidence">What the item has in common with the record: <c>verse</c>, <c>kin</c>, <c>chapter</c>, <c>identification</c>.</param>
internal sealed record WikipediaOffer(
    string Qid,
    string Label,
    string? Description,
    IReadOnlyDictionary<string, string> Articles,
    IReadOnlyList<string> Evidence);

internal sealed record WikipediaDecision(string Answer, string? Note, string? At);

/// <param name="Record">The record's slug.</param>
/// <param name="Others">How many more items go by its names that have no article, so are not offered.</param>
internal sealed record WikipediaQuestion(
    string Record,
    string Kind,
    string Label,
    string? Line,
    IReadOnlyList<string> References,
    IReadOnlyList<WikipediaOffer> Candidates,
    int Others,
    WikipediaDecision? Decision);

/// <param name="Written">Whether a load has written the list yet; until one has there is nothing to answer.</param>
internal sealed record WikipediaQuestionsResponse(bool Written, int Open, int Answered, IReadOnlyList<WikipediaQuestion> Entries);

/// <summary>
/// The records whose Wikipedia article the corpus could not tell from a namesake's, which the owner
/// answers: the item each one is, or none of those listed. An answer is written into the list, which
/// every load reads back and links the record by, so it takes effect on the next load of the step.
/// </summary>
internal sealed class WikipediaReview(DeskPaths paths, ChangeLog log)
{
    public const string FileName = "wikipedia-matches.json";

    public const string Section = "wikipedia";

    /// <summary>The answer that says none of the items listed is the record.</summary>
    public const string None = "none";

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string File => Path.Combine(paths.Review, FileName);

    public bool Exists => System.IO.File.Exists(File);

    public WikipediaQuestionsResponse Read()
    {
        if (!Exists)
        {
            return new WikipediaQuestionsResponse(false, 0, 0, []);
        }

        var entries = Entries(JsonFiles.Read(File)).Select(Question).ToList();
        return new WikipediaQuestionsResponse(
            true, entries.Count(e => e.Decision is null), entries.Count(e => e.Decision is not null), entries);
    }

    public int Unanswered() => Exists ? Entries(JsonFiles.Read(File)).Count(e => e["decision"] is null) : 0;

    /// <summary>Answers one record or takes its answer back. Null where the list holds no such record or the answer is not one of its items.</summary>
    public async Task<WikipediaQuestion?> Answer(string slug, WikipediaAnswerRequest request)
    {
        if ((request.Note?.Length ?? 0) > ThingReview.LongestNote || !Exists)
        {
            return null;
        }

        await _gate.WaitAsync();
        WikipediaQuestion before;
        WikipediaQuestion after;
        try
        {
            var root = JsonFiles.Read(File);
            var entry = Entries(root).FirstOrDefault(e => e["record"]?.GetValue<string>() == slug);
            if (entry is null)
            {
                return null;
            }

            before = Question(entry);
            if (request.Answer is not null && request.Answer != None
                && !before.Candidates.Any(c => c.Qid == request.Answer))
            {
                return null;
            }

            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (request.Answer is null)
            {
                entry.Remove("decision");
            }
            else
            {
                entry["decision"] = new JsonObject { ["answer"] = request.Answer, ["note"] = note, ["at"] = JsonFiles.Now() };
            }

            JsonFiles.Write(File, root);
            after = Question(entry);
        }
        finally
        {
            _gate.Release();
        }

        await log.Append(
            Section,
            "match",
            $"{after.Kind}/{slug}",
            Logged(before),
            Logged(after),
            request.Note,
            "load",
            after.Label);
        return after;
    }

    /// <summary>What a change is logged as: the item chosen, with its label, or <c>none</c>.</summary>
    private static JsonNode? Logged(WikipediaQuestion question) =>
        question.Decision is null
            ? null
            : question.Decision.Answer == None
                ? JsonValue.Create(None)
                : JsonValue.Create(question.Candidates.FirstOrDefault(c => c.Qid == question.Decision.Answer) is { } chosen
                    ? $"{chosen.Qid} {chosen.Label}"
                    : question.Decision.Answer);

    private static IEnumerable<JsonObject> Entries(JsonNode root) =>
        (root["entries"]?.AsArray() ?? []).OfType<JsonObject>();

    private static WikipediaQuestion Question(JsonObject entry) => new(
        entry["record"]?.GetValue<string>() ?? string.Empty,
        entry["kind"]?.GetValue<string>() ?? string.Empty,
        entry["label"]?.GetValue<string>() ?? string.Empty,
        entry["line"]?.GetValue<string>(),
        Strings(entry["references"]),
        [.. (entry["candidates"]?.AsArray() ?? []).OfType<JsonObject>().Select(Offer)],
        entry["others"]?.GetValue<int>() ?? 0,
        entry["decision"] is JsonObject decision && decision["answer"]?.GetValue<string>() is { Length: > 0 } answer
            ? new WikipediaDecision(answer, decision["note"]?.GetValue<string>(), decision["at"]?.GetValue<string>())
            : null);

    private static WikipediaOffer Offer(JsonObject offer) => new(
        offer["qid"]?.GetValue<string>() ?? string.Empty,
        offer["label"]?.GetValue<string>() ?? string.Empty,
        offer["description"]?.GetValue<string>(),
        (offer["articles"] as JsonObject)?.ToDictionary(a => a.Key, a => a.Value?.GetValue<string>() ?? string.Empty)
        ?? new Dictionary<string, string>(),
        Strings(offer["evidence"]));

    private static List<string> Strings(JsonNode? node) =>
        [.. (node?.AsArray() ?? []).Select(n => n?.GetValue<string>()).OfType<string>()];
}
