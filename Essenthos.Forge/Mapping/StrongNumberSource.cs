using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Essenthos.Core.Strong;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Mapping;

/// <summary>
/// A pair's <c>strong</c> rows from a translation's Strong numbers, matched against the witness's words at every frame
/// address the translation's verse stands at, by <see cref="StrongNumberMatch"/>, as <see cref="TaggedTextLinkLoader"/>
/// matches them in the database (DOC-0224 §6.1). Reads no database.
///
/// <para>
/// **Where the numbers are.** Luther 1912's are its own tags, the <c>strong</c> column of its words product. The others are
/// laid onto the translation from a numbered edition of the same text (<see cref="SideStrongNumbers"/>): Bob Jones
/// University's numbering of the Synodal, FHL's of the Chinese Union, Gómez's of the Reina-Valera, and the CrossWire
/// modules of the Segond, Darby, the Schlachter and the Revised Literal. The words product keeps only a tag's first number,
/// so the edition is laid onto the words again here (<see cref="SynodalStrongLayer.Lay"/>, as the conversion and the
/// database's loaders lay it), which gives every number a tag names and the phrase it tags as one unit; a number the words
/// product holds otherwise (a correction in <c>texts/&lt;SLUG&gt;/corrections.tsv</c>) is taken from the words.
/// </para>
///
/// <para>
/// Where the database's loader consults links already there, this reads the pair's other files: a match touching a word a
/// stated or a person's link names is left out unless it names exactly that link's words (then it is a claim on it), and
/// a word a stated or a person's absence names is left out of every match. What the database's loader did to the
/// aligner's links is the merge's business here. A Greek witness is matched with the redirects Strong's Greek dictionary
/// offers and the verses bear out (<see cref="GreekNumberResolution"/>), read from <c>Strong/StrongGreek.xml</c>.
/// </para>
/// </summary>
internal sealed class StrongNumberSource : IPairSource
{
    public const string GreekDictionary = "Strong/StrongGreek.xml";

    private const string Against = ", matched within the verse against ";

    private readonly string _translation;
    private readonly IReadOnlyList<string> _witnesses;
    private readonly string? _credit;
    private readonly IReadOnlyList<string> _prefixes;
    private readonly ConditionalWeakTable<ProductTexts, Lazy<Dictionary<long, WordTag>>> _tags = new();
    private readonly ConditionalWeakTable<ProductTexts, Lazy<Dictionary<long, long>>> _moved = new();

    /// <param name="credit">Whose numbering is laid onto the translation; null for numbers the translation carries itself.</param>
    private StrongNumberSource(string translation, IReadOnlyList<string> witnesses, string? credit)
    {
        _translation = translation;
        _witnesses = witnesses;
        _credit = credit;
        _prefixes = credit is null
            ? [.. TextAliases.Of(translation).Append(translation).Distinct(StringComparer.Ordinal).Select(t => TaggedTextLinkLoader.Source(t, string.Empty))]
            : [credit + Against];
    }

    /// <summary>Every numbering the corpus matches, with the witnesses its load matched it against.</summary>
    public static readonly IReadOnlyList<StrongNumberSource> All =
    [
        new("LUTH1912", ["BHSA", "NESTLE1904", "RP2018", "TR1550", "TR1894"], null),
        new(Bible4uTextSource.Synodal, SynodalStrongLinkLoader.Witnesses, SynodalStrongLinkLoader.Credit),
        new(SwordTextSource.ChineseUnion, UnionStrongLinkLoader.Witnesses, UnionStrongLinkLoader.Credit),
        new(SwordTextSource.ChineseUnionSimplified, UnionStrongLinkLoader.Witnesses, UnionStrongLinkLoader.Credit),
        new(EbibleTextSource.ReinaValera, ReinaValeraStrongLinkLoader.Witnesses, ReinaValeraStrongLinkLoader.Credit),
        .. CrossWireStrongLinkLoader.All.Select(numbering => new StrongNumberSource(numbering.Slug, numbering.Witnesses, numbering.Credit)),
    ];

    public string Method => MappingMethods.Strong;

    public string Name => _translation.ToLowerInvariant();

    private bool Laid => _credit is not null;

    public bool States(string pair) =>
        MappingProducts.Split(pair) is var (from, to) && from == _translation && _witnesses.Contains(to);

    public bool Makes(string source) => _prefixes.Any(prefix => source.StartsWith(prefix, StringComparison.Ordinal));

    public IEnumerable<string> Inputs(ProductStore store, string pair)
    {
        var (_, to) = MappingProducts.Split(pair);
        var inputs = new List<string>();
        foreach (var method in Consulted)
        {
            if (store.Find(MappingProducts.Of(pair, method)) is { } file)
            {
                inputs.Add(file);
            }
        }

        if (!Laid && Placed(_translation))
        {
            // The text read again up to MisplacedNumbers, which decides from the originals' words, to know what it moved.
            // Not a text product the frame names already: a path named twice never reads as current (PairSources.Write).
            var named = MethodFiles.TextInputs(store, pair).Select(input => input.Path).ToHashSet(StringComparer.Ordinal);
            inputs.AddRange(TextConverter.Inputs(store, TextCatalogue.Find(_translation)).Select(input => input.Path).Where(path => !named.Contains(path)));
        }

        if (IndexContext.Declared(store, to).Language == "grc")
        {
            inputs.Add(GreekDictionary);
        }

        if (Laid)
        {
            var (edition, stated) = SideStrongNumbers.Edition(_translation);
            inputs.Add(edition);
            if (stated && store.Find(TextNote.ProductOf(_translation)) is { } notes)
            {
                inputs.Add(notes);
            }
        }

        return inputs;
    }

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var store = texts.Store;
        var translation = texts[fromText];
        var witness = WitnessWords.Of(texts, toText);
        var series = witness.Series;
        var source = Named(store, pair, toText);
        var tags = Laid ? _tags.GetValue(texts, t => new Lazy<Dictionary<long, WordTag>>(() => LaidTags(t, log))).Value : null;
        var german = translation.Language == "deu";

        var verses = new List<(List<StrongNumberMatch.TaggedWord> Tagged, Dictionary<long, List<long>> Members, List<(long Id, StrongNumberMatch.WitnessWord Word)> Against)>(32_000);
        int tagged = 0, unplaced = 0;
        foreach (var own in translation.Verses)
        {
            var units = new List<StrongNumberMatch.TaggedWord>();
            var members = new Dictionary<long, List<long>>();
            var byUnit = new Dictionary<long, long>();
            foreach (var id in translation.InVerse(own))
            {
                var (numbers, unit) = Numbers(translation, tags, id, series);
                if (numbers is null)
                {
                    continue;
                }

                tagged++;
                if (byUnit.TryGetValue(unit, out var first))
                {
                    members[first].Add(id);
                    continue;
                }

                byUnit[unit] = id;
                members[id] = [id];
                units.Add(new StrongNumberMatch.TaggedWord(id, numbers, german && MisplacedNumbers.IsAuxiliary(translation.Words[id].Surface)));
            }

            if (units.Count == 0)
            {
                continue;
            }

            var against = witness.Pool(translation.Addresses(own));
            if (against.Count == 0)
            {
                unplaced += members.Values.Sum(m => m.Count);
                continue;
            }

            verses.Add((units, members, against));
        }

        var resolution = series == StrongNumbers.Greek ? Resolution(store, verses.Select(v => (v.Tagged, v.Against))) : [];
        var (absent, testimony) = Others(store, pair);
        var rows = new List<MappingRow>(300_000);
        int matched = 0, testified = 0, regained = 0;
        var kinds = new int[3];
        var moved = Laid || !Placed(_translation) ? null : _moved.GetValue(texts, t => new Lazy<Dictionary<long, long>>(() => Moved(t, _translation, log))).Value;
        foreach (var (units, members, against) in verses)
        {
            // The witness's words are numbered after the translation's, in the order the pool gives them, so one id names
            // one word on either side.
            var offset = (long)translation.Words.Count;
            var drafts = StrongNumberMatch.Verse(units, [.. against.Select((a, at) => a.Word with { Id = offset + at })], resolution, out _);
            foreach (var draft in drafts)
            {
                var ids = draft.From.SelectMany(id => members[id]).ToList();
                var from = ids.Select(translation.Key).Where(key => !absent.Contains(key.Own)).ToList();
                if (from.Count == 0)
                {
                    continue;
                }

                matched += from.Count;

                // The auxiliary or pronoun the conversion took a verb's number from joins the verb's link, attached to it.
                List<(WordKey Word, WordKey Verb)> attached = moved is null
                    ? []
                    : ids.Where(id => moved.ContainsKey(id) && !absent.Contains(translation.Key(id).Own))
                        .Select(verb => (Word: translation.Key(moved[verb]), Verb: translation.Key(verb)))
                        .Where(pair => !absent.Contains(pair.Word.Own) && !from.Contains(pair.Word))
                        .ToList();
                from.AddRange(attached.Select(pair => pair.Word));
                regained += attached.Count;
                var row = new MappingRow(MappingText.Keys(from), MappingText.Keys(draft.To.Select(id => witness.Text.Key(against[(int)(id - offset)].Id))), "renders",
                    MappingMethods.Strong, draft.Confidence, source, null,
                    attached.Count == 0 ? null : string.Join(' ', attached.OrderBy(pair => pair.Word).Select(pair => $"{pair.Word.Own}=attached@{pair.Verb.Own}")),
                    null);
                if (from.Any(key => testimony.Words.Contains(key.Own)))
                {
                    if (!testimony.Shapes.Contains(row.Shape))
                    {
                        testified++;

                        // A laid phrase may hold a word an approved EVIDENTIA verdict states absent, which this producer cannot read:
                        // the merge takes that word out and keeps the rest where it then names the stated link's words.
                        if (Laid)
                        {
                            rows.Add(row with { Status = MappingStatus.YieldsToStated });
                        }

                        continue;
                    }

                    // A match naming exactly a stated link's words is a claim on that link, as the database's loader made it.
                    row = row with { Evidence = TaggedTextLinkLoader.CorroborationNote };
                }

                kinds[(int)draft.Kind]++;
                rows.Add(row);
            }
        }

        log($"{verses.Count} verses, {matched} of {tagged} tagged words matched ({unplaced} in verses the witness lacks); {rows.Count} rows "
            + $"({kinds[(int)StrongMatchKind.Unambiguous]} unambiguous, {kinds[(int)StrongMatchKind.Paired]} paired, "
            + $"{kinds[(int)StrongMatchKind.Contended]} contended), {testified} left to a stated link"
            + (moved is null ? string.Empty : $"; {regained} auxiliaries or pronouns attached to the verb their number moved to"));
        return rows;
    }

    /// <summary>Whether the conversion moves a verb's number off an auxiliary or pronoun of the text (<see cref="MisplacedNumbers"/>).</summary>
    private static bool Placed(string slug) => new MisplacedNumbers().AppliesTo(slug);

    /// <summary>
    /// The word each verb took its number from at conversion (<see cref="MisplacedNumbers.VerbMoves"/>), by the verb's id
    /// in the words product: <em>geboten</em> for <em>hat</em>. A move the words product no longer bears out (a correction
    /// put a number back on the auxiliary, or took the verb's) is left out.
    /// </summary>
    private static Dictionary<long, long> Moved(ProductTexts texts, string slug, Action<string> log)
    {
        var text = texts[slug];
        var moved = new Dictionary<long, long>();
        var moves = MisplacedNumbers.VerbMoves(texts.Store, TextCatalogue.Find(slug));
        foreach (var (verse, word, verb) in moves)
        {
            var ids = text.InVerse(verse).ToList();
            if (word <= ids.Count && text.Words[ids[verb - 1]].Strong is not null && text.Words[ids[word - 1]].Strong is null)
            {
                moved[ids[verb - 1]] = ids[word - 1];
            }
        }

        log($"{moves.Count} verb numbers the conversion moved off an auxiliary or pronoun, {moved.Count} of them as the words product holds them");
        return moved;
    }

    /// <summary>The numbers of the series a word's tag names and the unit it belongs to, or null where it names none.</summary>
    private static (IReadOnlyList<string>? Numbers, long Unit) Numbers(ProductText text, Dictionary<long, WordTag>? tags, int id, char series)
    {
        if (tags is null)
        {
            return text.Words[id].Strong is { Length: > 0 } own && own[0] == series ? ([own], id) : (null, id);
        }

        if (!tags.TryGetValue(id, out var tag))
        {
            return (null, id);
        }

        var numbers = tag.Numbers.Where(number => number.Length > 0 && number[0] == series).ToList();
        return numbers.Count == 0 ? (null, id) : (numbers, tag.Unit);
    }

    /// <summary>
    /// The edition laid onto the translation's words as the conversion lays it, a word's id its place in the words product;
    /// where the words product's number differs from the tag's first (a correction set or cleared it), the words' stands.
    /// </summary>
    private Dictionary<long, WordTag> LaidTags(ProductTexts texts, Action<string> log)
    {
        var text = texts[_translation];
        var (_, stated) = SideStrongNumbers.Edition(_translation);
        var addresses = stated ? Stated(texts.Store, _translation) : [];
        var corpus = new List<CorpusVerse>(text.Verses.Count);
        foreach (var verse in text.Verses.OrderBy(v => (v.Book, v.Chapter, v.Verse)))
        {
            corpus.Add(new CorpusVerse(verse.Book, verse.Chapter, verse.Verse, addresses.GetValueOrDefault(verse) ?? [],
                [.. text.InVerse(verse).Select(id => new CorpusWord(id, text.Words[id].Surface))]));
        }

        var laid = SynodalStrongLayer.Lay(SideStrongNumbers.ReadEdition(_translation, texts.Store.ResourcesPath), corpus);
        var (tags, set, cleared) = Overlay(laid.Tags, [.. text.Words.Select(word => word.Strong)]);
        log($"{SideStrongNumbers.Edition(_translation).Source} laid onto {laid.Verses} verses ({laid.Renumbered} through the address the "
            + $"text prints, {laid.Joined} with a neighbour, {laid.Refused} refused, {laid.Unused} edition verses unused), {laid.TaggedWords} "
            + $"words numbered; {set} numbers the words product holds otherwise, {cleared} it clears");
        return tags;
    }

    /// <summary>
    /// The laid tags with the words product's numbers over them: where a word's number is not its tag's first (a correction
    /// set or cleared it), the word's number stands, first among the tag's others, or the tag goes; a number on a word the
    /// edition left untagged is a tag of its own. Returns how many were set and cleared.
    /// </summary>
    internal static (Dictionary<long, WordTag> Tags, int Set, int Cleared) Overlay(IReadOnlyDictionary<long, WordTag> laid, IReadOnlyList<string?> numbers)
    {
        var tags = new Dictionary<long, WordTag>(laid);
        int set = 0, cleared = 0;
        var unit = -1;
        for (var id = 0; id < numbers.Count; id++)
        {
            var number = numbers[id];
            var tag = tags.TryGetValue(id, out var found) ? found : (WordTag?)null;
            var first = tag is { Numbers.Count: > 0 } t ? t.Numbers[0] : null;
            if (number == first)
            {
                continue;
            }

            if (number is null)
            {
                tags.Remove(id);
                cleared++;
            }
            else
            {
                tags[id] = tag is { } had && had.Numbers.Count > 0
                    ? new WordTag([number, .. had.Numbers.Skip(1)], had.Unit)
                    : new WordTag([number], unit--);
                set++;
            }
        }

        return (tags, set, cleared);
    }

    /// <summary>The addresses the text's own file prints for its verses (the <c>stated</c> notes of its notes product).</summary>
    private static Dictionary<VerseKey, IReadOnlyList<VerseAddress>> Stated(ProductStore store, string slug)
    {
        var stated = new Dictionary<VerseKey, IReadOnlyList<VerseAddress>>();
        var product = TextNote.ProductOf(slug);
        if (!store.Exists(product))
        {
            return stated;
        }

        using var reader = store.Open(product, TextNote.Schema);
        foreach (var group in reader.Rows().Where(note => note.Scope == "stated").GroupBy(note => VerseKey.Parse(note.At)))
        {
            stated[group.Key] = [.. group.OrderBy(note => note.Position).Select(note => Address(note.Content))];
        }

        return stated;
    }

    /// <summary>A <c>stated</c> note's address, <c>chapter[label]:verse</c> (<c>22:1</c>, <c>9a:3</c>), as the laying reads it.</summary>
    internal static VerseAddress Address(string content)
    {
        var colon = content.IndexOf(':');
        var chapter = content[..colon];
        var digits = chapter.Length - chapter.TrimStart("0123456789".ToCharArray()).Length;
        return new VerseAddress(int.Parse(chapter[..digits], CultureInfo.InvariantCulture), int.Parse(content[(colon + 1)..], CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The source the pair's rows carry: the numbering, matched against the witness under the name the previous file gave
    /// it (the corpus's old name, SCRIVENER1894 for TR1894), or its slug.
    /// </summary>
    private string Named(ProductStore store, string pair, string toText)
    {
        var fallback = _credit is null ? TaggedTextLinkLoader.Source(_translation, toText) : _credit + Against + toText;
        return Previous(store, pair, MappingMethods.Strong, source => Makes(source) && NamesWitness(source, toText)) ?? fallback;
    }

    /// <summary>The source most rows of a pair's method file carry among those the test accepts, or null.</summary>
    internal static string? Previous(ProductStore store, string pair, string method, Func<string, bool> accepts)
    {
        var product = MappingProducts.Of(pair, method);
        if (!store.Exists(product))
        {
            return null;
        }

        using var reader = store.Open(product, MappingRow.Schema);
        return reader.Rows().Select(row => row.Source).Where(accepts)
            .GroupBy(source => source, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    /// <summary>The witness's words with what the matching reads of them, and the verses standing at each frame address.</summary>
    internal sealed class WitnessWords
    {
        private static readonly ConditionalWeakTable<ProductTexts, ConcurrentDictionary<string, Lazy<WitnessWords>>> Cache = new();

        public required ProductText Text { get; init; }

        public required char Series { get; init; }

        public required StrongNumberMatch.WitnessWord[] Words { get; init; }

        public required Dictionary<(int, int, int), List<VerseKey>> ByAddress { get; init; }

        public static WitnessWords Of(ProductTexts texts, string slug) =>
            Cache.GetValue(texts, _ => new ConcurrentDictionary<string, Lazy<WitnessWords>>(StringComparer.Ordinal))
                .GetOrAdd(slug, s => new Lazy<WitnessWords>(() => Read(texts[s]))).Value;

        /// <summary>The witness's words at any of these addresses, each verse once, in the order of the verses' primary addresses.</summary>
        public List<(long Id, StrongNumberMatch.WitnessWord Word)> Pool(IReadOnlyList<VerseKey> at) =>
        [
            .. at.Select(address => (address.Book, address.Chapter, address.Verse))
                .Where(ByAddress.ContainsKey)
                .SelectMany(address => ByAddress[address])
                .Distinct()
                .OrderBy(Text.Primary)
                .ThenBy(verse => verse)
                .SelectMany(Text.InVerse)
                .Select(id => ((long)id, Words[id])),
        ];

        private static WitnessWords Read(ProductText text)
        {
            var words = new StrongNumberMatch.WitnessWord[text.Words.Count];
            int hebrew = 0, greek = 0;
            for (var id = 0; id < words.Length; id++)
            {
                var word = text.Words[id];
                var (suffixed, infinitive) = Features(word.Strong, word.Morphology);
                words[id] = new StrongNumberMatch.WitnessWord(id, word.Strong, suffixed, infinitive);
                if (word.Strong is { Length: > 0 } number)
                {
                    if (number[0] == StrongNumbers.Hebrew) hebrew++;
                    else if (number[0] == StrongNumbers.Greek) greek++;
                }
            }

            var byAddress = new Dictionary<(int, int, int), List<VerseKey>>(text.Verses.Count + 256);
            foreach (var verse in text.Verses)
            {
                foreach (var address in text.Addresses(verse).Select(a => (a.Book, a.Chapter, a.Verse)).Distinct())
                {
                    if (!byAddress.TryGetValue(address, out var standing))
                    {
                        byAddress[address] = standing = [];
                    }

                    standing.Add(verse);
                }
            }

            return new WitnessWords
            {
                Text = text, Series = greek > hebrew ? StrongNumbers.Greek : StrongNumbers.Hebrew, Words = words, ByAddress = byAddress,
            };
        }

        /// <summary>Whether an object marker carries a suffix, and whether a word is an infinitive absolute, from its morphology.</summary>
        private static (bool Suffixed, bool InfinitiveAbsolute) Features(string? strong, string? morphology)
        {
            if (morphology is null || strong is null || strong[0] != StrongNumbers.Hebrew)
            {
                return (false, false);
            }

            using var document = JsonDocument.Parse(morphology);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, false);
            }

            var suffixed = strong == ObjectMarker.Number && root.TryGetProperty(ObjectMarker.SuffixFeature, out _);
            var infinitive = root.TryGetProperty("tense", out var tense) && tense.ValueKind == JsonValueKind.String && tense.GetString() == "infa";
            return (suffixed, infinitive);
        }
    }

    /// <summary>Strong's Greek dictionary's entries, as the database's <c>strong_entry</c> holds them for the redirects.</summary>
    internal static List<GreekEntry> GreekEntries(ProductStore store) =>
        [.. new StrongXmlParser().ParseGreek(File.ReadAllText(store.FullPath(GreekDictionary)))
            .Select(entry => new GreekEntry(entry.StrongNumber, entry.Lemma, entry.Derivation))];

    /// <summary>Whether a source names this witness last, under its slug or an old name, in any case.</summary>
    internal static bool NamesWitness(string source, string witness)
    {
        var name = source[(source.LastIndexOf(' ') + 1)..];
        return TextAliases.Of(witness).Append(witness).Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The redirects of Strong's Greek dictionary the verses bear out, as the database's loader admits them.</summary>
    internal static Dictionary<string, NumberRedirect> Resolution(
        ProductStore store,
        IEnumerable<(List<StrongNumberMatch.TaggedWord> Tagged, List<(long Id, StrongNumberMatch.WitnessWord Word)> Against)> verses)
    {
        var dictionary = GreekEntries(store);
        var list = verses.ToList();
        var attested = list.SelectMany(v => v.Against).Where(w => w.Word.Strong is not null).Select(w => w.Word.Strong!).ToHashSet(StringComparer.Ordinal);
        return GreekNumberResolution.Admit(dictionary, attested, Occurrences(list));

        static IEnumerable<NumberOccurrence> Occurrences(List<(List<StrongNumberMatch.TaggedWord> Tagged, List<(long Id, StrongNumberMatch.WitnessWord Word)> Against)> verses)
        {
            foreach (var (tagged, against) in verses)
            {
                var greek = against.Where(w => w.Word.Strong is not null).Select(w => w.Word.Strong!).ToHashSet(StringComparer.Ordinal);
                foreach (var number in tagged.SelectMany(word => word.Numbers))
                {
                    yield return new NumberOccurrence(number, greek);
                }
            }
        }
    }

    /// <summary>
    /// The method files whose links the database's loader found standing when it matched and yielded to. An EVIDENTIA
    /// verdict's absence is not among them: the merge gives it way to any correspondence but the aligner's
    /// (<see cref="MappingMethods.EvidentiaAbsence"/>), so a Strong match over its word is the merge's to settle.
    /// </summary>
    private static readonly string[] Consulted = [MappingMethods.Stated, MappingMethods.Manual];

    /// <summary>
    /// The words a stated or a person's absence names, and the words and shapes the stated and the person's links name.
    /// </summary>
    internal static (HashSet<string> Absent, (HashSet<string> Words, HashSet<string> Shapes) Testimony) Others(ProductStore store, string pair)
    {
        var absent = new HashSet<string>(StringComparer.Ordinal);
        var words = new HashSet<string>(StringComparer.Ordinal);
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in Consulted)
        {
            var product = MappingProducts.Of(pair, method);
            if (!store.Exists(product))
            {
                continue;
            }

            using var reader = store.Open(product, MappingRow.Schema);
            foreach (var row in reader.Rows().Where(row => row.Status is null))
            {
                if (row.Relation == "expands")
                {
                    absent.UnionWith(MappingText.Split(row.From));
                }
                else if (!MappingMethods.IsAbsence(row.Relation))
                {
                    words.UnionWith(MappingText.Split(row.From));
                    shapes.Add(row.Shape);
                }
            }
        }

        return (absent, (words, shapes));
    }
}
