using System.Collections.Concurrent;
using Essenthos.Core.Conversion;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The links a composition reads, from the mapping's finals (DOC-0224 §6, generators read files): each pair's links, their
/// words by their place in their own text (<see cref="ProductText"/>), each read as the final would hold it without the
/// compositions (<see cref="Read"/>). What a composition writes goes to a method file, and the merge decides what of it
/// becomes a link.
/// </summary>
internal sealed class MappingLinks(ProductTexts texts)
{
    /// <summary>One link of a final: its words by their place in each text, and its settled claim.</summary>
    internal sealed record FinalLink(long Id, string Relation, LinkMethod Method, double? Confidence, long[] From, long[] To);

    private readonly ConcurrentDictionary<string, Lazy<List<FinalLink>>> _finals = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Dictionary<string, int>>> _ids = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<TargetKind[]>> _kinds = new(StringComparer.Ordinal);

    /// <summary>
    /// The finals a composition of the pair may read, and their statuses, relative to Resources: its own, its middle texts', and
    /// its proxies'.
    /// </summary>
    public static IEnumerable<string> Inputs(ProductStore store, string fromSlug, IEnumerable<string> viaSlugs, string toSlug) =>
        viaSlugs.Append(fromSlug).Select(slug => MappingProducts.Pair(slug, toSlug))
            .Concat(ProxyPairs(store, fromSlug, toSlug))
            .Distinct(StringComparer.Ordinal)
            .SelectMany(pair => new[] { MappingProducts.Final, MappingProducts.Statuses }.Select(file => MappingProducts.Of(pair, file)))
            .Select(store.Find).OfType<string>();

    /// <summary>The links of a pair's final; empty where the pair has none.</summary>
    public List<FinalLink> Links(string fromSlug, string toSlug) =>
        _finals.GetOrAdd(MappingProducts.Pair(fromSlug, toSlug), pair => new Lazy<List<FinalLink>>(() => Read(pair, fromSlug, toSlug))).Value;

    /// <summary>
    /// What a composition reads as the middle text's links to the target, each at its confidence and its specificity. Every
    /// word of a phrase is carried, since the phrase renders the target words together: <em>he</em> of the King James's
    /// <em>he gave</em> reaches ἔδωκεν as <em>gave</em> does (<see cref="Targets"/>); but a phrase's article, conjunction,
    /// connective or particle only to a target word of its own kind.
    /// </summary>
    /// <param name="aligned">
    /// Whether the middle text's own aligner links count, at their confidence, and EVIDENTIA's rule-based ones with them: for a
    /// daughter version they are the only way its mother reaches the target. Otherwise neither is walked, so one inference is
    /// never the next one's bridge. A daughter's middle word that a one-to-one link places inside a wider one is carried by
    /// the one-to-one link alone: CATSS states ἐν ἀρχῇ over בְּרֵאשִׁית as one link, and the aligner's ἀρχῇ on רֵאשִׁית
    /// beside it says which of the two words the noun is, where the group would carry it to the preposition too. A daughter's
    /// phrase word with no such link goes to a target word of its kind: a preposition to the preposition, any other word that is
    /// not a function word to the content words (καὶ τὸ ἐμπλόκιον over וְהַקִּשֻּׁרִים: the noun to the noun, not to the וְ).
    /// </param>
    public ILookup<long, (long To, double Confidence)> Carried(string viaSlug, string toSlug, bool aligned)
    {
        var (via, to) = (texts[viaSlug], texts[toSlug]);
        var functions = FunctionWords.Of(texts.Store);
        var kinds = Kinds(toSlug);
        var links = Links(viaSlug, toSlug)
            .Where(l => l.To.Length > 0 && (aligned || l.Method is not (LinkMethod.Aligner or LinkMethod.RuleBased)))
            .ToList();
        var narrow = aligned
            ? links.Where(l => l.From.Length == 1 && l.To.Length == 1).ToLookup(l => l.From[0], l => l.To[0])
            : null;
        bool Refined(FinalLink link, long word) =>
            narrow is not null && (link.From.Length > 1 || link.To.Length > 1) && narrow[word].Any(link.To.Contains);
        IEnumerable<long> OfItsKind(FinalLink link, FunctionKind kind, IEnumerable<long> targets)
        {
            if (narrow is null || (link.From.Length == 1 && link.To.Length == 1))
            {
                return targets;
            }

            var all = targets.ToList();
            var fitting = kind switch
            {
                FunctionKind.Preposition => all.Where(t => kinds[(int)t] == TargetKind.Preposition).ToList(),
                FunctionKind.None or FunctionKind.Pronoun => all.Where(t => kinds[(int)t] is TargetKind.Other or TargetKind.Adverb).ToList(),
                _ => all,
            };
            return fitting.Count > 0 ? fitting : all;
        }

        return links
            .SelectMany(l => l.From.Where(f => !Refined(l, f)).SelectMany(f =>
            {
                var kind = l.From.Length > 1 ? functions.Kind(via.Language, via.Words[(int)f].Surface) : FunctionKind.None;
                return OfItsKind(l, kind, Targets(via.Words[(int)f].Strong, kind, l.To, t => to.Words[(int)t].Strong, t => kinds[(int)t]))
                    .Select(t => (Bridge: f, To: t, Confidence: (l.Confidence ?? 1) * CompositionPipeline.SpecificityOf(l.From.Length)));
            }))
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
    ///
    /// <para>
    /// A phrase's article, conjunction, connective or particle (<paramref name="kind"/>) is the exception: it renders a word of
    /// its own kind or none, never the case or person of a content word. Carried to the content word, the King James's
    /// <em>and pray</em> over δεόμενοι put the Synodal's «и» on the participle, and the Synodal's words with no Strong number,
    /// which no statement measures, were composed so about six times in ten right. So it reaches the target words
    /// its number names, else those of its kind (<see cref="Fits"/>), else none.
    /// </para>
    /// </summary>
    /// <param name="kind">What the middle word is, where it is one word of a phrase; <see cref="FunctionKind.None"/> otherwise.</param>
    /// <param name="carries">What each target word is, by its morphology.</param>
    internal static IEnumerable<long> Targets(
        string? own, FunctionKind kind, long[] targets, Func<long, string?> strong, Func<long, TargetKind> carries)
    {
        if (kind is FunctionKind.Article or FunctionKind.Conjunction or FunctionKind.Connective or FunctionKind.Particle)
        {
            return own is not null && targets.Where(t => strong(t) == own).ToList() is { Count: > 0 } numbered
                ? numbered
                : targets.Where(t => Fits(kind, carries(t))).ToList();
        }

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

    /// <summary>Whether a target word is of the kind a middle text's function word renders.</summary>
    internal static bool Fits(FunctionKind kind, TargetKind target) => kind switch
    {
        FunctionKind.Article => target == TargetKind.Article,
        FunctionKind.Conjunction => target == TargetKind.Conjunction,
        FunctionKind.Connective => target is TargetKind.Conjunction or TargetKind.Adverb,
        FunctionKind.Particle => target == TargetKind.Particle,
        _ => true,
    };

    /// <summary>What a target word is, for a middle text's function word: read from its morphology (<see cref="TargetKinds.Of"/>).</summary>
    private TargetKind[] Kinds(string slug) =>
        _kinds.GetOrAdd(slug, s => new Lazy<TargetKind[]>(() => texts[s].Words.Select(word => TargetKinds.Of(word.Morphology)).ToArray())).Value;

    private const string Article = "G3588";

    /// <summary>
    /// The source words an answer stands on, which a composition writes no guess beside: every word a link that is not the
    /// aligner's names, EVIDENTIA's rule-based ones included, except a Strong pairing that only guessed which occurrence of its
    /// number the word is (<see cref="StrongNumberMatch.PairedInOrder"/> and below). The number is right but the occurrence is
    /// not stated, and read so the matcher's order outranked what the readings agreed on: the Synodal's «крещусь» stood on
    /// βαπτισθῆναι and «креститься» on βαπτίζομαι where all three readings had them the other way. The merge settles a guess
    /// written there against the pairing as against any claim.
    /// </summary>
    public HashSet<long> Answered(string fromSlug, string toSlug) =>
        Links(fromSlug, toSlug)
            .Where(l => l.Method != LinkMethod.Aligner && !(l.Method == LinkMethod.StrongNumber && OccurrenceGuessed(l.Confidence)))
            .SelectMany(l => l.From)
            .ToHashSet();

    private static bool OccurrenceGuessed(double? confidence) => confidence is { } c && c <= StrongNumberMatch.PairedInOrder + 1e-9;

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

    /// <summary>
    /// A pair's links as its final would hold them without the compositions. A link a composition also claims is read as its
    /// best other claim, which the final lists next; and a row the merge withdrew for a composed claim stands as a link of its
    /// own. A composition reads its own pair's final, which is made from the file it writes: read as settled, its last answers
    /// would outrank the Strong pairings under them (<see cref="MappingMethods.Rank"/>) and hide them as statements, and each run
    /// would measure and agree with another set of them than the run before (PRB-1914). A link only compositions claim stays
    /// theirs, which every reading but a daughter version's leaves out as the aligner's. An EVIDENTIA row a composed claim
    /// withdrew stays withdrawn: its absence gives way to a composition (<see cref="MappingMethods.EvidentiaAbsence"/>), and read
    /// back it would keep the composition off its words instead.
    /// </summary>
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
        using (var reader = texts.Store.Open(product, FinalRow.Schema))
        {
            foreach (var claims in Runs(reader.Rows()))
            {
                var settled = claims.FirstOrDefault(r => r.Settled) ?? claims[0];
                var read = settled.Method == MappingMethods.Composed ? claims.FirstOrDefault(r => r.Method != MappingMethods.Composed) ?? settled : settled;
                if (Words(settled.From, fromIds) is { } from && Words(settled.To, toIds) is { } to)
                {
                    links.Add(new FinalLink(settled.Id, settled.Relation, MappingMethods.DatabaseMethod(read.Method), read.Confidence, from, to));
                }
            }
        }

        var statuses = MappingProducts.Of(pair, MappingProducts.Statuses);
        if (texts.Store.Exists(statuses))
        {
            using var reader = texts.Store.Open(statuses, MappingRow.Schema);
            var id = 0L;
            foreach (var row in reader.Rows().Where(r => r.Status is { } status && WithdrawnForComposition(status)
                         && MappingMethods.DatabaseMethod(r.Method) != LinkMethod.RuleBased))
            {
                if (Words(row.From, fromIds) is { } from && Words(row.To, toIds) is { } to)
                {
                    links.Add(new FinalLink(--id, row.Relation, MappingMethods.DatabaseMethod(row.Method), row.Confidence, from, to));
                }
            }
        }

        return links;
    }

    private static readonly string WithdrawnByComposed = MappingStatus.WithdrawnBy(MappingMethods.Composed);

    private static bool WithdrawnForComposition(string status) =>
        status == WithdrawnByComposed || status.StartsWith(WithdrawnByComposed + " in ", StringComparison.Ordinal);

    /// <summary>A final's rows, one list per link: the run of rows with one id, its claims from the top-ranked down.</summary>
    private static IEnumerable<List<FinalRow>> Runs(IEnumerable<FinalRow> rows)
    {
        var run = new List<FinalRow>();
        foreach (var row in rows)
        {
            if (run.Count > 0 && run[0].Id != row.Id)
            {
                yield return run;
                run = [];
            }

            run.Add(row);
        }

        if (run.Count > 0)
        {
            yield return run;
        }
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
