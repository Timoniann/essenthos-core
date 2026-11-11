using Essenthos.Core.Corpus;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading.Links;

/// <param name="Module">The SWORD module the numbers are read from, as its configuration names it.</param>
/// <param name="Slug">The corpus text the numbers are laid onto.</param>
/// <param name="Credit">What every link drawn from them begins with, and the dataset declaration claims.</param>
/// <param name="Witnesses">The texts the numbers are matched against, each a run of its own.</param>
internal sealed record SwordNumbering(string Module, string Slug, string Credit, IReadOnlyList<string> Witnesses);

/// <summary>
/// The Strong numbers on the CrossWire modules beyond the Chinese Union Version, matched to the
/// Hebrew and the Greek the way FHL's are (<see cref="UnionStrongLinks"/>): read from the module
/// for the length of one run, laid onto the words the corpus holds, and never stored. The links are
/// what the mapping keeps, each credited to its numbering.
///
/// <para>
/// **The Segond's numbers go onto a text loaded from elsewhere.** <c>FreSegond1910</c> is Richard
/// Lemay's digitisation of the 1910 Segond, and the corpus loads eBible's; the two are laid together
/// verse by verse and word by word (<see cref="SynodalStrongLayer"/>), and a verse whose words do not
/// agree gets no number rather than a neighbour's. The other three are loaded from the module they
/// are numbered in, so every word finds itself.
/// </para>
///
/// <para>
/// Each numbering is matched against the witnesses its translation was made from, as near as the
/// corpus holds them: the Received Text for the Segond, the Schlachter and the King James revision,
/// which print what it prints, and the critical texts for Darby, whose New Testament follows them.
/// </para>
/// </summary>
internal static class CrossWireNumberings
{
    private static readonly string Scrivener = Sources.ScrivenerSlug;

    private static readonly string Stephanus = Sources.StephanusSlug;

    public static readonly IReadOnlyList<SwordNumbering> All =
    [
        new("FreSegond1910", EbibleTextSource.Segond, Sources.SegondStrongCredit,
            [BhsaTextSource.Slug, Scrivener, NestleTextSource.Slug]),
        new("FreJND", SwordTextSource.DarbyFrench, Sources.DarbyFrenchStrongCredit,
            [BhsaTextSource.Slug, NestleTextSource.Slug, WestcottHortTextSource.Slug]),
        new("GerSch", SwordTextSource.Schlachter, Sources.SchlachterStrongCredit,
            [BhsaTextSource.Slug, Scrivener, NestleTextSource.Slug]),
        new("RLT", SwordTextSource.RevisedLiteral, Sources.RevisedLiteralStrongCredit,
            [BhsaTextSource.Slug, Scrivener, Stephanus]),
    ];
}
