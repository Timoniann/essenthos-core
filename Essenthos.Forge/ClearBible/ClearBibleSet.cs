using Essenthos.Core.Loading;

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
    /// <see cref="Loading.Links.ClearBibleLinkLoader.Retokenised"/>.
    /// </summary>
    Retokenised,
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
/// it renders; null where it does not. See <see cref="Loading.Links.ClearBibleLinkLoader.Shifted"/>.
/// </param>
/// <param name="Numbering">Where the numbers the records give the translation's words come from.</param>
/// <summary>
/// One of Clear Bible's alignments, and everything about it that differs from the others.
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
    ClearBibleNumbering Numbering = ClearBibleNumbering.TokenFile)
{
    private const string Repository = "github.com/Clear-Bible/Alignments";

    /// <summary>Every set the corpus loads, in the order it loads them.</summary>
    public static IReadOnlyList<ClearBibleSet> All() =>
    [
        Berean(BereanTextSource.Slug, NestleTextSource.Slug),
        ReinaValeraOldTestament(EbibleTextSource.ReinaValera, BhsaTextSource.Slug),
        ReinaValeraNewTestament(EbibleTextSource.ReinaValera, NestleTextSource.Slug),
        SegondOldTestament(EbibleTextSource.Segond, BhsaTextSource.Slug),
        SegondNewTestament(EbibleTextSource.Segond, NestleTextSource.Slug),
        VanDyckOldTestament(EbibleTextSource.VanDyck, BhsaTextSource.Slug),
        VanDyckNewTestament(EbibleTextSource.VanDyck, NestleTextSource.Slug),
        IrvHindiOldTestament(EbibleTextSource.IrvHindi, BhsaTextSource.Slug),
        IrvHindiNewTestament(EbibleTextSource.IrvHindi, NestleTextSource.Slug),
        AlmeidaNewTestament(AlmeidaTextSource.Slug, NestleTextSource.Slug),
    ];

    /// <summary>
    /// Clear Bible's hand-made alignment of the Berean Standard Bible, whose own publisher already
    /// states the same correspondences. It writes few links and many claims.
    /// </summary>
    public static ClearBibleSet Berean(string bereanSlug, string greekSlug) => new(
        bereanSlug,
        greekSlug,
        Path.Combine("data", "eng", "alignments", "BSB", "BGNT-BSB-manual.json"),
        Path.Combine("data", "eng", "targets", "BSB", "nt_BSB.tsv"),
        Path.Combine("data", "sources", "BGNT.tsv"),
        ClearBibleJoin.Edition,
        $"Clear Bible Alignments, BiblioNexus, {Repository}, CC BY 4.0");

    /// <summary>
    /// The Reina-Valera 1909 against the Hebrew, hand-made, whole Old Testament.
    ///
    /// Its metadata names the target as <c>identifier = "RV09"</c> at
    /// <c>ebible.org/find/details.php?id=spaRV1909</c>, which is the file this corpus loads, and
    /// that identity was checked against the text rather than taken from the metadata: of the
    /// 31,082 verses both hold, 30,441 are word for word the same once accents and punctuation are
    /// folded, and the words that differ are that copy's own typographical slips —
    /// <em>confudió</em>, <em>denlante</em>, <em>nostros</em>. A different Reina-Valera would differ
    /// in thousands of verses rather than in 350 words.
    /// </summary>
    public static ClearBibleSet ReinaValeraOldTestament(string spanishSlug, string hebrewSlug) => new(
        spanishSlug,
        hebrewSlug,
        Path.Combine("data", "spa", "alignments", "RV09", "WLCM-RV09-manual.json"),
        Path.Combine("data", "spa", "targets", "RV09", "ot_RV09.tsv"),
        Path.Combine("data", "sources", "WLCM.tsv"),
        ClearBibleJoin.Letters,
        $"Clear Bible Alignments WLCM-RV09-manual, BiblioNexus, {Repository}, CC BY 4.0, made by hand",
        new ClearBibleShift(Spanish.And, new HashSet<int> { Hebrew.Also, Hebrew.Even }));

    /// <inheritdoc cref="ReinaValeraOldTestament"/>
    public static ClearBibleSet ReinaValeraNewTestament(string spanishSlug, string greekSlug) => new(
        spanishSlug,
        greekSlug,
        Path.Combine("data", "spa", "alignments", "RV09", "SBLGNT-RV09-manual.json"),
        Path.Combine("data", "spa", "targets", "RV09", "nt_RV09.tsv"),
        Path.Combine("data", "sources", "SBLGNT.tsv"),
        ClearBibleJoin.Letters,
        $"Clear Bible Alignments SBLGNT-RV09-manual, BiblioNexus, {Repository}, CC BY 4.0, made by hand",
        new ClearBibleShift(Spanish.And, new HashSet<int> { Greek.And }));

    /// <summary>
    /// The Segond of 1910 against the Hebrew, hand-made by Biblica's team, whole Old Testament.
    ///
    /// The release ships no list of its French words: the Old Testament token file is a header and
    /// nothing else, and the New Testament's does not match its own records — 7,475 of the 104,807
    /// French words they name are a comma or a full stop in it, the way the Russian set's are. The
    /// records were numbered over the text divided at every elision, so that <em>l’un</em> is two
    /// words, with a hyphenated word kept whole and each mark of punctuation counted; the token
    /// file divides some elisions and not others. Counted that way over eBible's text, which has the
    /// same letters in 7,946 of the New Testament's 7,959 verses, 0.9% of the words the Old
    /// Testament's records name are punctuation, and a verse where any is has its records refused
    /// whole, since past that point every number in it is out. Of the records kept, a Hebrew proper
    /// name falls on a capitalised French word 98.7% of the time, where the Reina-Valera's own token
    /// file gives 97.3%.
    ///
    /// The set puts about two hundred Hebrew words on <em>et</em> where the next word renders them,
    /// as the Reina-Valera's does on <em>y</em>; in the New Testament the same shape is 36 records
    /// out of 5,713 on <em>et</em>, and those read as the translation's own choice.
    /// </summary>
    public static ClearBibleSet SegondOldTestament(string frenchSlug, string hebrewSlug) => new(
        frenchSlug,
        hebrewSlug,
        Path.Combine("data", "fra", "alignments", "LSG", "WLCM-LSG-manual.json"),
        string.Empty,
        Path.Combine("data", "sources", "WLCM.tsv"),
        ClearBibleJoin.Letters,
        $"Clear Bible Alignments WLCM-LSG-manual, Biblica, {Repository}, CC BY 4.0, made by hand",
        new ClearBibleShift(French.And, new HashSet<int> { Hebrew.Also, Hebrew.Even }),
        ClearBibleNumbering.Retokenised);

    /// <inheritdoc cref="SegondOldTestament"/>
    public static ClearBibleSet SegondNewTestament(string frenchSlug, string greekSlug) => new(
        frenchSlug,
        greekSlug,
        Path.Combine("data", "fra", "alignments", "LSG", "SBLGNT-LSG-manual.json"),
        string.Empty,
        Path.Combine("data", "sources", "SBLGNT.tsv"),
        ClearBibleJoin.Letters,
        $"Clear Bible Alignments SBLGNT-LSG-manual, Biblica, {Repository}, CC BY 4.0, made by hand",
        Numbering: ClearBibleNumbering.Retokenised);

    /// <summary>
    /// The Van Dyck against the Hebrew, hand-made by BiblioNexus, whole Old Testament.
    ///
    /// Its metadata points at the Digital Bible Library's fully vowelled Van Dyck rather than at
    /// eBible's, and says in a comment that nobody was sure it is the same version. It is: every
    /// word of both token files falls on a word of eBible's text, with a psalm's title — which the
    /// files number as a verse 0 — laid at the head of its first verse, and 408 of the two sets'
    /// 348,102 records name a Hebrew or Greek word this corpus could not place.
    /// </summary>
    public static ClearBibleSet VanDyckOldTestament(string arabicSlug, string hebrewSlug) => new(
        arabicSlug,
        hebrewSlug,
        Path.Combine("data", "arb", "alignments", "AVD", "WLCM-AVD-manual.json"),
        Path.Combine("data", "arb", "targets", "AVD", "ot_AVD.tsv"),
        Path.Combine("data", "sources", "WLCM.tsv"),
        ClearBibleJoin.Letters,
        $"Clear Bible Alignments WLCM-AVD-manual, BiblioNexus, {Repository}, CC BY 4.0, made by hand");

    /// <inheritdoc cref="VanDyckOldTestament"/>
    public static ClearBibleSet VanDyckNewTestament(string arabicSlug, string greekSlug) => new(
        arabicSlug,
        greekSlug,
        Path.Combine("data", "arb", "alignments", "AVD", "SBLGNT-AVD-manual.json"),
        Path.Combine("data", "arb", "targets", "AVD", "nt_AVD.tsv"),
        Path.Combine("data", "sources", "SBLGNT.tsv"),
        ClearBibleJoin.Letters,
        $"Clear Bible Alignments SBLGNT-AVD-manual, BiblioNexus, {Repository}, CC BY 4.0, made by hand");

    /// <summary>
    /// The Indian Revised Version against the Hebrew, hand-made by NLCI, whole Old Testament.
    ///
    /// The set's own terms are CC BY 4.0 and the text's are CC BY-SA 4.0. A link names a Hindi word
    /// and a Hebrew or Greek one and holds no text of either, so it is a statement about the text
    /// and not an adaptation of it, and it carries the set's terms rather than the text's.
    ///
    /// The Old Testament set puts 376 Hebrew words on <em>और</em> where nothing names the word after
    /// it, as the Reina-Valera's does on <em>y</em>; in a sample of twelve, none is rendered by the
    /// <em>और</em> — seven are the object marker אֵת and the rest belong to the word after. The New
    /// Testament's has 13 of that shape and they are left alone.
    /// </summary>
    public static ClearBibleSet IrvHindiOldTestament(string hindiSlug, string hebrewSlug) => new(
        hindiSlug,
        hebrewSlug,
        Path.Combine("data", "hin", "alignments", "IRVHin", "WLCM-IRVHin-manual.json"),
        Path.Combine("data", "hin", "targets", "IRVHin", "ot_IRVHin.tsv"),
        Path.Combine("data", "sources", "WLCM.tsv"),
        ClearBibleJoin.Letters,
        $"Clear Bible Alignments WLCM-IRVHin-manual, NLCI, {Repository}, CC BY 4.0, made by hand",
        new ClearBibleShift(Hindi.And, new HashSet<int> { Hebrew.Also, Hebrew.Even }));

    /// <inheritdoc cref="IrvHindiOldTestament"/>
    public static ClearBibleSet IrvHindiNewTestament(string hindiSlug, string greekSlug) => new(
        hindiSlug,
        greekSlug,
        Path.Combine("data", "hin", "alignments", "IRVHin", "SBLGNT-IRVHin-manual.json"),
        Path.Combine("data", "hin", "targets", "IRVHin", "nt_IRVHin.tsv"),
        Path.Combine("data", "sources", "SBLGNT.tsv"),
        ClearBibleJoin.Letters,
        $"Clear Bible Alignments SBLGNT-IRVHin-manual, NLCI, {Repository}, CC BY 4.0, made by hand");

    /// <summary>
    /// The Almeida of 1911 against the Greek, New Testament only, and not made by hand: BiblioNexus
    /// transferred it from their hand-made alignment of the Spanish Reina-Valera 1909, and the set's own
    /// metadata says so — <c>process = "transfer from Spanish RVR09"</c> — while every one of its
    /// records says <c>origin = "manual"</c>. The metadata is the statement about the set and is the one
    /// repeated on every link, so that nobody reads a transfer as somebody's reading of the Portuguese.
    ///
    /// Its target is Project Gutenberg's eBook 62383, the file this corpus loads, and its token file reads
    /// as that file does word for word, italics removed.
    /// </summary>
    public static ClearBibleSet AlmeidaNewTestament(string portugueseSlug, string greekSlug) => new(
        portugueseSlug,
        greekSlug,
        Path.Combine("data", "por", "alignments", "JFA11", "SBLGNT-JFA11-transfer.json"),
        Path.Combine("data", "por", "targets", "JFA11", "nt_JFA11.tsv"),
        Path.Combine("data", "sources", "SBLGNT.tsv"),
        ClearBibleJoin.Letters,
        $"Clear Bible Alignments SBLGNT-JFA11-transfer, BiblioNexus, {Repository}, CC BY 4.0, "
        + "transferred from their Spanish Reina-Valera 1909 alignment, not made by hand");

    private static class Spanish
    {
        public static readonly HashSet<string> And = new(["y", "e"], StringComparer.Ordinal);
    }

    private static class French
    {
        public static readonly HashSet<string> And = new(["et"], StringComparer.Ordinal);
    }

    private static class Hindi
    {
        public static readonly HashSet<string> And = new(["और", "तथा", "एवं"], StringComparer.Ordinal);
    }

    /// <summary>The Strong numbers of the Hebrew particles a translation can render with <em>and</em>.</summary>
    private static class Hebrew
    {
        /// <summary>גַּם.</summary>
        public const int Also = 1571;

        /// <summary>אַף.</summary>
        public const int Even = 637;
    }

    private static class Greek
    {
        /// <summary>καί, which the SBLGNT tags an adverb where it means <em>also</em>.</summary>
        public const int And = 2532;
    }
}

/// <param name="And">The translation's word for <em>and</em>, in lower case.</param>
/// <param name="Also">
/// The Strong numbers of the source words that are not tagged conjunctions and can still be rendered
/// by it, like גַּם and καί where it means <em>also</em>.
/// </param>
internal sealed record ClearBibleShift(IReadOnlySet<string> And, IReadOnlySet<int> Also);
