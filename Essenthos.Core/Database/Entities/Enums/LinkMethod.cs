namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// What established the correspondence. Only <see cref="StatedBySource"/> and <see cref="Manual"/>
/// are assertions somebody made; the rest are inferences, and each of those carries a confidence so
/// that a heuristic can never be read as scholarship.
/// </summary>
public enum LinkMethod
{
    StatedBySource,
    StrongNumber,
    Lexical,
    Aligner,
    Manual,

    /// <summary>
    /// A language model read the verse and said whom the word names.
    ///
    /// It is a method rather than a source because that is what it is: the answer is not carried
    /// from anywhere, it was worked out from the text, and it is exactly the kind of claim that
    /// must never be storable without a confidence. The value is last so that nothing already
    /// written moves; where it stands against the others is the claim standing's business and not
    /// this list's.
    /// </summary>
    ModelReading,

    /// <summary>
    /// EVIDENTIA's deterministic rules proposed the pair and a person approved it into the corpus
    /// without reading it — a tier accepted as a whole. Every such claim names the run and the rule
    /// version in its source, and the run keeps the signals that produced its score, so the claim
    /// can be read back as the reasoning it was rather than as a number. A proposal a person read
    /// and approved becomes <see cref="Manual"/> as well; this claim stays beside it.
    /// </summary>
    RuleBased,
}
