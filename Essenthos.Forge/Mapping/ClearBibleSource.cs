using Essenthos.Core.ClearBible;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Which word of a translation renders which Hebrew or Greek word, as Clear Bible's hand-made alignments state it
/// (<see cref="ClearBibleLinkLoader"/> has the measurements and the reasons, <see cref="ClearBibleSet"/> each set's
/// files and join), read from <c>ClearBible/data/</c> and both texts' words and frames. The join is the loader's own
/// (<see cref="ClearBibleLinkLoader.Join"/>); only the words come from the products instead of the database.
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
/// which is withheld where it was its only witness word, as the database's loader did with what
/// <see cref="LinkRulings"/> overruled.
/// </para>
/// </summary>
internal sealed class ClearBibleSource : IPairSource
{
    public const string Folder = "ClearBible";

    private const string Renders = "renders";

    private static readonly IReadOnlyList<ClearBibleSet> Sets = ClearBibleSet.All();

    public string Method => MappingMethods.Stated;

    public string Name => "clearbible";

    public bool States(string pair) => Set(pair) is not null;

    public bool Makes(string source) => Sets.Any(set => set.Statement == source);

    public IEnumerable<string> Inputs(ProductStore store, string pair)
    {
        var set = Set(pair)!;
        return
        [
            .. new[] { set.Alignment, set.Target, set.Source }.Where(path => path.Length > 0).Select(Relative),
            .. store.Find(MappingProducts.Of(pair, MappingProducts.Rulings)) is { } rulings ? [rulings] : Array.Empty<string>(),
        ];
    }

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var set = Set(pair)!;
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

        var joined = ClearBibleLinkLoader.Join(
            texts.Store.FullPath(Folder),
            set,
            new ClearBibleSide(
                translation.Language,
                retokenised ? null : Printed(translation),
                retokenised ? InOrder(translation) : null,
                Frame(translation)),
            new ClearBibleSide(witness.Language, edition ? Placed(witness, translation) : Printed(witness), null, edition ? null : Frame(witness)),
            overruled);

        log($"{joined.Records} records, {joined.Drafts.Count} resolved; {joined.Unresolved} unresolved ({joined.WithoutCounterpart} "
            + $"without a counterpart), {joined.Astray} astray, {joined.Shifted} shifted, {joined.Misnumbered} misnumbered, "
            + $"{joined.Overruled} overruled ({joined.Trimmed} trimmed); {joined.Placed}");

        var rows = new Dictionary<string, MappingRow>(StringComparer.Ordinal);
        foreach (var draft in joined.Drafts)
        {
            var row = new MappingRow(null,
                MappingText.Keys(draft.From.Select(translation.Key)),
                MappingText.Keys(draft.To.Select(witness.Key)),
                Renders, Method, null, set.Statement, null, null, null);
            rows.TryAdd(row.Shape, row);
        }

        return rows.Values;
    }

    private static ClearBibleSet? Set(string pair) => Sets.FirstOrDefault(set => $"{set.From}-{set.To}" == pair);

    private static string Relative(string path) => $"{Folder}/{path.Replace('\\', '/')}";

    /// <summary>A text's words by the verse it prints them in, as <see cref="ClearBibleAlignment.Address"/> numbers verses.</summary>
    private static Dictionary<(int, int, int), List<ClearBibleLinkLoader.Word>> Printed(ProductText text)
    {
        var words = new Dictionary<(int, int, int), List<ClearBibleLinkLoader.Word>>(text.Verses.Count);
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
    private static Dictionary<(int, int, int), List<ClearBibleLinkLoader.Word>> Placed(ProductText witness, ProductText translation)
    {
        var words = witness.ByAddress.ToDictionary(address => address.Key, address => address.Value.Select(id => Word(witness, id)).ToList());
        var printed = new Dictionary<(int, int, int), List<ClearBibleLinkLoader.Word>>(words);
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

    private static ClearBibleLinkLoader.Word Word(ProductText text, int id) =>
        new(id, ClearBibleLinkLoader.Comparable(text.Words[id].Surface, text.Language), text.Words[id].Strong);

    private static List<ClearBibleLinkLoader.PrintedWord> InOrder(ProductText text) =>
    [
        .. text.Words.Select((word, id) => new ClearBibleLinkLoader.PrintedWord(
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
