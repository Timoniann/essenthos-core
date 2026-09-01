namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// What a text is as an artefact — never what it is to another text. The Septuagint is a
/// translation of Hebrew, a witness to a Hebrew text older than the Masoretic, and the source the
/// New Testament quotes, all at once; a role belongs to a relation, not to the text.
/// </summary>
public enum TextKind
{
    ManuscriptTradition,
    CriticalEdition,

    /// <summary>
    /// A Renaissance printed edition — Stephanus 1550, Scrivener 1894. Not a critical edition: it
    /// was set by a printer from the manuscripts to hand, and Scrivener's was reconstructed
    /// backwards from what the King James translators must have read.
    /// </summary>
    PrintedEdition,
    Translation,
}
