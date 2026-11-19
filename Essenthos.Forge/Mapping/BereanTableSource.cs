using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Essenthos.Core.Utils;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Which Berean word renders which original word, as the Berean's translation tables state it (<see cref="BereanLinks"/>
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
/// A witness word a ruling takes out of this source's rows (<c>mapping/&lt;pair&gt;/rulings</c>) is left out.
/// </para>
/// </summary>
internal sealed class BereanTableSource : IPairSource
{
    public const string Tables = "Berean/bsb_tables.tsv";

    private const string Source = "Berean Standard Bible translation tables, bereanbible.com, public domain";

    /// <summary>BHSA's number for the conjunction ו, which it writes as a word of its own.</summary>
    private const string Waw = "H9000";

    /// <summary>BHSA's numbers for the prepositions ב, כ, ל and מִן, which it writes as words of their own where they are prefixed.</summary>
    private static readonly HashSet<string> Prepositions = new(["H9003", "H9004", "H9005", "H4480"], StringComparer.Ordinal);

    /// <summary>The verse a psalm's title stands inside where the tables print one.</summary>
    private const int FirstVerse = 1;

    public string Method => MappingMethods.Stated;

    public string Name => "berean";

    public bool States(string pair) => pair is "BSB-NESTLE1904" or "BSB-BHSA";

    public bool Makes(string source) => source == Source;

    public IEnumerable<string> Inputs(ProductStore store, string pair) =>
        [Tables, .. store.Find(MappingProducts.Of(pair, MappingProducts.Rulings)) is { } rulings ? [rulings] : Array.Empty<string>(), StrongNumberLists.Source];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var lists = StrongNumberLists.Of(texts.Store);
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

        var drafts = new List<BereanLinks.Draft>(150_000);
        var silences = new BereanLinks.Silences();
        int verses = 0, uneven = 0, divided = 0, drifted = 0, disputed = 0, unframed = 0;
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
            HashSet<int>? unprinted = null;
            if (rows[0].IsGreek)
            {
                if (BereanLinks.Counterparts(rows, witness, lists) is not { } counterparts)
                {
                    disputed++;
                    continue;
                }

                if (rows.Count != witness.Count)
                {
                    uneven++;
                }

                run = [.. counterparts.Select(at => at < 0 ? [] : new List<long> { witness[at].Id })];
                unprinted = [.. Enumerable.Range(0, rows.Count).Where(at => counterparts[at] < 0)];
            }
            else if (BereanLinks.Letters(
                         [.. rows.Select(row => HebrewLetters.Of(row.Original))],
                         [.. witness.Select(word => HebrewLetters.Of(word.Surface))]) is { } matched)
            {
                run = [.. matched.Select(words => words.Select(index => witness[index].Id).ToList())];
            }
            else if (number == FirstVerse
                     && witnessText.ByAddress.TryGetValue((address.Book, address.Chapter, CanonicalReference.TitleVerse), out var titleAt)
                     && (List<BereanLinks.Word>)[.. Words(witnessText, titleAt), .. witness] is var titled
                     && BereanLinks.Letters(
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

            var conjunctions = greekWitness
                ? null
                : run.SelectMany(words => words).Where(id => witnessText.Words[(int)id].Strong == Waw).ToHashSet();
            var prepositions = greekWitness
                ? null
                : run.SelectMany(words => words).Distinct()
                    .Where(id => witnessText.Words[(int)id].Strong is { } strong && Prepositions.Contains(strong))
                    .ToDictionary(id => id, id => witnessText.Words[(int)id].Strong!);
            if (!BereanLinks.Pair(rows, ours, run, drafts, silences, overruled, conjunctions, prepositions, unprinted))
            {
                drifted++;
                continue;
            }

            verses++;
        }

        // A word the English prints in brackets is the translators' own and renders nothing, so it is not linked, and a
        // rendering of nothing else is not written: Gen 6:11's second הָאָרֶץ is «[and]» in the table. The exception is the
        // bracketed words a conjunction or a preposition leaves to a word, «And [he]» on יְהוֹשֻׁעַ (BereanLinks.Supplied) and
        // «from [his]» on הַמִּצְרִי (BereanLinks.Prefixed).
        var supplied = drafts.Where(draft => !draft.Bracketed).Sum(draft => draft.From.Count(id => Bracketed(english, id)));
        var onlySupplied = drafts.Count(draft => !draft.Bracketed && draft.From.Count > 0 && draft.From.All(id => Bracketed(english, id)));
        drafts = [.. drafts
            .Select(draft => draft.Bracketed ? draft : draft with { From = [.. draft.From.Where(id => !Bracketed(english, id))] })
            .Where(draft => draft.Relation != LinkRelation.Renders || draft.From.Count > 0)];
        log($"{verses} verses ({uneven} of a word count the witness does not share), {drafts.Count} links; {silences.Absent} absent, {silences.Moved} moved, {silences.Joined} joined, "
            + $"{silences.Unmarked} unmarked, {silences.Overruled} overruled, {silences.Unprinted} over a word the witness does not print there; {divided} divided, {drifted} drifted, {disputed} disputed, "
            + $"{unframed} at an address the witness lacks; {supplied} bracketed words left unlinked, {onlySupplied} renderings of nothing else; "
            + $"{silences.ConjunctionsMoved + silences.ConjunctionsUnlinked} words rendered by bracketed words beside a conjunction, "
            + $"{silences.ConjunctionsMoved} of the conjunctions moved to the waw beside, {silences.ConjunctionsUnlinked} left unlinked; "
            + $"{silences.PrepositionsMoved} words rendered by bracketed words beside a preposition, the preposition moved to the prefix of the next word");
        return drafts.Select(draft => new MappingRow(MappingText.Keys(draft.From.Select(english.Key)),
            MappingText.Keys(draft.To.Select(witnessText.Key)),
            draft.Relation.ToString().ToLowerInvariant(), Method, null, Source, null, null, null));
    }

    /// <summary>
    /// Whether the Berean prints the word in brackets, as one it supplies. Its braces mark the auxiliaries a verb's tense or
    /// mood implies («{will}», «{let}»), which render that verb and stay linked.
    /// </summary>
    private static bool Bracketed(ProductText text, long id) => text.Words[(int)id].SuppliedMark == "[]";

    private static List<BereanLinks.Word> Words(ProductText text, IEnumerable<int> ids) =>
        [.. ids.Select(id => new BereanLinks.Word(id, 0, text.Words[id].Surface, text.Words[id].Strong))];
}
