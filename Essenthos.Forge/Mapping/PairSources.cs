using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// One source of a method's rows that a producer reads from files (DOC-0224 §6.1): a published alignment, a table, an
/// edition's own marks or numbers, or a rule over the two texts' words. It says which pairs it speaks about, which rows of a
/// method file are its own (by their source), what it reads, and its rows for a pair.
/// </summary>
internal interface IPairSource
{
    /// <summary>The method of its rows.</summary>
    string Method { get; }

    /// <summary>
    /// The file its rows go in, the method's own unless the source reads the pair's other method files: then it writes a
    /// file of its own that the build makes after them (<see cref="PairSources.Laid"/>), so no file reads itself through another.
    /// </summary>
    string File => Method;

    /// <summary>A short name for the log and for <c>--sources</c>.</summary>
    string Name { get; }

    /// <summary>Whether it speaks about the pair.</summary>
    bool States(string pair);

    /// <summary>Whether a row of the method file with this source is one it makes.</summary>
    bool Makes(string source);

    /// <summary>
    /// The files it reads for the pair beyond both texts' words and frames, relative to Resources; a product as
    /// <see cref="ProductStore.Find"/> gives its file.
    /// </summary>
    IEnumerable<string> Inputs(ProductStore store, string pair);

    /// <summary>Its rows for the pair; a link's id follows from its words when the merge numbers it.</summary>
    IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log);
}

/// <summary>
/// A method's file of a pair made from the sources that speak about it (<see cref="IPairSource"/>), each reading only files.
/// Rows of sources none of them makes are carried from the file before and named in its header, so the merge knows a
/// changed verse leaves those stale (<see cref="MethodFiles"/>).
/// </summary>
internal static class PairSources
{
    /// <summary>Every source by method. A method with none here keeps its exported file.</summary>
    public static readonly IReadOnlyList<IPairSource> All =
    [
        new BereanTableSource(),
        new ClearBibleSource(),
        new Door43AlignmentSource(),
        .. TableSources.All,
        .. StrongNumberSource.All,
        .. KingJamesGreekSource.All,
        new GreekWitnessSource(MappingMethods.Strong),
        new GreekWitnessSource(MappingMethods.Lexical),
        new GreekLetterSource(),
        new HebrewLetterSource(),
        new NameListSource(),
        new PossessiveSource(),
        new NamesFirstSource(),
    ];

    /// <summary>
    /// The file of stated rows a source lays on the links the pair already has (the editions' supplied words give way to a
    /// word's rendering; Robinson's apparatus reads the pair's Strong and lexical links): made after every method file it reads.
    /// </summary>
    public const string Laid = "stated-laid";

    /// <summary>The verb that writes a file of every pair, as the header and the declared graph name it.</summary>
    public static string Producer(string file) => $"forge {file}-pairs";

    public sealed record Outcome(string Pair, bool Current, MethodFiles.Written? Written, IReadOnlyList<string> Sources);

    /// <summary>
    /// The pairs some source of the file speaks about whose two texts the store holds; and the pairs whose file of a method
    /// holds rows a source of that method now writes into another file, so the file gives them up.
    /// </summary>
    public static List<string> Pairs(ProductTexts texts, string file, IReadOnlySet<string>? only = null) =>
    [
        .. MappingProducts.Pairs(texts.Store)
            .Where(pair => only is null || only.Contains(pair))
            .Where(pair => All.Any(source => source.States(pair)
                && (source.File == file || (source.Method == file && texts.Store.Exists(MappingProducts.Of(pair, file))))))
            .Where(pair => MappingProducts.Split(pair) is var (from, to) && texts.Holds(from) && texts.Holds(to)),
    ];

    /// <summary>Writes the pair's method file from its sources, unless every input is as the file was made from.</summary>
    public static Outcome Write(ProductTexts texts, string file, string pair, bool force, Action<string> log)
    {
        var store = texts.Store;
        var sources = All.Where(source => source.File == file && source.States(pair)).ToList();
        var inputs = MethodFiles.TextInputs(store, pair)
            .Concat(sources.SelectMany(source => source.Inputs(store, pair)).Select(store.Input))
            .DistinctBy(input => input.Path)
            .ToList();
        var product = MappingProducts.Of(pair, file);
        if (!force && store.Exists(product) && !store.NeedsBuild(product, ProductKind.Machine, inputs))
        {
            return new Outcome(pair, true, null, [.. sources.Select(s => s.Name)]);
        }

        var rows = new List<MappingRow>();
        foreach (var source in sources)
        {
            var before = rows.Count;
            rows.AddRange(source.Rows(texts, pair, line => log($"{pair} {source.Name}: {line}")));
            log($"{pair} {source.Name}: {rows.Count - before} rows");
        }

        // A row of a source that writes another file of the pair is not carried into this one: that file holds it.
        var written = MethodFiles.Write(store, pair, file, rows, Producer(file), inputs,
            made => All.Any(source => source.States(pair) && source.Makes(made)));
        return new Outcome(pair, false, written, [.. sources.Select(s => s.Name)]);
    }

    /// <summary>
    /// The words of the pair's TO text that a ruling takes out of one source's rows (<c>mapping/&lt;pair&gt;/rulings</c>: a
    /// <c>refuse</c> with no FROM words and a source), as own keys.
    /// </summary>
    public static HashSet<string> Overruled(ProductStore store, string pair, string source)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var product = MappingProducts.Of(pair, MappingProducts.Rulings);
        if (!store.Exists(product))
        {
            return words;
        }

        using var reader = store.Open(product, RulingRow.Schema);
        foreach (var ruling in reader.Rows())
        {
            if (ruling.Action == RulingRow.Refuse && ruling.From.Length == 0 && ruling.Source.Length > 0
                && source.StartsWith(ruling.Source, StringComparison.Ordinal))
            {
                words.UnionWith(MappingText.Split(ruling.To));
            }
        }

        return words;
    }
}
