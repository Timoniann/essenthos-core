using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Essenthos.Core.TextusReceptus;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The King James New Testament against the Greek, from CrossWire's KJV 3.1 laid onto the King James's words
/// (<see cref="CrossWireKingJames"/>). Reads no database: the module, both texts' words and frames, and the King James's
/// corrections (the words they add are not the module's).
///
/// <list type="bullet">
/// <item><c>stated</c>, against Scrivener 1894 and Stephanus 1550: each element linked to the Greek words at the places
/// it states, where the edition holds the word the module names there (<see cref="ReceivedTextPositions.Lay"/>), and each
/// element that prints no word over an article, a conjunction or a particle stated as the Greek word the English leaves
/// untranslated (<c>omits</c>). An empty element over any other word is not stated: it is mostly an idiom the next English
/// word renders (κατ' ἰδίαν <em>privately</em>) or a gap in the module (ὀκτώ in <em>eighteen</em>, Luke 13:11).</item>
/// <item><c>strong</c>, against Nestle 1904, the Byzantine Textform, Westcott-Hort and Tischendorf: each stated link to
/// Scrivener carried to the edition over the pairing of the Strong numbers both Greek editions carry
/// (<see cref="GreekWitnessSource"/>), at that pairing's confidence. It is not stated: the module counts its own Received
/// Text, and which word of another edition is the same word is the pairing's.</item>
/// <item><c>lexical</c>, against the same five: an English word in no element (the few the module leaves untagged), given the
/// Greek its phrase's own word states, by the morphology the edition states (<see cref="GreekFunctionWords"/>).</item>
/// </list>
///
/// <para>
/// **The tags number the verses as the King James prints them**, so a verse is the King James's own and the Greek words
/// are those standing at that verse's frame address (PRB-1458).
/// </para>
/// </summary>
internal sealed class KingJamesGreekSource : IPairSource
{
    private const string KingJames = Sources.KingJamesSlug;

    /// <summary>Rows the Zefania KJV+ layer wrote before CrossWire's replaced it; a file remade gives them up.</summary>
    private const string Withdrawn = "Zefania KJV+ ";

    private static readonly string Scrivener = TextusReceptusTextSource.Slug(Edition.Scrivener1894);

    private static readonly string Stephanus = TextusReceptusTextSource.Slug(Edition.Stephanus1550);

    private readonly string _method;

    private KingJamesGreekSource(string method) => _method = method;

    /// <summary>
    /// The Greek texts the King James is matched against, in the order they are worth reading: the one it was translated
    /// from, then the tradition that text belongs to, then the critical editions. Their order within that last group does
    /// not assert an ordering of their reach.
    /// </summary>
    internal static IReadOnlyList<string> Witnesses { get; } =
    [
        Scrivener,
        ByzantineTextSource.Slug,
        NestleTextSource.Slug,
        TischendorfTextSource.Slug,
        WestcottHortTextSource.Slug,
    ];

    /// <summary>The editions whose positions the module states.</summary>
    internal static IReadOnlyList<string> Stated { get; } = [Scrivener, Stephanus];

    public static readonly IReadOnlyList<KingJamesGreekSource> All =
        [new(MappingMethods.Stated), new(MappingMethods.Strong), new(MappingMethods.Lexical)];

    public string Method => _method;

    public string Name => _method switch
    {
        MappingMethods.Stated => "kjv-positions",
        MappingMethods.Strong => "kjv-carried",
        _ => "kjv-function-words",
    };

    /// <summary>
    /// The pairs it speaks about. The Strong file of Scrivener is stated too though it writes nothing there (the links are
    /// stated), so that the file is remade without the rows the withdrawn layer left in it.
    /// </summary>
    public bool States(string pair) =>
        MappingProducts.Split(pair) is var (from, to) && from == KingJames && (_method == MappingMethods.Stated ? Stated : Witnesses).Contains(to);

    /// <summary>The King James's pair with Stephanus, which only the module's positions state.</summary>
    public IEnumerable<string> Founds => _method == MappingMethods.Stated ? [MappingProducts.Pair(KingJames, Stephanus)] : [];

    public bool Makes(string source) => _method switch
    {
        MappingMethods.Stated => source is Sources.KingJamesPositionCredit or Sources.KingJamesUnrenderedCredit
                                 || source.StartsWith(Withdrawn, StringComparison.Ordinal),
        MappingMethods.Strong => source.StartsWith(Sources.KingJamesCredit + ":", StringComparison.Ordinal)
                                 || source.StartsWith(Withdrawn, StringComparison.Ordinal),
        _ => Recovered(source) is not null,
    };

    public IEnumerable<string> Inputs(ProductStore store, string pair) =>
        [CrossWireKingJames.Folder, .. store.Find(TextCorrections.ProductOf(KingJames)) is { } corrections ? [corrections] : Array.Empty<string>()];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var english = texts[fromText];
        var greek = texts[toText];
        return _method switch
        {
            MappingMethods.Stated => StatedRows(texts, english, greek, log),
            MappingMethods.Strong => toText == Scrivener ? [] : CarriedRows(texts, english, greek, log),
            _ => FunctionWordRows(texts, english, greek, Named(texts.Store, pair, toText), log),
        };
    }

    private static List<MappingRow> StatedRows(ProductTexts texts, ProductText english, ProductText greek, Action<string> log)
    {
        var links = Links.Of(texts, greek.Slug, log);
        var rows = new List<MappingRow>(links.Count + 6_000);
        rows.AddRange(links.All.Select(link => Row(english, greek, link.English, link.Greek, LinkRelation.Renders, MappingMethods.Stated, null,
            Sources.KingJamesPositionCredit)));
        rows.AddRange(links.Unrendered.Select(words => new MappingRow(string.Empty, MappingText.Keys(words.Select(greek.Key)),
            LinkRelation.Omits.ToString().ToLowerInvariant(), MappingMethods.Stated, null, Sources.KingJamesUnrenderedCredit, null, null, null)));
        log($"{links}");
        return rows;
    }

    private static List<MappingRow> CarriedRows(ProductTexts texts, ProductText english, ProductText greek, Action<string> log)
    {
        var links = Links.Of(texts, greek.Slug, log);
        var source = Sources.KingJamesCarriedCredit(greek.Slug);
        log($"{links}");
        return [.. links.All.Select(link => Row(english, greek, link.English, link.Greek, LinkRelation.Renders, MappingMethods.Strong, link.Confidence, source))];
    }

    private static List<MappingRow> FunctionWordRows(ProductTexts texts, ProductText english, ProductText greek, string source, Action<string> log)
    {
        var laid = Laying.Of(texts, log);
        var links = Links.Of(texts, greek.Slug, log);
        var rows = new List<MappingRow>(4_000);
        foreach (var (own, ids) in laid.Printed)
        {
            if (!greek.ByAddress.TryGetValue(english.Primary(own), out var at))
            {
                continue;
            }

            var greekAt = new Dictionary<long, int>(at.Count);
            for (var i = 0; i < at.Count; i++)
            {
                greekAt[at[i]] = i;
            }

            var morphology = at.Select(id => GreekMorphology.Parse(Morphology(greek.Words[id].Morphology))).ToList();
            var anchors = Enumerable.Repeat(-1, ids.Count).ToArray();
            var englishAt = ids.Select((id, i) => (id, i)).ToDictionary(p => (long)p.id, p => p.i);
            var claimed = new HashSet<int>();
            foreach (var link in links.InVerse(own))
            {
                var places = link.Greek.Where(greekAt.ContainsKey).Select(id => greekAt[id]).ToList();
                claimed.UnionWith(places);
                if (places.Count == 0 || link.Confidence < Settled)
                {
                    continue;
                }

                // A phrase anchors on its word that is not the article: "the world" over τὸν κόσμον stands for κόσμον.
                var anchor = places.FirstOrDefault(place => morphology[place].Part != GreekPart.Article, places[0]);
                foreach (var word in link.English.Where(englishAt.ContainsKey))
                {
                    anchors[englishAt[word]] = anchor;
                }
            }

            claimed.UnionWith(links.UnrenderedIn(own).Where(greekAt.ContainsKey).Select(id => greekAt[id]));
            var tags = ids.Select(id => laid.Laid.Tags.TryGetValue(id, out var tag) ? tag : default).ToList();
            var supplied = Enumerable.Range(0, ids.Count).Where(i => tags[i].Supplied || english.Words[ids[i]].Supplied is not null).ToHashSet();
            // A word of a numbered element is the element's, never a function word to guess: "the" over an element naming no
            // article carries no number of its own, yet its element states its Greek.
            var named = tags.Select(tag => tag.Numbers is { } numbers ? CrossWireKingJames.Primary(numbers) : null).ToList();
            foreach (var match in GreekFunctionWords.Match([.. ids.Select(id => english.Words[id].Surface)], named,
                         anchors, morphology, claimed, supplied))
            {
                rows.Add(Row(english, greek, [ids[match.EnglishWord]], [at[match.GreekWord]], LinkRelation.Renders, MappingMethods.Lexical, match.Confidence, source));
            }
        }

        log($"{rows.Count} English words in no element given a Greek word by the morphology {greek.Slug} states");
        return rows;
    }

    /// <summary>How sure a link must be for a function word to hang off it: the Strong pairing of a number standing more than once.</summary>
    private const double Settled = 0.85;

    private static MappingRow Row(ProductText english, ProductText greek, IEnumerable<long> from, IEnumerable<long> to, LinkRelation relation,
        string method, double? confidence, string source) =>
        new(MappingText.Keys(from.Select(english.Key)), MappingText.Keys(to.Select(greek.Key)), relation.ToString().ToLowerInvariant(),
            method, confidence, source, null, null, null);

    /// <summary>
    /// Whether an element that prints no word over this Greek word says the English leaves it untranslated: an article, a
    /// conjunction or a particle (Robinson's T, CONJ, COND, PRT), which the English so often has no word for.
    /// </summary>
    internal static bool GoesUntranslated(string? robinson) =>
        robinson?.Split('-')[0] is "T" or "CONJ" or "COND" or "PRT";

    /// <summary>The Robinson code the edition's morphology states (<see cref="GreekMorphology.Of(System.Text.Json.JsonDocument?)"/>).</summary>
    private static string? Morphology(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        return root.TryGetProperty("form", out var form) && form.ValueKind == System.Text.Json.JsonValueKind.String
            ? form.GetString()
            : root.TryGetProperty("robinson", out var robinson) && robinson.ValueKind == System.Text.Json.JsonValueKind.String
                ? robinson.GetString()
                : null;
    }

    /// <summary>The source the function words carry, under the witness's name the previous file gave it (scrivener1894), or its slug.</summary>
    private static string Named(ProductStore store, string pair, string toText) =>
        StrongNumberSource.Previous(store, pair, MappingMethods.Lexical,
            source => Recovered(source) is { } name && TextAliases.Of(toText).Append(toText).Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)))
        ?? RecoveredSource(toText);

    private const string RecoveredBefore = "the untagged English function words, matched to the morphology ";

    private const string RecoveredAfter = " states";

    internal static string RecoveredSource(string greekSlug) => RecoveredBefore + greekSlug + RecoveredAfter;

    /// <summary>The witness's name a function-word source carries, or null for a source of another kind.</summary>
    private static string? Recovered(string source) =>
        source.Length > RecoveredBefore.Length + RecoveredAfter.Length && source.StartsWith(RecoveredBefore, StringComparison.Ordinal)
                                                                       && source.EndsWith(RecoveredAfter, StringComparison.Ordinal)
            ? source[RecoveredBefore.Length..^RecoveredAfter.Length]
            : null;

    /// <summary>
    /// The module laid onto the King James's New Testament once per run: each verse's words as the source printed them (an
    /// epistle's subscription, appended by a correction, is not a verse word of the module), by <see cref="ProductText"/> id.
    /// </summary>
    internal sealed class Laying
    {
        private static readonly ConditionalWeakTable<ProductTexts, Lazy<Laying>> Cache = new();

        public required LaidKingJames Laid { get; init; }

        public required Dictionary<VerseKey, List<int>> Printed { get; init; }

        /// <summary>The unit of every word in an element, by word id.</summary>
        public required Dictionary<long, int> Units { get; init; }

        public static Laying Of(ProductTexts texts, Action<string> log) => Cache.GetValue(texts, t => new Lazy<Laying>(() => Make(t, log))).Value;

        private static Laying Make(ProductTexts texts, Action<string> log)
        {
            var english = texts[KingJames];
            var added = Added(texts.Store);
            var printed = new Dictionary<VerseKey, List<int>>(8_000);
            var corpus = new List<CorpusVerse>(8_000);
            foreach (var own in english.Verses.Where(own => CrossWireKingJames.InNewTestament(own.Book) && own.VerseLabel.Length == 0))
            {
                var all = english.InVerse(own).ToList();
                var (head, tail) = added.GetValueOrDefault(own);
                var ids = all.Skip(head).Take(all.Count - head - tail).ToList();
                printed[own] = ids;
                corpus.Add(new CorpusVerse(own.Book, own.Chapter, own.Verse, [], [.. ids.Select(id => new CorpusWord(id, english.Words[id].Surface))]));
            }

            var laid = CrossWireKingJames.Lay(texts.Store.FullPath(CrossWireKingJames.Folder), corpus);
            log(laid.ToString());
            return new Laying { Laid = laid, Printed = printed, Units = laid.Tags.ToDictionary(tag => tag.Key, tag => tag.Value.Unit) };
        }

        /// <summary>How many words the King James's corrections put before and after each verse's printed words (<c>prepend</c>, <c>append</c>).</summary>
        private static Dictionary<VerseKey, (int Head, int Tail)> Added(ProductStore store)
        {
            var added = new Dictionary<VerseKey, (int Head, int Tail)>();
            foreach (var row in TextCorrections.Read(store, KingJames).Rows.Where(row => row.Kind is TextCorrection.Prepend or TextCorrection.Append))
            {
                var verse = VerseKey.Parse(row.Own);
                var (head, tail) = added.GetValueOrDefault(verse);
                var count = TextCorrections.Words(row).Count;
                added[verse] = row.Kind == TextCorrection.Prepend ? (head + count, tail) : (head, tail + count);
            }

            return added;
        }
    }

    /// <param name="English">The King James words of the element, by <see cref="ProductText"/> id.</param>
    /// <param name="Greek">The witness's words, by its own ids.</param>
    /// <param name="Confidence">Null where the module states the link; the pairing's where it is carried.</param>
    internal sealed record Link(VerseKey Verse, IReadOnlyList<long> English, IReadOnlyList<long> Greek, double? Confidence);

    /// <summary>
    /// The King James's links to one Greek witness, once per run: stated where the module places the words in the witness,
    /// carried over the witness's pairing with Scrivener otherwise; and the witness's words the module states untranslated.
    /// </summary>
    internal sealed class Links
    {
        private static readonly ConditionalWeakTable<ProductTexts, ConcurrentDictionary<string, Lazy<Links>>> Cache = new();

        private Dictionary<VerseKey, List<Link>> _byVerse = [];

        private Dictionary<VerseKey, List<long>> _unrendered = [];

        public List<Link> All { get; private init; } = [];

        /// <summary>The witness's words each element printing no word stands for, one list per element.</summary>
        public List<IReadOnlyList<long>> Unrendered { get; private init; } = [];

        private string _summary = string.Empty;

        public int Count => All.Count;

        public IEnumerable<Link> InVerse(VerseKey own) => _byVerse.GetValueOrDefault(own) ?? [];

        public IEnumerable<long> UnrenderedIn(VerseKey own) => _unrendered.GetValueOrDefault(own) ?? [];

        public override string ToString() => _summary;

        public static Links Of(ProductTexts texts, string witness, Action<string> log) =>
            Cache.GetOrCreateValue(texts).GetOrAdd(witness, w => new Lazy<Links>(() => Stated.Contains(w) ? Placed(texts, w, log) : Carried(texts, w, log))).Value;

        /// <summary>The links the module states to an edition of the Received Text, and the words it leaves untranslated there.</summary>
        private static Links Placed(ProductTexts texts, string witness, Action<string> log)
        {
            var laying = Laying.Of(texts, log);
            var english = texts[KingJames];
            var greek = texts[witness];
            var verses = new Dictionary<(int, int, int), IReadOnlyList<CorpusWord>>(8_000);
            var own = new Dictionary<(int, int, int), VerseKey>(8_000);
            foreach (var verse in laying.Printed.Keys)
            {
                own[(verse.Book, verse.Chapter, verse.Verse)] = verse;
                if (greek.ByAddress.TryGetValue(english.Primary(verse), out var at))
                {
                    verses[(verse.Book, verse.Chapter, verse.Verse)] = [.. at.Select(id => new CorpusWord(id, greek.Words[id].Surface))];
                }
            }

            var laid = ReceivedTextPositions.Lay(laying.Laid.Positions, laying.Units, verses);
            var all = laid.Links.Select(link => new Link(english.Words[(int)link.From[0]].Key.Verse, link.From, link.To, null)).ToList();
            var linked = all.SelectMany(link => link.Greek).ToHashSet();

            var unrendered = new List<IReadOnlyList<long>>(6_000);
            var byVerse = new Dictionary<VerseKey, List<long>>();
            int disagreed = 0, idioms = 0;
            foreach (var (address, elements) in laying.Laid.Unrendered)
            {
                if (!verses.TryGetValue(address, out var words))
                {
                    continue;
                }

                foreach (var element in elements)
                {
                    var named = element.All(position => !position.NextVerse && position.Position <= words.Count
                                                                             && ReceivedTextPositions.Agrees(words[position.Position - 1].Text, position.Form))
                        ? element.Select(position => words[position.Position - 1].Id).Where(id => !linked.Contains(id)).Distinct().ToList()
                        : null;
                    if (named is not { Count: > 0 })
                    {
                        disagreed++;
                        continue;
                    }

                    named = [.. named.Where(id => GoesUntranslated(Morphology(greek.Words[(int)id].Morphology)))];
                    if (named.Count == 0)
                    {
                        idioms++;
                        continue;
                    }

                    unrendered.Add(named);
                    var list = byVerse.TryGetValue(own[address], out var held) ? held : byVerse[own[address]] = [];
                    list.AddRange(named);
                }
            }

            return new Links
            {
                All = all,
                Unrendered = unrendered,
                _byVerse = all.GroupBy(link => link.Verse).ToDictionary(group => group.Key, group => group.ToList()),
                _unrendered = byVerse,
                _summary = $"{laid}; {unrendered.Count} elements printing no word stand for a word of {witness} no element names, "
                           + $"{disagreed} name another word or one an element names, {idioms} over a word that is not an article, "
                           + "conjunction or particle left unstated",
            };
        }

        /// <summary>
        /// The stated links to Scrivener carried to another edition: each Greek word over the edition's pairing with Scrivener
        /// (<see cref="GreekWitnessSource.Drafts"/>), the link as sure as its least sure word. A word the edition does not pair
        /// is left out of the link, and so is one the edition prints in another verse; a link none of whose words is left is
        /// no link.
        /// </summary>
        private static Links Carried(ProductTexts texts, string witness, Action<string> log)
        {
            var hub = Of(texts, Scrivener, log);
            var drafts = GreekWitnessSource.Drafts(texts[witness], texts[Scrivener], out _);
            var over = new Dictionary<long, (long Word, double Confidence)>(150_000);
            foreach (var draft in drafts)
            {
                if (draft is { From.Count: 1, To.Count: 1, Relation: LinkRelation.Equals or LinkRelation.Renders })
                {
                    over.TryAdd(draft.To[0] - GreekWitnessSource.Second, (draft.From[0], draft.Confidence));
                }
            }

            var english = texts[KingJames];
            var edition = texts[witness];
            var all = new List<Link>(hub.Count);
            int partial = 0, lost = 0;
            foreach (var link in hub.All)
            {
                // Where the edition divides the verses otherwise (Rom 1:3-4), Scrivener's word can pair with one the edition
                // prints in the next verse; no verse link of the King James and the edition joins those two verses.
                var address = english.Primary(link.Verse);
                var carried = link.Greek.Where(over.ContainsKey).Select(word => over[word])
                    .Where(word => edition.Primary(edition.Words[(int)word.Word].Key.Verse) == address).ToList();
                if (carried.Count == 0)
                {
                    lost++;
                    continue;
                }

                partial += carried.Count < link.Greek.Count ? 1 : 0;
                all.Add(new Link(link.Verse, link.English, [.. carried.Select(word => word.Word).Distinct().Order()], carried.Min(word => word.Confidence)));
            }

            var unrendered = hub._unrendered.ToDictionary(verse => verse.Key,
                verse => verse.Value.Where(over.ContainsKey).Select(word => over[word].Word).ToList());
            return new Links
            {
                All = all,
                _byVerse = all.GroupBy(link => link.Verse).ToDictionary(group => group.Key, group => group.ToList()),
                _unrendered = unrendered,
                _summary = $"{all.Count} of the {hub.Count} links stated to {Scrivener} carried to {witness} ({partial} without a word it does "
                           + $"not pair), {lost} with no word it pairs",
            };
        }
    }
}
