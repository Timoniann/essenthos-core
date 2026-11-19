using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// A pair composed through its middle texts, as the corpus's runs of <c>compose</c> last ran it (<see cref="CompositionPipeline"/>).
/// </summary>
/// <param name="Vias">
/// The middle texts, one to three, in the order their routes are numbered. A third is added to a composition through the first two
/// (the Elberfelder's New Testament through Luther, PRB-0863), whose floors stand for what it does not find.
/// </param>
/// <param name="RefuseUnmeasured">
/// Refuse what one family of readings found where nothing measured that combination, instead of writing it from the
/// threshold (<see cref="Admission.RefusingUnmeasured"/>, NOT-0228). Off for every pair until the owner has seen what it
/// removes per text (PRB-1607).
/// </param>
internal sealed record ComposedPair(
    string From,
    IReadOnlyList<string> Vias,
    string To,
    double? Minimum = null,
    double Precision = Admission.DefaultPrecision,
    bool Unmeasured = false,
    bool Daughter = false,
    int Agreeing = 1,
    bool RefuseUnmeasured = false)
{
    public string Name => MappingProducts.Pair(From, To);
}

/// <summary>
/// The <c>composed</c> file of a pair from files alone: the aligner's answers stored for the pair and for each hop to a middle
/// text, the middle texts' finals into the target, and the pair's own final, verse links and words (<see cref="CompositionPipeline"/>).
/// A composition reads its own pair's final, which is made from the file it writes, so no build line runs it: the file is kept
/// and <c>forge compose</c> draws it again by hand, training what it lacks.
/// </summary>
internal sealed class ComposedSource : IPairSource
{
    private static readonly string[] English = ["KJV", "BSB"];

    /// <summary>The pairs composed, as the corpus's <c>compose</c> runs last drew them (the recipe of 2026-10-15).</summary>
    public static readonly IReadOnlyList<ComposedPair> Pairs =
    [
        new("UBIO", ["KJV"], "BHSA"),
        new("UBIO", ["KJV"], "TR1894", Unmeasured: true),
        new("UBIO", ["KJV"], "NESTLE1904"),
        new("LUTH1912", ["KJV"], "BHSA"),
        new("LUTH1912", English, "NESTLE1904"),
        new("ASV", English, "BHSA"),
        new("ASV", English, "NESTLE1904"),
        new("WEB", English, "BHSA"),
        new("WEB", English, "NESTLE1904"),
        new("GNV1599", English, "BHSA"),
        new("GNV1599", English, "NESTLE1904"),
        new("JPS1917", English, "BHSA"),
        new("RV1909", English, "BHSA"),
        new("RV1909", English, "NESTLE1904"),
        new("TYN1534", English, "NESTLE1904"),
        new("NPU2022", English, "NESTLE1904"),
        new("ELB1905", English, "BHSA"),
        new("ELB1905", ["KJV", "BSB", "LUTH1912"], "NESTLE1904"),
        new("BBE", English, "BHSA"),
        new("BBE", English, "NESTLE1904"),
        new("NPU2022", English, "BHSA"),
        new("RUSV", ["KJV"], "NESTLE1904"),
        new("RUSV", ["KJV"], "TR1894"),
        new("UKR1871", English, "BHSA"),
        new("UKR1871", English, "NESTLE1904"),
        new("ALM1911", ["RV1909", "KJV"], "BHSA", Unmeasured: true),
        new("ALM1911", ["RV1909", "KJV"], "NESTLE1904", Agreeing: 3),
        new("LSG1910", English, "BHSA"),
        new("LSG1910", English, "NESTLE1904"),
        new("RUSV", English, "BHSA"),
        new("GRCBRENT", ["KJV"], "BHSA", Unmeasured: true),
        new("GEEZ81", ["GRCBRENT"], "BHSA", Unmeasured: true, Daughter: true),
        new("YLT", English, "BHSA"),
        new("YLT", English, "NESTLE1904"),
    ];

    private readonly IReadOnlyList<ComposedPair> _pairs;

    public ComposedSource(IReadOnlyList<ComposedPair>? pairs = null) => _pairs = pairs ?? Pairs;

    /// <summary>Whether a hop whose stored answers were made from other words is trained here (<c>forge compose</c>), or waits.</summary>
    public bool Trains { get; init; }

    public string Method => MappingMethods.Composed;

    public string Name => "composed";

    public bool States(string pair) => _pairs.Any(p => p.Name == pair);

    public bool Makes(string source) => source.StartsWith(CompositionPipeline.Machine + ",", StringComparison.Ordinal);

    public IEnumerable<string> Inputs(ProductStore store, string pair)
    {
        var composed = Declared(pair);
        var proxies = MappingLinks.Inputs(store, composed.From, composed.Vias, composed.To).ToList();
        var floors = store.Find(MappingProducts.Of(pair, HandFloors.Name));
        var rulings = store.Find(MappingProducts.Of(pair, MappingProducts.Rulings));
        var readers = proxies.Select(final => MappingProducts.Split(final.Split('/')[2]).From).Append(composed.From).Distinct().ToList();
        var hops = readers.SelectMany(reader => new[]
        {
            CompositionPipeline.Workspace(reader, composed.To), CompositionPipeline.Workspace(reader, composed.To, written: true),
        }.Concat(composed.Vias.Select(via => CompositionPipeline.Workspace(reader, via))));
        return
        [
            .. readers.Concat(composed.Vias).Append(composed.To).Distinct()
                .SelectMany(text => ProductText.Products(text).Append(VerseManifestRow.ProductOf(text))).Select(store.Find).OfType<string>(),
            .. proxies,
            .. readers.SelectMany(reader => VerseJoins.Inputs(store, reader, composed.To)),
            .. hops.Distinct().SelectMany(workspace => AlignerFiles.StoredAnswers(store, workspace)),
            .. composed.To == AlignerFiles.Analysed ? AlignerFiles.SyntaxInputs(store) : [],
            .. floors is null ? [] : new[] { floors },
            .. rulings is null ? [] : new[] { rulings },
        ];
    }

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var composed = Declared(pair);
        var aligner = new AlignerFiles(texts, new AlignmentPipeline(null, new LineLogger<AlignmentPipeline>(log)) { Trains = Trains });
        var composer = new CompositionPipeline(texts, aligner, new LineLogger<CompositionPipeline>(log));
        var (_, rows) = composer.Run(composed.From, composed.Vias, composed.To, composed.Minimum, composed.Precision, composed.Unmeasured,
            composed.Daughter, composed.Agreeing, composed.RefuseUnmeasured).GetAwaiter().GetResult();
        return rows;
    }

    private ComposedPair Declared(string pair) =>
        _pairs.FirstOrDefault(p => p.Name == pair) ?? throw new ArgumentException($"{pair} is not a pair any composition draws.", nameof(pair));
}
