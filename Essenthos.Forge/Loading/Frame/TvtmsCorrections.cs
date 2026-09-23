namespace Essenthos.Core.Loading.Frame;

/// <param name="Scheme">The numbering scheme the corrected row is about, as the data names it.</param>
/// <param name="Source">The verse as that scheme numbers it.</param>
/// <param name="States">Where the data places it, which is what is corrected.</param>
/// <param name="Standard">Where its words place it.</param>
internal sealed record TvtmsCorrection(
    string Scheme,
    CanonicalReference Source,
    CanonicalReference States,
    CanonicalReference Standard);

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
/// </summary>
internal static class TvtmsCorrections
{
    public static IReadOnlyList<TvtmsCorrection> All { get; } =
    [
        Correction("Greek", "Mal.4:4", "Mal.4:6", "Mal.4:5"),
        Correction("Greek", "Mal.4:5", "Mal.4:4", "Mal.4:6"),
        Correction("Greek", "Mal.4:6", "Mal.4:5", "Mal.4:4"),
        Correction("Greek2", "Mal.3:22", "Mal.4:6", "Mal.4:5"),
        Correction("Greek2", "Mal.3:23", "Mal.4:4", "Mal.4:6"),
        Correction("Greek2", "Mal.3:24", "Mal.4:5", "Mal.4:4"),
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

        return applied is null ? row : row with { Standards = [applied.Standard] };
    }

    private static TvtmsCorrection Correction(string scheme, string source, string states, string standard) =>
        new(scheme, Reference(source), Reference(states), Reference(standard));

    private static CanonicalReference Reference(string value) =>
        CanonicalReference.TryParse(value, out var reference)
            ? reference
            : throw new InvalidOperationException(
                $"The correction reference \"{value}\" is not one the versification data could hold. Write it " +
                "as the data does, as in Mal.4:4.");
}
