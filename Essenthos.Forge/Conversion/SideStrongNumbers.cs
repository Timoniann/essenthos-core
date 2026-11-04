using Essenthos.Core.Loading;
using Essenthos.Core.Products;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Conversion;

/// <summary>
/// Strong numbers laid onto a translation from a numbered edition of the same translation, as the load's
/// strong verbs lay them before drawing links from them (<see cref="SynodalStrongLayer.Lay"/>): the Synodal by
/// the Strong-tagged digitisation swmail/RST, the Chinese Union by FHL's module, the Segond, Darby, Schlachter
/// and the Revised Literal by their CrossWire modules, the Reina-Valera by Rubén Gómez's numbering. A word takes
/// the first number its tag names. The load reads these for the run and stores no number on a word; here the
/// number is the word's <c>strong</c> column, and the header names the edition it came from.
/// </summary>
internal sealed class SideStrongNumbers : ITextStage
{
    private sealed record Layer(string Source, Func<string, Dictionary<(int Book, int Chapter, int Verse), List<EditionWord>>> Read, bool Stated = false);

    private static readonly Dictionary<string, Layer> Layers = Build();

    private static Dictionary<string, Layer> Build()
    {
        var layers = new Dictionary<string, Layer>(StringComparer.Ordinal)
        {
            [Bible4uTextSource.Synodal] = new(string.Join('/', SynodalStrongLinkLoader.EditionFile),
                resources => SynodalStrongEdition.Read(Path.Combine([resources, .. SynodalStrongLinkLoader.EditionFile])), Stated: true),
            [EbibleTextSource.ReinaValera] = new(string.Join('/', ReinaValeraStrongLinkLoader.EditionFolder),
                resources => EbibleTextSource.Numbers(Path.Combine([resources, .. ReinaValeraStrongLinkLoader.EditionFolder]))),
        };

        foreach (var numbering in CrossWireStrongLinkLoader.All)
        {
            var folder = (SwordTextSource.Texts.GetValueOrDefault(numbering.Module) ?? SwordTextSource.Numberings[numbering.Module]).Folder;
            layers[numbering.Slug] = new(folder, resources => SwordTextSource.Numbers(Path.Combine(resources, folder)));
        }

        foreach (var union in (string[])[SwordTextSource.ChineseUnion, SwordTextSource.ChineseUnionSimplified])
        {
            var folder = SwordTextSource.Texts.Values.Single(t => t.Definition.Slug == union).Folder;
            layers[union] = new(folder, resources => SwordTextSource.Numbers(Path.Combine(resources, folder)));
        }

        return layers;
    }

    public bool AppliesTo(string slug) => Layers.ContainsKey(slug);

    /// <summary>Whether a text's Strong numbers are laid onto it from another edition, not its own tags.</summary>
    public static bool Layered(string slug) => Layers.ContainsKey(slug);

    /// <summary>The texts whose Strong numbers are laid from another edition.</summary>
    public static IEnumerable<string> Texts => Layers.Keys;

    public IReadOnlyList<string> Sources(string slug) => [Layers[slug].Source, StrongTerms.Source];

    public void Apply(ConvertedText text, string resources)
    {
        var layer = Layers[text.Slug];
        var words = new List<ConvertedWord>(1 << 20);
        var corpus = new List<CorpusVerse>(text.Verses.Count);
        foreach (var verse in text.Verses.OrderBy(v => (v.Key.Book, v.Key.Chapter, v.Key.Verse)))
        {
            var corpusWords = new List<CorpusWord>(verse.Words.Count);
            foreach (var word in verse.Words)
            {
                corpusWords.Add(new CorpusWord(words.Count, word.Surface));
                words.Add(word);
            }

            corpus.Add(new CorpusVerse(verse.Key.Book, verse.Key.Chapter, verse.Key.Verse,
                layer.Stated ? [.. verse.Stated.Select(s => new VerseAddress(s.Chapter, s.Number))] : [], corpusWords));
        }

        var laid = SynodalStrongLayer.Lay(layer.Read(resources), corpus);
        var numbered = 0;
        foreach (var (id, tag) in laid.Tags)
        {
            if (tag.Numbers.Count > 0 && words[(int)id].Strong is null)
            {
                words[(int)id].Strong = tag.Numbers[0];
                numbered++;
            }
        }

        text.Provenance.Add(new("strong", $"{layer.Source}, laid onto the words: {numbered} words ({laid})"));
        var verdict = StrongTerms.Read(resources).GetValueOrDefault(text.Slug);
        text.Provenance.Add(new(StrongTerms.Served, verdict is { Served: true }
            ? "yes"
            : $"no: {verdict?.Why ?? "the terms of the numbering are not read"} ({StrongTerms.Source})"));
    }
}

/// <summary>
/// One row of <c>texts/strong-terms.tsv</c>: whether the terms of the numbering a text's Strong numbers are laid from
/// let the served database carry them, with the terms quoted and where they were read.
/// </summary>
internal sealed record StrongTerm(string Text, string Numbering, bool Served, string File, string Quote, string Why)
{
    public static readonly ProductSchema<StrongTerm> Schema = new ProductSchema<StrongTerm>()
        .Text("text", t => t.Text)
        .Text("numbering", t => t.Numbering)
        .Text("served", t => t.Served ? "yes" : "no")
        .Text("file", t => t.File)
        .Text("quote", t => t.Quote)
        .Text("why", t => t.Why)
        .Reads(row => new StrongTerm(row.Text(0), row.Text(1), row.Text(2) switch
        {
            "yes" => true,
            "no" => false,
            var other => throw new FormatException($"texts/strong-terms: served is yes or no, not \"{other}\" ({row.Text(0)})."),
        }, row.Text(3), row.Text(4), row.Text(5)));
}

/// <summary>
/// The verdicts on laid Strong numbers, and how the words product carries them: a header note <c>strong-served</c>,
/// <c>yes</c> or <c>no: why</c>. A text whose numbers are its own tags has no note, and its numbers are served.
/// </summary>
internal static class StrongTerms
{
    public const string Product = "texts/strong-terms";

    public const string Source = "Essenthos/texts/strong-terms.tsv";

    public const string Served = "strong-served";

    public static Dictionary<string, StrongTerm> Read(string resources)
    {
        var store = new ProductStore(resources);
        if (!store.Exists(Product))
        {
            return [];
        }

        using var reader = store.Open(Product, StrongTerm.Schema);
        return reader.Rows().ToDictionary(term => term.Text, StringComparer.Ordinal);
    }

    /// <summary>Whether the served database may carry the Strong numbers of a text's words product, as its header says.</summary>
    public static bool MayServe(ProductHeader? header) =>
        header?.Notes.LastOrDefault(note => note.Key == Served) is not { Value: { } value } || value == "yes";
}
