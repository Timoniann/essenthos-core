using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>The sources read from published tables, mappings and the editions' own marks.</summary>
internal static class TableSources
{
    public static readonly IReadOnlyList<IPairSource> All =
    [
        new OpenHebrewStatedSource(),
        new OpenHebrewLexicalSource(),
        new OpenHebrewCuvSource(),
        new PrintedEditionSource(),
        new ByzantineApparatusSource(),
        new ReceivedTextPositionSource(),
        new KingJamesItalicsSource(),
        new SuppliedWordSource(),
    ];
}

/// <summary>
/// The words the King James prints in italics in the New Testament, each stated absent from the Greek witness that has its
/// verse (<see cref="NewTestamentLinkLoader"/>): an <c>expands</c> row per word. The marks are the words product's
/// <c>supplied</c> runs, which its converter reads off Zefania's KJV+ the way the loader did (an italic word the edition
/// numbers nothing is supplied; <see cref="Conversion.KingJamesNumbers"/>).
/// </summary>
internal sealed class KingJamesItalicsSource : IPairSource
{
    private const int FirstNewTestamentBook = 40;

    public string Method => MappingMethods.Stated;

    public string Name => "kjv-italics";

    public bool States(string pair) =>
        KingJamesGreekSource.Witnesses.Any(witness => pair == $"{Bible4uTextSource.KingJames}-{witness}");

    public bool Makes(string source) => source == NewTestamentLinkLoader.SuppliedSource;

    public IEnumerable<string> Inputs(ProductStore store, string pair) => [];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var english = texts[fromText];
        var witness = texts[toText];
        var rows = new List<MappingRow>(4_200);
        foreach (var verse in english.Verses.Where(verse => verse.Book >= FirstNewTestamentBook))
        {
            if (!witness.ByAddress.ContainsKey(english.Primary(verse)))
            {
                continue;
            }

            rows.AddRange(english.InVerse(verse)
                .Where(id => english.Words[id].Supplied is not null)
                .Select(id => new MappingRow(english.Key(id).Own.ToString(), string.Empty,
                    LinkRelation.Expands.ToString().ToLowerInvariant(), Method, null, NewTestamentLinkLoader.SuppliedSource, null, null, null)));
        }

        log($"{rows.Count} italic words absent from {toText}");
        return rows;
    }
}

/// <summary>
/// The words an edition prints as its translators' own (<see cref="SuppliedWordAbsences"/>: the Almeida's, the American
/// Standard's, the Reina-Valera's and Brenton's italics, the Synodal's brackets, the Biblica Ukrainian's marks), each marked
/// run of a verse stated absent from an original-language witness the edition is paired with that has the verse: the Hebrew
/// for the Old Testament, the Greek for the New, and the Greek throughout for an edition translated from it.
///
/// <para>
/// **A run gives way to a rendering.** A marked word another link of the pair renders is left rendered and not stated
/// absent, whatever method draws that link, except the aligner's own guess when every word it names on the edition's side is
/// marked: the loader withdrew those, and the merge's stated absence outranks them. The renderings are read from the pair's
/// other method files and from the rows the other stated sources make. EVIDENTIA's file is not read: it is made from the
/// stated file (the build would have a cycle), so a marked word only an EVIDENTIA verdict renders is stated absent here.
/// </para>
/// </summary>
internal sealed class SuppliedWordSource : IPairSource
{
    private const string Hebrew = "hbo";
    private const string Greek = "grc";
    private const int LastOldTestamentBook = 39;
    private const int LastNewTestamentBook = 66;

    /// <summary>The pair's files whose renderings a marked word gives way to.</summary>
    public static readonly string[] Renderings =
    [
        MappingMethods.Strong, MappingMethods.Aligner, MappingMethods.Composed, MappingMethods.Names, MappingMethods.NameList,
        MappingMethods.Possessive, MappingMethods.Lexical, MappingMethods.Rule, MappingMethods.Manual, MappingMethods.Evidentia,
        MappingMethods.Stated,
    ];

    /// <summary>The texts in the original languages an edition's marks can be stated against; each one's declaration decides.</summary>
    private static readonly HashSet<string> Witnesses =
    [
        "BHSA", "SP", "ALEX", "GRCBRENT", "NESTLE1904", "OTTLEY", "RP2018", "SBLGNT", "SIN", "SWETE", "SWETEOG", "TISCH", "TR1550",
        "TR1894", "VAT", "WH1881",
    ];

    private const string Machine = "SIL.Machine";

    public string Method => MappingMethods.Stated;

    public string File => PairSources.Laid;

    public string Name => "supplied-words";

    public bool States(string pair) =>
        MappingProducts.Split(pair) is var (from, to) && Witnesses.Contains(to) && SuppliedWordAbsences.Editions.Any(e => e.Slug == from);

    public bool Makes(string source) => SuppliedWordAbsences.Editions.Any(edition => edition.Source == source);

    public IEnumerable<string> Inputs(ProductStore store, string pair) =>
        Renderings.Select(method => store.Find(MappingProducts.Of(pair, method))).OfType<string>();

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var edition = SuppliedWordAbsences.Editions.Single(e => e.Slug == fromText);
        var marked = texts[fromText];
        var witness = texts[toText];
        var declared = IndexContext.Declared(texts.Store, toText);
        if (declared.Kind == TextKind.Translation || declared.Language is not (Hebrew or Greek))
        {
            return [];
        }

        var runs = new List<List<WordKey>>();
        foreach (var verse in marked.Verses)
        {
            var language = verse.Book <= LastOldTestamentBook ? edition.OldTestament
                : verse.Book <= LastNewTestamentBook ? Greek
                : edition.OldTestament == Greek ? Greek : null;
            if (language != declared.Language || !witness.ByAddress.ContainsKey(marked.Primary(verse)))
            {
                continue;
            }

            runs.AddRange(marked.InVerse(verse)
                .Where(id => marked.Words[id].Supplied is not null)
                .GroupBy(id => marked.Words[id].Supplied!.Value)
                .Select(run => run.Select(id => marked.Key(id)).ToList()));
        }

        var absent = runs.SelectMany(run => run).Select(key => key.Own.ToString()).ToHashSet(StringComparer.Ordinal);
        var kept = Rendered(texts, pair, absent, out var withdrawn);
        var rows = runs
            .Select(run => run.Where(word => !kept.Contains(word.Own.ToString())).ToList())
            .Where(run => run.Count > 0)
            .Select(run => new MappingRow(MappingText.Keys(run), string.Empty,
                LinkRelation.Expands.ToString().ToLowerInvariant(), Method, null, edition.Source, null, null, null))
            .ToList();
        log($"{absent.Count} marked words in {runs.Count} runs; {kept.Count} rendered by another link and kept so, "
            + $"{withdrawn} aligner links over marked words alone give way");
        return rows;
    }

    /// <summary>
    /// The marked words a link of the pair renders that is not the aligner's alone over marked words only; and how many of
    /// the aligner's links over marked words only there are, which give way to the absence.
    /// </summary>
    private IReadOnlySet<string> Rendered(ProductTexts texts, string pair, IReadOnlySet<string> absent, out int withdrawn)
    {
        var rows = PairStanding.Rows(texts.Store, pair, Renderings)
            .Where(row => row.Relation is "renders" or "equals")
            .ToList();
        var claims = rows.GroupBy(row => row.Shape, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.All(Guess), StringComparer.Ordinal);

        var kept = new HashSet<string>(StringComparer.Ordinal);
        withdrawn = 0;
        foreach (var row in rows)
        {
            var from = MappingText.Split(row.From);
            if (!from.Any(absent.Contains))
            {
                continue;
            }

            if (claims[row.Shape] && from.All(absent.Contains))
            {
                withdrawn++;
                continue;
            }

            kept.UnionWith(from.Where(absent.Contains));
        }

        return kept;

        static bool Guess(MappingRow row) =>
            row.Relation == "renders"
            && MappingMethods.DatabaseMethod(row.Method) == LinkMethod.Aligner
            && row.Source.StartsWith(Machine, StringComparison.Ordinal);
    }
}
