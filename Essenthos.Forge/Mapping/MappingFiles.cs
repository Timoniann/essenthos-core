using Essenthos.Core.Conversion;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>Reads a pair's mapping files from a store and writes what the merge made of them.</summary>
internal static class MappingFiles
{
    public const string MergeProducer = "forge merge";

    public sealed record Merged(string Pair, bool Skipped, long Links, long Claims, IReadOnlyList<ReportRow> Report);

    /// <summary>
    /// Merges one pair: its method files, rulings, verse hashes and the two texts' manifests into its final and
    /// report, and the merge's statuses back into the method files. Skipped when the final is current with all of
    /// them, unless forced.
    /// </summary>
    public static Merged Merge(ProductStore store, string pair, bool force)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var methods = MappingProducts.Methods(store, pair);
        var finalProduct = MappingProducts.Of(pair, MappingProducts.Final);
        if (!force && store.Exists(finalProduct) && !store.NeedsBuild(finalProduct, ProductKind.Machine, Inputs(store, pair, methods, fromText, toText)))
        {
            return new Merged(pair, true, 0, 0, []);
        }

        var rows = new Dictionary<string, List<MappingRow>>(StringComparer.Ordinal);
        var headers = new Dictionary<string, ProductHeader?>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            using var reader = store.Open(MappingProducts.Of(pair, method), MappingRow.Schema);
            rows[method] = reader.ReadAll();
            headers[method] = reader.Header;
        }

        var rulings = new List<RulingRow>();
        if (store.Exists(MappingProducts.Of(pair, MappingProducts.Rulings)))
        {
            using var reader = store.Open(MappingProducts.Of(pair, MappingProducts.Rulings), RulingRow.Schema);
            rulings = reader.ReadAll();
        }

        var hashes = new Dictionary<(string, VerseKey), string>();
        var folds = new Dictionary<(string, VerseKey), string>();
        if (store.Exists(MappingProducts.Of(pair, MappingProducts.Hashes)))
        {
            foreach (var row in VerseHashRow.ReadAll(store, MappingProducts.Of(pair, MappingProducts.Hashes)))
            {
                hashes[(row.Text, row.Own)] = row.Hash;
                if (row.Fold is not null)
                {
                    folds[(row.Text, row.Own)] = row.Fold;
                }
            }
        }

        var manifests = new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>(StringComparer.Ordinal)
        {
            [fromText] = Manifest(store, fromText),
            [toText] = Manifest(store, toText),
        };

        var remade = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var producers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (method, header) in headers)
        {
            if (MethodFiles.KeptSources(store, header, fromText, toText) is { } kept)
            {
                remade[method] = kept;
            }

            if (ProducerVerb(header) is { } verb)
            {
                producers[method] = verb;
            }
        }

        var ordinal = PairOrdinals.Ensure(store, [pair])[pair];
        var output = MappingMerge.Merge(new MappingMerge.Input(
            pair, fromText, toText, rows, rulings, hashes, manifests, ordinal, folds, CurrentFolds(store), Frames(store, pair, fromText, toText), remade, producers));

        foreach (var (method, merged) in output.Methods)
        {
            if (merged.Select(r => r.Status).SequenceEqual(rows[method].Select(r => r.Status)))
            {
                continue;
            }

            var header = headers[method];
            using var writer = store.Create(MappingProducts.Of(pair, method), ProductKind.Machine, MappingRow.Schema,
                header?.Producer ?? ProductHeader.DescribeProducer(MergeProducer), header?.Inputs ?? [], copy =>
                {
                    if (header is not null)
                    {
                        copy.Notes.AddRange(header.Notes);
                        copy.Comments.AddRange(header.Comments);
                    }
                });
            writer.WriteAll(merged);
        }

        using (var writer = store.Create(finalProduct, ProductKind.Machine, FinalRow.Schema,
                   ProductHeader.DescribeProducer(MergeProducer), Inputs(store, pair, methods, fromText, toText)))
        {
            writer.WriteAll(output.Final);
        }

        using (var writer = store.Create(MappingProducts.Of(pair, MappingProducts.Report), ProductKind.Machine, ReportRow.Schema,
                   ProductHeader.DescribeProducer(MergeProducer), [store.ProductInput(finalProduct)]))
        {
            writer.WriteAll(output.Report);
        }

        return new Merged(pair, false, output.Final.Count(r => r.Settled), output.Final.Count, output.Report);
    }

    /// <summary>
    /// The verb of the producer from files that wrote a method file (<c>forge stated-pairs</c>), without its pair and version; null
    /// for a file exported from a database or written by the merge alone.
    /// </summary>
    private static string? ProducerVerb(ProductHeader? header)
    {
        if (header is null || header.IsHandWritten)
        {
            return null;
        }

        var producer = header.Producer;
        var version = producer.IndexOf("  (", StringComparison.Ordinal);
        if (version >= 0)
        {
            producer = producer[..version];
        }

        var words = producer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length < 2 || words[1] is "mapping-export" or "merge" ? null : $"{words[0]} {words[1]}";
    }

    /// <summary>
    /// Adds to a pair's verse hashes the verses new rows cite that it does not list yet, from the texts' manifests
    /// where they exist, so that a later change to one of those verses finds the rows that cite it.
    /// </summary>
    public static void RecordHashes(ProductStore store, string pair, IEnumerable<MappingRow> rows)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var product = MappingProducts.Of(pair, MappingProducts.Hashes);
        var hashes = new Dictionary<(string Text, VerseKey Own), (string Hash, string? Fold)>();
        if (store.Exists(product))
        {
            foreach (var row in VerseHashRow.ReadAll(store, product))
            {
                hashes[(row.Text, row.Own)] = (row.Hash, row.Fold);
            }
        }

        var foldNow = CurrentFolds(store);

        var manifests = new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>(StringComparer.Ordinal)
        {
            [fromText] = Manifest(store, fromText),
            [toText] = Manifest(store, toText),
        };
        var added = 0;
        foreach (var row in rows)
        {
            foreach (var (text, keys) in new[] { (fromText, row.From), (toText, row.To) })
            {
                if (manifests[text] is not { } manifest)
                {
                    continue;
                }

                foreach (var key in MappingText.Split(keys))
                {
                    var verse = VerseKey.Parse(key[..key.LastIndexOf('.')]);
                    if (!hashes.ContainsKey((text, verse)) && manifest.TryGetValue(verse, out var hash))
                    {
                        hashes[(text, verse)] = (hash, foldNow(text, verse));
                        added++;
                    }
                }
            }
        }

        if (added == 0)
        {
            return;
        }

        using var writer = store.Create(product, ProductKind.Machine, VerseHashRow.Schema, ProductHeader.DescribeProducer(MergeProducer), []);
        writer.WriteAll(hashes.Select(h => new VerseHashRow(h.Key.Text, h.Key.Own, h.Value.Hash, h.Value.Fold))
            .OrderBy(r => r.Text, StringComparer.Ordinal).ThenBy(r => r.Own));
    }

    /// <summary>
    /// The fold of a verse as a text's words product has it (<see cref="VerseFold"/>), each text read once and only when
    /// asked; null where the store has no words or no declaration for the text.
    /// </summary>
    public static Func<string, VerseKey, string?> CurrentFolds(ProductStore store)
    {
        var texts = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<VerseKey, string>>>(StringComparer.Ordinal);
        return (text, verse) => texts.GetOrAdd(text, slug => new Lazy<IReadOnlyDictionary<VerseKey, string>>(() => FoldsOf(store, slug)))
            .Value.GetValueOrDefault(verse);
    }

    private static IReadOnlyDictionary<VerseKey, string> FoldsOf(ProductStore store, string text)
    {
        var folds = new Dictionary<VerseKey, string>();
        if (!store.Exists(TextWord.ProductOf(text)) || !store.Exists(TextField.ProductOf(text)))
        {
            return folds;
        }

        var language = Essenthos.Core.Indexes.IndexContext.Declared(store, text).Language;
        using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        foreach (var verse in reader.Rows().GroupBy(w => w.Key.Verse))
        {
            folds[verse.Key] = VerseFold.Of(verse.Select(w => w.Surface), language);
        }

        return folds;
    }

    /// <summary>The two texts' frames for the merge's crossing rule, where the store holds both texts' manifests.</summary>
    private static PairFrames? Frames(ProductStore store, string pair, string fromText, string toText) =>
        store.Exists(VerseManifestRow.ProductOf(fromText)) && store.Exists(VerseManifestRow.ProductOf(toText))
            ? VerseLinkFiles.Frames(store, pair)
            : null;

    /// <summary>What a pair's final is made from: its method files, its rulings and hashes, and the two texts' manifests and frames where they exist.</summary>
    private static IReadOnlyList<ProductInput> Inputs(ProductStore store, string pair, List<string> methods, string fromText, string toText)
    {
        var products = methods.Select(m => MappingProducts.Of(pair, m))
            .Append(MappingProducts.Of(pair, MappingProducts.Rulings))
            .Append(MappingProducts.Of(pair, MappingProducts.Hashes))
            .Append(VerseManifestRow.ProductOf(fromText))
            .Append(VerseManifestRow.ProductOf(toText))
            .Append(Loading.Frame.FrameFile.Product(fromText))
            .Append(Loading.Frame.FrameFile.Product(toText));
        return [.. products.Select(store.Find).OfType<string>().Select(store.Input)];
    }

    private static IReadOnlyDictionary<VerseKey, string>? Manifest(ProductStore store, string text)
    {
        var product = VerseManifestRow.ProductOf(text);
        if (!store.Exists(product))
        {
            return null;
        }

        using var reader = store.Open(product, VerseManifestRow.Schema);
        return reader.Rows().ToDictionary(r => r.Own, r => r.Hashes.Text);
    }
}
