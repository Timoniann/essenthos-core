using System.Text.Json.Nodes;

namespace Essenthos.Core.Desk;

/// <param name="Decision">One of <see cref="RelationshipReview.Decisions"/>, or null to take the decision back.</param>
internal sealed record RelationshipDecisionRequest(string? Decision, string? Note);

/// <param name="Keys">The facts to decide, each as its key.</param>
internal sealed record RelationshipBulkRequest(IReadOnlyList<string> Keys, string Decision);

/// <param name="Decisions">The decisions as they now stand for the facts asked about; a fact taken back is absent.</param>
internal sealed record RelationshipDecisionResponse(IReadOnlyDictionary<string, JsonNode?> Decisions);

/// <summary>
/// BibleData's relationship facts the text did not confirm, and the owner's decision on each —
/// the list the review page on claude.ai held, now a tracked file beside the other review lists.
///
/// <para>
/// A decision is stored under the fact's key exactly as the review page stored it — the decision,
/// the note, and the fact's rows, ends, relation, category and reason — because
/// <c>scripts/relationships.py decide</c> reads that shape, and reads this file as it read the page's
/// folder of decisions. Deciding here writes nothing into the corpus: the decisions wait in the
/// file until somebody runs that command, as they always did.
/// </para>
/// </summary>
internal sealed class RelationshipReview(DeskPaths paths)
{
    public const string FileName = "bibledata-relationships.json";

    public static readonly IReadOnlySet<string> Decisions =
        new HashSet<string>(StringComparer.Ordinal) { "confirm", "remove", "keep", "reask" };

    /// <summary>The longest note taken, which is a paragraph.</summary>
    public const int LongestNote = 4000;

    public string File => Path.Combine(paths.Review, FileName);

    public bool Exists => System.IO.File.Exists(File);

    /// <summary>The whole list: the facts and every decision, including those about facts no longer listed.</summary>
    public JsonNode Read() => JsonFiles.Read(File);

    /// <summary>The list as the page shows it: each verse also carries the address the reading API takes.</summary>
    public JsonNode ReadForPage()
    {
        var node = Read();
        foreach (var verse in (node["facts"]?.AsArray() ?? []).SelectMany(fact => fact?["verses"]?.AsArray() ?? []))
        {
            if (verse is JsonObject shown && shown["ref"]?.GetValue<string>() is { } reference)
            {
                shown["address"] = Addresses.Of(reference);
            }
        }

        return node;
    }

    /// <summary>How many listed facts have no decision yet.</summary>
    public int Undecided()
    {
        var node = Read();
        var decisions = node["decisions"]?.AsObject();
        return (node["facts"]?.AsArray() ?? [])
            .Count(fact => fact is not null && decisions?[Key(fact)] is null);
    }

    public static string Key(JsonNode fact) => (fact["id"]?.GetValue<string>() ?? string.Empty).Replace('|', '-');

    public Task<RelationshipDecisionResponse?> Decide(string key, RelationshipDecisionRequest request) =>
        JsonFiles.Change(File, node =>
        {
            var fact = Fact(node, key);
            if (fact is null || !Valid(request.Decision, request.Note))
            {
                return (false, (RelationshipDecisionResponse?)null);
            }

            var decisions = DecisionsOf(node);
            var note = request.Note?.Trim() ?? string.Empty;
            if (request.Decision is null && note.Length == 0)
            {
                decisions.Remove(key);
            }
            else
            {
                decisions[key] = Body(fact, request.Decision, note);
            }

            return (true, new RelationshipDecisionResponse(new Dictionary<string, JsonNode?> { [key] = decisions[key]?.DeepClone() }));
        });

    /// <summary>Decides every fact named that has no decision yet, and leaves the decided ones as they are.</summary>
    public Task<RelationshipDecisionResponse?> DecideAll(RelationshipBulkRequest request) =>
        JsonFiles.Change(File, node =>
        {
            if (!Decisions.Contains(request.Decision ?? string.Empty))
            {
                return (false, (RelationshipDecisionResponse?)null);
            }

            var decisions = DecisionsOf(node);
            var written = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach (var key in (request.Keys ?? []).Distinct(StringComparer.Ordinal))
            {
                if (decisions[key] is not null || Fact(node, key) is not { } fact)
                {
                    continue;
                }

                decisions[key] = Body(fact, request.Decision, string.Empty);
                written[key] = decisions[key]!.DeepClone();
            }

            return (written.Count > 0, new RelationshipDecisionResponse(written));
        });

    private static bool Valid(string? decision, string? note) =>
        (decision is null || Decisions.Contains(decision)) && (note?.Length ?? 0) <= LongestNote;

    private static JsonNode? Fact(JsonNode node, string key) =>
        (node["facts"]?.AsArray() ?? []).FirstOrDefault(fact => fact is not null && Key(fact) == key);

    private static JsonObject DecisionsOf(JsonNode node)
    {
        if (node["decisions"] is JsonObject decisions)
        {
            return decisions;
        }

        var created = new JsonObject();
        node["decisions"] = created;
        return created;
    }

    /// <summary>The decision in the review page's own shape, which is what the decide command reads.</summary>
    private static JsonObject Body(JsonNode fact, string? decision, string note) => new()
    {
        ["decision"] = decision,
        ["note"] = note,
        ["subset"] = fact["subset"]?.DeepClone(),
        ["why"] = fact["why"]?.DeepClone(),
        ["a"] = fact["a"]?["slug"]?.DeepClone(),
        ["relation"] = (fact["relation"] ?? fact["says"]?[0]?["type"])?.DeepClone(),
        ["b"] = fact["b"]?["slug"]?.DeepClone(),
        ["rows"] = fact["id"]?.DeepClone(),
        ["decidedAt"] = JsonFiles.Now(),
    };
}
