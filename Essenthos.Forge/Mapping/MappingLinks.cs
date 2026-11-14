using System.Collections.Concurrent;
using Essenthos.Core.Conversion;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The links a composition reads, from the mapping's finals (DOC-0224 §6, generators read files): each pair's settled
/// claims, their words by their place in their own text (<see cref="ProductText"/>). What a composition writes goes to a
/// method file, and the merge decides what of it becomes a link.
/// </summary>
internal sealed class MappingLinks(ProductTexts texts)
{
    /// <summary>One link of a final: its words by their place in each text, and its settled claim.</summary>
    internal sealed record FinalLink(long Id, string Relation, LinkMethod Method, double? Confidence, long[] From, long[] To);

    private readonly ConcurrentDictionary<string, Lazy<List<FinalLink>>> _finals = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Dictionary<string, int>>> _ids = new(StringComparer.Ordinal);

    /// <summary>The finals a composition of the pair may read, relative to Resources: its own, its middle texts', and its proxies'.</summary>
    public static IEnumerable<string> Inputs(ProductStore store, string fromSlug, IEnumerable<string> viaSlugs, string toSlug) =>
        viaSlugs.Append(fromSlug).Select(slug => MappingProducts.Pair(slug, toSlug))
            .Concat(ProxyPairs(store, fromSlug, toSlug))
            .Distinct(StringComparer.Ordinal)
            .Select(pair => store.Find(MappingProducts.Of(pair, MappingProducts.Final))).OfType<string>();

    /// <summary>The links of a pair's final; empty where the pair has none.</summary>
    public List<FinalLink> Links(string fromSlug, string toSlug) =>
        _finals.GetOrAdd(MappingProducts.Pair(fromSlug, toSlug), pair => new Lazy<List<FinalLink>>(() => Read(pair, fromSlug, toSlug))).Value;

    /// <summary>
    /// What a composition reads as the middle text's links to the target, each at its confidence and its specificity. Every
    /// word of a phrase is carried, since the phrase renders the target words together: <em>he</em> of the King James's
    /// <em>he gave</em> reaches ἔδωκεν as <em>gave</em> does (<see cref="Targets"/>).
    /// </summary>
    /// <param name="aligned">
    /// Whether the middle text's own aligner links count, at their confidence, and EVIDENTIA's rule-based ones with them: for a
    /// daughter version they are the only way its mother reaches the target. Otherwise neither is walked, so one inference is
    /// never the next one's bridge.
    /// </param>
    public ILookup<long, (long To, double Confidence)> Carried(string viaSlug, string toSlug, bool aligned)
    {
        var (via, to) = (texts[viaSlug], texts[toSlug]);
        return Links(viaSlug, toSlug)
            .Where(l => l.To.Length > 0 && (aligned || l.Method is not (LinkMethod.Aligner or LinkMethod.RuleBased)))
            .SelectMany(l => l.From.SelectMany(f => Targets(via.Words[(int)f].Strong, l.To, t => to.Words[(int)t].Strong).Select(t => (Bridge: f, To: t,
                Confidence: (l.Confidence ?? 1) * CompositionPipeline.SpecificityOf(l.From.Length)))))
            .ToLookup(r => r.Bridge, r => (r.To, r.Confidence));
    }

    /// <summary>
    /// The target words of a link one of its middle words is carried to. A link naming one target word carries every word
    /// to it. One naming several says that the phrase renders them together, and a middle word's own Strong number says
    /// which of them it renders where it names one: <em>Spirit</em> of <em>the Spirit</em> over τοῦ Πνεύματος reaches
    /// Πνεύματος and <em>the</em> reaches τοῦ, where carrying both to both made the article as likely an answer for
    /// <em>Spirit</em> as the noun. A word with no number of its own, or one the target words do not carry (<em>of</em> in
    /// <em>of the Spirit</em>, which renders the case), reaches the words that are not the article, since the case and the
    /// person a function word renders are the content word's; and every target word where all are articles.
    /// </summary>
    internal static IEnumerable<long> Targets(string? own, long[] targets, Func<long, string?> strong)
    {
        if (targets.Length == 1)
        {
            return targets;
        }

        if (own is not null && targets.Where(t => strong(t) == own).ToList() is { Count: > 0 } named)
        {
            return named;
        }

        return targets.Where(t => strong(t) != Article).ToList() is { Count: > 0 } content ? content : targets;
    }

    private const string Article = "G3588";

    /// <summary>
    /// The target words each source word is joined to by a link that is not the aligner's: the words that already have an
    /// answer standing on them, and what it is. A source word a source says renders nothing has an answer too, the empty one.
    /// </summary>
    /// <param name="rules">
    /// Whether EVIDENTIA's rule-based links count. They are an answer standing on a word, which a composition must not write a
    /// guess beside; they are not a statement to measure one against, which would score one inference by another.
    /// </param>
    public Dictionary<long, HashSet<long>> Statements(string fromSlug, string toSlug, bool rules)
    {
        var statements = new Dictionary<long, HashSet<long>>();
        foreach (var link in Links(fromSlug, toSlug).Where(l => l.Method != LinkMethod.Aligner && (rules || l.Method != LinkMethod.RuleBased)))
        {
            foreach (var word in link.From)
            {
                if (!statements.TryGetValue(word, out var targets))
                {
                    statements[word] = targets = [];
                }

                targets.UnionWith(link.To);
            }
        }

        return statements;
    }

    /// <summary>
    /// The word pairs a source has already stated for the two texts, by their two words, with the link's id: only the links
    /// naming one word on each side, the only shape a composed link has and so the only one it can duplicate.
    /// </summary>
    public Dictionary<(long From, long To), long> Stated(string fromSlug, string toSlug) =>
        Links(fromSlug, toSlug)
            .Where(l => l.Relation == "renders" && l.Method != LinkMethod.Aligner && l.From.Length == 1 && l.To.Length == 1)
            .GroupBy(l => (l.From[0], l.To[0]))
            .ToDictionary(g => g.Key, g => g.First().Id);

    /// <summary>The target words a link that is not the aligner's says the source text does not render.</summary>
    public HashSet<long> Unrendered(string fromSlug, string toSlug) =>
        [.. Links(fromSlug, toSlug).Where(l => l.Relation == "omits" && l.Method != LinkMethod.Aligner).SelectMany(l => l.To)];

    /// <summary>
    /// The other texts in the source's language whose words a source states a word of the target for, enough of them to
    /// measure on, most first: for English against the originals, the King James and the Berean; not a text EVIDENTIA alone
    /// links, whose links are another inference.
    /// </summary>
    public List<string> Proxies(string fromSlug, string toSlug)
    {
        var proxies = new List<(string Slug, int Words)>();
        foreach (var pair in ProxyPairs(texts.Store, fromSlug, toSlug))
        {
            var (proxy, _) = MappingProducts.Split(pair);
            if (!texts.Holds(proxy))
            {
                continue;
            }

            var words = Links(proxy, toSlug)
                .Where(l => l.To.Length > 0 && l.Method is not (LinkMethod.Aligner or LinkMethod.RuleBased))
                .SelectMany(l => l.From).Distinct().Count();
            if (words >= Admission.FewestToMeasure)
            {
                proxies.Add((proxy, words));
            }
        }

        return [.. proxies.OrderByDescending(p => p.Words).ThenBy(p => p.Slug, StringComparer.Ordinal).Select(p => p.Slug)];
    }

    /// <summary>The pairs into the target from another text of the source's language that have a final.</summary>
    private static IEnumerable<string> ProxyPairs(ProductStore store, string fromSlug, string toSlug)
    {
        var language = Language(store, fromSlug);
        foreach (var pair in MappingProducts.Pairs(store).Where(p => p.EndsWith("-" + toSlug, StringComparison.Ordinal)))
        {
            var (proxy, _) = MappingProducts.Split(pair);
            if (proxy != fromSlug && store.Exists(MappingProducts.Of(pair, MappingProducts.Final))
                && store.Exists(TextField.ProductOf(proxy)) && Language(store, proxy) == language)
            {
                yield return pair;
            }
        }
    }

    private static string Language(ProductStore store, string slug) => IndexContext.Declared(store, slug).Language;

    private List<FinalLink> Read(string pair, string fromSlug, string toSlug)
    {
        var links = new List<FinalLink>();
        var product = MappingProducts.Of(pair, MappingProducts.Final);
        if (!texts.Store.Exists(product))
        {
            return links;
        }

        var fromIds = Ids(fromSlug);
        var toIds = Ids(toSlug);
        using var reader = texts.Store.Open(product, FinalRow.Schema);
        foreach (var row in reader.Rows().Where(r => r.Settled))
        {
            if (Words(row.From, fromIds) is { } from && Words(row.To, toIds) is { } to)
            {
                links.Add(new FinalLink(row.Id, row.Relation, MappingMethods.DatabaseMethod(row.Method), row.Confidence, from, to));
            }
        }

        return links;
    }

    private Dictionary<string, int> Ids(string slug) =>
        _ids.GetOrAdd(slug, s => new Lazy<Dictionary<string, int>>(() => PairLinkFiles.Ids(texts[s]))).Value;

    /// <summary>A row's words by their place in the text; null where the text no longer holds one of them.</summary>
    private static long[]? Words(string keys, Dictionary<string, int> ids)
    {
        if (keys.Length == 0)
        {
            return [];
        }

        var parts = keys.Split(' ');
        var words = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!ids.TryGetValue(parts[i], out var id))
            {
                return null;
            }

            words[i] = id;
        }

        return words;
    }
}
