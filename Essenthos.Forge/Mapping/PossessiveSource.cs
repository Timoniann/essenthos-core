using System.Text.Json;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The possessive a Slavic translation spends beside a word linked to a Hebrew word with a pronominal suffix
/// (<see cref="PossessivePass"/> has the rule and its reasons), read from both texts' words and the pair's other
/// method files (<see cref="PairLinkFiles"/>) instead of the links a database held.
/// </summary>
internal sealed class PossessiveSource : IPairSource
{
    private const string SuffixPerson = "suffixPerson";

    public string Method => MappingMethods.Possessive;

    public string Name => "possessive";

    public bool States(string pair) => pair is "RUSV-BHSA" or "UBIO-BHSA" or "UKR1871-BHSA";

    public bool Makes(string source) => source == PossessivePass.Source;

    public IEnumerable<string> Inputs(ProductStore store, string pair) => PairLinkFiles.Inputs(store, pair, Method);

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var translation = texts[fromText];
        var hebrew = texts[toText];
        var translationIds = PairLinkFiles.Ids(translation);
        var hebrewIds = PairLinkFiles.Ids(hebrew);

        var linked = new Dictionary<int, SortedSet<int>>();
        var reaching = new HashSet<int>();
        foreach (var row in PairLinkFiles.Rows(texts.Store, pair, Method))
        {
            var from = PairLinkFiles.Words(row.From, translationIds);
            reaching.UnionWith(from);
            if (row.Relation is not ("renders" or "equals"))
            {
                continue;
            }

            var to = PairLinkFiles.Words(row.To, hebrewIds);
            foreach (var word in from)
            {
                if (!linked.TryGetValue(word, out var targets))
                {
                    linked[word] = targets = [];
                }

                targets.UnionWith(to);
            }
        }

        var suffixed = Suffixed(hebrew);
        var forms = PossessivePass.Forms.ToDictionary(form => form.Form, StringComparer.Ordinal);
        var rows = new List<MappingRow>();
        foreach (var verse in translation.Verses)
        {
            var words = translation.InVerse(verse).ToList();
            for (var at = 0; at < words.Count; at++)
            {
                var word = words[at];
                if (reaching.Contains(word) || !forms.TryGetValue(translation.Words[word].Surface.ToLowerInvariant(), out var form))
                {
                    continue;
                }

                var hebrewWord = Agreeing(words, at, -1, translation, linked, suffixed, form)
                                 ?? Agreeing(words, at, +1, translation, linked, suffixed, form);
                if (hebrewWord is { } agreed)
                {
                    rows.Add(new MappingRow(null, translation.Words[word].Key.Own.ToString(), hebrew.Words[agreed].Key.Own.ToString(),
                        "renders", Method, PossessivePass.Confidence, PossessivePass.Source, null, null, null));
                }
            }
        }

        log($"{rows.Count} possessives beside a word linked to a suffixed word they agree with, over {rows.Select(r => r.To).Distinct().Count()} suffixed words");
        return rows;
    }

    /// <summary>
    /// The first suffixed Hebrew word, in key order, that the neighbour on one side renders and the possessive agrees with;
    /// the neighbour is the word whose position is one away, as the database's rule read it.
    /// </summary>
    private static int? Agreeing(
        List<int> verse,
        int at,
        int side,
        ProductText translation,
        Dictionary<int, SortedSet<int>> linked,
        Dictionary<int, (string? Person, string? Number, string? Gender)> suffixed,
        (string Form, string? Person, string? Number, string? Gender) form)
    {
        var position = translation.Words[verse[at]].Key.Position + side;
        var neighbour = verse.FirstOrDefault(word => translation.Words[word].Key.Position == position, -1);
        if (neighbour < 0 || !linked.TryGetValue(neighbour, out var targets))
        {
            return null;
        }

        foreach (var target in targets)
        {
            if (suffixed.TryGetValue(target, out var suffix)
                && (form.Person is null || form.Person == suffix.Person)
                && (form.Number is null || form.Number == suffix.Number)
                && (form.Gender is null || form.Gender == suffix.Gender))
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>The words whose morphology names a pronominal suffix, with its person, number and gender.</summary>
    private static Dictionary<int, (string? Person, string? Number, string? Gender)> Suffixed(ProductText text)
    {
        var suffixed = new Dictionary<int, (string?, string?, string?)>();
        for (var id = 0; id < text.Words.Count; id++)
        {
            var morphology = text.Words[id].Morphology;
            if (morphology is null || !morphology.Contains(SuffixPerson, StringComparison.Ordinal))
            {
                continue;
            }

            using var json = JsonDocument.Parse(morphology);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(SuffixPerson, out var person))
            {
                suffixed[id] = (Text(person), Text(json.RootElement, "suffixNumber"), Text(json.RootElement, "suffixGender"));
            }
        }

        return suffixed;
    }

    private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? Text(value) : null;

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        _ => value.GetRawText(),
    };
}
