using System.Globalization;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>One number the reckoning reads, and where it reads it.</summary>
/// <param name="Run">Which of the verse's numbers, where it states more than one.</param>
internal sealed record SeptuagintReading(string Key, int Book, int Chapter, int Verse, int Run = 0)
{
    public string Reference => $"{BookReferences.Name(Book)} {Chapter}:{Verse}";
}

/// <summary>A step of the arithmetic: a reading added or taken away.</summary>
internal sealed record SeptuagintTerm(string Reading, int Sign = 1);

/// <summary>
/// How one event's year is reached: from another event's year, or from the creation, by the terms.
/// </summary>
/// <param name="Event">The event, by its BibleData id — or a key of this reckoning's own.</param>
/// <param name="From">The event it counts from; null for the creation, which is year 1.</param>
internal sealed record SeptuagintRule(string Event, string? From, params SeptuagintTerm[] Terms);

/// <summary>A year this reckoning gives, with the sentence that shows how.</summary>
internal sealed record SeptuagintYear(int Year, string Calculation, string? Citation);

/// <summary>What a reckoning makes of the base chronology's events it is not computed for.</summary>
internal enum SeptuagintSide
{
    /// <summary>Counted forward from Abram or the Exodus: moves with the genealogies.</summary>
    Exodus,

    /// <summary>Counted back from the Temple, or anchored to the common era: moves with the zero.</summary>
    Temple,
}

/// <summary>
/// The Septuagint's chronology, computed rather than looked up: nobody publishes one as data.
///
/// The method is BibleData's own — every year a sum over the ages of Genesis 5 and 11 — and only the
/// numbers change, read out of the Greek the corpus holds. That is the whole difference between the
/// two chronologies of the Bible, and it is large: the Septuagint's fathers beget a century later
/// than the Hebrew's, it has one more generation (Cainan, whom Luke 3:36 also names), and the Flood
/// falls some six hundred years after the creation later than it does in the Masoretic count.
///
/// <para>
/// **Two readings, because the owner asked for both.** The Greek of Genesis survives in more than one
/// form. Brenton prints the Sixtine edition, where Methuselah begets Lamech at 167 — and so outlives
/// the Flood by fourteen years. Codex Alexandrinus, which Swete prints because Vaticanus has lost
/// Genesis up to 46:28, has him beget at 187. The second reckoning is Brenton's numbers with that one
/// reading taken from Swete; Swete's digitisation drops number words elsewhere in Genesis (9:28 and
/// 12:4 each lose one), so reading the whole chapter from it would compute a digitisation fault.
/// </para>
///
/// <para>
/// **After Genesis** both read Brenton. Exodus 12:40 counts the 430 years in Egypt <em>and in
/// Canaan</em>, which is how the base reckoning already counts them, so nothing moves. 1 Kings 6:1
/// puts the Temple 440 years after the Exodus where the Hebrew says 480: the base reckoning counts
/// the judges forward from the Exodus and Saul and David back from the Temple, and each keeps its
/// own distance, so the forty years come out of the one stretch where the two counts meet. From the
/// Temple on the reigns are the base reckoning's, and everything anchored to the common era keeps
/// its historical year and changes only its distance from the creation.
/// </para>
/// </summary>
internal static partial class SeptuagintReckoning
{
    private const int Genesis = 1;
    private const int Exodus = 2;
    private const int FirstKings = 11;

    /// <summary>Where Cainan's birth and death sit among the events, which the Hebrew does not have.</summary>
    public const string CainanBorn = "cainan-born";

    public const string CainanDied = "cainan-died";

    public const string AbramBorn = "Birth_Abram_1";
    public const string AbramLeavesHaran = "Abram_departed_Haran";
    public const string TheExodus = "The_Exodus";
    public const string TheTemple = "Begin_first_Temple_construction";

    public const string Sojourn = "sojourn";
    public const string ExodusToTemple = "exodus-to-temple";

    /// <summary>
    /// Every number the reckoning reads. The ages a father lived after his son are read too, in
    /// Genesis 5, only to check that each man's two ages add up to his whole life.
    /// </summary>
    public static readonly IReadOnlyList<SeptuagintReading> Readings =
    [
        .. Fathers(
            ("adam", 3), ("seth", 6), ("enosh", 9), ("kenan", 12), ("mahalalel", 15), ("jared", 18),
            ("enoch", 21), ("methuselah", 25)),
        new("lamech-begets", Genesis, 5, 28), new("lamech-after", Genesis, 5, 30), new("lamech-life", Genesis, 5, 31),
        new("noah-flood", Genesis, 7, 6), new("noah-flood-ends", Genesis, 8, 13), new("noah-after", Genesis, 9, 28),
        new("noah-life", Genesis, 9, 29),
        new("shem-begets", Genesis, 11, 10), new("after-the-flood", Genesis, 11, 10, 1), new("shem-after", Genesis, 11, 11),
        new("arphaxad-begets", Genesis, 11, 12), new("arphaxad-after", Genesis, 11, 13),
        new("cainan-begets", Genesis, 11, 13, 1), new("cainan-after", Genesis, 11, 13, 2),
        new("shelah-begets", Genesis, 11, 14), new("shelah-after", Genesis, 11, 15),
        new("eber-begets", Genesis, 11, 16), new("eber-after", Genesis, 11, 17),
        new("peleg-begets", Genesis, 11, 18), new("peleg-after", Genesis, 11, 19),
        new("reu-begets", Genesis, 11, 20), new("reu-after", Genesis, 11, 21),
        new("serug-begets", Genesis, 11, 22), new("serug-after", Genesis, 11, 23),
        new("nahor-begets", Genesis, 11, 24), new("nahor-after", Genesis, 11, 25),
        new("terah-begets", Genesis, 11, 26), new("terah-life", Genesis, 11, 32),
        new("abram-leaves-haran", Genesis, 12, 4),
        new(Sojourn, Exodus, 12, 40),
        new(ExodusToTemple, FirstKings, 6, 1),
    ];

    private static IEnumerable<SeptuagintReading> Fathers(params (string Name, int Verse)[] fathers) =>
        fathers.SelectMany(father => new SeptuagintReading[]
        {
            new($"{father.Name}-begets", Genesis, 5, father.Verse),
            new($"{father.Name}-after", Genesis, 5, father.Verse + 1),
            new($"{father.Name}-life", Genesis, 5, father.Verse + 2),
        });

    /// <summary>The Genesis 5 fathers whose three ages must close: begetting, after, and the whole.</summary>
    private static readonly string[] Closing =
        ["adam", "seth", "enosh", "kenan", "mahalalel", "jared", "enoch", "methuselah", "lamech"];

    /// <summary>
    /// The arithmetic, in BibleData's own order and by BibleData's own ids, with Cainan added where
    /// the Greek puts him. Every step here is one BibleData takes; only the numbers are the Greek's.
    /// </summary>
    public static readonly IReadOnlyList<SeptuagintRule> Rules =
    [
        new("Creation", null),
        new("Birth_Adam_1", null),
        new("Birth_Seth_1", "Birth_Adam_1", T("adam-begets")),
        new("Birth_Enosh_1", "Birth_Seth_1", T("seth-begets")),
        new("Birth_Kenan_1", "Birth_Enosh_1", T("enosh-begets")),
        new("Birth_Mahalalel_1", "Birth_Kenan_1", T("kenan-begets")),
        new("Birth_Jared_1", "Birth_Mahalalel_1", T("mahalalel-begets")),
        new("Birth_Enoch_2", "Birth_Jared_1", T("jared-begets")),
        new("Birth_Methuselah_1", "Birth_Enoch_2", T("enoch-begets")),
        new("Birth_Lamech_2", "Birth_Methuselah_1", T("methuselah-begets")),
        new("Birth_Noah_1", "Birth_Lamech_2", T("lamech-begets")),
        new("Death_Adam_1", "Birth_Adam_1", T("adam-life")),
        new("Death_Seth_1", "Birth_Seth_1", T("seth-life")),
        new("Death_Enosh_1", "Birth_Enosh_1", T("enosh-life")),
        new("Death_Kenan_1", "Birth_Kenan_1", T("kenan-life")),
        new("Death_Mahalalel_1", "Birth_Mahalalel_1", T("mahalalel-life")),
        new("Death_Jared_1", "Birth_Jared_1", T("jared-life")),
        new("Death_Enoch_2", "Birth_Enoch_2", T("enoch-life")),
        new("Death_Methuselah_1", "Birth_Methuselah_1", T("methuselah-life")),
        new("Death_Lamech_2", "Birth_Lamech_2", T("lamech-life")),
        new("Begin_Flood", "Birth_Noah_1", T("noah-flood")),
        new("End_Flood", "Birth_Noah_1", T("noah-flood-ends")),
        new("Covenant_with_Noah", "End_Flood"),
        new("Death_Noah_1", "End_Flood", T("noah-after")),
        new("Birth_Arpachshad_1", "End_Flood", T("after-the-flood")),
        new("Birth_Shem_1", "Birth_Arpachshad_1", T("shem-begets", -1)),
        new(CainanBorn, "Birth_Arpachshad_1", T("arphaxad-begets")),
        new("Birth_Shelah_1", CainanBorn, T("cainan-begets")),
        new("Birth_Eber_1", "Birth_Shelah_1", T("shelah-begets")),
        new("Birth_Peleg_1", "Birth_Eber_1", T("eber-begets")),
        new("Birth_Reu_1", "Birth_Peleg_1", T("peleg-begets")),
        new("Birth_Serug_1", "Birth_Reu_1", T("reu-begets")),
        new("Birth_Nahor_1", "Birth_Serug_1", T("serug-begets")),
        new("Birth_Terah_1", "Birth_Nahor_1", T("nahor-begets")),
        new("Death_Shem_1", "Birth_Arpachshad_1", T("shem-after")),
        new("Death_Arpachshad_1", CainanBorn, T("arphaxad-after")),
        new(CainanDied, "Birth_Shelah_1", T("cainan-after")),
        new("Death_Shelah_1", "Birth_Eber_1", T("shelah-after")),
        new("Death_Eber_1", "Birth_Peleg_1", T("eber-after")),
        new("Death_Peleg_1", "Birth_Reu_1", T("peleg-after")),
        new("Tower_of_Babel", "Death_Peleg_1"),
        new("Death_Reu_1", "Birth_Serug_1", T("reu-after")),
        new("Death_Serug_1", "Birth_Nahor_1", T("serug-after")),
        new("Death_Nahor_1", "Birth_Terah_1", T("nahor-after")),
        new("Birth_Haran_1", "Birth_Terah_1", T("terah-begets")),
        new("Death_Terah_1", "Birth_Terah_1", T("terah-life")),
        // Abram left Haran at 75 when Terah died at 205, as Acts 7:4 reads it; BibleData counts the same.
        new(AbramBorn, "Birth_Terah_1", T("terah-life"), T("abram-leaves-haran", -1)),
    ];

    private static SeptuagintTerm T(string reading, int sign = 1) => new(reading, sign);

    /// <summary>
    /// Reads every number the reckoning needs out of the verses, and says what could not be read.
    /// </summary>
    /// <param name="verse">The verse's text in the edition, by canonical book, chapter and verse.</param>
    public static (Dictionary<string, (int Value, SeptuagintReading Reading)> Values, List<string> Problems) Read(
        Func<int, int, int, string?> verse)
    {
        var values = new Dictionary<string, (int, SeptuagintReading)>(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var reading in Readings)
        {
            var text = verse(reading.Book, reading.Chapter, reading.Verse);
            var runs = text is null ? [] : GreekNumerals.In(text);
            if (reading.Run < runs.Count)
            {
                values[reading.Key] = (runs[reading.Run], reading);
            }
            else
            {
                problems.Add($"{reading.Reference} states no number {reading.Run + 1} for {reading.Key}: \"{text}\"");
            }
        }

        foreach (var father in Closing)
        {
            if (values.TryGetValue($"{father}-begets", out var begets)
                && values.TryGetValue($"{father}-after", out var after)
                && values.TryGetValue($"{father}-life", out var life)
                && begets.Item1 + after.Item1 != life.Item1)
            {
                problems.Add(
                    $"{father}: {begets.Item1} + {after.Item1} is not {life.Item1} — one of " +
                    $"{begets.Item2.Reference}, {after.Item2.Reference}, {life.Item2.Reference} is misread.");
            }
        }

        if (values.TryGetValue("noah-flood", out var flood)
            && values.TryGetValue("noah-after", out var afterFlood)
            && values.TryGetValue("noah-life", out var whole)
            && flood.Item1 + afterFlood.Item1 != whole.Item1)
        {
            problems.Add(
                $"noah: {flood.Item1} + {afterFlood.Item1} is not {whole.Item1} — one of Genesis 7:6, 9:28, 9:29 is misread.");
        }

        return (values, problems);
    }

    /// <summary>The year of every event the rules reach, with the sentence that shows how.</summary>
    /// <param name="names">What each event is called, for the sentence; the id where it has no name.</param>
    public static Dictionary<string, SeptuagintYear> Compute(
        IReadOnlyDictionary<string, (int Value, SeptuagintReading Reading)> values,
        Func<string, string> names)
    {
        var years = new Dictionary<string, SeptuagintYear>(StringComparer.Ordinal);

        foreach (var rule in Rules)
        {
            var year = rule.From is null ? 1 : years[rule.From].Year;
            var said = rule.From is null
                ? $"{names(rule.Event)} is year 1"
                : $"{names(rule.Event)} is {names(rule.From)} ({Number(year)})";
            var cited = new List<string>();

            foreach (var term in rule.Terms)
            {
                var (value, reading) = values[term.Reading];
                year += term.Sign * value;
                said += $" {(term.Sign < 0 ? "−" : "+")} {Number(value)} ({reading.Reference})";
                cited.Add(reading.Reference);
            }

            if (rule.Terms.Length > 0)
            {
                said += $" = {Number(year)}";
            }

            years[rule.Event] = new SeptuagintYear(year, said, cited.Count > 0 ? string.Join("; ", cited) : null);
        }

        return years;
    }

    /// <summary>
    /// For each event between the Exodus and the Temple, which of the two counts it belongs to.
    ///
    /// Read from the arithmetic BibleData wrote for it: the first year its sentence names is the
    /// event it counts from, and that event's side is its side. Where the sentence names no other
    /// year it counts from an event of the same year. An event this cannot settle is left out
    /// rather than put on a side by guess.
    /// </summary>
    /// <param name="events">The base reckoning's events: id, year, and its sentence of arithmetic.</param>
    public static Dictionary<string, SeptuagintSide> Sides(
        IReadOnlyList<(string Id, int Year, string? Calculation)> events,
        int exodus,
        int temple)
    {
        var sides = new Dictionary<string, SeptuagintSide>(StringComparer.Ordinal);
        foreach (var (id, year, _) in events)
        {
            if (year <= exodus)
            {
                sides[id] = SeptuagintSide.Exodus;
            }
            else if (year >= temple)
            {
                sides[id] = SeptuagintSide.Temple;
            }
        }

        var byYear = events.ToLookup(e => e.Year);
        var open = events.Where(e => !sides.ContainsKey(e.Id)).ToList();

        bool settled;
        do
        {
            settled = false;
            foreach (var (id, year, calculation) in open.Where(e => !sides.ContainsKey(e.Id)))
            {
                var counted = Years().Matches(calculation ?? string.Empty)
                    .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
                    .Where(named => named != year)
                    .DefaultIfEmpty(year)
                    .First();

                var candidates = byYear[counted]
                    .Where(e => e.Id != id && sides.ContainsKey(e.Id))
                    .Select(e => sides[e.Id])
                    .Distinct()
                    .ToList();

                if (candidates.Count == 1)
                {
                    sides[id] = candidates[0];
                    settled = true;
                }
            }
        }
        while (settled);

        return sides;
    }

    public static string Number(int value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\b\d{4}\b")]
    private static partial Regex Years();
}
