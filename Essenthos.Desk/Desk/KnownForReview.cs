using System.Text.Json.Nodes;

namespace Essenthos.Core.Desk;

/// <param name="Answer"><c>approved</c>, <c>rejected</c>, or null to take the answer back.</param>
internal sealed record KnownForAnswerRequest(string? Answer, string? Note);

internal sealed record KnownForAnswer(string Answer, string? Note, string? At);

/// <param name="Addresses">Each reference's first verse as the reading API addresses it, in step with <paramref name="References"/>.</param>
/// <param name="Changed">What the reading of the renderings put right in them, where it changed anything.</param>
internal sealed record KnownForClauseView(
    string English,
    string? Ukr,
    string? Deu,
    string? Spa,
    IReadOnlyList<string> References,
    IReadOnlyList<string?> Addresses,
    string? Changed);

/// <param name="Readings">What the three readings said of the record: the first reader's reason and doubts, the second's verdict on each clause, its note.</param>
internal sealed record KnownForEntry(
    string Slug,
    string Kind,
    string Name,
    string? Sex,
    int Verses,
    IReadOnlyList<KnownForClauseView> Clauses,
    JsonNode? Readings,
    KnownForAnswer? Decision);

internal sealed record KnownForDeclined(string Slug, string Kind, string Name, int Verses, string? Reason);

/// <param name="Written">Whether the list exists; until it does there is nothing to read.</param>
/// <param name="Open">Entries with no answer yet.</param>
internal sealed record KnownForReviewResponse(
    bool Written,
    int Open,
    int Approved,
    int Rejected,
    IReadOnlyList<KnownForEntry> Entries,
    IReadOnlyList<KnownForDeclined> Declined);

/// <summary>
/// What people, places and things are known for, as a language model wrote it, which the owner reads
/// before any of it is shown: he approves an entry or rejects it. The answer is written into the list,
/// and the next load of the known-for step loads every entry he approved and nothing else.
/// </summary>
internal sealed class KnownForReview(DeskPaths paths, ChangeLog log)
{
    public const string FileName = "known-for.json";

    public const string Section = "known-for";

    public const string Approved = "approved";

    public const string Rejected = "rejected";

    public string File => Path.Combine(paths.Review, FileName);

    public bool Exists => System.IO.File.Exists(File);

    public KnownForReviewResponse Read()
    {
        if (!Exists)
        {
            return new KnownForReviewResponse(false, 0, 0, 0, [], []);
        }

        var root = JsonFiles.Read(File);
        var entries = Entries(root).Select(Entry).ToList();
        var declined = (root["declined"]?.AsArray() ?? []).OfType<JsonObject>().Select(d => new KnownForDeclined(
            d["slug"]?.GetValue<string>() ?? string.Empty,
            d["kind"]?.GetValue<string>() ?? string.Empty,
            d["name"]?.GetValue<string>() ?? string.Empty,
            d["verses"]?.GetValue<int>() ?? 0,
            d["reason"]?.GetValue<string>())).ToList();
        return new KnownForReviewResponse(
            true,
            entries.Count(e => e.Decision is null),
            entries.Count(e => e.Decision?.Answer == Approved),
            entries.Count(e => e.Decision?.Answer == Rejected),
            entries,
            declined);
    }

    public int Unanswered() => Exists ? Entries(JsonFiles.Read(File)).Count(e => e["decision"] is null) : 0;

    /// <summary>Answers one entry or takes its answer back. Null where the list holds no such record or the answer is neither.</summary>
    public async Task<KnownForEntry?> Answer(string slug, KnownForAnswerRequest request)
    {
        if ((request.Note?.Length ?? 0) > ThingReview.LongestNote || !Exists
            || request.Answer is not (null or Approved or Rejected))
        {
            return null;
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var change = await JsonFiles.Change(File, root =>
        {
            var entry = Entries(root).FirstOrDefault(e => e["slug"]?.GetValue<string>() == slug);
            if (entry is null)
            {
                return (false, ((KnownForEntry, KnownForEntry)?)null);
            }

            var before = Entry(entry);
            if (request.Answer is null)
            {
                entry.Remove("decision");
            }
            else
            {
                entry["decision"] = new JsonObject { ["answer"] = request.Answer, ["note"] = note, ["at"] = JsonFiles.Now() };
            }

            return (true, (before, Entry(entry)));
        });
        if (change is not { } changed)
        {
            return null;
        }

        var (was, now) = changed;

        await log.Append(
            Section,
            "verdict",
            $"{now.Kind}/{slug}",
            was.Decision?.Answer is { } before ? JsonValue.Create(before) : null,
            now.Decision?.Answer is { } after ? JsonValue.Create(after) : null,
            request.Note,
            "load",
            now.Name);
        return now;
    }

    private static IEnumerable<JsonObject> Entries(JsonNode root) =>
        (root["records"]?.AsArray() ?? []).OfType<JsonObject>();

    private static KnownForEntry Entry(JsonObject entry) => new(
        entry["slug"]?.GetValue<string>() ?? string.Empty,
        entry["kind"]?.GetValue<string>() ?? string.Empty,
        entry["name"]?.GetValue<string>() ?? string.Empty,
        entry["sex"]?.GetValue<string>(),
        entry["verses"]?.GetValue<int>() ?? 0,
        [.. (entry["clauses"]?.AsArray() ?? []).OfType<JsonObject>().Select(Clause)],
        entry["readings"]?.DeepClone(),
        entry["decision"] is JsonObject decision && decision["answer"]?.GetValue<string>() is { Length: > 0 } answer
            ? new KnownForAnswer(answer, decision["note"]?.GetValue<string>(), decision["at"]?.GetValue<string>())
            : null);

    private static KnownForClauseView Clause(JsonObject clause)
    {
        var references = (clause["references"]?.AsArray() ?? []).Select(n => n?.GetValue<string>()).OfType<string>().ToList();
        return new KnownForClauseView(
            clause["english"]?.GetValue<string>() ?? string.Empty,
            clause["ukr"]?.GetValue<string>(),
            clause["deu"]?.GetValue<string>(),
            clause["spa"]?.GetValue<string>(),
            references,
            [.. references.Select(reference => Desk.Addresses.Of(reference.Split('-')[0]))],
            clause["renderingsChanged"]?.GetValue<string>());
    }
}
