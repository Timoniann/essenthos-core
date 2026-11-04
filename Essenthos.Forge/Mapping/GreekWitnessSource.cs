using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Two Greek editions of the New Testament paired by the Strong numbers both carry, within each address of the frame, and
/// the words no number answers matched by their place in the verse (<see cref="GreekWitnessLinkLoader"/> has the reasons),
/// read from both texts' words and frames. One pairing makes both methods' rows: the numbers' pairings and absences are
/// <c>strong</c>, the leftovers matched by place <c>lexical</c>; each method's source takes its own.
/// </summary>
internal sealed class GreekWitnessSource(string method) : IPairSource
{
    /// <summary>Added to the second edition's word ids: the pairing tells the leftovers of both editions apart by id alone.</summary>
    internal const long Second = 1L << 32;

    public string Method => method;

    public string Name => method == MappingMethods.Strong ? "greek-witnesses" : "greek-witness-leftovers";

    /// <summary>The pairs the database's load step linked: every edition to Scrivener, Nestle's two ingredients to Nestle.</summary>
    public bool States(string pair) =>
        pair is "NESTLE1904-TR1894" or "RP2018-TR1894" or "TISCH-TR1894" or "WH1881-TR1894" or "TISCH-NESTLE1904" or "WH1881-NESTLE1904";

    public bool Makes(string source) => method == MappingMethods.Strong
        ? source is GreekWitnessLinkLoader.Source or GreekWitnessLinkLoader.LemmatisedSource
        : source == GreekWitnessLinkLoader.SubstitutionSource;

    public IEnumerable<string> Inputs(ProductStore store, string pair) => [];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var drafts = Drafts(texts[fromText], texts[toText], out var verses);
        var database = MappingMethods.DatabaseMethod(method);
        var mine = drafts.Where(draft => draft.Method == database).ToList();
        log($"{verses} addresses, {drafts.Count} pairings, {mine.Count} of them {method}");
        return mine.Select(draft => new MappingRow(MappingText.Keys(draft.From.Select(texts[fromText].Key)),
            MappingText.Keys(draft.To.Select(id => texts[toText].Key(id - Second))),
            draft.Relation.ToString().ToLowerInvariant(), method, draft.Confidence, draft.Source, null, null, null));
    }

    /// <summary>
    /// Every address both editions fill, paired as the database's loader paired it. A word's id is its place in its text's
    /// words, plus <see cref="Second"/> for the second edition's, its position the one its key gives.
    /// </summary>
    internal static List<GreekWitnessLinkLoader.GreekDraft> Drafts(ProductText from, ProductText to, out int verses)
    {
        var drafts = new List<GreekWitnessLinkLoader.GreekDraft>(150_000);
        verses = 0;
        foreach (var (address, left) in from.ByAddress)
        {
            if (!to.ByAddress.TryGetValue(address, out var right))
            {
                continue;
            }

            verses++;
            GreekWitnessLinkLoader.Pair(Words(from, left, 0), Words(to, right, Second), drafts);
        }

        return drafts;
    }

    private static List<GreekWitnessLinkLoader.GreekWord> Words(ProductText text, List<int> ids, long offset) =>
        [.. ids.Select(id => new GreekWitnessLinkLoader.GreekWord(offset + id, text.Words[id].Key.Position, text.Words[id].Surface, text.Words[id].Strong))];
}
