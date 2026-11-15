using Essenthos.Core.Corpus;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Russian Synodal reaches the Hebrew and the Greek by the Strong numbers Bob Jones University
/// put on it in 1996 — the route the King James and Luther already have, and the first one the
/// Synodal has that is not this project's model.
///
/// <para>
/// **The numbering is an input and never a row.** Its only statement of terms permits use of the
/// work unmodified, and the owner decided on 2026-09-13 to use it for the mapping alone
/// (Resources/SynodalStrong/LICENCE.md). So the edition is read from the file, laid onto the
/// Synodal's words (<see cref="SynodalStrongLayer"/>, <see cref="Conversion.SideStrongNumbers"/>): the tagged
/// edition is not a text, and nothing serves its numbers. What the build draws from them is the pair's
/// <c>strong</c> rows (<see cref="Mapping.StrongNumberSource"/>), credited to the numbering (<see cref="Credit"/>).
/// </para>
/// </summary>
internal static class SynodalStrongLinks
{
    /// <summary>Where the fetch script puts the edition, under the corpus sources.</summary>
    public static readonly string[] EditionFile = ["SynodalStrong", "RSTE_verse_words.xml"];

    /// <summary>
    /// What every link drawn from the numbering begins with, and what the dataset declaration
    /// claims. It names whose numbering it is and where the copy came from, because the copy
    /// carries no credit of its own.
    /// </summary>
    public const string Credit = Sources.SynodalStrongCredit;

    /// <summary>
    /// The witnesses the Synodal is matched against. The numbering's Greek is the King James's
    /// Received Text, so the three editions of it are here beside Nestle.
    /// </summary>
    public static readonly string[] Witnesses =
    [
        BhsaTextSource.Slug,
        TextusReceptusTextSource.Slug(Edition.Scrivener1894),
        TextusReceptusTextSource.Slug(Edition.Stephanus1550),
        ByzantineTextSource.Slug,
        NestleTextSource.Slug,
    ];
}
