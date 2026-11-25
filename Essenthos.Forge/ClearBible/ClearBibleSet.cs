using System.Collections.Concurrent;
using System.Globalization;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.ClearBible;

/// <summary>How a record's word identifiers become the corpus's own word ids.</summary>
internal enum ClearBibleJoin
{
    /// <summary>
    /// The letters, aligned inside the verse each text numbers for itself.
    ///
    /// Counting positions cannot be used where the two sides are different editions or different
    /// tokenisations. The second is true of every target file, whose tokeniser numbers punctuation
    /// like a word, and both are true of the Reina-Valera set: its Hebrew is the Westminster
    /// morphology, which divides the Leningrad Codex into 469,476 morphemes where BHSA divides it
    /// into 426,590 words, and its Greek is the SBLGNT, which this corpus does not hold at all. Its
    /// Hebrew is also numbered the Hebrew way, so the verse to look in is the one each text calls
    /// by that number rather than the one the canonical frame places it at.
    /// </summary>
    Letters,

    /// <summary>
    /// The words, laid against each other edition against edition inside the verse.
    ///
    /// For a source edition in the same language as the corpus's and close to it, where the letters
    /// agree almost everywhere and the words that differ are the textual variants: the Berean Greek
    /// New Testament against Nestle 1904. Counting positions cannot be used there, because a word
    /// one edition has and the other has not puts every word after it one place out — BGNT's
    /// <em>καὶ κηρύσσων</em> in Mark 1:4 against Nestle's bare <em>κηρύσσων</em> — and the letters
    /// join cannot either, because it takes a single different word in the same place for a
    /// spelling. So each verse is aligned with <see cref="Loading.Links.WitnessAlignment"/>, the
    /// alignment the corpus joins its own witnesses with, and a word is placed only on the word it
    /// is.
    /// </summary>
    Edition,
}

/// <summary>Where the numbers a record gives the translation's words come from.</summary>
internal enum ClearBibleNumbering
{
    /// <summary>The token file the release ships beside the alignment.</summary>
    TokenFile,

    /// <summary>
    /// The translation's own words, counted the way the records count them, because the release's
    /// token file does not: every word divided after an elided article or pronoun, a hyphenated word
    /// kept whole and each mark of punctuation counted as a word. See
    /// <see cref="Loading.Links.ClearBibleLinks.Retokenised"/>.
    /// </summary>
    Retokenised,

    /// <summary>
    /// The token file the release ships, which is a later revision of the translation than some of the records were
    /// numbered over. The Berean's: in Matthew 26:51 the records count <em>Jesus’</em> as one word where the file makes
    /// the apostrophe a token of its own, so from there each number names the word before the one meant, «companions» for
    /// «drew»; in Romans 16:12 they count the brackets of a <em>[women]</em> the file no longer prints; in Mark 8:10 every
    /// number is one early and none falls on a mark. A verse is refused whole where a record names a mark or a number past
    /// it, or where two of its records name English that shares no word with the glosses of their Greek words (see
    /// <see cref="Loading.Links.ClearBibleLinks.Unglossed"/>).
    /// </summary>
    Revised,

    /// <summary>
    /// The token file the release ships, with a verse here and there whose records were counted over another division of
    /// the text. A verse is refused whole where a record names a mark or a number past it, as in <see cref="Revised"/>;
    /// the glosses cannot tell the rest, because they are English and the translation is not.
    /// </summary>
    Redivided,
}

/// <param name="From">The translation the alignment is about, as the corpus slugs it.</param>
/// <param name="To">The witness it aligns that translation to.</param>
/// <param name="Alignment">The records, relative to the download.</param>
/// <param name="Target">
/// One row per token of the translation, in the tokenisation the alignment's target ids number.
/// </param>
/// <param name="Source">
/// One row per word of the source edition, in the tokenisation the alignment's source ids number.
/// </param>
/// <param name="Join">How the source edition's words become the witness's. The target's always join on the letters.</param>
/// <param name="Statement">
/// What this file is, in the words <c>link.source</c> answers a reader with. It names the team and
/// the licence rather than the path: a reader asking where a claim came from is not asking where
/// the bytes sit on this machine.
/// </param>
/// <param name="Shift">
/// The translation's word for <em>and</em>, where the set puts source words on it that the word after
/// it renders; null where it does not (columns <c>and</c> and <c>also</c>). See <see cref="Loading.Links.ClearBibleLinks.Shifted"/>.
/// </param>
/// <param name="Numbering">Where the numbers the records give the translation's words come from.</param>
/// <param name="Early">
/// The translation's prepositions and articles, where the set puts a name on the one before it and leaves the name
/// unnamed; null where it does not. See <see cref="Loading.Links.ClearBibleLinks.Early"/>.
/// </param>
/// <param name="Edited">
/// Whether the records are numbered over the source edition as the team edited it verse by verse, words added and
/// taken out, rather than over its token file. See <see cref="ClearBibleSourceEdits"/>.
/// </param>
/// <param name="Possessive">
/// The translation's possessive determiners, where the set puts one on another word of the verse than the one whose
/// suffix or genitive it renders; null where it does not. See <see cref="Loading.Links.ClearBibleLinks.Stray"/>.
/// </param>
/// <param name="Function">
/// The translation's function words, where the set's records are numbered off as a block in some verses, a noun or verb
/// named on such a word; null where they are not. See <see cref="Loading.Links.ClearBibleLinks.Scrambled"/>.
/// </param>
/// <summary>
/// One of Clear Bible's alignments, and everything about it that differs from the others, as <c>sources/clear-bible.tsv</c>
/// declares it (the reasons for each set are in that file).
///
/// They are one format and one repository and they are not one dataset: the Berean's is a second
/// opinion on links the corpus already holds, the Reina-Valera's is the only statement anybody has
/// ever made about which Spanish word renders which Hebrew or Greek one. What separates them
/// mechanically is which editions they are keyed to, which is why the join is part of the set
/// rather than of the loader.
/// </summary>
internal sealed record ClearBibleSet(
    string From,
    string To,
    string Alignment,
    string Target,
    string Source,
    ClearBibleJoin Join,
    string Statement,
    ClearBibleShift? Shift = null,
    ClearBibleNumbering Numbering = ClearBibleNumbering.TokenFile,
    IReadOnlySet<string>? Early = null,
    bool Edited = false,
    IReadOnlySet<string>? Possessive = null,
    IReadOnlySet<string>? Function = null)
{
    public const string Product = "sources/clear-bible";

    /// <summary>The file that declares the sets, under Resources (<c>sources/clear-bible.tsv</c>); each pair's stated rows name it among their inputs.</summary>
    public const string File = "Essenthos/sources/clear-bible.tsv";

    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<ClearBibleSet>>> Loaded = new(StringComparer.Ordinal);

    /// <summary>The pair the set states, <c>FROM-TO</c>.</summary>
    public string Pair => $"{From}-{To}";

    /// <summary>Every set the corpus loads, in the order the store's file lists them, read once a store.</summary>
    public static IReadOnlyList<ClearBibleSet> All(ProductStore store) =>
        Loaded.GetOrAdd(Path.GetFullPath(store.ResourcesPath), _ => new Lazy<IReadOnlyList<ClearBibleSet>>(() => Read(store))).Value;

    private static List<ClearBibleSet> Read(ProductStore store)
    {
        if (!store.Exists(Product))
        {
            throw new FileNotFoundException($"Clear Bible's sets are not declared at {store.FullPath(File)}.", store.FullPath(File));
        }

        var words = SourceWords.Of(store);
        IReadOnlySet<string>? Words(string? list) => list is null ? null : words.Set(SourceWords.ClearBible, list, StringComparer.Ordinal);

        using var reader = store.Open(Product, ClearBibleSetRow.Schema);
        var sets = new List<ClearBibleSet>();
        foreach (var row in reader.Rows())
        {
            if ((row.And is null) != (row.Also is null))
            {
                throw new InvalidDataException($"{File}: {row.From}-{row.To} names {(row.And is null ? "also" : "and")} without the other; a shift needs both.");
            }

            var shift = row.And is null
                ? null
                : new ClearBibleShift(Words(row.And)!, Words(row.Also)!.Select(number => int.Parse(number.AsSpan(1), CultureInfo.InvariantCulture)).ToHashSet());
            var set = new ClearBibleSet(
                row.From, row.To, row.Alignment, row.Target ?? string.Empty, row.Source, Parse<ClearBibleJoin>(row.Join, row), row.Statement, shift,
                Parse<ClearBibleNumbering>(row.Numbering, row), Words(row.Early), row.Edited, Words(row.Possessive),
                Words(row.Function));
            if (sets.Any(other => other.Pair == set.Pair))
            {
                throw new InvalidDataException($"{File}: {set.Pair} is declared twice.");
            }

            sets.Add(set);
        }

        return sets;
    }

    private static T Parse<T>(string text, ClearBibleSetRow row) where T : struct, Enum =>
        Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value) && !char.IsDigit(text[0])
            ? value
            : throw new InvalidDataException(
                $"{File}: {row.From}-{row.To} says \"{text}\", which is no {typeof(T).Name}: {string.Join(", ", Enum.GetNames<T>().Select(name => name.ToLowerInvariant()))}.");
}

/// <summary>One row of <c>sources/clear-bible.tsv</c> (<see cref="ClearBibleSet"/>), as the file writes it.</summary>
internal sealed record ClearBibleSetRow(
    string From,
    string To,
    string Alignment,
    string? Target,
    string Source,
    string Join,
    string Numbering,
    bool Edited,
    string? And,
    string? Also,
    string? Early,
    string? Possessive,
    string? Function,
    string Statement,
    string Reason)
{
    public static readonly ProductSchema<ClearBibleSetRow> Schema = new ProductSchema<ClearBibleSetRow>()
        .Text("from", r => r.From)
        .Text("to", r => r.To)
        .Text("alignment", r => r.Alignment)
        .OptionalText("target", r => r.Target)
        .Text("source", r => r.Source)
        .Text("join", r => r.Join)
        .Text("numbering", r => r.Numbering)
        .Boolean("edited", r => r.Edited)
        .OptionalText("and", r => r.And)
        .OptionalText("also", r => r.Also)
        .OptionalText("early", r => r.Early)
        .OptionalText("possessive", r => r.Possessive)
        .OptionalText("function", r => r.Function)
        .Text("statement", r => r.Statement)
        .Text("reason", r => r.Reason)
        .Reads(row => new ClearBibleSetRow(row.Text(0), row.Text(1), row.Text(2), row.TextOrNull(3), row.Text(4), row.Text(5), row.Text(6),
            row.Boolean(7), row.TextOrNull(8), row.TextOrNull(9), row.TextOrNull(10), row.TextOrNull(11), row.TextOrNull(12), row.Text(13), row.Text(14)));
}

/// <param name="And">The translation's word for <em>and</em>, in lower case.</param>
/// <param name="Also">
/// The Strong numbers of the source words that are not tagged conjunctions and can still be rendered
/// by it, like גַּם and καί where it means <em>also</em>.
/// </param>
internal sealed record ClearBibleShift(IReadOnlySet<string> And, IReadOnlySet<int> Also);
