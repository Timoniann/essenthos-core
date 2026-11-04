using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Which Berean word renders which original word, as the Berean's translation tables state it (<see cref="BereanLinkLoader"/>
/// has the measurements and the reasons), read from <c>Berean/bsb_tables.tsv</c> and both texts' words and frames.
///
/// <para>
/// **The tables number the Berean's verses as it prints them**, so a table verse is the Berean's own verse, and the original's
/// words are those standing at that verse's frame address. Where the Berean frames two verses crosswise (Philippians 1:16 and
/// 1:17 stand at each other's addresses, as the Textus Receptus orders them), the table's verse 16 meets the Berean's own
/// verse 16 and the original's words at its address.
/// </para>
///
/// <para>
/// A witness word a ruling takes out of this source's rows (<c>mapping/&lt;pair&gt;/rulings</c>) is left out, as the
/// database's loader left out what <see cref="LinkRulings"/> overruled.
/// </para>
/// </summary>
internal sealed class BereanTableSource : IPairSource
{
    public const string Tables = "Berean/bsb_tables.tsv";

    private const string Source = "Berean Standard Bible translation tables, bereanbible.com, public domain";

    /// <summary>How much of a verse's Strong numbers must agree before its order is believed (<see cref="BereanLinkLoader"/>).</summary>
    private const double Agreeing = 0.5;

    /// <summary>The verse a psalm's title stands inside where the tables print one.</summary>
    private const int FirstVerse = 1;

    public string Method => MappingMethods.Stated;

    public string Name => "berean";

    public bool States(string pair) => pair is "BSB-NESTLE1904" or "BSB-BHSA";

    public bool Makes(string source) => source == Source;

    public IEnumerable<string> Inputs(ProductStore store, string pair) =>
        [Tables, .. store.Find(MappingProducts.Of(pair, MappingProducts.Rulings)) is { } rulings ? [rulings] : Array.Empty<string>()];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var english = texts[fromText];
        var witnessText = texts[toText];
        var greekWitness = witnessText.Language == "grc";
        var overruledKeys = PairSources.Overruled(texts.Store, pair, Source);
        var overruled = new HashSet<long>();
        for (var id = 0; id < witnessText.Words.Count; id++)
        {
            if (overruledKeys.Contains(witnessText.Words[id].Key.Own.ToString()))
            {
                overruled.Add(id);
            }
        }

        var drafts = new List<BereanLinkLoader.Draft>(150_000);
        var silences = new BereanLinkLoader.Silences();
        int verses = 0, divided = 0, drifted = 0, disputed = 0, unframed = 0;
        foreach (var (reference, rows) in BereanTable.Verses(texts.Store.FullPath(Tables)))
        {
            if (!BereanTextSource.Address(reference, out var book, out var chapter, out var number) || rows[0].IsGreek != greekWitness)
            {
                continue;
            }

            var own = new VerseKey(book, chapter, number);
            if (!english.Has(own))
            {
                continue;
            }

            var address = english.Primary(own);
            if (!witnessText.ByAddress.TryGetValue(address, out var at))
            {
                unframed++;
                continue;
            }

            var ours = Words(english, english.InVerse(own));
            var witness = Words(witnessText, at);
            List<List<long>> run;
            if (rows[0].IsGreek)
            {
                if (rows.Count != witness.Count)
                {
                    divided++;
                    continue;
                }

                var (agreed, of) = BereanLinkLoader.Agreement(rows, witness);
                if (of > 0 && (double)agreed / of < Agreeing)
                {
                    disputed++;
                    continue;
                }

                run = [.. witness.Select(word => new List<long> { word.Id })];
            }
            else if (BereanLinkLoader.Letters(
                         [.. rows.Select(row => HebrewLetters.Of(row.Original))],
                         [.. witness.Select(word => HebrewLetters.Of(word.Surface))]) is { } matched)
            {
                run = [.. matched.Select(words => words.Select(index => witness[index].Id).ToList())];
            }
            else if (number == FirstVerse
                     && witnessText.ByAddress.TryGetValue((address.Book, address.Chapter, CanonicalReference.TitleVerse), out var titleAt)
                     && (List<BereanLinkLoader.Word>)[.. Words(witnessText, titleAt), .. witness] is var titled
                     && BereanLinkLoader.Letters(
                         [.. rows.Select(row => HebrewLetters.Of(row.Original))],
                         [.. titled.Select(word => HebrewLetters.Of(word.Surface))]) is { } withTitle)
            {
                run = [.. withTitle.Select(words => words.Select(index => titled[index].Id).ToList())];
            }
            else
            {
                divided++;
                continue;
            }

            if (!BereanLinkLoader.Pair(rows, ours, run, drafts, silences, overruled))
            {
                drifted++;
                continue;
            }

            verses++;
        }

        log($"{verses} verses, {drafts.Count} links; {silences.Absent} absent, {silences.Moved} moved, {silences.Joined} joined, "
            + $"{silences.Unmarked} unmarked, {silences.Overruled} overruled; {divided} divided, {drifted} drifted, {disputed} disputed, "
            + $"{unframed} at an address the witness lacks");
        return drafts.Select(draft => new MappingRow(MappingText.Keys(draft.From.Select(english.Key)),
            MappingText.Keys(draft.To.Select(witnessText.Key)),
            draft.Relation.ToString().ToLowerInvariant(), Method, null, Source, null, null, null));
    }

    private static List<BereanLinkLoader.Word> Words(ProductText text, IEnumerable<int> ids) =>
        [.. ids.Select(id => new BereanLinkLoader.Word(id, 0, text.Words[id].Surface, text.Words[id].Strong))];
}
