using Essenthos.Core.Loading;
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

    public IReadOnlyList<string> Sources(string slug) => [Layers[slug].Source];

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
    }
}
