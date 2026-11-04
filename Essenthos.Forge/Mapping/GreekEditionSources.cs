using Essenthos.Core.Byzantine;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Essenthos.Core.TextusReceptus;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Stephanus 1550 against Scrivener 1894 from the composite both are printed from (<see cref="PrintedEditionLinkLoader"/>):
/// each verse's segments, a word outside a variant group and a group inside one, laid against each other. Both texts are
/// converted from the same file, so a file verse is each text's own verse of that number.
/// </summary>
internal sealed class PrintedEditionSource : IPairSource
{
    public const string Parsed = "TextusReceptus/parsed";

    private static readonly string Stephanus = TextusReceptusTextSource.Slug(Edition.Stephanus1550);
    private static readonly string Scrivener = TextusReceptusTextSource.Slug(Edition.Scrivener1894);

    public string Method => MappingMethods.Stated;

    public string Name => "printed-editions";

    public bool States(string pair) => pair == $"{Stephanus}-{Scrivener}";

    public bool Makes(string source) => source == PrintedEditionLinkLoader.Source;

    public IEnumerable<string> Inputs(ProductStore store, string pair) => [Parsed];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var first = texts[Stephanus];
        var second = texts[Scrivener];
        var drafts = new List<PrintedEditionLinkLoader.EditionDraft>(150_000);
        int verses = 0, unequal = 0;
        foreach (var book in TextusReceptusTextSource.Books)
        {
            var content = File.ReadAllText(Path.Combine(texts.Store.FullPath(Parsed), $"{book}.UTR"));
            var stephanus = UtrReader.Read(content, Edition.Stephanus1550);
            var scrivener = UtrReader.Read(content, Edition.Scrivener1894)
                .GroupBy(verse => (verse.Chapter, verse.Number)).ToDictionary(group => group.Key, group => group.First());
            var canonical = TextusReceptusTextSource.Canonical(book);
            foreach (var verse in stephanus)
            {
                var own = new VerseKey(canonical, verse.Chapter, verse.Number);
                if (!first.Has(own) || !second.Has(own))
                {
                    continue;
                }

                List<long> left = [.. first.InVerse(own).Select(id => (long)id)];
                List<long> right = [.. second.InVerse(own).Select(id => (long)id)];
                if (!scrivener.TryGetValue((verse.Chapter, verse.Number), out var other)
                    || verse.Words.Count != left.Count || other.Words.Count != right.Count)
                {
                    unequal++;
                    continue;
                }

                verses++;
                PrintedEditionLinkLoader.Pair(verse, other, left, right, drafts);
            }
        }

        log($"{verses} verses, {drafts.Count} links; {unequal} verses whose words are not the file's");
        return drafts.Select(draft => new MappingRow(null,
            MappingText.Keys(draft.From.Select(first.Key)),
            MappingText.Keys(draft.To.Select(second.Key)),
            draft.Relation.ToString().ToLowerInvariant(), Method, null, PrintedEditionLinkLoader.Source, null, null, null));
    }
}

/// <summary>
/// Robinson's apparatus to the Byzantine Textform laid onto the editions it describes (<see cref="ByzantineApparatus"/>):
/// the Nestle-Aland and Editio Critica Maior notes against Nestle 1904, the divided Byzantine readings against Scrivener.
///
/// <para>
/// **An apparatus verse is the Byzantine Textform's own verse** (its text is converted from the same parse), and the
/// witness's words are those standing at that verse's frame address, so a verse Robinson numbers elsewhere (Romans 16:25-27
/// after 14:23) meets the witness's verse at the place the frame puts it.
/// </para>
///
/// <para>
/// A note the pair already contradicts is left out, as the loader left it out: the same words under another relation, an
/// absence over a rendered word or a rendering over an absent one. What the pair holds is read from its Strong and lexical
/// method files, the links the pair held when the apparatus was laid (Robinson against the editions has no stated
/// source but this one).
/// </para>
/// </summary>
internal sealed class ByzantineApparatusSource : IPairSource
{
    public const string Folder = "Byzantine";

    public const string Apparatus = "Byzantine/ccat";

    /// <summary>The methods whose links a note may contradict.</summary>
    private static readonly string[] Standing = [MappingMethods.Strong, MappingMethods.Lexical];

    public string Method => MappingMethods.Stated;

    public string Name => "byzantine-apparatus";

    public bool States(string pair) =>
        ByzantineApparatusLinkLoader.Witnesses.Any(witness => pair == $"{ByzantineTextSource.Slug}-{witness.Witness}");

    public bool Makes(string source) => source == ByzantineApparatusLinkLoader.Credit;

    public IEnumerable<string> Inputs(ProductStore store, string pair) =>
        [Apparatus, .. Standing.Select(method => store.Find(MappingProducts.Of(pair, method))).OfType<string>()];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var robinson = texts[fromText];
        var witness = texts[toText];
        var kinds = ByzantineApparatusLinkLoader.Witnesses.Single(w => w.Witness == toText).Kinds;

        var ours = new Dictionary<(int, int, int), IReadOnlyList<CorpusWord>>(8_000);
        var theirs = new Dictionary<(int, int, int), IReadOnlyList<CorpusWord>>(8_000);
        foreach (var verse in robinson.Verses)
        {
            var address = (verse.Book, verse.Chapter, verse.Verse);
            ours[address] = Corpus(robinson, robinson.InVerse(verse));
            if (witness.ByAddress.TryGetValue(robinson.Primary(verse), out var at))
            {
                theirs[address] = Corpus(witness, at);
            }
        }

        var laid = ByzantineApparatus.Lay(ByzantineApparatusLinkLoader.Read(texts.Store.FullPath(Folder)), ours, theirs, kinds);
        var standing = PairStanding.Read(texts.Store, pair, Standing, robinson, witness);
        var offset = PairStanding.Offset(robinson);
        var admitted = laid.Links
            .Where(link => !ByzantineApparatusLinkLoader.Contrary(link with { To = [.. link.To.Select(id => id + offset)] }, standing))
            .ToList();
        log($"{laid}; {admitted.Count} links, {laid.Links.Count - admitted.Count} left out where the pair already says otherwise");
        return admitted.Select(link => new MappingRow(null,
            MappingText.Keys(link.From.Select(robinson.Key)),
            MappingText.Keys(link.To.Select(witness.Key)),
            link.Relation.ToString().ToLowerInvariant(), Method, null, ByzantineApparatusLinkLoader.Credit,
            ByzantineApparatusLinkLoader.Notes[link.Kind], null, null));
    }

    private static List<CorpusWord> Corpus(ProductText text, IEnumerable<int> ids) =>
        [.. ids.Select(id => new CorpusWord(id, text.Words[id].Surface))];
}

/// <summary>
/// The Revised Literal Translation's New Testament linked to the Received Text by the positions CrossWire's RLT states on
/// each element (<see cref="ReceivedTextPositions"/>), against both printed editions: the module's numbers laid onto the
/// translation's own verses (<see cref="SynodalStrongLayer"/>) give each word its element, and an element's positions are
/// counted in the witness's words standing at that verse's frame address.
///
/// <para>
/// The loader also left out an element on a word the pair already called absent; the pair had no absence when the positions
/// were laid (its only other links are the Strong numbers), so none is read.
/// </para>
/// </summary>
internal sealed class ReceivedTextPositionSource : IPairSource
{
    private static readonly SwordPositions Module = CrossWirePositionLinkLoader.All.Single();

    public static string Folder => SwordTextSource.Texts[Module.Module].Folder.Replace('\\', '/');

    public string Method => MappingMethods.Stated;

    public string Name => "received-text-positions";

    public bool States(string pair) => CrossWirePositionLinkLoader.Witnesses.Any(witness => pair == $"{Module.Slug}-{witness}");

    public bool Makes(string source) => source == Module.Credit;

    public IEnumerable<string> Inputs(ProductStore store, string pair) => [Folder];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var translation = texts[fromText];
        var witness = texts[toText];
        var folder = texts.Store.FullPath(Folder);

        var corpus = translation.Verses
            .Select(verse => new CorpusVerse(verse.Book, verse.Chapter, verse.Verse, [],
                [.. translation.InVerse(verse).Select(id => new CorpusWord(id, translation.Words[id].Surface))]))
            .OrderBy(verse => (verse.Book, verse.Chapter, verse.Verse))
            .ToList();
        var tags = SynodalStrongLayer.Lay(SwordTextSource.Numbers(folder), corpus).Tags;
        var units = tags.ToDictionary(tag => tag.Key, tag => tag.Value.Unit);

        var verses = new Dictionary<(int, int, int), IReadOnlyList<CorpusWord>>(8_000);
        foreach (var verse in translation.Verses)
        {
            if (witness.ByAddress.TryGetValue(translation.Primary(verse), out var at))
            {
                verses[(verse.Book, verse.Chapter, verse.Verse)] = [.. at.Select(id => new CorpusWord(id, witness.Words[id].Surface))];
            }
        }

        var laid = ReceivedTextPositions.Lay(SwordTextSource.Positions(folder), units, verses);
        log($"{units.Count} words laid onto the module's elements; {laid}");
        return laid.Links.Select(link => new MappingRow(null,
            MappingText.Keys(link.From.Select(translation.Key)),
            MappingText.Keys(link.To.Select(witness.Key)),
            LinkRelation.Renders.ToString().ToLowerInvariant(), Method, null, Module.Credit, null, null, null));
    }
}

/// <summary>
/// What a pair's method files already say, as <see cref="ByzantineApparatusLinkLoader.PairStanding"/> holds it for a loader:
/// each shape's relation, and the words rendered and the words called absent, by <see cref="ProductText"/> ids, the TO
/// text's shifted past the FROM text's (<see cref="Offset"/>) so that the two never share an id, as words of the corpus do not.
/// </summary>
internal static class PairStanding
{
    /// <summary>What a TO word's id is shifted by beside the FROM text's.</summary>
    public static long Offset(ProductText from) => from.Words.Count;

    public static ByzantineApparatusLinkLoader.PairStanding Read(
        ProductStore store, string pair, IEnumerable<string> methods, ProductText from, ProductText to)
    {
        var fromIds = Ids(from);
        var toIds = Ids(to);
        var standing = new ByzantineApparatusLinkLoader.PairStanding([], [], []);
        foreach (var row in Rows(store, pair, methods))
        {
            List<long> left = [.. MappingText.Split(row.From).Select(key => (long)fromIds[key])];
            List<long> right = [.. MappingText.Split(row.To).Select(key => toIds[key] + Offset(from))];
            var relation = Enum.Parse<LinkRelation>(row.Relation, ignoreCase: true);
            standing.Shapes.TryAdd(LinkShape.Of(left, right), relation);
            (relation is LinkRelation.Expands or LinkRelation.Omits ? standing.Absent : standing.Rendered).UnionWith(left.Concat(right));
        }

        return standing;
    }

    /// <summary>Every row of the pair's files of those methods.</summary>
    public static IEnumerable<MappingRow> Rows(ProductStore store, string pair, IEnumerable<string> methods)
    {
        foreach (var method in methods)
        {
            var product = MappingProducts.Of(pair, method);
            if (!store.Exists(product))
            {
                continue;
            }

            using var reader = store.Open(product, MappingRow.Schema);
            foreach (var row in reader.Rows())
            {
                yield return row;
            }
        }
    }

    /// <summary>Each word's <see cref="ProductText"/> id by its own key as the method files write it.</summary>
    public static Dictionary<string, int> Ids(ProductText text)
    {
        var ids = new Dictionary<string, int>(text.Words.Count, StringComparer.Ordinal);
        for (var id = 0; id < text.Words.Count; id++)
        {
            ids[text.Words[id].Key.Own.ToString()] = id;
        }

        return ids;
    }
}
