using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Two Greek editions laid against each other by the letters each prints within each address of the canonical frame
/// (<see cref="SeptuagintLinkLoader"/> has the reasons): Swete against Brenton, Ottley and Swete's Old Greek against Swete,
/// the Old Greek against Brenton, and the SBL Greek New Testament against Nestle and Scrivener. Read from both texts' words
/// and frames; an address either edition fills with two verses is taken whole, the verses in order.
/// </summary>
internal sealed class GreekLetterSource : IPairSource
{
    public string Method => MappingMethods.Lexical;

    public string Name => "greek-letters";

    public bool States(string pair) =>
        pair is "SWETE-GRCBRENT" or "OTTLEY-SWETE" or "SWETEOG-SWETE" or "SWETEOG-GRCBRENT" or "SBLGNT-NESTLE1904" or "SBLGNT-TR1894";

    public bool Makes(string source) => source == SeptuagintLinkLoader.Source;

    public IEnumerable<string> Inputs(ProductStore store, string pair) => [];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var from = texts[fromText];
        var to = texts[toText];
        var drafts = new List<SeptuagintLinkLoader.GreekDraft>(600_000);
        int addresses = 0, unpaired = 0;
        foreach (var (address, left) in from.ByAddress.OrderBy(entry => entry.Key))
        {
            if (!to.ByAddress.TryGetValue(address, out var right))
            {
                unpaired++;
                continue;
            }

            addresses++;
            SeptuagintLinkLoader.Pair(Words(from, left), Words(to, right), drafts);
        }

        unpaired += to.ByAddress.Keys.Count(address => !from.ByAddress.ContainsKey(address));
        log($"{addresses} shared addresses, {unpaired} only one fills, {drafts.Count} links");
        return drafts.Select(draft => Row(from, to, draft.Relation, draft.From, draft.To, draft.Confidence, SeptuagintLinkLoader.Source));
    }

    private static List<SeptuagintLinkLoader.GreekWord> Words(ProductText text, List<int> ids) =>
    [
        .. ids.Select(id => new SeptuagintLinkLoader.GreekWord(id,
            new WitnessForm(GreekLetters.Bare(text.Words[id].Surface), GreekLetters.Bare(text.Words[id].Lemma ?? string.Empty)))),
    ];

    internal static MappingRow Row(ProductText from, ProductText to, LinkRelation relation, List<long> fromIds, List<long> toIds,
        double confidence, string source) =>
        new(null, MappingText.Keys(fromIds.Select(from.Key)), MappingText.Keys(toIds.Select(to.Key)),
            relation.ToString().ToLowerInvariant(), MappingMethods.Lexical, confidence, source, null, null, null);
}

/// <summary>
/// The Samaritan Pentateuch against BHSA by the consonants both write, verse by verse on the numbering both follow, and the
/// runs of verses the Samaritan writes elsewhere placed where it writes them (<see cref="SamaritanLinkLoader"/> has the
/// reasons). Read from both texts' words.
/// </summary>
internal sealed class HebrewLetterSource : IPairSource
{
    public string Method => MappingMethods.Lexical;

    public string Name => "hebrew-letters";

    public bool States(string pair) => pair == "SP-BHSA";

    public bool Makes(string source) => source == SamaritanLinkLoader.Source;

    public IEnumerable<string> Inputs(ProductStore store, string pair) => [];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var from = texts[fromText];
        var to = texts[toText];
        var here = Verses(from);
        var there = Verses(to);
        var drafts = new List<SamaritanLinkLoader.HebrewDraft>(130_000);
        var expandedAt = new Dictionary<(int Book, int Chapter, int Verse), List<SamaritanLinkLoader.HebrewDraft>>();
        var verses = 0;
        foreach (var (address, left) in here.OrderBy(entry => entry.Key))
        {
            if (!there.TryGetValue(address, out var right))
            {
                continue;
            }

            verses++;
            var before = drafts.Count;
            SamaritanLinkLoader.Pair(left, right, drafts);
            var added = drafts.Skip(before).Where(draft => draft.Relation == LinkRelation.Expands).ToList();
            if (added.Count > 0)
            {
                expandedAt[address] = added;
            }
        }

        var (placed, moved, _) = SamaritanLinkLoader.Relocate(here, there, expandedAt, drafts);
        log($"{verses} verses, {drafts.Count} links; {placed} verses placed where the Samaritan writes them, {moved} words transposed");
        return drafts.Select(draft => GreekLetterSource.Row(from, to, draft.Relation, draft.From, draft.To, draft.Confidence, SamaritanLinkLoader.Source));
    }

    /// <summary>A text's words by the address its own numbering gives each verse, in order.</summary>
    private static Dictionary<(int Book, int Chapter, int Verse), List<SamaritanLinkLoader.HebrewWord>> Verses(ProductText text)
    {
        var verses = new Dictionary<(int, int, int), List<SamaritanLinkLoader.HebrewWord>>(text.Verses.Count);
        foreach (var verse in text.Verses)
        {
            var address = (verse.Book, verse.Chapter, verse.Verse);
            if (!verses.TryGetValue(address, out var words))
            {
                verses[address] = words = [];
            }

            words.AddRange(text.InVerse(verse).Select(id => new SamaritanLinkLoader.HebrewWord(id,
                new WitnessForm(HebrewLetters.Of(text.Words[id].Surface), HebrewLetters.Of(text.Words[id].Lemma ?? string.Empty)))));
        }

        return verses;
    }
}
