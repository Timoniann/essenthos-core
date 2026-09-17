namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// The chapter and verse numbering a text follows. Schemes disagree on where verses begin and how
/// the Psalms are numbered, so a verse number is only meaningful together with its scheme. Names
/// follow the schemes used by Paratext and the SIL versification files.
/// </summary>
public enum Versification
{
    Unknown,
    Original,
    English,
    Septuagint,
    Vulgate,
    RussianOrthodox,
    RussianProtestant,
}
