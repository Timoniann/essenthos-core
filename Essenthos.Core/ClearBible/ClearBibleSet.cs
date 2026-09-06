namespace Essenthos.Core.ClearBible;

/// <summary>How a record's word identifiers become the corpus's own word ids.</summary>
internal enum ClearBibleJoin
{
    /// <summary>
    /// The nth word of the verse, counting the tokens the file does not exclude. It holds where the
    /// file's text and the corpus's are the same edition tokenised the same way — the Berean's own
    /// Greek against Nestle 1904, and the Berean's English against itself.
    /// </summary>
    Position,

    /// <summary>
    /// The letters, aligned inside the verse each text numbers for itself.
    ///
    /// Position cannot be used where the two sides are different editions or different
    /// tokenisations, and both are true of the Reina-Valera set: its Hebrew is the Westminster
    /// morphology, which divides the Leningrad Codex into 469,476 morphemes where BHSA divides it
    /// into 426,590 words, and its Greek is the SBLGNT, which this corpus does not hold at all. Its
    /// Hebrew is also numbered the Hebrew way, so the verse to look in is the one each text calls
    /// by that number rather than the one the canonical frame places it at.
    /// </summary>
    Letters,
}

/// <param name="From">The translation the alignment is about, as the corpus slugs it.</param>
/// <param name="To">The witness it aligns that translation to.</param>
/// <param name="Alignment">The records, relative to the download.</param>
/// <param name="Target">
/// One row per token of the translation, in the tokenisation the alignment's target ids number.
/// </param>
/// <param name="Source">
/// One row per word of the source edition, needed wherever that edition is not one the corpus
/// holds. Null where the ids resolve by position and nothing has to be read to place them.
/// </param>
/// <param name="Statement">
/// What this file is, in the words <c>link.source</c> answers a reader with. It names the team and
/// the licence rather than the path: a reader asking where a claim came from is not asking where
/// the bytes sit on this machine.
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
    string? Source,
    ClearBibleJoin Join,
    string Statement)
{
    private const string Repository = "github.com/Clear-Bible/Alignments";

    /// <summary>
    /// Clear Bible's hand-made alignment of the Berean Standard Bible, whose own publisher already
    /// states the same correspondences. It writes few links and many claims.
    /// </summary>
    public static ClearBibleSet Berean(string bereanSlug, string greekSlug) => new(
        bereanSlug,
        greekSlug,
        Path.Combine("data", "eng", "alignments", "BSB", "BGNT-BSB-manual.json"),
        Path.Combine("data", "eng", "targets", "BSB", "nt_BSB.tsv"),
        null,
        ClearBibleJoin.Position,
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
        $"Clear Bible Alignments WLCM-RV09-manual, BiblioNexus, {Repository}, CC BY 4.0, made by hand");

    /// <inheritdoc cref="ReinaValeraOldTestament"/>
    public static ClearBibleSet ReinaValeraNewTestament(string spanishSlug, string greekSlug) => new(
        spanishSlug,
        greekSlug,
        Path.Combine("data", "spa", "alignments", "RV09", "SBLGNT-RV09-manual.json"),
        Path.Combine("data", "spa", "targets", "RV09", "nt_RV09.tsv"),
        Path.Combine("data", "sources", "SBLGNT.tsv"),
        ClearBibleJoin.Letters,
        $"Clear Bible Alignments SBLGNT-RV09-manual, BiblioNexus, {Repository}, CC BY 4.0, made by hand");
}
