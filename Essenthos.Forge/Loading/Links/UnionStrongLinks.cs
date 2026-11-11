using Essenthos.Core.Corpus;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Chinese Union Version reaches the Hebrew and the Greek by the Strong numbers the Faith Hope
/// Love foundation (信望愛, FHL) put on it — the route Luther and the Synodal already have.
///
/// <para>
/// **The numbers are an input and never a row**, as the Synodal's are. FHL released them under the
/// GNU Free Documentation License, which permits this use without asking; they are read from the
/// module for the length of one run, laid onto the words the corpus loaded from the same module
/// (<see cref="SynodalStrongLayer"/>, which requires the two to write each verse the same way), and
/// the links drawn from them are what the mapping keeps, credited to FHL (<see cref="Credit"/>).
/// </para>
///
/// <para>
/// Both scripts are the same text with the same tagging, so each is matched on its own module's
/// numbers and neither borrows the other's.
/// </para>
/// </summary>
internal static class UnionStrongLinks
{
    /// <summary>What every link drawn from FHL's numbers begins with, and the dataset declaration claims.</summary>
    public const string Credit = Sources.UnionStrongCredit;

    /// <summary>
    /// The witnesses the Union Version is matched against. Its New Testament was translated from the
    /// Greek behind the English Revised Version, which Westcott and Hort's text is the nearest
    /// printed form of; Nestle's is the critical text every other translation here meets.
    /// </summary>
    public static readonly string[] Witnesses =
    [
        BhsaTextSource.Slug,
        NestleTextSource.Slug,
        WestcottHortTextSource.Slug,
    ];
}
