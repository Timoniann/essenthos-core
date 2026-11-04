using System.Text.Json;
using Essenthos.Core.Conversion;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Mapping;

/// <summary>
/// A pair's <c>strong</c> method file made from the two texts' words and frames, for a translation that carries its own
/// Strong numbers in its words product (DOC-0224 §6.1): every verse of the translation is matched against the witness's
/// words at every frame address it stands at, by <see cref="StrongNumberMatch"/>, as <see cref="TaggedTextLinkLoader"/>
/// matches it in the database. Reads no database.
///
/// <para>
/// Where the database's loader consults links already there, this reads the pair's other files: a match touching a word a
/// stated or a person's link names is left out unless it names exactly that link's words (then it is a claim on it), and
/// a word a stated or a person's absence names is left out of every match. What the database's loader did to the
/// aligner's links is the merge's business here. A Greek witness is matched with the redirects Strong's Greek dictionary
/// offers and the verses bear out (<see cref="GreekNumberResolution"/>), read from <c>Strong/StrongGreek.xml</c>.
/// </para>
///
/// <para>
/// A row keeps the id the previous file gave its words; a new shape has none, and the merge numbers it.
/// </para>
/// </summary>
internal static class StrongFiles
{
    public const string Producer = "forge strong-pairs";

    private const string GreekDictionary = "Strong/StrongGreek.xml";

    /// <summary>What the file holds and how much of it the previous file already held.</summary>
    internal sealed record Outcome(string Pair, int Verses, int Tagged, int Matched, int Rows, int Unambiguous, int Paired, int Contended,
        int Testified, int Kept, int Gone)
    {
        public override string ToString() =>
            $"{Pair}: {Verses} verses, {Matched} of {Tagged} tagged words matched; {Rows} rows ({Unambiguous} unambiguous, {Paired} paired, "
            + $"{Contended} contended), {Testified} left to a stated link; {Kept} shapes the previous file held, {Rows - Kept} new, {Gone} gone";
    }

    /// <summary>
    /// Whether the pair's Strong rows are this producer's to write: both texts' words and frames are in the store, and the
    /// previous file, if any, holds only the rows of the translation's own numbers.
    /// </summary>
    public static string? Refusal(ProductStore store, string pair)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        foreach (var product in new[] { TextWord.ProductOf(fromText), FrameFile.Product(fromText), TextWord.ProductOf(toText), FrameFile.Product(toText) })
        {
            if (!store.Exists(product))
            {
                return $"the store has no {product}";
            }
        }

        if (MappingMethods.LaysStrong(fromText, toText))
        {
            return $"{fromText}'s numbers are laid from another edition, which its own loader matches";
        }

        var sources = Sources(store, pair);
        var own = OwnSource(fromText);
        return sources.FirstOrDefault(source => !source.StartsWith(own, StringComparison.Ordinal)) is { } other
            ? $"its strong file holds rows of another source (\"{other}\"), which another loader writes"
            : sources.Count > 1
                ? $"its strong file holds rows of {sources.Count} sources"
                : null;
    }

    /// <summary>The start of the source every row of a translation's own numbers carries; the witness it names follows.</summary>
    private static string OwnSource(string fromText) => TaggedTextLinkLoader.Source(fromText, string.Empty);

    /// <summary>The sources the pair's strong file names now.</summary>
    private static List<string> Sources(ProductStore store, string pair)
    {
        var product = MappingProducts.Of(pair, MappingMethods.Strong);
        if (!store.Exists(product))
        {
            return [];
        }

        using var reader = store.Open(product, MappingRow.Schema);
        return [.. reader.Rows().Select(row => row.Source).Distinct(StringComparer.Ordinal)];
    }

    public static Outcome Write(ProductStore store, string pair)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        if (Refusal(store, pair) is { } refusal)
        {
            throw new InvalidOperationException($"{pair}: {refusal}.");
        }

        // The witness keeps the name the corpus gave it when the pair was first matched (SCRIVENER1894 for TR1894).
        var source = Sources(store, pair) is [var named] ? named : TaggedTextLinkLoader.Source(fromText, toText);
        var witness = Witness(store, toText);
        var series = witness.Series;
        var translation = Translation(store, fromText, series);
        var pairs = new List<(List<WordKey> Words, List<string?> Numbers, List<bool> Auxiliaries, List<StrongNumberMatch.WitnessWord> Against, List<WordKey> AgainstKeys)>(32_000);
        int tagged = 0;
        foreach (var (addresses, words) in translation)
        {
            tagged += words.Count;
            var against = witness.Pool(addresses);
            if (against.Count > 0)
            {
                pairs.Add(([.. words.Select(w => w.Key)], [.. words.Select(w => w.Strong)], [.. words.Select(w => w.Auxiliary)],
                    [.. against.Select(a => a.Word)], [.. against.Select(a => a.Key)]));
            }
        }

        var resolution = series == 'G' ? Resolution(store, pairs.Select(p => (p.Numbers, p.Against))) : [];
        var (absent, testimony) = Others(store, pair);
        var keys = new Dictionary<long, WordKey>();
        var rows = new List<MappingRow>(300_000);
        int matched = 0, testified = 0;
        int[] kinds = new int[3];
        var next = 0L;
        foreach (var (words, numbers, auxiliaries, against, againstKeys) in pairs)
        {
            var ids = words.Select(_ => next++).ToList();
            for (var at = 0; at < words.Count; at++)
            {
                keys[ids[at]] = words[at];
            }

            var witnessIds = new List<StrongNumberMatch.WitnessWord>(against.Count);
            for (var at = 0; at < against.Count; at++)
            {
                var id = next++;
                keys[id] = againstKeys[at];
                witnessIds.Add(against[at] with { Id = id });
            }

            var drafts = StrongNumberMatch.Verse(
                [.. ids.Select((id, at) => new StrongNumberMatch.TaggedWord(id, [numbers[at]!], auxiliaries[at]))], witnessIds, resolution, out _);
            foreach (var draft in drafts)
            {
                var from = draft.From.Select(id => keys[id]).Where(key => !absent.Contains(key.Own)).ToList();
                if (from.Count == 0)
                {
                    continue;
                }

                matched += from.Count;
                var row = new MappingRow(null, MappingText.Keys(from), MappingText.Keys(draft.To.Select(id => keys[id])), "renders",
                    MappingMethods.Strong, draft.Confidence, source, draft.Kind.ToString().ToLowerInvariant(), null, null);
                if (from.Any(key => testimony.Words.Contains(key.Own)) && !testimony.Shapes.Contains(row.Shape))
                {
                    testified++;
                    continue;
                }

                kinds[(int)draft.Kind]++;
                rows.Add(row);
            }

            keys.Clear();
        }

        var previous = Previous(store, pair);
        rows = [.. rows.Select(row => previous.TryGetValue(row.Shape, out var id) ? row with { Id = id } : row)];
        var kept = rows.Count(row => row.Id is not null);
        var made = rows.Select(row => row.Shape).ToHashSet(StringComparer.Ordinal);
        var gone = previous.Keys.Count(shape => !made.Contains(shape));

        var sorted = rows.Select(r => (Sort: MappingText.SortKey(r.From, r.To, fromText, toText), Row: r)).ToList();
        sorted.Sort((a, b) => MappingExport.Order(a.Sort.Side, a.Sort.Key, a.Row, b.Sort.Side, b.Sort.Key, b.Row));
        var inputs = new[] { TextWord.ProductOf(fromText), FrameFile.Product(fromText), TextWord.ProductOf(toText), FrameFile.Product(toText) }
            .Select(store.ProductInput)
            .Concat(new[] { MappingMethods.Stated, MappingMethods.Manual }
                .Select(method => store.Find(MappingProducts.Of(pair, method))).OfType<string>().Select(store.Input))
            .Concat(series == 'G' ? [store.Input(GreekDictionary)] : [])
            .ToList();
        using (var writer = store.Create(MappingProducts.Of(pair, MappingMethods.Strong), ProductKind.Machine, MappingRow.Schema,
                   ProductHeader.DescribeProducer($"{Producer} {pair}"), inputs,
                   header => header.Notes.Add(new(MethodFiles.RemadeNote, $"every row, from {fromText}'s and {toText}'s words as the inputs name them"))))
        {
            writer.WriteAll(sorted.Select(r => r.Row));
        }

        MappingFiles.RecordHashes(store, pair, rows);
        return new Outcome(pair, pairs.Count, tagged, matched, rows.Count, kinds[(int)StrongMatchKind.Unambiguous],
            kinds[(int)StrongMatchKind.Paired], kinds[(int)StrongMatchKind.Contended], testified, kept, gone);
    }

    private readonly record struct Numbered(WordKey Key, string? Strong, bool Auxiliary);

    /// <summary>The translation's verses in key order, each with its frame addresses (primary first) and its words carrying a number of the series.</summary>
    private static List<(List<(int, int, int)> Addresses, List<Numbered> Words)> Translation(ProductStore store, string text, char series)
    {
        var frame = Frame(store, text);
        var german = Language(store, text) == "deu";
        var verses = new List<(List<(int, int, int)>, List<Numbered>)>(32_000);
        VerseKey? current = null;
        List<Numbered>? words = null;
        using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        foreach (var word in reader.Rows())
        {
            if (current != word.Key.Verse)
            {
                current = word.Key.Verse;
                words = [];
                verses.Add((Addresses(frame, current.Value), words));
            }

            if (word.Strong is { Length: > 0 } number && number[0] == series)
            {
                words!.Add(new Numbered(word.Key, number, german && MisplacedNumbers.IsAuxiliary(word.Surface)));
            }
        }

        return [.. verses.Where(v => v.Item2.Count > 0)];
    }

    private static string? Language(ProductStore store, string text)
    {
        using var reader = store.Open(TextField.ProductOf(text), TextField.Schema);
        return reader.Rows().FirstOrDefault(field => field.Field == "language")?.Value;
    }

    private static Dictionary<VerseKey, IReadOnlyList<VerseKey>> Frame(ProductStore store, string text)
    {
        using var reader = store.Open(FrameFile.Product(text), FrameRow.Schema);
        return reader.Rows().SelectMany(row => row.Verses()).ToDictionary(v => v.Own, v => v.Canonical);
    }

    private static List<(int, int, int)> Addresses(Dictionary<VerseKey, IReadOnlyList<VerseKey>> frame, VerseKey own) =>
        frame.TryGetValue(own, out var canonical)
            ? [canonical[0].Tuple(), .. canonical.Skip(1).Select(a => a.Tuple()).Order()]
            : [FrameFile.AtOwn(own).Tuple()];

    private static (int, int, int) Tuple(this VerseKey key) => (key.Book, key.Chapter, key.Verse);

    /// <summary>The witness's words by verse and the verses standing at each frame address.</summary>
    private sealed class WitnessWords
    {
        public required char Series { get; init; }

        public required Dictionary<VerseKey, List<(WordKey Key, StrongNumberMatch.WitnessWord Word)>> ByVerse { get; init; }

        public required Dictionary<(int, int, int), List<VerseKey>> ByAddress { get; init; }

        public required Dictionary<VerseKey, (int, int, int)> Primary { get; init; }

        /// <summary>The witness's words at any of these addresses, each verse once, in the order of the verses' primary addresses.</summary>
        public List<(WordKey Key, StrongNumberMatch.WitnessWord Word)> Pool(List<(int, int, int)> at) =>
        [
            .. at.Where(ByAddress.ContainsKey)
                .SelectMany(address => ByAddress[address])
                .Distinct()
                .Where(ByVerse.ContainsKey)
                .OrderBy(verse => Primary[verse])
                .ThenBy(verse => verse)
                .SelectMany(verse => ByVerse[verse]),
        ];
    }

    private static WitnessWords Witness(ProductStore store, string text)
    {
        var frame = Frame(store, text);
        var byVerse = new Dictionary<VerseKey, List<(WordKey, StrongNumberMatch.WitnessWord)>>(32_000);
        int hebrew = 0, greek = 0;
        using (var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text)))
        {
            foreach (var word in reader.Rows())
            {
                if (!byVerse.TryGetValue(word.Key.Verse, out var words))
                {
                    byVerse[word.Key.Verse] = words = [];
                }

                var (suffixed, infinitive) = Features(word.Strong, word.Morphology);
                words.Add((word.Key, new StrongNumberMatch.WitnessWord(0, word.Strong, suffixed, infinitive)));
                if (word.Strong is { Length: > 0 } number)
                {
                    if (number[0] == 'H') hebrew++;
                    else if (number[0] == 'G') greek++;
                }
            }
        }

        var byAddress = new Dictionary<(int, int, int), List<VerseKey>>(byVerse.Count + 256);
        var primary = new Dictionary<VerseKey, (int, int, int)>(byVerse.Count);
        foreach (var verse in byVerse.Keys)
        {
            var addresses = Addresses(frame, verse);
            primary[verse] = addresses[0];
            foreach (var address in addresses)
            {
                if (!byAddress.TryGetValue(address, out var standing))
                {
                    byAddress[address] = standing = [];
                }

                standing.Add(verse);
            }
        }

        return new WitnessWords { Series = greek > hebrew ? 'G' : 'H', ByVerse = byVerse, ByAddress = byAddress, Primary = primary };
    }

    /// <summary>Whether an object marker carries a suffix, and whether a word is an infinitive absolute, from its morphology.</summary>
    private static (bool Suffixed, bool InfinitiveAbsolute) Features(string? strong, string? morphology)
    {
        if (morphology is null || strong is null || strong[0] != 'H')
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

    /// <summary>The redirects of Strong's Greek dictionary the verses bear out, as the database's loader admits them.</summary>
    private static Dictionary<string, NumberRedirect> Resolution(
        ProductStore store,
        IEnumerable<(List<string?> Numbers, List<StrongNumberMatch.WitnessWord> Against)> pairs)
    {
        var path = store.FullPath(GreekDictionary);
        var dictionary = new StrongXmlParser().ParseGreek(File.ReadAllText(path))
            .Select(entry => new GreekEntry(entry.StrongNumber, entry.Lemma, entry.Derivation))
            .ToList();
        var list = pairs.ToList();
        var attested = list.SelectMany(p => p.Against).Where(w => w.Strong is not null).Select(w => w.Strong!).ToHashSet(StringComparer.Ordinal);
        return GreekNumberResolution.Admit(dictionary, attested, Occurrences(list));

        static IEnumerable<NumberOccurrence> Occurrences(List<(List<string?> Numbers, List<StrongNumberMatch.WitnessWord> Against)> pairs)
        {
            foreach (var (numbers, against) in pairs)
            {
                var greek = against.Where(w => w.Strong is not null).Select(w => w.Strong!).ToHashSet(StringComparer.Ordinal);
                foreach (var number in numbers)
                {
                    yield return new NumberOccurrence(number!, greek);
                }
            }
        }
    }

    /// <summary>The words a stated or a person's absence names, and the words and shapes their links name.</summary>
    private static (HashSet<string> Absent, (HashSet<string> Words, HashSet<string> Shapes) Testimony) Others(ProductStore store, string pair)
    {
        var absent = new HashSet<string>(StringComparer.Ordinal);
        var words = new HashSet<string>(StringComparer.Ordinal);
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in new[] { MappingMethods.Stated, MappingMethods.Manual })
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

    /// <summary>The id the previous file gave each shape.</summary>
    private static Dictionary<string, long> Previous(ProductStore store, string pair)
    {
        var product = MappingProducts.Of(pair, MappingMethods.Strong);
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!store.Exists(product))
        {
            return ids;
        }

        using var reader = store.Open(product, MappingRow.Schema);
        foreach (var row in reader.Rows())
        {
            if (row.Id is { } id)
            {
                ids.TryAdd(row.Shape, id);
            }
        }

        return ids;
    }
}
