using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record NewTestamentOutcome(
    bool AlreadyLoaded,
    int Events,
    int Resolved,
    int Dates,
    int Periods,
    int Linked,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the New Testament events are already loaded"
            : $"{Events} events ({Resolved} of them dated by following the source's own predecessor " +
              $"chain), {Dates} dates, {Periods} periods and {Linked} links to a person, in {Elapsed}";
}

/// <summary>
/// The New Testament, which the chosen chronology does not have.
///
/// BibleData computes every date from the genealogies and reign lengths, and that method stops
/// where the genealogies stop: its last narrated event is the end of Artaxerxes' reign, 424 BCE.
/// One event in the whole file cites a New Testament verse. So the corpus had a chronology that
/// ended four centuries before the thing it exists to serve.
///
/// Theographic fills it — 153 dated events from the espousal of Mary to Paul in Rome, each with
/// its verses, its participants and its place. DOC-0099 records why it was not chosen for the Old
/// Testament: its dates there are asserted rather than computed, contradict its own tables, and
/// give Joshua a 97-year life against the 110 the text states. None of that argument reaches the
/// New Testament, where it is the only candidate and where its dates rest on Luke's synchronisms
/// rather than on a genealogical sum.
///
/// **CC BY-SA 4.0**, which is not the licence of the rest of the encyclopedia. These rows are a
/// separately licensed component of a collection, not a remix — every event carries its source, so
/// what came from where can be answered per row rather than per corpus.
/// </summary>
internal sealed class TheographicEventLoader(AppDbContext db, ILogger<TheographicEventLoader> logger)
{
    private const string Source = "Theographic Bible Data, github.com/robertrouse/theographic-bible-metadata, CC BY-SA 4.0";

    /// <summary>
    /// The last year BibleData narrates, in astronomical numbering — the end of Artaxerxes' reign.
    /// Everything from here on is Theographic's; everything before it would double the events the
    /// other dataset already dates, from a source whose Old Testament dates are the worse of the
    /// two.
    /// </summary>
    private const int WhereTheOtherDatasetStops = -423;

    /// <summary>
    /// Theographic writes its years astronomically: its creation is <c>-4003</c>, meaning 4004 BCE.
    /// So a year <c>y</c> sits at <c>zero + y</c> on any reckoning whose <c>zero</c> is its own year
    /// 1 BCE — <c>-3</c> is 4 BCE and <c>30</c> is AD 30, with no year zero to trip over.
    /// </summary>
    private static int AnnoMundi(int zero, int astronomical) => zero + astronomical;

    public async Task<NewTestamentOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var file = Path.Combine(folder, "events.csv");
        if (!File.Exists(file))
        {
            logger.LogWarning("No New Testament events: {File} is not there.", file);
            return new NewTestamentOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (await db.Events.AnyAsync(e => e.Source.StartsWith("Theographic"), cancellationToken))
        {
            return new NewTestamentOutcome(true, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var chronologies = await db.Chronologies.ToListAsync(cancellationToken);
        if (chronologies.Count == 0)
        {
            logger.LogWarning("No New Testament events: the chronologies are not loaded yet.");
            return new NewTestamentOutcome(true, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var rows = Csv.Read(file).ToList();
        var byTitle = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            byTitle.TryAdd(row["title"].Trim(), row);
        }

        var places = Places(folder);
        var people = People(folder);
        var ours = await Ours(cancellationToken);
        var bySlug = await db.Entities.ToDictionaryAsync(e => e.Slug, e => e.Id, cancellationToken);
        var taken = await db.Events.Select(e => e.Slug).ToHashSetAsync(cancellationToken);

        var events = new List<Event>();
        var years = new Dictionary<Event, (int Astronomical, string Citation)>();
        var resolved = 0;
        var linked = 0;

        foreach (var row in rows)
        {
            var stated = Stated(row["startDate"]);
            var year = YearOf(row, byTitle);
            if (year is not { } astronomical || astronomical <= WhereTheOtherDatasetStops)
            {
                continue;
            }

            if (stated is null)
            {
                resolved++;
            }

            var title = row["title"].Trim();
            var slug = Unique(Slugs.Of(title), taken);
            var entity = Person(row["participants"], people, ours, bySlug);
            if (entity is not null)
            {
                linked++;
            }

            var reference = Reference(row["verses"]);
            var made = new Event
            {
                Slug = slug,
                Name = title,
                Description = Blank(row["notes"]),
                Kind = Kind(title),
                EntityId = entity,
                YearFromCreation = AnnoMundi(3961, astronomical),
                BceYear = astronomical <= 0 ? 1 - astronomical : null,
                Location = Place(row["locations"], places),
                CanonicalBook = reference?.Book,
                CanonicalChapter = reference?.Chapter,
                CanonicalVerse = reference?.Verse,
                Notes = stated is { } date ? BeyondTheAxis(date) : Chained,
                Source = Source,
            };

            events.Add(made);
            years[made] = (astronomical, stated is { } given
                ? $"Theographic dates this {Written(given)}"
                : $"Theographic states no date for this event; {Written((astronomical, 0, 0))} follows " +
                  "from the predecessor chain it records.");
        }

        db.Events.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);

        var dates = new List<EventDate>(events.Count * chronologies.Count);
        foreach (var (made, dated) in years)
        {
            foreach (var chronology in chronologies)
            {
                dates.Add(new EventDate
                {
                    EventId = made.Id,
                    ChronologyId = chronology.Id,
                    Year = AnnoMundi(chronology.LastYearBeforeTheCommonEra, dated.Astronomical),
                    Citation = dated.Citation,
                    Notes = "Anchored to the common era rather than to the creation, so every " +
                            "reckoning places it in the same historical year and they differ only " +
                            "in what they call that year counting from the creation.",
                });
            }
        }

        db.EventDates.AddRange(dates);

        var anchors = events.ToDictionary(e => e.Slug, e => e, StringComparer.Ordinal);
        if (await db.Events.FirstOrDefaultAsync(e => e.Slug == "endartaxerxes1reign", cancellationToken) is { } last)
        {
            anchors[last.Slug] = last;
        }

        var periods = Periods.ForTheNewTestament(anchors, Journeys(rows, events), Source);
        db.Periods.AddRange(periods);
        await db.SaveChangesAsync(cancellationToken);

        var outcome = new NewTestamentOutcome(
            false, events.Count, resolved, dates.Count, periods.Count, linked, started.Elapsed);
        logger.LogInformation("Loaded the New Testament chronology: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The one join the source's own graph is missing.
    ///
    /// Where Acts begins, <em>The Holy Spirit is promised</em> (Acts 1:4) names no predecessor, so
    /// a chain reaching it stops there and the four events after it — the ascension, Matthias,
    /// Pentecost and Peter's sermon — would have nothing to be dated from. The gospel chain ends
    /// at the resurrection and the Acts chain starts the next verse after it; joining them is what
    /// the source's own model says, and it is the only edge added here.
    /// </summary>
    private static readonly Dictionary<string, string> Bridges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["The Holy Spirit is promised"] = "Resurrection and Ascension",
    };

    /// <summary>
    /// The journeys, from the groups the source marks its events with.
    ///
    /// Twenty-one of Paul's events carry a <c>partOf</c> naming one of the three missionary
    /// journeys, which is the source saying these belong together — the only grouping it offers,
    /// and the one a reader of Acts is already looking for.
    /// </summary>
    private static List<(string Name, string From, string To)> Journeys(
        IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<Event> events)
    {
        var bySlug = events.ToDictionary(e => e.Slug, e => e, StringComparer.Ordinal);
        var journeys = new List<(string, string, string)>();

        var groups = rows
            .Where(row => Unquote(row["partOf"]).Length > 0)
            .GroupBy(row => Unquote(row["partOf"]), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var members = group
                .Select(row => bySlug.GetValueOrDefault(Slugs.Of(row["title"].Trim())))
                .OfType<Event>()
                .Where(e => e.YearFromCreation is not null)
                .OrderBy(e => e.YearFromCreation)
                .ToList();

            if (members.Count >= 2)
            {
                journeys.Add((group.Key, members[0].Slug, members[^1].Slug));
            }
        }

        return journeys;
    }

    /// <summary>
    /// The year the corpus gives a row: what the source states, and only where it states nothing,
    /// what its predecessor chain works out to.
    /// </summary>
    internal static int? YearOf(
        Dictionary<string, string> row,
        Dictionary<string, Dictionary<string, string>> byTitle) =>
        Stated(row["startDate"])?.Year ?? Follow(row, byTitle, []);

    /// <summary>
    /// A date for an event the source leaves undated, from the chain it already records: walk to
    /// the nearest dated ancestor and add each step's duration.
    ///
    /// The fallback, not the rule. Every row in the file as it stands states a date, so this runs
    /// on nothing — but the source is a live repository, its events name predecessors, and a row
    /// that arrives with a predecessor and no date is datable rather than undatable.
    ///
    /// Worked in days and floored to a year at the end, because the steps are mostly a day or a
    /// month and summing them as whole years would drift by one per step.
    /// </summary>
    private static int? Follow(
        Dictionary<string, string> row,
        Dictionary<string, Dictionary<string, string>> byTitle,
        HashSet<string> visiting)
    {
        var days = Days(row, byTitle, visiting);
        return days is { } total ? (int)Math.Floor(total / 365.0) : null;
    }

    private static double? Days(
        Dictionary<string, string> row,
        Dictionary<string, Dictionary<string, string>> byTitle,
        HashSet<string> visiting)
    {
        if (Stated(row["startDate"]) is { } stated)
        {
            return stated.Year * 365.0 + Into(stated);
        }

        var title = row["title"].Trim();
        // A chain that comes back to where it started has no answer, and following it has no end.
        if (!visiting.Add(title))
        {
            return null;
        }

        var predecessor = Unquote(row["predecessor"]);
        if (predecessor.Length == 0 && Bridges.TryGetValue(title, out var joined))
        {
            predecessor = joined;
        }

        if (predecessor.Length == 0 || !byTitle.TryGetValue(predecessor, out var before))
        {
            return null;
        }

        var start = Days(before, byTitle, visiting);
        return start is { } from ? from + Duration(before["duration"]) : null;
    }

    /// <summary>
    /// A duration in days. The source writes <c>1D</c>, <c>3M</c>, <c>2.5Y</c>, <c>1W</c> — a
    /// number and a unit, and the number is not always whole.
    /// </summary>
    private static double Duration(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length < 2)
        {
            return 0;
        }

        if (!double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var many))
        {
            return 0;
        }

        return char.ToUpperInvariant(text[^1]) switch
        {
            'D' => many,
            'W' => many * 7,
            'M' => many * 30,
            'Y' => many * 365,
            _ => 0,
        };
    }

    /// <summary>
    /// The date the source states, in the two shapes it writes.
    ///
    /// Most rows are a bare astronomical year — <c>-4003</c> for 4004 BCE, <c>30</c> for AD 30 —
    /// and fifty are a full ISO date, <c>0030-04-04</c>, with one of them writing a single-digit
    /// day. Same reckoning either way; only the second carries a month, and <c>Month</c> is zero
    /// where the source gives none.
    /// </summary>
    internal static (int Year, int Month, int Day)? Stated(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var negative = text[0] == '-';
        var parts = (negative ? text[1..] : text).Split('-');
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }

        if (negative)
        {
            year = -year;
        }

        if (parts.Length == 1)
        {
            return (year, 0, 0);
        }

        if (parts.Length != 3
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || month is < 1 or > 12
            || day is < 1 or > 31)
        {
            return null;
        }

        return (year, month, day);
    }

    private static readonly int[] DaysBeforeMonth = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];

    /// <summary>
    /// Days from the start of the stated year, so a chain hung off a day-precise date does not
    /// gain or lose a year at its first step.
    /// </summary>
    private static int Into((int Year, int Month, int Day) date) =>
        date.Month == 0 ? 0 : DaysBeforeMonth[date.Month - 1] + date.Day - 1;

    private const string Chained =
        "Theographic leaves this event undated. The year here is its own: followed up the " +
        "predecessor chain it records until a dated event, adding each step's duration.";

    /// <summary>
    /// What the source states and the axis cannot hold.
    ///
    /// Twenty-six events of Holy Week and the opening of Acts share one year, and the day is the
    /// only thing that orders them — so where the source gives one it is written down, even though
    /// nothing draws it yet.
    /// </summary>
    private static string? BeyondTheAxis((int Year, int Month, int Day) date) =>
        date.Month == 0
            ? null
            : $"Theographic dates this {Written(date)}. The axis is a year, so the day is recorded " +
              "here rather than drawn.";

    /// <summary>The date as a reader says it — <c>4 April AD 30</c>, <c>4004 BCE</c>.</summary>
    private static string Written((int Year, int Month, int Day) date)
    {
        var era = date.Year <= 0 ? $"{1 - date.Year} BCE" : $"AD {date.Year}";
        return date.Month == 0
            ? era
            : $"{date.Day} {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(date.Month)} {era}";
    }

    /// <summary>
    /// What sort of event it is, inferred from the title.
    ///
    /// Theographic does not classify its events — the field the other dataset has does not exist
    /// here — and a timeline where every New Testament mark is the same grey says less than one
    /// where most are right. So the obvious words are read and everything else falls to the
    /// catch-all the other dataset uses for the same purpose. This is inference, and the only
    /// thing resting on it is a colour.
    /// </summary>
    private static string Kind(string title)
    {
        var text = title.ToLowerInvariant();
        return text switch
        {
            _ when text.StartsWith("birth of", StringComparison.Ordinal) || text.Contains("born") => "Birth",
            _ when text.Contains("crucifixion") || text.Contains("death of") || text.Contains("suicide")
                || text.Contains("beheaded") || text.Contains("stoned") || text.Contains("dies") => "Death",
            _ when text.Contains("journey") || text.Contains("voyage") || text.Contains("mission to")
                || text.Contains("flee") || text.Contains("goes to") || text.Contains("arrives")
                || text.Contains("leaves") || text.Contains("sails") || text.Contains("return") => "Travel",
            _ when text.Contains("parable") || text.Contains("discourse") || text.Contains("sermon")
                || text.Contains("teach") || text.Contains("preach") || text.Contains("speaks")
                || text.Contains("addresses") => "Message",
            _ when text.Contains("dream") || text.Contains("vision") || text.Contains("transfigu")
                || text.Contains("angel") => "Vision",
            _ when text.Contains("riot") || text.Contains("uproar") || text.Contains("conspir")
                || text.Contains("trial") || text.Contains("arrest") || text.Contains("imprison")
                || text.Contains("beaten") || text.Contains("plot") => "Battle",
            _ when text.Contains("marri") || text.Contains("espousal") || text.Contains("wedding") => "Marriage",
            _ when text.Contains("council") || text.Contains("meets") || text.Contains("visit") => "Meeting",
            _ => "Unique",
        };
    }

    /// <summary>
    /// The principal figures, reconciled by hand.
    ///
    /// The two datasets have separate identifier spaces and disagree about names, so a name join
    /// reaches almost none of the people who matter: Theographic writes *Jesus Christ* where we
    /// write *Jesus*, *Paul* where BibleData writes *Saul*, *Simon Peter* where it writes *Simon*,
    /// and *Barnabas* for the man it calls *Joseph, a Levite of Cyprian birth*. Every pair below
    /// was checked against both sides' own description; the rest are left to the name rule, and
    /// what that cannot settle is left unlinked rather than linked wrongly.
    /// </summary>
    private static readonly Dictionary<string, string> Reconciled = new(StringComparer.Ordinal)
    {
        ["jesus_905"] = "jesus",              // Jesus Christ — ours is "Jesus, of Nazareth"
        ["god_1324"] = "yhvh",
        ["paul_2479"] = "saul-2",             // BibleData files him under Saul (ACT 7:58)
        ["peter_2745"] = "simon",             // "Peter, brother of Andrew" (MAT 4:18)
        ["barnabas_1722"] = "joseph-12",      // "a Levite of Cyprian birth" (ACT 4:36)
        ["mark_1679"] = "john-5",             // "also called Mark" (ACT 12:12)
        ["john_1676"] = "john",               // the Baptist (MAT 3:1)
        ["john_1677"] = "john-2",             // the apostle, son of Zebedee (MAT 4:21)
        ["james_717"] = "james",              // son of Zebedee (MAT 4:21)
        ["mary_1938"] = "mary",               // the mother of Jesus (MAT 1:16)
        ["mary_1939"] = "mary-3",             // sister of Martha (LUK 10:39), who anoints him
        ["joseph_1715"] = "joseph-6",         // Mary's husband (MAT 1:16)
        ["timotheus_2863"] = "timothy",
        ["philip_2344"] = "philip",           // the apostle (MAT 10:3)
        ["philip_2347"] = "philip-3",         // the evangelist (ACT 6:5)
        ["judas_1760"] = "judas",             // Iscariot
        ["lazarus_1812"] = "lazarus-2",       // brother of Mary and Martha (JHN 11:1)
        ["ananias_259"] = "ananias-2",        // the disciple at Damascus (ACT 9:10)
        ["ananias_260"] = "ananias-3",        // the high priest (ACT 23:2)
        ["ananias_258"] = "ananias",          // husband of Sapphira (ACT 5:1)
        ["gamaliel_1277"] = "gamaliel-2",     // the Pharisee of the Council (ACT 5:34)
        ["elisabeth_1152"] = "elizabeth",
        ["zacharias_2971"] = "zacharias",     // the priest, father of John (LUK 1:5)
        ["herod_1504"] = "herod",             // the king (MAT 2:1)
        ["herod_1505"] = "herod-2",           // Antipas, the tetrarch (MAT 14:1)
        ["herod_1506"] = "herod-3",           // Agrippa I, king of Judea (ACT 12:1)
    };

    /// <summary>
    /// The one person the event is about, where that can be said without guessing.
    ///
    /// The two datasets have separate identifier spaces, so the join has to be by name — and a name
    /// join is exactly where PRB-0034 went wrong in the other direction. So it is made only where
    /// the name is unambiguous on **both** sides: Theographic has two Marys in these events and we
    /// have several, and no rule over names can tell which anointed Jesus. Those are left unlinked
    /// rather than linked wrongly.
    /// </summary>
    private static int? Person(
        string? participants,
        IReadOnlyDictionary<string, string?> people,
        IReadOnlyDictionary<string, int> ours,
        IReadOnlyDictionary<string, int> bySlug)
    {
        var first = (participants ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();

        if (first is null)
        {
            return null;
        }

        if (Reconciled.TryGetValue(first, out var slug))
        {
            return bySlug.TryGetValue(slug, out var reconciled) ? reconciled : null;
        }

        return people.TryGetValue(first, out var name) && name is not null && ours.TryGetValue(name, out var id)
            ? id
            : null;
    }

    /// <summary>Theographic's people, by lookup id, keeping only names unique within its own file.</summary>
    private static Dictionary<string, string?> People(string folder)
    {
        var file = Path.Combine(folder, "people.csv");
        if (!File.Exists(file))
        {
            return [];
        }

        var rows = Csv.Read(file)
            .Select(row => (Lookup: row["personLookup"].Trim(), Name: row["displayTitle"].Trim()))
            .Where(row => row.Lookup.Length > 0 && row.Name.Length > 0)
            .ToList();

        var many = rows.GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var people = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (lookup, name) in rows)
        {
            people[lookup] = many.Contains(name) ? null : name;
        }

        return people;
    }

    private static Dictionary<string, string> Places(string folder)
    {
        var file = Path.Combine(folder, "places.csv");
        if (!File.Exists(file))
        {
            return [];
        }

        var places = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Csv.Read(file))
        {
            var lookup = row["placeLookup"].Trim();
            var name = row["displayTitle"].Trim();
            if (lookup.Length > 0 && name.Length > 0)
            {
                places.TryAdd(lookup, name);
            }
        }

        return places;
    }

    private static string? Place(string? locations, IReadOnlyDictionary<string, string> places)
    {
        var first = (locations ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is not null && places.TryGetValue(first.Trim(), out var name) ? name : null;
    }

    /// <summary>Our own people, by name, keeping only the names that name exactly one of them.</summary>
    private async Task<Dictionary<string, int>> Ours(CancellationToken cancellationToken)
    {
        var rows = await db.Entities
            .Where(e => e.Kind == Database.Entities.Enums.EntityKind.Person)
            .Select(e => new { e.Id, e.Name })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The first verse the event rests on. Theographic writes them as <c>Matt.1.18</c>, in the
    /// order it considers the account to run, so the first is the one to show.
    /// </summary>
    private static (int Book, int Chapter, int Verse)? Reference(string? verses)
    {
        var first = (verses ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var parts = (first ?? string.Empty).Split('.');
        if (parts.Length != 3
            || BibleBookAbbreviation.GetAbbreviation(parts[0].Trim()) is not { } book
            || !int.TryParse(parts[1], out var chapter)
            || !int.TryParse(parts[2], out var verse))
        {
            return null;
        }

        return (book.Ordinal, chapter, verse);
    }

    /// <summary>The source quotes a predecessor whose own title has a comma in it.</summary>
    private static string Unquote(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1].Trim() : text;
    }

    private static string Unique(string slug, HashSet<string> taken)
    {
        if (taken.Add(slug))
        {
            return slug;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{slug}-{n}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
