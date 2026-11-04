using System.Text.Json;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Conversion;
using GeezTextSource = Essenthos.Core.Loading.GeezTextSource;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The names of a verse paired by their spelling and their order (<see cref="NameLists"/>, <see cref="NameListPass"/>),
/// where nothing else of the pair answers for the translation's name and the aligner did not join the two words: read from
/// both texts' words and frames and the pair's earlier method files (<see cref="PairLinkFiles"/>), the aligner's and the
/// composition's rows as the guesses and every other method's as what is stated.
///
/// <para>
/// The aligner links the database's pass refused stay in the aligner's file: this source writes only the pairs it adds,
/// and of those only the ones whose letters still read as one name, which is what the database kept once the load's
/// <see cref="NameListPass.WithdrawUnlike"/> had run. A pair of words a ruling refuses is left to the merge, which
/// suppresses that shape whatever proposes it.
/// </para>
/// </summary>
internal sealed class NameListSource : IPairSource
{
    /// <summary>
    /// The pairs the recipe settled the names of, but RUSV-KJV, which it aligned again afterwards: that alignment replaced
    /// every link of the aligner's kind, these included, and settles its names itself.
    /// </summary>
    private static readonly HashSet<string> Settled = new(StringComparer.Ordinal)
    {
        "ASV-BHSA", "ASV-NESTLE1904", "BSB-KJV", "GNV1599-BHSA", "GNV1599-NESTLE1904", "JPS1917-BHSA", "LUTH1912-BHSA",
        "LUTH1912-NESTLE1904", "RV1909-NESTLE1904", "TYN1534-NESTLE1904", "UBIO-BHSA", "UBIO-KJV", "UBIO-NESTLE1904",
        "UBIO-TR1894", "WEB-BHSA", "WEB-NESTLE1904", "YLT-BHSA", "YLT-NESTLE1904", "RV1909-BHSA", "RUSV-NESTLE1904", "RUSV-TR1894", "RUSV-BHSA", "GRCBRENT-KJV", "GRCBRENT-BHSA", "ELB1905-BHSA",
        "ELB1905-NESTLE1904", "SCH1951-BHSA", "GEEZ81-GRCBRENT",
    };

    public string Method => MappingMethods.NameList;

    public string Name => "name-list";

    public bool States(string pair) => Settled.Contains(pair);

    public bool Makes(string source) => source == NameListPass.Source;

    public IEnumerable<string> Inputs(ProductStore store, string pair)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        return
        [
            .. PairLinkFiles.Inputs(store, pair, Method),
            .. new[] { fromText, toText }.Select(TextField.ProductOf).Select(store.Find).OfType<string>(),
        ];
    }

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var from = texts[fromText];
        var to = texts[toText];
        var lettered = Versification(texts.Store, fromText) == Versification(texts.Store, toText);
        var source = NamedWords.Read(from, lettered);
        var target = NamedWords.Read(to, lettered);
        var links = Links(texts.Store, pair, from, to);

        var refuseStrays = AlignmentPipeline.BothMarkTheirNames(from.Language, to.Language);
        var common = AlignmentPipeline.MarksNoNames(from.Language) ? NameListPass.CommonWords(source, target, links) : null;
        var added = new List<(long From, long To)>();
        var verses = 0;
        foreach (var address in source.Keys.Intersect(target.Keys).Order())
        {
            added.AddRange(NameListPass.Settle(source[address], target[address], links, new HashSet<long>(), refuseStrays, common).Added);
            verses++;
        }

        var sourceLetters = Letters(source);
        var targetLetters = Letters(target);
        var settled = added.Distinct().ToList();
        var rows = settled
            .Where(pair => NameLists.Alike(sourceLetters[pair.From], targetLetters[pair.To]) >= NameLists.LeastLikeness)
            .Select(pair => new MappingRow(from.Key(pair.From).Own.ToString(), to.Key(pair.To).Own.ToString(),
                "renders", Method, NameLists.Settled, NameListPass.Source, null, null, null))
            .ToList();
        log($"{rows.Count} name pairs no link joins, over {verses} verses both texts print; {settled.Count - rows.Count} whose letters read as no one name left out");
        return rows;
    }

    /// <summary>
    /// The pair's links one word to one word, as the database's pass read them: the aligner's and the composition's as the
    /// guesses, by source word, every other method's as stated. A phrase answers for each of its words.
    /// </summary>
    private static NameListPass.StoredLinks Links(ProductStore store, string pair, ProductText from, ProductText to)
    {
        var fromIds = PairLinkFiles.Ids(from);
        var toIds = PairLinkFiles.Ids(to);
        var guessed = new List<(long From, long Link, long To)>(600_000);
        var stated = new List<(long From, long To)>(600_000);
        long link = 0;
        foreach (var row in PairLinkFiles.Rows(store, pair, MappingMethods.NameList))
        {
            link++;
            var guess = row.Method is MappingMethods.Aligner or MappingMethods.Composed;
            foreach (var fromWord in PairLinkFiles.Words(row.From, fromIds))
            {
                foreach (var toWord in PairLinkFiles.Words(row.To, toIds))
                {
                    if (guess)
                    {
                        guessed.Add((fromWord, link, toWord));
                    }
                    else
                    {
                        stated.Add((fromWord, toWord));
                    }
                }
            }
        }

        return new NameListPass.StoredLinks(
            guessed.ToLookup(row => row.From, row => (row.Link, row.To)),
            stated.ToLookup(row => row.From, row => row.To));
    }

    private static Dictionary<long, string> Letters(Dictionary<(int, int, int), List<AlignmentPipeline.Word>> words)
    {
        var letters = new Dictionary<long, string>();
        foreach (var word in words.Values.SelectMany(verse => verse))
        {
            letters[word.Id] = word.Letters;
        }

        return letters;
    }

    private static string? Versification(ProductStore store, string slug)
    {
        using var reader = store.Open(TextField.ProductOf(slug), TextField.Schema);
        return reader.Rows().FirstOrDefault(field => field.Field == "versification")?.Value;
    }
}

/// <summary>
/// Every word of a text by frame address as it is written, with the consonants of its names: the words
/// <see cref="AlignmentPipeline"/> reads from a database for <see cref="NameLists"/>, read from the text's words and frame.
/// </summary>
internal static class NamedWords
{
    /// <param name="lettered">Whether the verses the text prints with a letter are read: only against a text numbered in the same tradition.</param>
    public static Dictionary<(int, int, int), List<AlignmentPipeline.Word>> Read(ProductText text, bool lettered)
    {
        var verses = text.Verses.Where(verse => lettered || verse.VerseLabel.Length == 0).ToList();
        var uncapitalised = verses.SelectMany(text.InVerse)
            .Select(id => text.Words[id])
            .Where(word => word.Key.Position > 1 && word.Surface.Length > 0 && char.IsLower(word.Surface[0]))
            .Select(word => word.Surface.ToLowerInvariant())
            .ToHashSet();

        var mapped = text.Slug == GeezTextSource.Slug;
        var placed = new Dictionary<(int, int, int), List<AlignmentPipeline.Word>>();
        foreach (var verse in verses)
        {
            IEnumerable<(int, int, int)> addresses = !mapped
                ? text.Addresses(verse).Select(address => (address.Book, address.Chapter, address.Verse)).Distinct()
                : !GeezVerseMap.Books.Contains(verse.Book)
                    ? [text.Primary(verse)]
                    : GeezVerseMap.Addresses.TryGetValue((verse.Book, verse.Chapter, verse.Verse), out var at) ? at : [];
            List<AlignmentPipeline.Word>? words = null;
            foreach (var address in addresses)
            {
                words ??= [.. text.InVerse(verse).Select(id => Word(text, id, uncapitalised))];
                if (!placed.TryGetValue(address, out var standing))
                {
                    placed[address] = standing = [];
                }

                standing.AddRange(words);
            }
        }

        return placed;
    }

    private static AlignmentPipeline.Word Word(ProductText text, int id, IReadOnlySet<string> uncapitalised)
    {
        var word = text.Words[id];
        string? consonantal = null, pos = null, form = null, robinson = null;
        if (word.Morphology is { } morphology)
        {
            using var json = JsonDocument.Parse(morphology);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                consonantal = Text(json.RootElement, "consonantal");
                pos = Text(json.RootElement, "pos");
                form = Text(json.RootElement, "form");
                robinson = Text(json.RootElement, "robinson");
            }
        }

        var forms = new AlignmentPipeline.WordForms(word.Surface, word.Lemma, consonantal, word.Strong, text.Language, word.Key.Position);
        var letters = NameLists.Skeleton(consonantal ?? word.Surface, text.Language);
        return new AlignmentPipeline.Word(
            id,
            AlignmentTokens.One(word.Surface.ToLowerInvariant()),
            word.Strong,
            letters,
            AlignmentPipeline.IsNamed(forms, pos, form ?? robinson, uncapitalised) && letters.Length > 0 ? letters : null);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
