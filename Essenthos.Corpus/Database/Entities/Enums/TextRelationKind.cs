namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// How one text stands to another. A declared relation is a hint telling a loader where to look
/// first, never a constraint on which links may exist: the Synodal is translated from the Masoretic
/// and from the Septuagint both, and which applies where is settled per link.
/// </summary>
public enum TextRelationKind
{
    TranslatedFrom,
    RevisedFrom,
    SameFamilyAs,
    CollatedAgainst,
}
