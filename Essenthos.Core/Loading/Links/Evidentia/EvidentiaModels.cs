namespace Essenthos.Core.Loading.Links.Evidentia;

internal static class EvidentiaDefaults
{
    public const int NeighbourVerseDistance = 2;
    public const double MinimumContentCoverage = 0.30;
    public const double ExactAddressScore = 0.30;
    public const double NeighbourAddressScore = 0.08;
    public const double MatchingNormalisedFormScore = 0.55;

    /// <summary>What a learned rendering is worth before its share is counted.</summary>
    public const double KnownRenderingBaseScore = 0.20;

    /// <summary>What the whole of its share adds on top.</summary>
    public const double KnownRenderingShareScore = 0.45;

    /// <summary>
    /// The most an inferred rendering may be worth. It is one rung below a number printed on both
    /// sides, by the deduction <see cref="StrongNumberMatch.ResolvedNumber"/> already names and for
    /// the reason it names: one more inference between the link and the two texts that state it.
    /// A rule this corpus concluded must never reach the confidence of a number somebody printed,
    /// or the standing order that puts source before inference survives only in the method name.
    /// </summary>
    public const double InferredRenderingCeiling =
        StrongNumberMatch.Unambiguous - StrongNumberMatch.ResolvedNumber;

    /// <summary>How often a source form must have been seen before the index speaks about it.</summary>
    public const int MinimumRenderingObservations = 2;

    public const double SafeRenderingScore = 0.40;
    public const double SafeRenderingLead = 0.10;
    public const int SafeRenderingObservations = 4;
    public const double SafeRenderingSenseRatio = 2;

    public const double ReviewRenderingScore = 0.30;
    public const double ReviewRenderingLead = 0.05;

    /// <summary>A word named in a reader-language Strong definition, which is a secondary reading.</summary>
    public const double DictionarySenseScore = 0.34;

    /// <summary>A witness's own English gloss, which is a dictionary hint and not a rendering.</summary>
    public const double TargetGlossScore = 0.20;

    public const double DictionaryReviewConfidence = 0.40;
    public const double TargetGlossReviewConfidence = 0.45;

    /// <summary>What a word edge must score before a phrase may be built out of it.</summary>
    public const double PhraseWordScore = 0.65;

    /// <summary>What a preview calls a supported candidate when it counts them for a reader.</summary>
    public const double SupportedCandidateScore = 0.55;

    /// <summary>
    /// Mechanical rather than calibrated: how much a lexical score outweighs a position score in
    /// the assignment, and what an inadmissible pairing costs so the solver never chooses one.
    /// </summary>
    public const double AssignmentEvidenceWeight = 1_000;

    public const double InvalidAssignmentCost = 1_000_000;
}

[Flags]
internal enum LanguagePackCapability
{
    None = 0,
    Normalisation = 1,
    Lemma = 2,
    PartOfSpeech = 4,
    Morphology = 8,
    Syntax = 16,
    FunctionWords = 32,
    NamedEntities = 64,
}

internal readonly record struct EvidentiaAddress(int Book, int Chapter, int Verse)
{
    public int DistanceTo(EvidentiaAddress other) =>
        Book == other.Book && Chapter == other.Chapter ? Math.Abs(Verse - other.Verse) : int.MaxValue;
}

internal sealed record EvidentiaToken(
    long Id,
    EvidentiaAddress Address,
    int Position,
    string Surface,
    string Language,
    string Trailer = "",
    string? Lemma = null,
    string? StrongNumber = null,
    string? Gloss = null,
    string? PartOfSpeech = null,
    IReadOnlyDictionary<string, string>? Morphology = null);

/// <summary>
/// What a language pack concluded a word is. <see cref="Unknown"/> is the reason this is not a
/// bool: a token no pack claimed is not a content word and not a function word, and a guard
/// written as "not a function word" would otherwise admit it.
/// </summary>
internal enum EvidentiaWordClass
{
    Unknown,
    Content,
    Function,
}

internal sealed record EvidentiaAnalysis(
    EvidentiaToken Token,
    string Normalised,
    string? Lemma,
    string? PartOfSpeech,
    EvidentiaWordClass WordClass,
    LanguagePackCapability Capabilities)
{
    public bool IsFunctionWord => WordClass == EvidentiaWordClass.Function;

    /// <summary>
    /// Deliberately narrower than "not a function word": only a pack that claimed the word said
    /// so. Every guard that admits a word into lexical evidence asks this one.
    /// </summary>
    public bool IsContentWord => WordClass == EvidentiaWordClass.Content;
}

internal enum EvidentiaEvidenceKind
{
    ExactCanonicalAddress,
    NeighbouringCanonicalAddress,
    MatchingNormalisedForm,
    SharedStrongNumber,
    DictionarySense,
    TargetGloss,
    KnownRendering,
    Morphology,
    Syntax,
    StatisticalAligner,
}

/// <summary>
/// Which form of a word an index is filed under, from the most specific to the most general. The
/// order is the order a lookup asks them in, so an exact form is a preference among the keys that
/// could answer rather than a condition on the ones that may.
/// </summary>
internal enum EvidentiaFormKind
{
    Surface,
    Lemma,
    Normalised,
}

/// <summary>
/// How much the corpus actually knows about the reading this evidence proposes, as opposed to how
/// much that reading scored. A score compresses both into one number and a policy needs them
/// apart: a form seen twice and a form seen two hundred times can carry the same share.
/// </summary>
/// <param name="Observations">Every observation of the source form, whichever sense it landed on.</param>
/// <param name="Share">The fraction of those that landed on this one.</param>
/// <param name="NextShare">
/// The fraction the strongest competing sense holds across the whole index - not across the verse
/// being read, where a competitor is usually simply absent and so reads as no competition at all.
/// </param>
/// <param name="MatchedForm">
/// Which key of a multi-key index answered. A reader cannot otherwise tell a reading the corpus
/// has seen written exactly this way from one it has only seen a related form of, and those are
/// two different claims.
/// </param>
internal sealed record EvidentiaEvidenceSupport(
    int Observations,
    double Share,
    double NextShare,
    EvidentiaFormKind? MatchedForm = null);

internal sealed record EvidentiaEvidence(
    EvidentiaEvidenceKind Kind,
    double Score,
    string Source,
    EvidentiaEvidenceSupport? Support = null);

internal sealed record EvidentiaCandidate(
    EvidentiaAnalysis Source,
    EvidentiaAnalysis Target,
    IReadOnlyList<EvidentiaEvidence> Evidence)
{
    public double Score => Math.Clamp(Evidence.Sum(evidence => evidence.Score), 0, 1);

    /// <summary>
    /// A word the witness marks as an article, a preposition or a conjunction, matched by a source
    /// word that neither its pack nor its own analysis calls one. English writes a real word for a
    /// preposition, so <em>upon</em> against a Hebrew preposition is an ordinary correspondence;
    /// a content word against a Hebrew article is a gloss read as a claim.
    /// </summary>
    public bool PairsAContentWordWithAFunctionWord =>
        Target.IsFunctionWord
        && !Source.IsFunctionWord
        && EvidentiaMorphologyLabels.IsFunctionWord(Source.PartOfSpeech, Source.Token.Language) != true;
}

internal sealed record EvidentiaPhraseCandidate(
    IReadOnlyList<EvidentiaToken> Source,
    IReadOnlyList<EvidentiaToken> Target,
    double Score,
    string Reason);

internal enum EvidentiaPreviewStatus
{
    ReadyForRules,
    InsufficientEvidence,
    UnsupportedLanguage,
}

internal enum EvidentiaTodo
{
    LanguagePack,
    DictionaryEvidence,

    /// <summary>The decision trace is built and is in memory only; nothing stores it.</summary>
    StructuredExplanationStorage,
    StatisticalFallback,
}

internal sealed record EvidentiaRequest(
    IReadOnlyList<EvidentiaToken> Source,
    IReadOnlyList<EvidentiaToken> Target,
    int NeighbourVerseDistance = EvidentiaDefaults.NeighbourVerseDistance,
    double MinimumContentCoverage = EvidentiaDefaults.MinimumContentCoverage,
    bool AllowSourceStrongEvidence = true);

internal sealed record EvidentiaPreview(
    EvidentiaPreviewStatus Status,
    IReadOnlyList<EvidentiaCandidate> Candidates,
    int ContentSourceWords,
    int CoveredContentSourceWords,
    double ContentCoverage,
    bool NeedsStatisticalFallback,
    IReadOnlyList<EvidentiaTodo> Todos,
    IReadOnlyList<EvidentiaPhraseCandidate> Phrases);
