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
/// <para>
/// Both Greek editions list the unclean birds of Leviticus 11:13-19 in their own order: the ostrich,
/// the owl and the gull come before the raven, which the Hebrew names first. Brenton prints them as
/// 11:15 and puts the raven with the hawk in 11:16, which stands with them at 11:16 and covers the
/// Hebrew's raven as well; Swete prints no raven at all. The chapters count the same, so no scheme
/// notices.
/// </para>
/// <para>
/// The Reina-Valera of 1909, as eBible publishes it, follows the Spanish division where the Spanish
/// runs ahead of the English — a chapter that opens one to three verses earlier, as the Hebrew and
/// the Latin do — but it is numbered to the English count: the verse the Spanish moved away is left
/// empty, every verse after it stands one to three places early, and the chapter's last verse holds
/// what the count had no room for. Its Job 39:30 carries the English 39:27 to 40:5. The data's own
/// Spanish column for Job says the same of the printed edition.
/// </para>
/// <para>
/// Kulish's Bible of 1903 divides forty-odd chapters in its own way: it runs two verses into one at a
/// chapter's end, opens Genesis 3 with what the English prints as two verses, gives the Hebrew's
/// order of Leviticus 5-6 and Job 39-41 a division of its own, prints the titles of Psalm 60 as two
/// verses of their own and swaps the weeping and the rallying at Judges 20:22-23. Where its verse
/// merely starts half a line before the English one, it is left where it stands.
/// </para>
/// <para>
/// The Synodal, as bible4u publishes it, is renumbered to the English chapters and verse counts, and
/// in a few chapters keeps its own division inside them: Song of Songs 1 opens with <em>let him kiss
/// me</em>, which the English numbers 1:2, and divides the last verse in two; Psalm 90 prints its
/// title as a verse of its own and joins the flood and the grass; Isaiah 3 lists the ornaments in
/// fewer verses and divides the last; Esther 1 and Revelation 20 divide a verse where the English
/// does not.
/// </para>
/// </summary>
internal static class TvtmsSupplements
{
    private const string Brenton = "Brenton";

    private const string Swete = "Swete";

    private const string ReinaValera = "ReinaValera1909";

    private const string Kulish = "Kulish";

    private const string Synodal = "Synodal1876";

    private const string BrentonJeremiah = "Jer.30:33=Last & Jer.25:20=Last";

    private const string BrentonLeviticus = "Lev.8:30<Lev.8:29";

    private const string SweteProverbs = "Pro.24:77=Last";

    private const string SweteJoshua = "Jos.8:29=Last & Jos.9:33=Last";

    /// <summary>
    /// The verse eBible's Reina-Valera leaves empty at the start of Jonah's prayer, which no other
    /// edition in the corpus lacks: it tells the file apart wherever a passage's own tests would also
    /// describe a Bible that simply follows the Hebrew.
    /// </summary>
    private const string ReinaValeraEdition = "Jon.1:17=NotExist";

    /// <summary>Kulish's Genesis 3, which ends at verse 23 and in no other edition here.</summary>
    private const string KulishEdition = "Gen.3:23=Last";

    /// <summary>
    /// The Synodal's last verse of Song of Songs 1, <em>the rafters of fir</em>, printed apart from
    /// <em>the beams of our house are cedar</em>, which no other edition here does.
    /// </summary>
    private const string SynodalEdition = "Sng.1:16*2<Sng.1:15";

    private static readonly Dictionary<string, Versification> Traditions = new()
    {
        [Brenton] = Versification.Septuagint,
        [Swete] = Versification.Septuagint,
        [ReinaValera] = Versification.English,
        [Kulish] = Versification.English,
        [Synodal] = Versification.English,
    };

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
        Passage(Brenton, null, BrentonLeviticus,
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
        Passage(Brenton, null, $"Lev.11:15<Lev.11:16 & {BrentonLeviticus}",
            ("Lev.11:15", "Lev.11:16"),
            ("Lev.11:16", "Lev.11:16; 11:15")),
        Passage(Swete, null, $"Lev.11:16<Lev.11:15 & {SweteProverbs}",
            ("Lev.11:15-16", "Lev.11:16")),
        .. ReinaValeraPassages,
        .. KulishPassages,
        .. SynodalPassages,
    ];

    private static IEnumerable<TvtmsSupplement> ReinaValeraPassages =>
    [
        Passage(ReinaValera, "Num.12:16", $"{ReinaValeraEdition} & Num.12:16=NotExist & Num.13:33=Last",
            ("Num.13:1", "Num.12:16"),
            ("Num.13:2-32", "Num.13:1-31"),
            ("Num.13:33", "Num.13:32-33")),
        Passage(ReinaValera, "Num.29:40", $"{ReinaValeraEdition} & Num.29:40=NotExist & Num.30:16=Last",
            ("Num.30:1", "Num.29:40"),
            ("Num.30:2-15", "Num.30:1-14"),
            ("Num.30:16", "Num.30:15-16")),
        Passage(ReinaValera, null, $"{ReinaValeraEdition} & Jdg.14:19<Jdg.14:18",
            ("Jdg.14:19", "Jdg.14:18"),
            ("Jdg.14:20", "Jdg.14:19-20")),
        Passage(ReinaValera, "1Sa.23:29", $"{ReinaValeraEdition} & 1Sa.23:29=NotExist & 1Sa.24:22=Last",
            ("1Sa.24:1", "1Sa.23:29"),
            ("1Sa.24:2-21", "1Sa.24:1-20"),
            ("1Sa.24:22", "1Sa.24:21-22")),
        Passage(ReinaValera, null, $"{ReinaValeraEdition} & 2Sa.20:26=NotExist & 2Sa.20:25=Last",
            ("2Sa.20:25", "2Sa.20:25-26")),
        Passage(ReinaValera, "1Ki.22:43", $"{ReinaValeraEdition} & 1Ki.22:53=Last & 1Ki.22:45<1Ki.22:44",
            ("1Ki.22:44", "1Ki.22:43"),
            ("1Ki.22:45-52", "1Ki.22:44-51"),
            ("1Ki.22:53", "1Ki.22:52-53")),
        Passage(ReinaValera, null, $"{ReinaValeraEdition} & 1Ch.1:32<1Ch.1:31",
            ("1Ch.1:30", "1Ch.1:30-31"),
            ("1Ch.1:31-32", "1Ch.1:32")),
        Passage(ReinaValera, null, $"{ReinaValeraEdition} & 1Ch.21:28<1Ch.21:27",
            ("1Ch.21:16", "1Ch.21:15"),
            ("1Ch.21:17-29", "1Ch.21:16-28"),
            ("1Ch.21:30", "1Ch.21:29-30")),
        Passage(ReinaValera, null, $"{ReinaValeraEdition} & 2Ch.33:25=NotExist & 2Ch.33:24=Last",
            ("2Ch.33:10", "2Ch.33:10-11"),
            ("2Ch.33:11-24", "2Ch.33:12-25")),
        Passage(ReinaValera, null, $"{ReinaValeraEdition} & Job.35:16=NotExist & Job.35:15=Last",
            ("Job.35:15", "Job.35:15-16")),
        Passage(ReinaValera, "Job.38:39",
            $"{ReinaValeraEdition} & Job.38:39=NotExist & Job.39:30=Last & Job.40:19=Last",
            ("Job.39:1-3", "Job.38:39-41"),
            ("Job.39:4-29", "Job.39:1-26"),
            ("Job.39:30", "Job.39:27-30; 40:1-5"),
            ("Job.40:1-19", "Job.40:6-24")),
        Passage(ReinaValera, "Hos.11:12", $"{ReinaValeraEdition} & Hos.11:12=NotExist & Hos.12:14=Last",
            ("Hos.12:1", "Hos.11:12"),
            ("Hos.12:2-13", "Hos.12:1-12"),
            ("Hos.12:14", "Hos.12:13-14")),
        Passage(ReinaValera, "Jon.1:17", $"{ReinaValeraEdition} & Jon.2:10=Last",
            ("Jon.2:1", "Jon.1:17"),
            ("Jon.2:2-9", "Jon.2:1-8"),
            ("Jon.2:10", "Jon.2:9-10")),
    ];

    private static IEnumerable<TvtmsSupplement> KulishPassages =>
    [
        Passage(Kulish, "Gen.3:1", KulishEdition,
            ("Gen.3:1", "Gen.3:1-2"),
            ("Gen.3:2-23", "Gen.3:3-24")),
        Passage(Kulish, null, $"{KulishEdition} & Gen.6:21=Last",
            ("Gen.6:20", "Gen.6:20-21"),
            ("Gen.6:21", "Gen.6:22")),
        Passage(Kulish, null, $"{KulishEdition} & Gen.48:21=Last",
            ("Gen.48:21", "Gen.48:21-22")),
        Passage(Kulish, "Lev.6:1", $"{KulishEdition} & Lev.5:27=Last & Lev.6:22=Last",
            ("Lev.5:20-24", "Lev.6:1-5"),
            ("Lev.5:25", "Lev.6:5"),
            ("Lev.5:26-27", "Lev.6:6-7"),
            ("Lev.6:1-21", "Lev.6:8-28"),
            ("Lev.6:22", "Lev.6:29-30")),
        Passage(Kulish, null, $"{KulishEdition} & Lev.14:55=Last",
            ("Lev.14:54", "Lev.14:54-55"),
            ("Lev.14:55", "Lev.14:56-57")),
        Passage(Kulish, null, $"{KulishEdition} & Lev.17:15=Last",
            ("Lev.17:15", "Lev.17:15-16")),
        Passage(Kulish, null, $"{KulishEdition} & Num.8:25=Last",
            ("Num.8:25", "Num.8:25-26")),
        Passage(Kulish, null, $"{KulishEdition} & Num.14:44=Last",
            ("Num.14:44", "Num.14:44-45")),
        Passage(Kulish, null, $"{KulishEdition} & Num.15:40=Last",
            ("Num.15:40", "Num.15:40-41")),
        Passage(Kulish, "Num.20:29", $"{KulishEdition} & Num.20:28=Last",
            ("Num.20:28", "Num.20:28-29")),
        Passage(Kulish, null, $"{KulishEdition} & Num.23:31=Last",
            ("Num.23:18", "Num.23:17"),
            ("Num.23:19-31", "Num.23:18-30")),
        Passage(Kulish, null, $"{KulishEdition} & Num.25:17=Last",
            ("Num.25:17", "Num.25:17-18")),
        Passage(Kulish, null, $"{KulishEdition} & Num.27:22=Last",
            ("Num.27:22", "Num.27:22-23")),
        Passage(Kulish, null, $"{KulishEdition} & Deu.16:21=Last",
            ("Deu.16:21", "Deu.16:21-22")),
        Passage(Kulish, "Deu.24:22", $"{KulishEdition} & Deu.24:21=Last",
            ("Deu.24:21", "Deu.24:21-22")),
        Passage(Kulish, null, $"{KulishEdition} & Deu.27:7*2<Deu.27:8",
            ("Deu.27:7", "Deu.27:6"),
            ("Deu.27:8", "Deu.27:7-8")),
        Passage(Kulish, "Deu.29:1", $"{KulishEdition} & Deu.28:69=Last & Deu.29:29=Last",
            ("Deu.28:69", "Deu.29:1"),
            ("Deu.29:1-2", "Deu.29:2")),
        Passage(Kulish, null, $"{KulishEdition} & Deu.32:51=Last",
            ("Deu.32:51", "Deu.32:51-52")),
        Passage(Kulish, null, $"{KulishEdition} & Deu.34:11=Last",
            ("Deu.34:11", "Deu.34:11-12")),
        Passage(Kulish, null, $"{KulishEdition} & Jdg.20:22>Jdg.20:23",
            ("Jdg.20:22", "Jdg.20:23"),
            ("Jdg.20:23", "Jdg.20:22")),
        Passage(Kulish, null, $"{KulishEdition} & 2Sa.2:33=Last",
            ("2Sa.2:5", "2Sa.2:4"),
            ("2Sa.2:6-33", "2Sa.2:5-32")),
        Passage(Kulish, null, $"{KulishEdition} & Est.1:7*2<Est.1:8",
            ("Est.1:7", "Est.1:6"),
            ("Est.1:8", "Est.1:7-8")),
        Passage(Kulish, null, $"{KulishEdition} & Job.21:33=Last",
            ("Job.21:32", "Job.21:32-33"),
            ("Job.21:33", "Job.21:34")),
        Passage(Kulish, "Job.38:39", $"{KulishEdition} & Job.39:35=Last & Job.40:27=Last & Job.41:26=Last",
            ("Job.39:31-35", "Job.40:1-5"),
            ("Job.40:1-19", "Job.40:6-24"),
            ("Job.40:20-27", "Job.41:1-8"),
            ("Job.41:1-26", "Job.41:9-34")),
        Passage(Kulish, "Psa.13:5", $"{KulishEdition} & Psa.13:5=Last",
            ("Psa.13:5", "Psa.13:5-6")),
        Passage(Kulish, "Psa.24:1", $"{KulishEdition} & Psa.24:9=Last",
            ("Psa.24:9", "Psa.24:9-10")),
        Passage(Kulish, "Psa.29:11", $"{KulishEdition} & Psa.29:10=Last",
            ("Psa.29:7", "Psa.29:7-8"),
            ("Psa.29:8-10", "Psa.29:9-11")),
        Passage(Kulish, "Psa.54:7", $"{KulishEdition} & Psa.54:6=Last",
            ("Psa.54:4", "Psa.54:4-5"),
            ("Psa.54:5-6", "Psa.54:6-7")),
        Passage(Kulish, "Psa.60:1", $"{KulishEdition} & Psa.60:14=Last",
            ("Psa.60:1-2", "Psa.60:Title"),
            ("Psa.60:3-14", "Psa.60:1-12")),
        Passage(Kulish, "Psa.89:52", $"{KulishEdition} & Psa.89:51=Last",
            ("Psa.89:51", "Psa.89:51-52")),
        Passage(Kulish, "Psa.106:48", $"{KulishEdition} & Psa.106:47=Last",
            ("Psa.106:47", "Psa.106:47-48")),
        Passage(Kulish, null, $"{KulishEdition} & Psa.127:6=Last",
            ("Psa.127:6", "Psa.127:5")),
        Passage(Kulish, null, $"{KulishEdition} & Pro.30:32=Last",
            ("Pro.30:30", "Pro.30:30-31"),
            ("Pro.30:31-32", "Pro.30:32-33")),
        Passage(Kulish, null, $"{KulishEdition} & Isa.3:20*2<Isa.3:19",
            ("Isa.3:19", "Isa.3:19-20"),
            ("Isa.3:20-21", "Isa.3:21-22"),
            ("Isa.3:22", "Isa.3:22")),
        Passage(Kulish, "Isa.9:21", $"{KulishEdition} & Isa.9:22=Last",
            ("Isa.9:22", "Isa.9:21")),
        Passage(Kulish, null, $"{KulishEdition} & Php.3:20=Last",
            ("Php.3:20", "Php.3:20-21")),
        Passage(Kulish, null, $"{KulishEdition} & Phm.1:24=Last",
            ("Phm.1:23", "Phm.1:23-24"),
            ("Phm.1:24", "Phm.1:25")),
    ];

    private static IEnumerable<TvtmsSupplement> SynodalPassages =>
    [
        Passage(Synodal, "Sng.1:2", SynodalEdition,
            ("Sng.1:1-15", "Sng.1:2-16"),
            ("Sng.1:16-17", "Sng.1:17")),
        Passage(Synodal, "Psa.90:1", $"{SynodalEdition} & Psa.90:1*2<Psa.90:3",
            ("Psa.90:1-2", "Psa.90:1"),
            ("Psa.90:3-5", "Psa.90:2-4"),
            ("Psa.90:6", "Psa.90:5-6")),
        Passage(Synodal, null, $"{SynodalEdition} & Est.1:7*2<Est.1:8",
            ("Est.1:7", "Est.1:6"),
            ("Est.1:8", "Est.1:7-8")),
        Passage(Synodal, null, $"{SynodalEdition} & Isa.3:20*2<Isa.3:19",
            ("Isa.3:19", "Isa.3:19-20"),
            ("Isa.3:20-24", "Isa.3:21-25"),
            ("Isa.3:25-26", "Isa.3:26")),
        Passage(Synodal, null, $"{SynodalEdition} & Rev.20:8*2<Rev.20:7",
            ("Rev.20:7", "Rev.20:7-8"),
            ("Rev.20:8-9", "Rev.20:9")),
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
            Traditions[scheme],
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
