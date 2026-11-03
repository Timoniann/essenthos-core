using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Frame;

/// <param name="Scheme">The name the numbering goes by here: the edition's, as the data itself names
/// Brenton's merged psalm titles.</param>
/// <param name="Joins">A verse the data's own passage about the same text places, so the rows join
/// that passage and are weighed against its schemes; null where the data has no passage there.</param>
/// <param name="Rows">One row per printed verse, in the data's own shape.</param>
internal sealed record TvtmsSupplement(
    Versification Tradition,
    string Scheme,
    CanonicalReference? Joins,
    IReadOnlyList<TvtmsRow> Rows);

/// <summary>
/// Passages an edition in the corpus numbers in a way none of the versification data's schemes
/// describes, written down in the data's own form in <c>Resources/Essenthos/versification/supplements.tsv</c>:
/// the edition's verse, where its words stand, the tests that tell that edition apart, and what was
/// read. These are this project's, not Tyndale House's.
///
/// A supplement is only ever taken for an edition that answers every one of its tests, and then it
/// is taken in preference to the data's schemes for the passage it joins, because the tests were
/// written to recognise that one edition. An edition the tests do not describe is placed exactly as
/// before.
/// </summary>
internal static class TvtmsSupplements
{
    public static IReadOnlyList<string> Schemes(IReadOnlyList<TvtmsSupplement> supplements, Versification tradition) =>
    [
        .. supplements.Where(supplement => supplement.Tradition == tradition)
            .Select(supplement => supplement.Scheme)
            .Distinct(),
    ];

    /// <summary>
    /// Adds each supplement to the passage it joins, or as a passage of its own, and says which of
    /// them found the passage they were written to join.
    /// </summary>
    public static IReadOnlySet<TvtmsSupplement> Join(List<IReadOnlyList<TvtmsRow>> blocks, IReadOnlyList<TvtmsSupplement> supplements)
    {
        var joined = new HashSet<TvtmsSupplement>();

        foreach (var supplement in supplements)
        {
            var at = supplement.Joins is { } verse
                ? blocks.FindIndex(block => block.Any(row => row.Standards.Contains(verse)))
                : -1;
            if (at < 0)
            {
                blocks.Add(supplement.Rows);
                continue;
            }

            blocks[at] = [.. blocks[at], .. supplement.Rows];
            joined.Add(supplement);
        }

        return joined;
    }

    /// <summary>
    /// The file's passages in the order it writes them, each from the rows that name it. A passage is
    /// one edition's and joins one place, so its rows have to agree on both.
    /// </summary>
    public static List<TvtmsSupplement> Read(VersificationData data)
    {
        var passages = new List<(string Scheme, string Name, Versification Tradition, CanonicalReference? Joins, List<TvtmsRow> Rows)>();
        var traditions = new Dictionary<string, Versification>(StringComparer.Ordinal);

        foreach (var line in data.Rows(VersificationData.SupplementsProduct,
                     "passage", "scheme", "tradition", "joins", "source", "standard", "tests", "why"))
        {
            var scheme = line.Required("scheme");
            var name = line.Required("passage");
            var tradition = line.Tradition("tradition");
            if (traditions.TryGetValue(scheme, out var known) && known != tradition)
            {
                throw line.Fail($"the scheme {scheme} is given to {tradition} here and to {known} above");
            }

            traditions[scheme] = tradition;
            var joins = line.Optional("joins") is { } verse ? References(line, verse)[0] : (CanonicalReference?)null;
            var at = passages.FindIndex(passage => passage.Scheme == scheme && passage.Name == name);
            if (at < 0)
            {
                passages.Add((scheme, name, tradition, joins, []));
                at = passages.Count - 1;
            }
            else if (passages[at].Joins != joins)
            {
                throw line.Fail($"the passage {scheme} {name} joins {passages[at].Joins} above and {joins} here");
            }

            var tests = line.Required("tests");
            var conditions = VersificationTest.ParseAll(tests) ??
                             throw line.Fail(
                                 $"the tests \"{tests}\" say nothing this corpus can answer; write them as the " +
                                 "versification data does, as in Jer.30:33=Last");
            passages[at].Rows.AddRange(Rows(line, scheme, line.Required("source"), line.Required("standard"), conditions));
        }

        return [.. passages.Select(passage => new TvtmsSupplement(passage.Tradition, passage.Scheme, passage.Joins, passage.Rows))];
    }

    /// <summary>
    /// A range against a range of the same length is verse against verse; one verse against a range
    /// spans it, and a range against one verse puts each of them there.
    /// </summary>
    private static IEnumerable<TvtmsRow> Rows(
        VersificationLine line,
        string scheme,
        string printed,
        string standard,
        VersificationConditions tests)
    {
        var sources = References(line, printed);
        var standards = References(line, standard);

        if (sources.Count == standards.Count)
        {
            return sources.Zip(standards, (source, target) => new TvtmsRow([scheme], [source], [target], tests));
        }

        if (sources.Count == 1)
        {
            return [new TvtmsRow([scheme], sources, standards, tests)];
        }

        return standards.Count == 1
            ? sources.Select(source => new TvtmsRow([scheme], [source], standards, tests))
            : throw line.Fail(
                $"it pairs {printed} with {standard}, which are neither the same length nor a single verse on " +
                "either side; split it into rows that are");
    }

    private static IReadOnlyList<CanonicalReference> References(VersificationLine line, string value)
    {
        var references = CanonicalReference.ParseAll(value);
        return references.Count > 0
            ? references
            : throw line.Fail(
                $"the reference \"{value}\" is not one the versification data could hold; write it as the data " +
                "does, as in Jer.30:1-16");
    }
}
