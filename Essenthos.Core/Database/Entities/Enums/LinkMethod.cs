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
}
