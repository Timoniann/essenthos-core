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
}
