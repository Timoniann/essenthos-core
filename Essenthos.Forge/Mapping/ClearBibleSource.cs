using Essenthos.Core.ClearBible;
using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Which word of a translation renders which Hebrew or Greek word, as Clear Bible's hand-made alignments state it
/// (<see cref="ClearBibleLinks"/> has the measurements and the reasons, <see cref="ClearBibleSet"/> each set's
/// files and join, as <c>sources/clear-bible.tsv</c> declares them), read from <c>ClearBible/data/</c> and both texts' words and frames. The join is the loader's own
/// (<see cref="ClearBibleLinks.Join"/>); only the words come from the products instead of the database.
///
/// <para>
/// The sets number every word by the verse its own edition prints it in, so a record's translation words are found in
/// the translation's own verse and its source words in the witness's own verse, wherever the frame places either. The
/// Berean's set alone is keyed to another edition of the witness's text (the Berean Greek New Testament against Nestle
/// 1904, <see cref="ClearBibleJoin.Edition"/>), and that edition numbers its verses as the Berean prints them: its
/// Philippians 1:16 is the Berean's 1:16, which the frame places at 1:17 as the Textus Receptus orders the two. So the
/// witness's words it is laid against under a verse are those at the frame address of the Berean's verse of that number.
/// The database's loader took the witness's words at the frame address of the number itself, which crossed the two verses.
/// </para>
///
/// <para>
/// A witness word a ruling takes out of this source's rows (<c>mapping/&lt;pair&gt;/rulings</c>) is left out of the record,
/// which is withheld where it was its only witness word.
/// </para>
/// </summary>
internal sealed class ClearBibleSource : IPairSource
{
    public const string Folder = "ClearBible";

    private const string Renders = "renders";

    /// <summary>What a row says of itself where its record named the word before the name (<see cref="ClearBibleLinks.Early"/>).</summary>
    internal const string MovedNote = "the set's record names the preposition or article before this name and leaves the name unnamed; moved onto the name";

    /// <summary>What a row says of itself where its record named the preposition or article before a common word (<see cref="ClearBibleLinks.Slipped"/>).</summary>
    internal const string MovedOntoWordNote =
        "the set's record names the preposition or article before this word and leaves the word unnamed; moved onto the word, which the set renders the same Strong number with elsewhere";

    public string Method => MappingMethods.Stated;

    public string Name => "clearbible";

    /// <summary>Never without the store: the sets are the store's file's (<see cref="States(ProductStore, string)"/>).</summary>
    public bool States(string pair) => false;

    /// <summary>Never without the store: the sets are the store's file's (<see cref="Makes(ProductStore, string)"/>).</summary>
    public bool Makes(string source) => false;

    public bool States(ProductStore store, string pair) => Set(store, pair) is not null;

    public bool Makes(ProductStore store, string source) => ClearBibleSet.All(store).Any(set => set.Statement == source);

    public IEnumerable<string> Inputs(ProductStore store, string pair)
    {
        var set = Set(store, pair)!;
        return
        [
            .. new[] { set.Alignment, set.Target, set.Source }.Where(path => path.Length > 0).Select(Relative),
            ClearBibleSet.File,
            .. set.Shift is not null || set.Early is not null || set.Function is not null ? [SourceWords.Source] : Array.Empty<string>(),
            .. store.Find(MappingProducts.Of(pair, MappingProducts.Rulings)) is { } rulings ? [rulings] : Array.Empty<string>(),
            StrongNumberLists.Source,
            .. set.Edited ? ProductText.Products(Sources.ScrivenerSlug).Select(store.Find).OfType<string>() : [],
        ];
    }

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var set = Set(texts.Store, pair)!;
        var translation = texts[set.From];
        var witness = texts[set.To];
        var retokenised = set.Numbering == ClearBibleNumbering.Retokenised;
        var edition = set.Join == ClearBibleJoin.Edition;

        var overruledKeys = PairSources.Overruled(texts.Store, pair, set.Statement);
        var overruled = new HashSet<long>();
        for (var id = 0; id < witness.Words.Count; id++)
        {
            if (overruledKeys.Contains(witness.Words[id].Key.Own.ToString()))
            {
                overruled.Add(id);
            }
        }

        var joined = ClearBibleLinks.Join(
            texts.Store.FullPath(Folder),
            set,
            new ClearBibleSide(
                translation.Language,
                retokenised ? null : Printed(translation),
                retokenised ? InOrder(translation) : null,
                Frame(translation)),
            new ClearBibleSide(witness.Language, edition ? Placed(witness, translation) : Printed(witness), null, edition ? null : Frame(witness)),
            overruled,
            StrongNumberLists.Of(texts.Store),
            set.Edited ? Editions(texts, witness) : null);

        log($"{joined.Records} records, {joined.Drafts.Count} resolved; {joined.Unresolved} unresolved ({joined.WithoutCounterpart} "
            + $"without a counterpart), {joined.Astray} astray, {joined.Shifted} shifted, {joined.Moved} moved onto the name after them, "
            + $"{joined.MovedOntoWord} moved onto the common word after them and {joined.Slipped} refused as on the word before one, "
            + $"{joined.Stray} possessives taken off a word they do not render ({joined.StrayRecords} records left with no word), "
            + $"{joined.Misnumbered} misnumbered, {joined.Renumbered} renumbered and {joined.Unplaced} placed on nothing over "
            + $"{joined.Edited} verses numbered over an edited source ({joined.Crossed} identifiers crossed), "
            + $"{joined.Overruled} overruled ({joined.Trimmed} trimmed), {joined.Scrambled} refused in verses numbered off as a block; {joined.Placed}");

        var rows = new Dictionary<string, MappingRow>(StringComparer.Ordinal);
        foreach (var draft in joined.Drafts)
        {
            var row = new MappingRow(MappingText.Keys(draft.From.Select(translation.Key)),
                MappingText.Keys(draft.To.Select(witness.Key)),
                Renders, Method, null, set.Statement, draft.Moved switch
                {
                    ClearBibleLinks.Move.Name => MovedNote,
                    ClearBibleLinks.Move.Word => MovedOntoWordNote,
                    _ => null,
                }, null, null);
            rows.TryAdd(row.Shape, row);
        }

        return rows.Values;
    }

    /// <summary>
    /// The editions whose readings a set's edited source text takes (<see cref="ClearBibleSourceEdits"/>): the witness
    /// itself, and the Textus Receptus Young translated, at a higher cost because the edited text follows it less often.
    /// </summary>
    private static List<ClearBibleSourceEdits.Edition> Editions(ProductTexts texts, ProductText witness) =>
    [
        new(Readings(witness), WitnessCost),
        new(Readings(texts[Sources.ScrivenerSlug]), ReceivedCost),
    ];

    /// <summary>What a word added or taken out where the witness, or the Textus Receptus, reads so costs the edited text, as a logarithm.</summary>
    private const double WitnessCost = 2, ReceivedCost = 4;

    private static Dictionary<int, List<ClearBibleSourceEdits.Reading>> Readings(ProductText text) =>
        Printed(text).ToDictionary(
            verse => ClearBibleAlignment.Verse(verse.Key.Item1, verse.Key.Item2, verse.Key.Item3),
            verse => verse.Value.Select(word => new ClearBibleSourceEdits.Reading(word.Folded, word.Strong)).ToList());

    private static ClearBibleSet? Set(ProductStore store, string pair) =>
        ClearBibleSet.All(store).FirstOrDefault(set => set.Pair == pair);

    private static string Relative(string path) => $"{Folder}/{path.Replace('\\', '/')}";

    /// <summary>A text's words by the verse it prints them in, as <see cref="ClearBibleAlignment.Address"/> numbers verses.</summary>
    private static Dictionary<(int, int, int), List<ClearBibleLinks.Word>> Printed(ProductText text)
    {
        var words = new Dictionary<(int, int, int), List<ClearBibleLinks.Word>>(text.Verses.Count);
        foreach (var verse in text.Verses)
        {
            var address = (verse.Book, verse.Chapter, verse.Verse);
            if (!words.TryGetValue(address, out var list))
            {
                words[address] = list = [];
            }

            list.AddRange(text.InVerse(verse).Select(id => Word(text, id)));
        }

        return words;
    }

    /// <summary>
    /// The witness's words for a source edition numbered as the translation prints its verses: under each verse the
    /// translation prints, the witness's words at that verse's frame address; under an address the translation prints no
    /// verse at, the witness's words standing there.
    /// </summary>
    private static Dictionary<(int, int, int), List<ClearBibleLinks.Word>> Placed(ProductText witness, ProductText translation)
    {
        var words = witness.ByAddress.ToDictionary(address => address.Key, address => address.Value.Select(id => Word(witness, id)).ToList());
        var printed = new Dictionary<(int, int, int), List<ClearBibleLinks.Word>>(words);
        foreach (var verse in translation.Verses)
        {
            var own = (verse.Book, verse.Chapter, verse.Verse);
            var address = translation.Primary(verse);
            if (address != own)
            {
                if (words.TryGetValue(address, out var there))
                {
                    printed[own] = there;
                }
                else
                {
                    printed.Remove(own);
                }
            }
        }

        return printed;
    }

    private static ClearBibleLinks.Word Word(ProductText text, int id) =>
        new(id, ClearBibleLinks.Comparable(text.Words[id].Surface, text.Language), text.Words[id].Strong);

    private static List<ClearBibleLinks.PrintedWord> InOrder(ProductText text) =>
    [
        .. text.Words.Select((word, id) => new ClearBibleLinks.PrintedWord(
            word.Key.Verse.Book, word.Key.Verse.Chapter, word.Key.Verse.Verse, word.Key.Position, id, word.Surface, word.Trailer)),
    ];

    /// <summary>Every frame address each verse a text prints stands at, both as <see cref="ClearBibleAlignment.Verse(int, int, int)"/> writes them.</summary>
    private static Dictionary<int, HashSet<int>> Frame(ProductText text)
    {
        var frame = new Dictionary<int, HashSet<int>>(text.Verses.Count);
        foreach (var verse in text.Verses)
        {
            var printed = ClearBibleAlignment.Verse(verse.Book, verse.Chapter, verse.Verse);
            if (!frame.TryGetValue(printed, out var placed))
            {
                frame[printed] = placed = [];
            }

            placed.UnionWith(text.Addresses(verse).Select(address => ClearBibleAlignment.Verse(address.Book, address.Chapter, address.Verse)));
        }

        return frame;
    }
}
