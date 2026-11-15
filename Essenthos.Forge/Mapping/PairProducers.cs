using System.Collections.Concurrent;
using Essenthos.Core.Conversion;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// One text's words as its products hold them, for a producer of method files that reads no database. A word's id here
/// is its place in <see cref="Words"/>, never the corpus's id; <see cref="Key"/> turns it back into the word's key.
/// </summary>
internal sealed class ProductText
{
    private readonly Dictionary<VerseKey, (int Start, int Count)> _verses;
    private readonly Dictionary<VerseKey, IReadOnlyList<VerseKey>> _frame;
    private readonly Lazy<Dictionary<(int Book, int Chapter, int Verse), List<int>>> _byAddress;
    private readonly Lazy<Dictionary<VerseKey, int>> _sequence;

    private ProductText(string slug, string language, List<TextWord> words, List<VerseKey> order,
        Dictionary<VerseKey, (int, int)> verses, Dictionary<VerseKey, IReadOnlyList<VerseKey>> frame, Func<Dictionary<VerseKey, int>> sequence)
    {
        Slug = slug;
        Language = language;
        Words = words;
        Verses = order;
        _verses = verses;
        _frame = frame;
        _byAddress = new Lazy<Dictionary<(int, int, int), List<int>>>(Group);
        _sequence = new Lazy<Dictionary<VerseKey, int>>(sequence);
    }

    public string Slug { get; }

    public string Language { get; }

    public IReadOnlyList<TextWord> Words { get; }

    /// <summary>The text's verses in key order.</summary>
    public IReadOnlyList<VerseKey> Verses { get; }

    /// <summary>The products a producer reading this text names among its inputs.</summary>
    public static IEnumerable<string> Products(string slug) => [TextWord.ProductOf(slug), FrameFile.Product(slug), TextField.ProductOf(slug)];

    public static ProductText Read(ProductStore store, string slug)
    {
        var words = new List<TextWord>(64_000);
        var order = new List<VerseKey>(32_000);
        var verses = new Dictionary<VerseKey, (int, int)>(32_000);
        using (var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug)))
        {
            foreach (var word in reader.Rows())
            {
                if (order.Count == 0 || order[^1] != word.Key.Verse)
                {
                    if (order.Count > 0)
                    {
                        var (start, _) = verses[order[^1]];
                        verses[order[^1]] = (start, words.Count - start);
                    }

                    order.Add(word.Key.Verse);
                    verses[word.Key.Verse] = (words.Count, 0);
                }

                words.Add(word);
            }
        }

        if (order.Count > 0)
        {
            var (start, _) = verses[order[^1]];
            verses[order[^1]] = (start, words.Count - start);
        }

        var frame = new Dictionary<VerseKey, IReadOnlyList<VerseKey>>();
        if (store.Exists(FrameFile.Product(slug)))
        {
            using var reader = store.Open(FrameFile.Product(slug), FrameRow.Schema);
            foreach (var (own, canonical) in reader.Rows().SelectMany(row => row.Verses()))
            {
                frame[own] = canonical;
            }
        }

        return new ProductText(slug, IndexContext.Declared(store, slug).Language, words, order, verses, frame, () => Sequences(store, slug));
    }

    /// <summary>Where the edition prints each verse in its chapter, from its verses product; empty where the store has none.</summary>
    public IReadOnlyDictionary<VerseKey, int> Sequence => _sequence.Value;

    private static Dictionary<VerseKey, int> Sequences(ProductStore store, string slug)
    {
        if (!store.Exists(VerseManifestRow.ProductOf(slug)))
        {
            return [];
        }

        using var reader = store.Open(VerseManifestRow.ProductOf(slug), VerseManifestRow.Schema);
        return reader.Rows().ToDictionary(row => row.Own, row => row.Sequence);
    }

    /// <summary>The ids of a verse's words in order; empty where the text has no such verse.</summary>
    public IEnumerable<int> InVerse(VerseKey own) =>
        _verses.TryGetValue(own, out var range) ? Enumerable.Range(range.Start, range.Count) : [];

    public bool Has(VerseKey own) => _verses.ContainsKey(own);

    /// <summary>The frame addresses a verse stands at, the primary first.</summary>
    public IReadOnlyList<VerseKey> Addresses(VerseKey own) => _frame.TryGetValue(own, out var canonical) ? canonical : [FrameFile.AtOwn(own)];

    /// <summary>The frame address a verse stands at first.</summary>
    public (int Book, int Chapter, int Verse) Primary(VerseKey own)
    {
        var address = Addresses(own)[0];
        return (address.Book, address.Chapter, address.Verse);
    }

    /// <summary>
    /// The words standing at each frame address by their verse's primary address, a verse after the one before it where two
    /// stand at one address (BHSA's 1 Kings 22:43 and 22:44 are the English 22:43), never interleaved.
    /// </summary>
    public IReadOnlyDictionary<(int Book, int Chapter, int Verse), List<int>> ByAddress => _byAddress.Value;

    public WordKey Key(long id) => Words[(int)id].Key;

    private Dictionary<(int, int, int), List<int>> Group()
    {
        var grouped = new Dictionary<(int, int, int), List<int>>(Verses.Count + 256);
        foreach (var verse in Verses)
        {
            var address = Primary(verse);
            if (!grouped.TryGetValue(address, out var words))
            {
                grouped[address] = words = [];
            }

            words.AddRange(InVerse(verse));
        }

        return grouped;
    }
}

/// <summary>The texts a run of producers reads, each read once whatever the number of pairs that need it.</summary>
internal sealed class ProductTexts(ProductStore store)
{
    private readonly ConcurrentDictionary<string, Lazy<ProductText>> _texts = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Lazy<object>> _shared = new(StringComparer.Ordinal);

    public ProductStore Store => store;

    public ProductText this[string slug] =>
        _texts.GetOrAdd(slug, s => new Lazy<ProductText>(() => ProductText.Read(store, s))).Value;

    /// <summary>Whether the store holds both products a producer reads of a text.</summary>
    public bool Holds(string slug) => ProductText.Products(slug).All(store.Exists);

    /// <summary>
    /// What the pairs of a run read alike, made once for all of them: the original's side of a reference shared by every
    /// translation's pair. The value is shared between threads, so nobody may change it.
    /// </summary>
    public T Shared<T>(string key, Func<T> make) where T : class =>
        (T)_shared.GetOrAdd(key, _ => new Lazy<object>(() => make())).Value;
}

/// <summary>
/// Writing a pair's method file from a producer that reads files (DOC-0224 §6.1): its rows, made from the two texts' words
/// as they stand, replace the producer's rows of the file before; the rows of sources the producer does not make are
/// carried over from that file as they were and named in the header (<see cref="KeptNote"/>), so the merge can tell which
/// rows a changed verse leaves stale and which the producer made again.
/// </summary>
internal static class MethodFiles
{
    /// <summary>A header note: every row but those of the kept sources was made from the inputs as they are.</summary>
    public const string RemadeNote = "remade";

    /// <summary>A header note, once per source: rows of this source were carried from the previous file, which no producer makes.</summary>
    public const string KeptNote = "kept";

    public sealed record Written(string Pair, string Method, int Rows, int Kept, int New, int Gone, int Carried, IReadOnlyList<string> CarriedSources)
    {
        public override string ToString() =>
            $"{Pair} {Method}: {Rows} rows, {Kept} shapes the previous file held, {New} new, {Gone} gone"
            + (Carried == 0 ? string.Empty : $"; {Carried} rows of {CarriedSources.Count} sources no producer makes carried over ({string.Join("; ", CarriedSources)})");
    }

    /// <param name="made">The rows the producer made.</param>
    /// <param name="remakes">Whether the producer makes a source's rows; the previous file's rows of any other source are carried.</param>
    public static Written Write(
        ProductStore store,
        string pair,
        string method,
        IReadOnlyCollection<MappingRow> made,
        string producer,
        IEnumerable<ProductInput> inputs,
        Func<string, bool> remakes)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var product = MappingProducts.Of(pair, method);
        var previous = new List<MappingRow>();
        if (store.Exists(product))
        {
            using var reader = store.Open(product, MappingRow.Schema);
            previous = reader.ReadAll();
        }

        var carried = previous.Where(row => !remakes(row.Source)).Select(row => row with { Status = null }).ToList();
        var carriedSources = carried.Select(row => row.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var rows = made.Select(row => row with { Status = null }).Concat(carried).ToList();
        var madeShapes = made.Select(row => row.Shape).ToHashSet(StringComparer.Ordinal);
        var previousMade = previous.Where(row => remakes(row.Source)).Select(row => row.Shape).ToHashSet(StringComparer.Ordinal);

        var sorted = rows.Select(r => (Sort: MappingText.SortKey(r.From, r.To, fromText, toText), Row: r)).ToList();
        sorted.Sort((a, b) => MappingExport.Order(a.Sort.Side, a.Sort.Key, a.Row, b.Sort.Side, b.Sort.Key, b.Row));
        using (var writer = store.Create(product, ProductKind.Machine, MappingRow.Schema, ProductHeader.DescribeProducer($"{producer} {pair}"),
                   [.. inputs.DistinctBy(input => input.Path)], header =>
                   {
                       header.Notes.Add(new(RemadeNote, $"every row but those of a kept source, from {fromText}'s and {toText}'s words as the inputs name them"));
                       header.Notes.AddRange(carriedSources.Select(source => new KeyValuePair<string, string>(KeptNote, source)));
                   }))
        {
            writer.WriteAll(sorted.Select(r => r.Row));
        }

        MappingFiles.RecordHashes(store, pair, made);
        return new Written(pair, method, rows.Count, madeShapes.Count(previousMade.Contains), madeShapes.Count(shape => !previousMade.Contains(shape)),
            previousMade.Count(shape => !madeShapes.Contains(shape)), carried.Count, carriedSources);
    }

    /// <summary>A header note: the verses whose rows were made from the inputs as they are; every other row is the file before's.</summary>
    public const string VersesNote = "verses";

    /// <summary>
    /// Writes a pair's method file again within some verses of its FROM text only: the rows of a FROM word in
    /// <paramref name="verses"/> are replaced by <paramref name="made"/> (those of its rows that stand there), and every other
    /// row of the file before is carried as it was. The Synodal's Greek additions to Esther were divided into verses after its
    /// pairs were aligned (PRB-1681): an aligner trained again moves rows all over the pair, so only the divided verses are
    /// taken from it.
    ///
    /// The header says which verses were made and carries no <see cref="RemadeNote"/>, so the merge holds every row to the
    /// verse hashes as before; the hash of a verse every row citing it was made now is renewed, the others are only added
    /// where missing.
    /// </summary>
    /// <returns>What was written, and the rows made in the verses that cite a verse the hashes still hold otherwise.</returns>
    public static (Written Written, int HeldStale) WriteVerses(
        ProductStore store,
        string pair,
        string method,
        IReadOnlyCollection<MappingRow> made,
        IReadOnlyList<VerseRange> verses,
        string producer,
        IEnumerable<ProductInput> inputs)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var product = MappingProducts.Of(pair, method);
        if (!store.Exists(product))
        {
            throw new InvalidOperationException($"{product} does not exist: there is no file to keep the other verses of.");
        }

        List<MappingRow> previous;
        using (var reader = store.Open(product, MappingRow.Schema))
        {
            previous = reader.ReadAll();
        }

        var within = made.Where(row => Within(row.From, verses)).Select(row => row with { Status = null }).ToList();
        var replaced = previous.Where(row => Within(row.From, verses)).ToList();
        var carried = previous.Where(row => !Within(row.From, verses)).ToList();
        var rows = within.Concat(carried).ToList();

        var sorted = rows.Select(r => (Sort: MappingText.SortKey(r.From, r.To, fromText, toText), Row: r)).ToList();
        sorted.Sort((a, b) => MappingExport.Order(a.Sort.Side, a.Sort.Key, a.Row, b.Sort.Side, b.Sort.Key, b.Row));
        var named = string.Join(",", verses);
        using (var writer = store.Create(product, ProductKind.Machine, MappingRow.Schema, ProductHeader.DescribeProducer(producer),
                   [.. inputs.DistinctBy(input => input.Path)], header => header.Notes.Add(new(VersesNote,
                       $"rows of a {fromText} word in {named} made from the inputs as they are; every other row as the file before held it"))))
        {
            writer.WriteAll(sorted.Select(r => r.Row));
        }

        // A verse a carried row cites keeps its hash: renewing it would let a carried row of a moved verse stand.
        var cited = new HashSet<(string Text, VerseKey Verse)>();
        foreach (var row in carried)
        {
            foreach (var (text, keys) in new[] { (fromText, row.From), (toText, row.To) })
            {
                foreach (var key in MappingText.Split(keys))
                {
                    cited.Add((text, VerseKey.Parse(key[..key.LastIndexOf('.')])));
                }
            }
        }

        MappingFiles.RecordHashes(store, pair, within, (text, verse) => !cited.Contains((text, verse)));
        var held = HeldStale(store, pair, fromText, toText, within);

        var madeShapes = within.Select(row => row.Shape).ToHashSet(StringComparer.Ordinal);
        var replacedShapes = replaced.Select(row => row.Shape).ToHashSet(StringComparer.Ordinal);
        return (new Written(pair, method, rows.Count, madeShapes.Count(replacedShapes.Contains), madeShapes.Count(shape => !replacedShapes.Contains(shape)),
            replacedShapes.Count(shape => !madeShapes.Contains(shape)), carried.Count, []), held);
    }

    /// <summary>Whether a FROM side names a word in one of the verses.</summary>
    public static bool Within(string from, IReadOnlyList<VerseRange> verses) =>
        MappingText.Split(from).Any(key => VerseKey.Parse(key[..key.LastIndexOf('.')]) is var verse && verses.Any(range => range.Contains(verse)));

    /// <summary>
    /// The rows citing a verse whose recorded hash differs from the verse as it stands (or a verse the text no longer
    /// has), which the merge will hold stale unless the verse reads the same.
    /// </summary>
    private static int HeldStale(ProductStore store, string pair, string fromText, string toText, IEnumerable<MappingRow> rows)
    {
        var product = MappingProducts.Of(pair, MappingProducts.Hashes);
        var hashes = store.Exists(product)
            ? VerseHashRow.ReadAll(store, product).ToDictionary(row => (row.Text, row.Own), row => row.Hash)
            : new Dictionary<(string Text, VerseKey Own), string>();
        var now = new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>(StringComparer.Ordinal)
        {
            [fromText] = MappingFiles.Manifest(store, fromText),
            [toText] = MappingFiles.Manifest(store, toText),
        };

        bool Moved(string text, string key)
        {
            var verse = VerseKey.Parse(key[..key.LastIndexOf('.')]);
            return now[text] is { } manifest
                   && (!manifest.TryGetValue(verse, out var hash) || (hashes.TryGetValue((text, verse), out var then) && then != hash));
        }

        return rows.Count(row => MappingText.Split(row.From).Any(k => Moved(fromText, k)) || MappingText.Split(row.To).Any(k => Moved(toText, k)));
    }

    /// <summary>
    /// The laid Strong rows a producer left to a stated link (<see cref="MappingProducts.StrongYielded"/>), sorted, from the
    /// same inputs as the method file; only the merge reads them, so no other producer sees them as links.
    /// </summary>
    public static void WriteYielded(ProductStore store, string pair, IReadOnlyCollection<MappingRow> yielded, string producer, IEnumerable<ProductInput> inputs)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var sorted = yielded.Select(r => (Sort: MappingText.SortKey(r.From, r.To, fromText, toText), Row: r)).ToList();
        sorted.Sort((a, b) => MappingExport.Order(a.Sort.Side, a.Sort.Key, a.Row, b.Sort.Side, b.Sort.Key, b.Row));
        using var writer = store.Create(MappingProducts.Of(pair, MappingProducts.StrongYielded), ProductKind.Machine, MappingRow.Schema,
            ProductHeader.DescribeProducer($"{producer} {pair}"), [.. inputs.DistinctBy(input => input.Path)]);
        writer.WriteAll(sorted.Select(r => r.Row));
    }

    /// <summary>The inputs every producer of a pair names: both texts' words and frames.</summary>
    public static IEnumerable<ProductInput> TextInputs(ProductStore store, string pair)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        return ProductText.Products(fromText).Concat(ProductText.Products(toText)).Where(store.Exists).Select(store.ProductInput);
    }

    /// <summary>
    /// The sources of a method file whose rows were not made from the two texts' words as they stand now, when every other
    /// row was: the file was written by a producer from files (it says so, or it is EVIDENTIA's from the ledger) and the
    /// words products it names are the ones the store holds. Null where that cannot be said, and then every row of the file
    /// answers for a verse that moved.
    /// </summary>
    public static IReadOnlySet<string>? KeptSources(ProductStore store, ProductHeader? header, string fromText, string toText)
    {
        if (header is null
            || !(header.Notes.Any(note => note.Key == RemadeNote) || header.Producer.StartsWith(EvidentiaFiles.Producer, StringComparison.Ordinal)))
        {
            return null;
        }

        foreach (var text in new[] { fromText, toText })
        {
            var words = TextWord.ProductOf(text);
            if (!store.Exists(words))
            {
                return null;
            }

            var now = store.ProductInput(words);
            if (!header.Inputs.Any(input => input.Path == now.Path && input.Sha256 == now.Sha256))
            {
                return null;
            }
        }

        return header.Notes.Where(note => note.Key == KeptNote).Select(note => note.Value).ToHashSet(StringComparer.Ordinal);
    }
}
