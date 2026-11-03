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
/// put those verses instead, as <c>Resources/Essenthos/versification/corrections.tsv</c> writes them
/// with what was read for each. These are this project's, not Tyndale House's.
///
/// Each one replaces a single row, and only while the row still says what it was found saying: a
/// release of the data that corrects the rule, or changes it, is taken as it stands.
/// </summary>
internal static class TvtmsCorrections
{
    /// <summary>
    /// The row with its placement corrected, or the row itself. A row serving several schemes is
    /// never corrected: the correction is about one scheme, and would silently move the others.
    /// </summary>
    public static TvtmsRow Apply(TvtmsRow row, IReadOnlyList<TvtmsCorrection> corrections, out TvtmsCorrection? applied)
    {
        applied = row is { Traditions.Count: 1, Sources.Count: 1, Standards.Count: 1 }
            ? corrections.FirstOrDefault(correction =>
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

    public static List<TvtmsCorrection> Read(VersificationData data) =>
    [
        .. data.Rows(VersificationData.CorrectionsProduct,
                "scheme", "data-source", "data-standard", "source", "standard", "tests", "why")
            .Select(line => new TvtmsCorrection(
                line.Required("scheme"),
                Reference(line, "data-source"),
                Reference(line, "data-standard"),
                Reference(line, "source"),
                Reference(line, "standard"),
                line.Optional("tests") is { } tests
                    ? VersificationTest.ParseAll(tests) is null
                        ? throw line.Fail($"the tests \"{tests}\" say nothing this corpus can answer")
                        : tests
                    : null)),
    ];

    private static CanonicalReference Reference(VersificationLine line, string column)
    {
        var value = line.Required(column);
        return SongOfTheThree.TryParseAll(value, out var song) && song is [var verse] ? verse
            : CanonicalReference.TryParse(value, out var reference)
                ? reference
                : throw line.Fail(
                    $"the reference \"{value}\" is not one the versification data could hold; write it as the " +
                    "data does, as in Mal.4:4");
    }
}
