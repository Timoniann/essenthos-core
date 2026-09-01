namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// Which end of a link a word sits at. A link names a set on each side, and either set may be
/// empty; word order within a set carries no meaning.
/// </summary>
public enum LinkSide
{
    From,
    To,
}
