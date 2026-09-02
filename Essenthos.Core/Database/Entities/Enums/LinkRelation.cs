namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// What the correspondence asserts. <see cref="Omits"/> and <see cref="Expands"/> are stored
/// positively — the side that lacks the words has none in the link, and the row itself is the
/// statement — which is what turns an absence from silence into an explanation.
///
/// The two are one absence read from either end, and only the relation says which end: an
/// <see cref="Omits"/> link names words on the <c>to</c> side alone, because the <c>from</c> text
/// is the one that lacks them, and an <see cref="Expands"/> link names words on the <c>from</c>
/// side alone. A link with one empty side cannot say this for itself, so a loader that writes
/// <see cref="Omits"/> in both directions has thrown the direction away.
/// </summary>
public enum LinkRelation
{
    Renders,
    Equals,
    Expands,
    Omits,
    Transposes,
}
