using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>The owner's word on one record: the item it is, or that it is none of those listed.</summary>
/// <param name="Answer">A Wikidata item, <c>Q1613144</c>, or <see cref="WikipediaReviewList.None"/>.</param>
internal sealed record WikipediaDecision(string Answer, string? Note, string? At);

/// <summary>One item offered to the owner for a record.</summary>
internal sealed record WikipediaOffer(
    string Qid,
    string Label,
    string? Description,
    IReadOnlyDictionary<string, string> Articles,
    IReadOnlyList<string> Evidence);

/// <summary>One record whose item the evidence does not settle.</summary>
/// <param name="Others">How many more items go by its names that have no article, so are not offered.</param>
internal sealed record WikipediaQuestion(
    string Record,
    string Kind,
    string Label,
    string? Line,
    IReadOnlyList<string> References,
    IReadOnlyList<WikipediaOffer> Offers,
    int Others);

/// <summary>
/// The records the matching could not tie to one Wikidata item, with the items each might be, and the
/// owner's answers.
///
/// <para>
/// The file is the owner's: his console shows each entry and writes his answer into it, and every
/// load reads the answers back. A load rewrites the open entries and the candidates of the answered
/// ones, and keeps every answer — an answer taken back is the only thing that removes one. A record
/// is named by its slug, as every file the owner decides in names one.
/// </para>
/// </summary>
internal static class WikipediaReviewList
{
    public const string FileName = "wikipedia-matches.json";

    /// <summary>The answer that says none of the items listed is the record.</summary>
    public const string None = "none";

    private const string About =
        "Persons, places and things whose Wikipedia article the corpus could not tell from a namesake's. Each entry " +
        "lists the Wikidata items that go by the record's names and have an article, with what each has in common " +
        "with the record. Answering with one tells every load to link the record to that item's articles; answering " +
        "none leaves the record without a link. The load rewrites the open entries and keeps the answered ones.";

    private static readonly JsonWriterOptions Writing = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The owner's answers by record slug, from the file as he left it.</summary>
    public static Dictionary<string, WikipediaDecision> Answers(string path)
    {
        var answers = new Dictionary<string, WikipediaDecision>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return answers;
        }

        foreach (var entry in Entries(Read(path)))
        {
            if (entry["record"]?.GetValue<string>() is { } slug
                && entry["decision"] is JsonObject decision
                && decision["answer"]?.GetValue<string>() is { Length: > 0 } answer)
            {
                answers[slug] = new WikipediaDecision(
                    answer, decision["note"]?.GetValue<string>(), decision["at"]?.GetValue<string>());
            }
        }

        return answers;
    }

    /// <summary>
    /// Writes the list: the open questions, in the order given, and every entry of the owner's own file
    /// that carries an answer — rewritten with the offers this load found where the record is still
    /// open, and as he left it where it is not.
    /// </summary>
    /// <param name="owners">The owner's file, where his answers are read from.</param>
    /// <param name="path">Where the list is written: the same file, or a copy's own.</param>
    public static void Write(string owners, string path, IReadOnlyList<WikipediaQuestion> questions)
    {
        var kept = File.Exists(owners)
            ? Entries(Read(owners)).Where(e => e["decision"] is not null).ToList()
            : [];

        var entries = new JsonArray();
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in questions)
        {
            var answered = kept.FirstOrDefault(e => e["record"]?.GetValue<string>() == question.Record);
            var entry = Entry(question);
            if (answered?["decision"] is { } decision)
            {
                entry["decision"] = decision.DeepClone();
            }

            entries.Add(entry);
            written.Add(question.Record);
        }

        foreach (var entry in kept.Where(e => !written.Contains(e["record"]?.GetValue<string>() ?? string.Empty)))
        {
            entries.Add(entry.DeepClone());
        }

        var root = new JsonObject
        {
            ["about"] = About,
            ["entries"] = entries,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Writing))
        {
            root.WriteTo(writer);
        }

        var text = Encoding.UTF8.GetString(buffer.ToArray()).ReplaceLineEndings("\n") + "\n";
        var temporary = path + ".writing";
        File.WriteAllText(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }

    private static JsonObject Entry(WikipediaQuestion question)
    {
        var offers = new JsonArray();
        foreach (var offer in question.Offers)
        {
            var articles = new JsonObject();
            foreach (var (language, title) in offer.Articles.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                articles[language] = title;
            }

            offers.Add(new JsonObject
            {
                ["qid"] = offer.Qid,
                ["label"] = offer.Label,
                ["description"] = offer.Description,
                ["articles"] = articles,
                ["evidence"] = new JsonArray([.. offer.Evidence.Select(e => (JsonNode?)e)]),
            });
        }

        return new JsonObject
        {
            ["record"] = question.Record,
            ["kind"] = question.Kind,
            ["label"] = question.Label,
            ["line"] = question.Line,
            ["references"] = new JsonArray([.. question.References.Select(r => (JsonNode?)r)]),
            ["candidates"] = offers,
            ["others"] = question.Others,
        };
    }

    private static JsonObject Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) as JsonObject
               ?? throw new InvalidDataException($"{path} holds no JSON object. Restore it from git before loading again.");
    }

    private static IEnumerable<JsonObject> Entries(JsonObject root) =>
        (root["entries"]?.AsArray() ?? []).OfType<JsonObject>();
}
