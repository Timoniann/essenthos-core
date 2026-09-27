namespace Essenthos.Core.Loading.Frame;

/// <param name="Scheme">The numbering scheme the corrected row is about, as the data names it.</param>
/// <param name="Source">The verse as the row names it.</param>
/// <param name="States">Where the data places it.</param>
/// <param name="Printed">Where that scheme prints the verse, which differs from <paramref name="Source"/>
/// only where the row names the wrong one.</param>
/// <param name="Standard">Where its words place it.</param>
/// <param name="Tests">The row's tests restated for the verse it now names, or null to keep its own.</param>
internal sealed record TvtmsCorrection(
    string Scheme,
    CanonicalReference Source,
    CanonicalReference States,
    CanonicalReference Printed,
    CanonicalReference Standard,
    string? Tests = null);

/// <summary>
/// The rules of the versification data that the texts themselves contradict, and where the texts
/// put those verses instead. These are this project's, not Tyndale House's.
///
/// Each one replaces a single row, and only while the row still says what it was found saying: a
/// release of the data that corrects the rule, or changes it, is taken as it stands.
///
/// <para>
/// The Septuagint ends Malachi with Elijah, the turning of hearts, and then the law of Moses, which
/// the Hebrew puts first. Brenton prints them as 3:22, 3:23 and 3:24, and Swete as 4:4, 4:5 and
/// 4:6, both in that order. The data's expanded rows for both schemes place each verse where the
/// next one belongs — the inverse of the reordering — so every row of the reader showed the Greek
/// of another verse beside the Hebrew and the English. Its own condensed table for the 3:22 scheme
/// says what the words say.
/// </para>
/// <para>
/// The Greek of Jeremiah prints the oracle against Ammon as 30:1-5, before Kedar at 30:6-11 and
/// Damascus at 30:12-16. The data's Greek column names it 30:17-21 while testing that the chapter
/// ends at 30:16, which no edition can satisfy, so the scheme failed for the one edition it was
/// written for and Swete's Ammon stood beside the Hebrew of Jeremiah 30.
/// </para>
/// <para>
/// In the Song of the Three the data's Greek and Latin columns place Daniel 3:77 at the seas and 3:78
/// at the springs, and its Greek column 3:80 at the beasts and 3:81 at the birds, where its Latin
/// column has them the other way round. Swete, the Clementine, the Douay and the Ge'ez print the
/// springs before the seas, and every edition here, Brenton's included, the birds before the beasts,
/// which is the order the standard's own verses have.
/// </para>
/// </summary>
internal static class TvtmsCorrections
{
    private const string JeremiahGreekTests = "{0}=Exist & Jer.30:16=Last & Jer.25:19=Last";

    public static IReadOnlyList<TvtmsCorrection> All { get; } =
    [
        Correction("Greek", "Mal.4:4", "Mal.4:6", "Mal.4:5"),
        Correction("Greek", "Mal.4:5", "Mal.4:4", "Mal.4:6"),
        Correction("Greek", "Mal.4:6", "Mal.4:5", "Mal.4:4"),
        Correction("Greek2", "Mal.3:22", "Mal.4:6", "Mal.4:5"),
        Correction("Greek2", "Mal.3:23", "Mal.4:4", "Mal.4:6"),
        Correction("Greek2", "Mal.3:24", "Mal.4:5", "Mal.4:4"),
        .. Enumerable.Range(1, 5).Select(verse => Renamed(
            "Greek",
            $"Jer.30:{verse + 16}",
            $"Jer.30:{verse}",
            $"Jer.49:{verse}",
            JeremiahGreekTests)),
        .. new[] { "Greek", "Latin" }.SelectMany(scheme => new[]
        {
            Correction(scheme, "Dan.3:77", "S3Y.1:56", "S3Y.1:55"),
            Correction(scheme, "Dan.3:78", "S3Y.1:55", "S3Y.1:56"),
        }),
        Correction("Greek", "Dan.3:80", "S3Y.1:59", "S3Y.1:58"),
        Correction("Greek", "Dan.3:81", "S3Y.1:58", "S3Y.1:59"),
    ];

    /// <summary>
    /// The row with its placement corrected, or the row itself. A row serving several schemes is
    /// never corrected: the correction is about one scheme, and would silently move the others.
    /// </summary>
    public static TvtmsRow Apply(TvtmsRow row, out TvtmsCorrection? applied)
    {
        applied = row is { Traditions.Count: 1, Sources.Count: 1, Standards.Count: 1 }
            ? All.FirstOrDefault(correction =>
                correction.Scheme == row.Traditions[0] &&
                correction.Source == row.Sources[0] &&
                correction.States == row.Standards[0])
            : null;

        return applied is null
            ? row
            : row with
            {
                Sources = [applied.Printed],
                Standards = [applied.Standard],
                Tests = applied.Tests is null ? row.Tests : VersificationTest.ParseAll(applied.Tests),
            };
    }

    private static TvtmsCorrection Correction(string scheme, string source, string states, string standard) =>
        new(scheme, Reference(source), Reference(states), Reference(source), Reference(standard));

    private static TvtmsCorrection Renamed(
        string scheme,
        string source,
        string printed,
        string standard,
        string tests) =>
        new(scheme, Reference(source), Reference(standard), Reference(printed), Reference(standard),
            string.Format(tests, printed));

    private static CanonicalReference Reference(string value) =>
        SongOfTheThree.TryParseAll(value, out var song) && song is [var verse] ? verse
        : CanonicalReference.TryParse(value, out var reference)
            ? reference
            : throw new InvalidOperationException(
                $"The correction reference \"{value}\" is not one the versification data could hold. Write it " +
                "as the data does, as in Mal.4:4.");
}
