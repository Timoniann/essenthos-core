using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>What a Wikidata item is, as the fetch script sorted it: a person, a place or anything else.</summary>
internal enum WikidataKind
{
    Person,
    Place,
    Other,
}

/// <summary>A verse or a run of verses an item's description names: <c>1 Samuel 22:20-23</c>.</summary>
internal readonly record struct CitedSpan(int Book, int Chapter, int First, int Last);

/// <summary>
/// One Wikidata item as the matching reads it. Only what a match is decided on is kept: the names
/// folded the way the corpus folds its own, the family statements, the verses the description cites
/// and the chapters the item is said to be present in, and the title of its article in each of the
/// four languages the interface speaks.
/// </summary>
internal sealed class WikidataItem
{
    public required string Id { get; init; }

    public WikidataKind Kind { get; init; }

    /// <summary>Which of the fetch script's four queries found it: <c>openbible</c> alone means only a gazetteer named it.</summary>
    public required IReadOnlyList<string> Seeds { get; init; }

    /// <summary>The label to show, in English where the item has one.</summary>
    public required string Label { get; init; }

    /// <summary>Its labels in every language, folded.</summary>
    public required IReadOnlySet<string> Names { get; init; }

    /// <summary>Its aliases in every language, folded.</summary>
    public required IReadOnlySet<string> Aliases { get; init; }

    /// <summary>Its descriptions by language.</summary>
    public required IReadOnlyDictionary<string, string> Descriptions { get; init; }

    /// <summary>Its articles by two-letter language, for the languages a link is kept in.</summary>
    public required IReadOnlyDictionary<string, string> Articles { get; init; }

    /// <summary><c>male</c>, <c>female</c> or null.</summary>
    public string? Sex { get; init; }

    /// <summary>The items of each family property, by property: P22 father, P25 mother, P40 child, P3373 sibling, P26 spouse.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Relatives { get; init; }

    /// <summary>Items said to be different from this one (P1889) and said to be the same (P460).</summary>
    public required IReadOnlyList<string> DifferentFrom { get; init; }

    public required IReadOnlyList<string> SameAs { get; init; }

    public required IReadOnlyList<CitedSpan> Verses { get; init; }

    /// <summary>Chapters the item is present in or whose verses its description cites, as (book, chapter).</summary>
    public required IReadOnlySet<(int Book, int Chapter)> Chapters { get; init; }

    /// <summary>The relations its descriptions state, as (<c>son</c>, folded name).</summary>
    public required IReadOnlySet<(string Relation, string Name)> StatedRelations { get; init; }

    /// <summary>What the item is an instance of (P31).</summary>
    public required IReadOnlyList<string> Classes { get; init; }

    /// <summary>Whether the item is an angel, a demon or a god: a being the text names beside the men it names.</summary>
    public bool Supernatural => Classes.Any(Supernaturals.Contains);

    /// <summary>Whether the item stands for a page about pages — a disambiguation page, a list, a category — not for a thing.</summary>
    public bool IsAWikimediaPage =>
        Descriptions.TryGetValue("en", out var description)
        && description.StartsWith("Wikimedia", StringComparison.OrdinalIgnoreCase);

    public bool HasArticle => Articles.Count > 0;

    private static readonly HashSet<string> Supernaturals = new(StringComparer.Ordinal)
    {
        "Q10822464", "Q581450", "Q690175", "Q178342", "Q178885", "Q6058157", "Q177413", "Q11688446", "Q235113",
        "Q22989102", "Q4762337", "Q728388", "Q1266031", "Q194077", "Q42092139", "Q185569", "Q11631135", "Q825", "Q190",
    };

    public override string ToString() => $"WikidataItem({Id} {Label})";
}

/// <summary>
/// The Wikidata items under <c>Resources/Wikidata</c>: the biblical items and the labels of the
/// ones they point at. Read once and held, 5,622 items.
/// </summary>
internal sealed partial class WikidataItems
{
    public const string Folder = "Wikidata";

    public const string ItemsFile = "items.jsonl";

    public const string ReferencedFile = "referenced.jsonl";

    /// <summary>The Wikipedias a link is kept for, by the site name Wikidata uses for each.</summary>
    private static readonly (string Site, string Language)[] Sites =
        [("enwiki", "en"), ("ukwiki", "uk"), ("dewiki", "de"), ("eswiki", "es")];

    private static readonly Dictionary<string, string> Maleness = new(StringComparer.Ordinal)
    {
        ["Q6581097"] = "male",
        ["Q6581072"] = "female",
    };

    private static readonly string[] FamilyProperties = ["P22", "P25", "P40", "P3373", "P26"];

    private readonly Dictionary<string, WikidataItem> _items = new(StringComparer.Ordinal);

    private readonly Dictionary<string, IReadOnlySet<string>> _referenced = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _englishLabels = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, WikidataItem> Items => _items;

    public bool TryGet(string id, out WikidataItem item) => _items.TryGetValue(id, out item!);

    /// <summary>Every name an item or a referenced item goes by, folded; empty for one the files do not hold.</summary>
    public IReadOnlySet<string> NamesOf(string id)
    {
        if (_items.TryGetValue(id, out var item))
        {
            return item.Names.Union(item.Aliases).ToHashSet(StringComparer.Ordinal);
        }

        return _referenced.TryGetValue(id, out var names) ? names : new HashSet<string>();
    }

    /// <summary>Whether the files hold the item, as an item or as a label of something it points at.</summary>
    public bool Knows(string id) => _items.ContainsKey(id) || _referenced.ContainsKey(id);

    public static WikidataItems Read(string resources)
    {
        var folder = Path.Combine(resources, Folder);
        var itemsFile = Path.Combine(folder, ItemsFile);
        if (!File.Exists(itemsFile))
        {
            throw new FileNotFoundException(
                $"The Wikidata items are not at {itemsFile}. Run scripts/fetch-wikidata-biblical.ps1 (avioniq action " +
                "fetch-wikidata-biblical) to put them back; without them no record can be tied to its article.");
        }

        var read = new WikidataItems();
        var referencedFile = Path.Combine(folder, ReferencedFile);
        if (File.Exists(referencedFile))
        {
            foreach (var line in File.ReadLines(referencedFile))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var id = root.GetProperty("id").GetString()!;
                var names = Labels(root).Select(NameFolding.Fold).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
                read._referenced[id] = names;
                if (Labels(root, "en").FirstOrDefault() is { } english)
                {
                    read._englishLabels[id] = english;
                }
            }
        }

        var raw = new List<(JsonElement Root, JsonDocument Document)>();
        foreach (var line in File.ReadLines(itemsFile))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var document = JsonDocument.Parse(line);
            var id = document.RootElement.GetProperty("id").GetString()!;
            if (Labels(document.RootElement, "en").FirstOrDefault() is { } english)
            {
                read._englishLabels[id] = english;
            }

            raw.Add((document.RootElement, document));
        }

        foreach (var (root, document) in raw)
        {
            var item = read.Build(root);
            read._items[item.Id] = item;
            document.Dispose();
        }

        return read;
    }

    private WikidataItem Build(JsonElement root)
    {
        var id = root.GetProperty("id").GetString()!;
        var kind = root.GetProperty("kind").GetString() switch
        {
            "person" => WikidataKind.Person,
            "place" => WikidataKind.Place,
            _ => WikidataKind.Other,
        };

        var labels = Labels(root).ToList();
        var aliases = new List<string>();
        if (root.TryGetProperty("aliases", out var aliasProperty))
        {
            foreach (var language in aliasProperty.EnumerateObject())
            {
                if (language.Value.ValueKind == JsonValueKind.String)
                {
                    aliases.Add(language.Value.GetString()!);
                }
                else
                {
                    aliases.AddRange(language.Value.EnumerateArray().Select(a => a.GetString()!));
                }
            }
        }

        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("descriptions", out var descriptionProperty))
        {
            foreach (var language in descriptionProperty.EnumerateObject())
            {
                descriptions[language.Name] = language.Value.GetString()!;
            }
        }

        var articles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("sitelinks", out var sitelinks))
        {
            foreach (var (site, language) in Sites)
            {
                if (sitelinks.TryGetProperty(site, out var title) && title.GetString() is { Length: > 0 } text)
                {
                    articles[language] = text;
                }
            }
        }

        string? sex = null;
        var relatives = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var different = new List<string>();
        var same = new List<string>();
        var works = new List<string>();
        var classes = new List<string>();
        if (root.TryGetProperty("claims", out var claims))
        {
            foreach (var claim in claims.EnumerateObject())
            {
                var values = claim.Value.EnumerateArray()
                    .Select(c => c.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : null)
                    .OfType<string>()
                    .ToList();
                switch (claim.Name)
                {
                    case "P21":
                        sex = values.Select(v => Maleness.GetValueOrDefault(v)).FirstOrDefault(v => v is not null);
                        break;
                    case "P1889":
                        different.AddRange(values);
                        break;
                    case "P460":
                        same.AddRange(values);
                        break;
                    case "P1441" or "P1343":
                        works.AddRange(values);
                        break;
                    case "P31":
                        classes.AddRange(values);
                        break;
                    default:
                        if (FamilyProperties.Contains(claim.Name))
                        {
                            relatives[claim.Name] = values;
                        }

                        break;
                }
            }
        }

        var verses = new List<CitedSpan>();
        var chapters = new HashSet<(int, int)>();
        var stated = new HashSet<(string, string)>();
        foreach (var (language, description) in descriptions)
        {
            if (language == "en")
            {
                foreach (var span in CitedScripture.Find(description))
                {
                    if (span.First > 0)
                    {
                        verses.Add(span);
                        chapters.Add((span.Book, span.Chapter));
                    }
                    else
                    {
                        chapters.Add((span.Book, span.Chapter));
                    }
                }
            }

            foreach (var relation in CitedScripture.Relations(description))
            {
                stated.Add(relation);
            }
        }

        foreach (var work in works)
        {
            if (_englishLabels.TryGetValue(work, out var label))
            {
                foreach (var span in CitedScripture.Find(label).Where(s => s.First == 0))
                {
                    chapters.Add((span.Book, span.Chapter));
                }
            }
        }

        var english = Labels(root, "en").FirstOrDefault() ?? Labels(root, "mul").FirstOrDefault()
            ?? labels.FirstOrDefault() ?? id;
        return new WikidataItem
        {
            Id = id,
            Kind = kind,
            Seeds = root.TryGetProperty("seeds", out var seeds)
                ? [.. seeds.EnumerateArray().Select(s => s.GetString()!)]
                : [],
            Label = english,
            Names = labels.Select(NameFolding.Fold).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal),
            Aliases = aliases.Select(NameFolding.Fold).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal),
            Descriptions = descriptions,
            Articles = articles,
            Sex = sex,
            Relatives = relatives,
            DifferentFrom = different,
            SameAs = same,
            Verses = verses,
            Chapters = chapters,
            StatedRelations = stated,
            Classes = classes,
        };
    }

    private static IEnumerable<string> Labels(JsonElement root, string? language = null)
    {
        if (!root.TryGetProperty("labels", out var labels))
        {
            yield break;
        }

        foreach (var label in labels.EnumerateObject())
        {
            if ((language is null || label.Name == language) && label.Value.GetString() is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }
}

/// <summary>Verses and family statements as English prose writes them.</summary>
internal static partial class CitedScripture
{
    [GeneratedRegex(
        @"\b((?:[123] |First |Second |Third )?[A-Z][a-z]+(?: of [A-Z][a-z]+)?(?: [A-Z][a-z]+)?) (\d{1,3})(?::(\d{1,3})(?:[–-](\d{1,3}))?)?")]
    private static partial Regex Reference();

    [GeneratedRegex(@"\b(son|daughter|father|mother|wife|husband|brother|sister) of (?:King |the )?([A-Z][\w'\-]+)")]
    private static partial Regex Relation();

    /// <summary>
    /// Every chapter and verse a text cites, with <c>First</c> 0 where only a chapter is named. A
    /// name that is not a book is skipped, and a long one is shortened until a book answers, so
    /// <em>Book of Judges 13</em> and <em>Genesis 14 Amraphel</em> still read as Judges 13 and Genesis 14.
    /// </summary>
    public static IEnumerable<CitedSpan> Find(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        foreach (Match match in Reference().Matches(text))
        {
            var words = match.Groups[1].Value.Split(' ');
            int? book = null;
            for (var length = words.Length; length >= 1 && book is null; length--)
            {
                for (var start = 0; start + length <= words.Length && book is null; start++)
                {
                    var candidate = string.Join(' ', words[start..(start + length)]);
                    if (candidate.Length >= 3 || char.IsDigit(candidate[0]))
                    {
                        book = ResolveBook(candidate);
                    }
                }
            }

            if (book is null)
            {
                continue;
            }

            var chapter = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (!match.Groups[3].Success)
            {
                yield return new CitedSpan(book.Value, chapter, 0, 0);
                continue;
            }

            var first = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            var last = match.Groups[4].Success
                ? int.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture)
                : first;
            yield return new CitedSpan(book.Value, chapter, first, Math.Max(first, last));
        }
    }

    private static int? ResolveBook(string name)
    {
        var ordinal = BookReferences.ResolveOrdinal(name);
        return ordinal is >= 1 and <= BookReferences.CanonBookCount ? ordinal : null;
    }

    /// <summary>The (relation, folded name) pairs a text states: <em>son of Joash</em>.</summary>
    public static IEnumerable<(string Relation, string Name)> Relations(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        foreach (Match match in Relation().Matches(text))
        {
            var name = NameFolding.Fold(match.Groups[2].Value);
            if (name.Length > 0)
            {
                yield return (match.Groups[1].Value, name);
            }
        }
    }
}
