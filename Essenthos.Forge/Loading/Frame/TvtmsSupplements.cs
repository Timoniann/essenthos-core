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
/// describes, written down in the data's own form: the edition's verse, where its words stand, and
/// the tests that tell that edition apart. These are this project's, not Tyndale House's, and each
/// was read verse against verse before it was written.
///
/// A supplement is only ever taken for an edition that answers every one of its tests, and then it
/// is taken in preference to the data's schemes for the passage it joins, because the tests were
/// written to recognise that one edition. An edition the tests do not describe is placed exactly as
/// before.
///
/// <para>
/// Brenton divides the Septuagint's Jeremiah as no other edition does. Its chapter 30 runs Edom
/// (30:1-16), Ammon (30:17-21), Kedar (30:23-28) and Damascus (30:29-33); it ends Elam at 25:20 with
/// the date the Hebrew puts first; and it prints the cup of wrath as 32:15-38 under the Hebrew's own
/// verse numbers. The data's two Greek schemes each describe one of those and fail the other.
/// </para>
/// <para>
/// Both editions print Agur's sayings and Lemuel's mother's in chapter 24 of Proverbs, where the
/// Septuagint puts them, and number them onward from there rather than under the Hebrew's numbers
/// as Rahlfs does; Swete also numbers the chapters around them straight through past the verses the
/// Septuagint leaves out. The data has no Greek rule for any of it.
/// </para>
/// <para>
/// Brenton divides Leviticus 8:18-21 and 8:30 differently from every other text, so its verses
/// 8:19-29 each stand one before the words they carry.
/// </para>
/// <para>
/// Swete prints 2 Samuel 19 under the English numbers and without its last verse, which fails both
/// the English and the Hebrew tests; and it prints the altar on Ebal as Joshua 9:3-8 where the
/// Hebrew has it as 8:30-35, numbering the Gibeonites on from 9:9. At the end of Joshua it keeps the
/// Septuagint's order under the Hebrew's verse numbers, which the data's tests cannot tell apart
/// from Brenton's renumbering of the same three verses.
/// </para>
/// </summary>
internal static class TvtmsSupplements
{
    private const string Brenton = "Brenton";

    private const string Swete = "Swete";

    private const string BrentonJeremiah = "Jer.30:33=Last & Jer.25:20=Last";

    private const string SweteProverbs = "Pro.24:77=Last";

    private const string SweteJoshua = "Jos.8:29=Last & Jos.9:33=Last";

    public static IReadOnlyList<TvtmsSupplement> All { get; } =
    [
        Passage(Brenton, "Jer.49:1", BrentonJeremiah,
            ("Jer.30:17-21", "Jer.49:1-5"),
            ("Jer.30:1-16", "Jer.49:7-22"),
            ("Jer.30:29-33", "Jer.49:23-27"),
            ("Jer.30:23-28", "Jer.49:28-33"),
            ("Jer.25:14", "Jer.49:34"),
            ("Jer.25:15-19", "Jer.49:35-39"),
            ("Jer.25:20", "Jer.49:34")),
        Passage(Brenton, "Jer.25:15", "Jer.32:38=Last & Jer.25:20=Last",
            ("Jer.32:15-38", "Jer.25:15-38")),
        Passage(Brenton, null, "Pro.24:62=Last",
            ("Pro.24:35-53", "Pro.30:15-33"),
            ("Pro.24:54-62", "Pro.31:1-9")),
        Passage(Brenton, null, "Lev.8:30<Lev.8:29",
            ("Lev.8:18", "Lev.8:18-19"),
            ("Lev.8:19", "Lev.8:20-21"),
            ("Lev.8:20", "Lev.8:21"),
            ("Lev.8:21-28", "Lev.8:22-29"),
            ("Lev.8:29-30", "Lev.8:30")),
        Passage(Swete, "Pro.18:23", SweteProverbs,
            ("Pro.18:23", "Pro.19:3"),
            ("Pro.19:1-26", "Pro.19:4-29")),
        Passage(Swete, "Pro.20:20", SweteProverbs,
            ("Pro.20:10-12", "Pro.20:20-22"),
            ("Pro.20:13-16", "Pro.20:10-13"),
            ("Pro.20:17-24", "Pro.20:23-30")),
        Passage(Swete, null, SweteProverbs,
            ("Pro.24:24-37", "Pro.30:1-14"),
            ("Pro.24:38-49", "Pro.24:23-34"),
            ("Pro.24:50-68", "Pro.30:15-33"),
            ("Pro.24:69-77", "Pro.31:1-9"),
            ("Pro.29:28-42", "Pro.31:10-24"),
            ("Pro.29:43", "Pro.31:26"),
            ("Pro.29:44", "Pro.31:25"),
            ("Pro.29:45-49", "Pro.31:27-31")),
        Passage(Swete, "2Sa.19:1", "2Sa.18:33=Last & 2Sa.19:42=Last",
            ("2Sa.18:33", "2Sa.18:33"),
            ("2Sa.19:1-42", "2Sa.19:1-42")),
        Passage(Swete, "Jos.8:30", SweteJoshua,
            ("Jos.9:3-8", "Jos.8:30-35"),
            ("Jos.9:9-27", "Jos.9:3-21")),
        Passage(Swete, "Jos.9:27", SweteJoshua,
            ("Jos.9:28-33", "Jos.9:22-27")),
        Passage(Swete, "Jos.24:29", "Jos.24:30.2=Exist & Jos.24:33.1=Exist",
            ("Jos.24:29-31", "Jos.24:29-31")),
    ];

    public static IReadOnlyList<string> Schemes(Versification tradition) =>
    [
        .. All.Where(supplement => supplement.Tradition == tradition)
            .Select(supplement => supplement.Scheme)
            .Distinct(),
    ];

    /// <summary>
    /// Adds each supplement to the passage it joins, or as a passage of its own, and says which of
    /// them found the passage they were written to join.
    /// </summary>
    public static IReadOnlySet<TvtmsSupplement> Join(List<IReadOnlyList<TvtmsRow>> blocks)
    {
        var joined = new HashSet<TvtmsSupplement>();

        foreach (var supplement in All)
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

    private static TvtmsSupplement Passage(
        string scheme,
        string? joins,
        string tests,
        params (string Printed, string Standard)[] verses)
    {
        var conditions = VersificationTest.ParseAll(tests) ??
                         throw new InvalidOperationException(
                             $"The supplement tests \"{tests}\" say nothing this corpus can answer. Write them as the " +
                             "versification data does, as in Jer.30:33=Last.");

        return new TvtmsSupplement(
            Versification.Septuagint,
            scheme,
            joins is null ? null : References(joins)[0],
            [.. verses.SelectMany(pair => Rows(scheme, pair.Printed, pair.Standard, conditions))]);
    }

    /// <summary>
    /// A range against a range of the same length is verse against verse; one verse against a range
    /// spans it, and a range against one verse puts each of them there.
    /// </summary>
    private static IEnumerable<TvtmsRow> Rows(
        string scheme,
        string printed,
        string standard,
        VersificationConditions tests)
    {
        var sources = References(printed);
        var standards = References(standard);

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
            : throw new InvalidOperationException(
                $"The supplement pairs {printed} with {standard}, which are neither the same length nor a single " +
                "verse on either side. Split it into pairs that are.");
    }

    private static IReadOnlyList<CanonicalReference> References(string value)
    {
        var references = CanonicalReference.ParseAll(value);
        return references.Count > 0
            ? references
            : throw new InvalidOperationException(
                $"The supplement reference \"{value}\" is not one the versification data could hold. Write it as " +
                "the data does, as in Jer.30:1-16.");
    }
}
