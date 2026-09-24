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
    ClearBibleShift? Shift = null)
{
    private const string Repository = "github.com/Clear-Bible/Alignments";

    /// <summary>Every set the corpus loads, in the order it loads them.</summary>
    public static IReadOnlyList<ClearBibleSet> All() =>
    [
        Berean(BereanTextSource.Slug, NestleTextSource.Slug),
        ReinaValeraOldTestament(EbibleTextSource.ReinaValera, BhsaTextSource.Slug),
        ReinaValeraNewTestament(EbibleTextSource.ReinaValera, NestleTextSource.Slug),
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

    private static class Spanish
    {
        public static readonly HashSet<string> And = new(["y", "e"], StringComparer.Ordinal);
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
