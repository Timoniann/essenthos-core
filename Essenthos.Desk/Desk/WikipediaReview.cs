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

/// <param name="Qid">The item the matching tied the record to on its own.</param>
/// <param name="By">What tied it: <c>verse</c>, <c>kin</c>, <c>chapter</c>, <c>identification</c> or <c>name</c>.</param>
internal sealed record WikipediaTie(string Qid, string By);

/// <summary>What the agents' readings propose for a record, for the owner to take or leave.</summary>
/// <param name="Answer">One of the entry's items, or <c>none</c>.</param>
/// <param name="Confidence">The lower of the two readings' confidences: <c>high</c>, <c>medium</c> or <c>low</c>.</param>
/// <param name="Agreed">Whether two independent readings gave the same answer.</param>
/// <param name="Other">Where they did not, the second reading's answer.</param>
internal sealed record WikipediaProposal(
    string Answer,
    string Confidence,
    bool Agreed,
    string? Why,
    string? Other = null,
    string? OtherWhy = null);

/// <param name="Accepted">How many records took their proposal as the answer.</param>
internal sealed record WikipediaAcceptResponse(int Accepted);

/// <param name="Record">The record's slug.</param>
/// <param name="Addresses">Each reference as the reading API addresses it, in step with <paramref name="References"/>, for showing its text.</param>
/// <param name="Others">How many more items go by its names that have no article, so are not offered.</param>
internal sealed record WikipediaQuestion(
    string Record,
    string Kind,
    string Label,
    string? Line,
    IReadOnlyList<string> References,
    IReadOnlyList<string?> Addresses,
    IReadOnlyList<WikipediaOffer> Candidates,
    int Others,
    WikipediaDecision? Decision,
    WikipediaTie? Tied = null,
    WikipediaProposal? Proposal = null);

/// <param name="Written">Whether a load has written the list yet; until one has there is nothing to answer.</param>
/// <param name="Open">Records the matching could not decide that have no answer yet.</param>
/// <param name="Tied">Records the matching tied on its own that have no answer: the owner may leave them, set them to another item or to none.</param>
internal sealed record WikipediaQuestionsResponse(
    bool Written, int Open, int Answered, IReadOnlyList<WikipediaQuestion> Entries, int Tied = 0);

/// <summary>
/// The records whose Wikipedia article the corpus could not tell from a namesake's, which the owner
/// answers: the item each one is, or none of those listed; and the records the matching tied on its
/// own, which he may set to another item or take back. An answer is written into the list, which
/// every load reads back and links the record by, so it takes effect on the next load of the step.
/// </summary>
internal sealed class WikipediaReview(DeskPaths paths, ChangeLog log)
{
    public const string FileName = "wikipedia-matches.json";

    /// <summary>The agents' proposals beside the list: generated, never written by the console, read only to show and to accept.</summary>
    public const string ProposalsFileName = "wikipedia-proposals.json";

    /// <summary>The note an answer carries when it was taken from an agreed proposal in bulk.</summary>
    public const string AcceptedNote = "Пропозиція агентів: два незалежні читання згодні";

    public const string Section = "wikipedia";

    /// <summary>The answer that says none of the items listed is the record.</summary>
    public const string None = "none";

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string File => Path.Combine(paths.Review, FileName);

    public bool Exists => System.IO.File.Exists(File);

    private string ProposalsFile => Path.Combine(paths.Review, ProposalsFileName);

    public WikipediaQuestionsResponse Read()
    {
        if (!Exists)
        {
            return new WikipediaQuestionsResponse(false, 0, 0, []);
        }

        var proposals = Proposals();
        var entries = Entries(JsonFiles.Read(File)).Select(e => Question(e, proposals)).ToList();
        return new WikipediaQuestionsResponse(
            true,
            entries.Count(e => e.Decision is null && e.Tied is null),
            entries.Count(e => e.Decision is not null),
            entries,
            entries.Count(e => e.Decision is null && e.Tied is not null));
    }

    public int Unanswered() => Exists ? Entries(JsonFiles.Read(File)).Count(e => e["decision"] is null && e["tied"] is null) : 0;

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
            var proposals = Proposals();
            var root = JsonFiles.Read(File);
            var entry = Entries(root).FirstOrDefault(e => e["record"]?.GetValue<string>() == slug);
            if (entry is null)
            {
                return null;
            }

            before = Question(entry, proposals);
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
            after = Question(entry, proposals);
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

    /// <summary>
    /// Takes the proposal as the answer of every open record whose two readings agree with high
    /// confidence. A record the matching tied on its own, or that already has an answer, is left for
    /// the owner to look at one by one.
    /// </summary>
    public async Task<WikipediaAcceptResponse> AcceptAgreed()
    {
        if (!Exists)
        {
            return new WikipediaAcceptResponse(0);
        }

        var accepted = new List<WikipediaQuestion>();
        await _gate.WaitAsync();
        try
        {
            var proposals = Proposals();
            var root = JsonFiles.Read(File);
            foreach (var entry in Entries(root))
            {
                if (Question(entry, proposals) is { Decision: null, Tied: null, Proposal: { Agreed: true, Confidence: "high" } proposal })
                {
                    entry["decision"] = new JsonObject { ["answer"] = proposal.Answer, ["note"] = AcceptedNote, ["at"] = JsonFiles.Now() };
                    accepted.Add(Question(entry, proposals));
                }
            }

            if (accepted.Count > 0)
            {
                JsonFiles.Write(File, root);
            }
        }
        finally
        {
            _gate.Release();
        }

        foreach (var after in accepted)
        {
            await log.Append(Section, "match", $"{after.Kind}/{after.Record}", null, Logged(after), AcceptedNote, "load", after.Label);
        }

        return new WikipediaAcceptResponse(accepted.Count);
    }

    /// <summary>The proposals by record slug; empty where none have been written.</summary>
    private Dictionary<string, JsonObject> Proposals()
    {
        var proposals = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (System.IO.File.Exists(ProposalsFile))
        {
            foreach (var proposal in Entries(JsonFiles.Read(ProposalsFile)))
            {
                if (proposal["record"]?.GetValue<string>() is { Length: > 0 } slug)
                {
                    proposals[slug] = proposal;
                }
            }
        }

        return proposals;
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

    private static WikipediaQuestion Question(JsonObject entry, IReadOnlyDictionary<string, JsonObject> proposals)
    {
        var references = Strings(entry["references"]);
        var candidates = (entry["candidates"]?.AsArray() ?? []).OfType<JsonObject>().Select(Offer).ToList();
        var record = entry["record"]?.GetValue<string>() ?? string.Empty;
        return new WikipediaQuestion(
            record,
            entry["kind"]?.GetValue<string>() ?? string.Empty,
            entry["label"]?.GetValue<string>() ?? string.Empty,
            entry["line"]?.GetValue<string>(),
            references,
            [.. references.Select(Desk.Addresses.Of)],
            candidates,
            entry["others"]?.GetValue<int>() ?? 0,
            entry["decision"] is JsonObject decision && decision["answer"]?.GetValue<string>() is { Length: > 0 } answer
                ? new WikipediaDecision(answer, decision["note"]?.GetValue<string>(), decision["at"]?.GetValue<string>())
                : null,
            entry["tied"] is JsonObject tied && tied["qid"]?.GetValue<string>() is { Length: > 0 } qid
                ? new WikipediaTie(qid, tied["by"]?.GetValue<string>() ?? string.Empty)
                : null,
            proposals.TryGetValue(record, out var proposal) ? Proposal(proposal, candidates) : null);
    }

    /// <summary>A proposal, where its answer is still one the entry offers; one a later load no longer offers is dropped.</summary>
    private static WikipediaProposal? Proposal(JsonObject proposal, IReadOnlyList<WikipediaOffer> candidates)
    {
        bool Offered(string? answer) => answer == None || candidates.Any(c => c.Qid == answer);

        var answer = proposal["answer"]?.GetValue<string>();
        if (answer is null || !Offered(answer))
        {
            return null;
        }

        var other = proposal["other"]?.GetValue<string>();
        return new WikipediaProposal(
            answer,
            proposal["confidence"]?.GetValue<string>() ?? "low",
            proposal["agreed"]?.GetValue<bool>() ?? false,
            proposal["why"]?.GetValue<string>(),
            other is not null && Offered(other) ? other : null,
            proposal["otherWhy"]?.GetValue<string>());
    }

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
