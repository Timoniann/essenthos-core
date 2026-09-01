namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// What the correspondence asserts. <see cref="Omits"/> and <see cref="Expands"/> are stored
/// positively — the side that lacks the words has none in the link, and the row itself is the
/// statement — which is what turns an absence from silence into an explanation.
/// </summary>
public enum LinkRelation
{
    Renders,
    Equals,
    Expands,
    Omits,
    Transposes,
}
