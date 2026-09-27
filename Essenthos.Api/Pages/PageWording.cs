using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Pages;

/// <summary>
/// What pages are called in one language: the reader's own <c>meta</c> namespace, read as the
/// reader's i18next reads it — dotted keys, <c>{{name}}</c> placeholders, and plurals by the
/// language's own rules — so a title the server writes is the title the tab shows once the script runs.
/// </summary>
internal sealed partial class PageWording
{
    private readonly JsonNode _root;

    private PageWording(string code, JsonNode root)
    {
        Code = code;
        _root = root;
    }

    public string Code { get; }

    public static PageWording Parse(string code, string json) =>
        new(code, JsonNode.Parse(json) ?? throw new JsonException($"The wording for \"{code}\" is empty."));

    /// <summary>The copy compiled into the API, for when the web container publishes none.</summary>
    public static PageWording Compiled(string code)
    {
        using var stream = typeof(PageWording).Assembly.GetManifestResourceStream($"Wording.{code}.json")
                           ?? throw new InvalidOperationException(
                               $"The API was built without Pages/Wording/{code}.json; every language the site speaks needs one.");
        using var reader = new StreamReader(stream);
        return Parse(code, reader.ReadToEnd());
    }

    /// <summary>The text at a dotted key, its placeholders filled; the key itself where there is none, as i18next does.</summary>
    public string Text(string key, params (string Name, string Value)[] values) =>
        Fill(Find(key) ?? key, values);

    /// <summary>The form of a counted phrase this number takes in this language, with <c>{{count}}</c> filled.</summary>
    public string Counted(string key, int count) =>
        Fill(Find($"{key}_{Category(count)}") ?? Find($"{key}_other") ?? key, [("count", count.ToString())]);

    public IReadOnlyDictionary<string, string> Map(string key) =>
        Node(key) is JsonObject map
            ? map.Where(pair => pair.Value is JsonValue).ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>())
            : new Dictionary<string, string>();

    public string Titled(string title) => Text("titled", ("title", title));

    public string Quoted(string text) => Text("quoted", ("text", text));

    /// <summary>A verse's reference as this language writes one: <c>Markus 3,3</c> in German, <c>Marcos 3:3</c> in Spanish.</summary>
    public string Reference(string book, int chapter, int verse) =>
        $"{book} {Text("chapterVerse", ("chapter", chapter.ToString()), ("verse", verse.ToString()))}";

    /// <summary>What a reader of this language calls a book when citing it, where the wording names it.</summary>
    public string? CitedBook(int ordinal) => Find($"books.{ordinal}");

    /// <summary>
    /// The plural category of a whole number, as <c>Intl.PluralRules</c> gives it for each language
    /// the site speaks: Ukrainian has one, few and many; Spanish sets a million and its multiples apart.
    /// </summary>
    private string Category(int count) => Code switch
    {
        "uk" => (count % 10, count % 100) switch
        {
            (1, not 11) => "one",
            (>= 2 and <= 4, < 12 or > 14) => "few",
            _ => "many",
        },
        "es" => count == 1 ? "one" : count != 0 && count % 1_000_000 == 0 ? "many" : "other",
        _ => count == 1 ? "one" : "other",
    };

    private string? Find(string key) => Node(key) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private JsonNode? Node(string key)
    {
        var node = _root;
        foreach (var part in key.Split('.'))
        {
            node = node is JsonObject map ? map[part] : null;
        }

        return node;
    }

    private static string Fill(string text, (string Name, string Value)[] values) =>
        Placeholder().Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            foreach (var (key, value) in values)
            {
                if (key == name)
                {
                    return value;
                }
            }

            return match.Value;
        });

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex Placeholder();
}
