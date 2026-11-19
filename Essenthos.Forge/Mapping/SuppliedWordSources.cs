using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
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
/// verse: an <c>expands</c> row per word. The marks are the words product's <c>supplied</c> runs, which its converter reads
/// off CrossWire's KJV (a word in <c>transChange type="added"</c> whose element names no number is supplied;
/// <see cref="Conversion.KingJamesNumbers"/>).
/// </summary>
internal sealed class KingJamesItalicsSource : IPairSource
{
    private const int FirstNewTestamentBook = 40;

    public string Method => MappingMethods.Stated;

    public string Name => "kjv-italics";

    public bool States(string pair) =>
        KingJamesGreekSource.Witnesses.Concat(KingJamesGreekSource.Stated).Any(witness => pair == $"{Bible4uTextSource.KingJames}-{witness}");

    public bool Makes(string source) => source == Sources.KingJamesSuppliedCredit;

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
                    LinkRelation.Expands.ToString().ToLowerInvariant(), Method, null, Sources.KingJamesSuppliedCredit, null, null, null)));
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
/// marked: the loader withdrew those, and the merge's stated absence outranks them. Nor does it give way to a supporting
/// numbering's rows (<see cref="StrongNumberSource.Supporting"/>, the ASV's): the merge's stated absence outranks them too.
/// The renderings are read from the pair's
/// other method files and from the rows the other stated sources make. EVIDENTIA's file is not read: it is made from the
/// stated file (the build would have a cycle), so a marked word only an EVIDENTIA verdict renders is stated absent here.
/// </para>
///
/// <para>
/// **A run is absent only from a witness that agrees with the edition's base there** (PRB-1736). An edition marks what
/// it added to the text it translated, so against that text (<see cref="SuppliedWordAbsences.Edition"/>'s base) every run
/// stands. Against another witness a run of a verse is stated only when every word the witness has in the verse is one the
/// base has there too (<see cref="BaseAgreement"/>): a word with no counterpart in the base cannot be in a witness all of
/// whose words have one. A verse where the witness has a word the base lacks, or that the base does not hold, states nothing
/// against it: the witness's variant may print the word (Vaticanus's κατεβη πρωτον in Ephesians 4:9).
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
        MappingMethods.Strong, MappingMethods.Aligner, MappingMethods.Composed, MappingMethods.Names,
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

    /// <summary>
    /// Every catalogued edition with every catalogued witness in the language of some book both hold, whether or not the
    /// pair has a folder yet: the marks are the edition's own, so the pair is stated as soon as both texts are there.
    /// </summary>
    public IEnumerable<string> Founds(ProductTexts texts)
    {
        var store = texts.Store;
        var witnesses = Witnesses.Order(StringComparer.Ordinal)
            .Where(slug => texts.Holds(slug) && store.Exists(TextBook.ProductOf(slug)))
            .Select(slug => (Slug: slug, Declared: IndexContext.Declared(store, slug)))
            .Where(w => w.Declared.Kind != TextKind.Translation && w.Declared.Language is Hebrew or Greek)
            .Select(w => (w.Slug, w.Declared.Language, Books: Books(store, w.Slug)))
            .ToList();
        foreach (var edition in SuppliedWordAbsences.Editions.Where(e => texts.Holds(e.Slug) && store.Exists(TextBook.ProductOf(e.Slug))))
        {
            var books = Books(store, edition.Slug);
            foreach (var witness in witnesses.Where(w => books.Any(book => LanguageOf(edition, book) == w.Language && w.Books.Contains(book))))
            {
                yield return MappingProducts.Pair(edition.Slug, witness.Slug);
            }
        }
    }

    private static HashSet<int> Books(ProductStore store, string slug)
    {
        using var reader = store.Open(TextBook.ProductOf(slug), TextBook.Schema);
        return [.. reader.Rows().Select(book => book.Book)];
    }

    /// <summary>The language an edition's book was translated from, which the witness it is stated against must print.</summary>
    private static string? LanguageOf(SuppliedWordAbsences.Edition edition, int book) =>
        book <= LastOldTestamentBook ? edition.OldTestament
        : book <= LastNewTestamentBook ? Greek
        : edition.OldTestament == Greek ? Greek : null;

    /// <summary>The corpus's text of what the edition translated a book from; null where it translated none in that language.</summary>
    private static string? BaseOf(SuppliedWordAbsences.Edition edition, int book) =>
        LanguageOf(edition, book) is null ? null
        : book is > LastOldTestamentBook and <= LastNewTestamentBook ? edition.NewTestamentBase : edition.OldTestamentBase;

    /// <summary>The pair's other method files, and the words of every base text the witness's verses are held against.</summary>
    public IEnumerable<string> Inputs(ProductStore store, string pair) =>
        Renderings.Select(method => store.Find(MappingProducts.Of(pair, method)))
            .Concat(Bases(pair).SelectMany(ProductText.Products).Select(store.Find))
            .OfType<string>();

    /// <summary>The edition's base texts other than the pair's witness.</summary>
    private static IEnumerable<string> Bases(string pair)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var edition = SuppliedWordAbsences.Editions.Single(e => e.Slug == fromText);
        return new[] { edition.OldTestamentBase, edition.NewTestamentBase }.OfType<string>().Where(slug => slug != toText).Distinct();
    }

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
        var refused = 0;
        foreach (var verse in marked.Verses)
        {
            var language = LanguageOf(edition, verse.Book);
            var address = marked.Primary(verse);
            if (language != declared.Language || !witness.ByAddress.TryGetValue(address, out var witnessed))
            {
                continue;
            }

            if (!Agrees(texts, BaseOf(edition, verse.Book), toText, witness, witnessed, address, language))
            {
                refused += marked.InVerse(verse).Select(id => marked.Words[id].Supplied).OfType<int>().Distinct().Count();
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
            + $"{withdrawn} aligner links over marked words alone give way; {refused} runs not stated, the witness's verse "
            + "having a word the edition's base lacks there");
        return rows;
    }

    /// <summary>
    /// Whether a witness's verse can have no word the edition added: the witness is the base, or every word it has at the
    /// address the base has there too.
    /// </summary>
    private static bool Agrees(ProductTexts texts, string? reference, string toText, ProductText witness, IReadOnlyList<int> witnessed,
        (int Book, int Chapter, int Verse) address, string? language)
    {
        if (reference == toText)
        {
            return true;
        }

        if (reference is null || !texts.Holds(reference))
        {
            return false;
        }

        var based = texts[reference];
        return based.ByAddress.TryGetValue(address, out var words) && BaseAgreement.Agrees(witness, witnessed, based, words, language);
    }

    /// <summary>
    /// The marked words a link of the pair renders that is not the aligner's alone over marked words only; and how many of
    /// the aligner's links over marked words only there are, which give way to the absence.
    /// </summary>
    private IReadOnlySet<string> Rendered(ProductTexts texts, string pair, IReadOnlySet<string> absent, out int withdrawn)
    {
        var rows = PairStanding.Rows(texts.Store, pair, Renderings)
            .Where(row => row.Relation is "renders" or "equals" && !StrongNumberSource.Supporting(row.Source))
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

/// <summary>
/// Whether a witness's words at a frame address are all words the edition's base has there (PRB-1736): a multiset inclusion,
/// so the witness has no word, and no more copies of a word, than the base. Words are compared by Strong number where every
/// word of both verses carries one (a lexeme, whatever its spelling or form), otherwise by their unaccented form with the
/// spellings of one word made alike: in Greek itacism, a movable nu, doubled consonants and the scribes' contractions of
/// the sacred names (θυ for θεου); in Hebrew the vowel letters vav and yod inside a word. A word of one letter (the
/// conjunction vav, a preposition) is compared as it is.
/// </summary>
internal static class BaseAgreement
{
    private const string Greek = "grc";

    private const string Consonants = "βδζθκλμνπρστφχψ";

    /// <summary>The stems the manuscripts contract (nomina sacra), as the base spells them.</summary>
    private static readonly string[] SacredStems =
    [
        "θε", "κυρι", "ιησ", "χριστ", "πνευμ", "ανθρωπ", "ουραν", "δαυ", "δαβ", "ισραηλ", "ιερουσαλημ", "πατ", "μητ", "υι", "σωτ",
        "σταυρ",
    ];

    public static bool Agrees(ProductText witness, IReadOnlyList<int> witnessed, ProductText based, IReadOnlyList<int> words, string? language)
    {
        if (witnessed.All(id => witness.Words[id].Strong is { Length: > 0 }) && words.All(id => based.Words[id].Strong is { Length: > 0 }))
        {
            return Included(witnessed.Select(id => witness.Words[id].Strong!), words.Select(id => based.Words[id].Strong!), out _);
        }

        var greek = language == Greek;
        var forms = words.Select(id => Plain(based.Words[id], language)).ToList();
        if (Included(witnessed.Select(id => Alike(Plain(witness.Words[id], language), greek)), forms.Select(form => Alike(form, greek)), out _))
        {
            return true;
        }

        if (!greek)
        {
            return false;
        }

        // What the forms leave: each must be a contraction of a sacred name among the base's words not yet matched.
        var pool = forms.Select(form => Alike(form, greek)).CountBy(form => form, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var unmatched = new List<string>();
        foreach (var form in witnessed.Select(id => Plain(witness.Words[id], language)))
        {
            var alike = Alike(form, greek);
            if (pool.TryGetValue(alike, out var count) && count > 0)
            {
                pool[alike] = count - 1;
            }
            else
            {
                unmatched.Add(form);
            }
        }

        foreach (var form in unmatched)
        {
            var whole = forms.FirstOrDefault(full => pool[Alike(full, greek)] > 0 && Contracts(form, full));
            if (whole is null)
            {
                return false;
            }

            pool[Alike(whole, greek)]--;
        }

        return true;
    }

    /// <summary>Whether every item of the first is one of the second, as many times as it occurs; and which of the first were left.</summary>
    private static bool Included(IEnumerable<string> part, IEnumerable<string> whole, out int left)
    {
        var pool = whole.CountBy(item => item, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        left = 0;
        foreach (var item in part)
        {
            if (pool.TryGetValue(item, out var count) && count > 0)
            {
                pool[item] = count - 1;
            }
            else
            {
                left++;
            }
        }

        return left == 0;
    }

    /// <summary>The word's unaccented lowercase form, with a final sigma written as any other.</summary>
    private static string Plain(TextWord word, string? language) => (word.Normalised ?? WordFolding.Fold(word.Surface, language)).Replace('ς', 'σ');

    /// <summary>
    /// One spelling for the spellings of a word. Greek: ει, οι and η are ι, αι is ε, ω is ο, υ outside the diphthongs αυ,
    /// ευ, ου is ι, a final movable ν goes, a doubled consonant is one. Hebrew: the final letters are the others, and vav and
    /// yod go from a word of more than one letter.
    /// </summary>
    public static string Alike(string form, bool greek)
    {
        if (!greek)
        {
            var hebrew = form.Replace('ך', 'כ').Replace('ם', 'מ').Replace('ן', 'נ').Replace('ף', 'פ').Replace('ץ', 'צ');
            var bare = hebrew.Length > 1 ? hebrew.Replace("ו", string.Empty).Replace("י", string.Empty) : hebrew;
            return bare.Length > 0 ? bare : hebrew;
        }

        var s = form.Length > 2 && form.EndsWith('ν') ? form[..^1] : form;
        s = s.Replace("ει", "ι").Replace("οι", "ι").Replace('η', 'ι').Replace("αι", "ε").Replace('ω', 'ο');
        var alike = new System.Text.StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i] == 'υ' && (i == 0 || s[i - 1] is not ('α' or 'ε' or 'ο')) ? 'ι' : s[i];
            if (alike.Length == 0 || alike[^1] != c || !Consonants.Contains(c))
            {
                alike.Append(c);
            }
        }

        return alike.ToString();
    }

    /// <summary>
    /// Whether a short word is a scribe's contraction of a sacred name the base writes out: its first and last letters, and
    /// the letters between in order.
    /// </summary>
    public static bool Contracts(string contraction, string full) =>
        contraction.Length is >= 2 and <= 5 && full.Length > contraction.Length && contraction[0] == full[0] && contraction[^1] == full[^1]
        && SacredStems.Any(stem => full.StartsWith(stem, StringComparison.Ordinal)) && InOrder(contraction, full);

    private static bool InOrder(string part, string whole)
    {
        var at = 0;
        foreach (var c in part)
        {
            at = whole.IndexOf(c, at) + 1;
            if (at == 0)
            {
                return false;
            }
        }

        return true;
    }
}
