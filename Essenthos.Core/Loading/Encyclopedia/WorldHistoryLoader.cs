using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record WorldOutcome(
    bool AlreadyLoaded,
    int Events,
    int Beginnings,
    int Periods,
    int Dates,
    int AlreadyInScripture,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "world history is already loaded"
            : $"{Events} events and {Periods} spans of world history, {Beginnings} of the events " +
              $"dated by an inception rather than by something happening, with {Dates} dates and " +
              $"{AlreadyInScripture} rows left to the scripture layer, in {Elapsed}";
}

/// <summary>
/// What else was happening.
///
/// A chronology of one people, drawn alone, quietly implies that nothing else was going on. Putting
/// world history on the same axis is what makes the corpus answerable — and the disagreements are
/// the point rather than an embarrassment: the Great Pyramid is finished around 2560 BCE and the
/// Masoretic reckoning puts the Flood at 2304 BCE, so on that reckoning a surviving building
/// predates the Flood, and on the Septuagint's longer genealogies it does not. Both are drawn.
/// Nothing here is reconciled, hidden, or nudged to fit. The two exceptions are named in code and
/// neither is about a disagreement: an event the scripture layer already carries is not drawn a
/// second time, and a date the source itself has mistyped is not drawn at all.
///
/// **Wikidata, CC0.** Chosen over the encyclopedias with better prose because they are share-alike
/// or non-commercial, and a condition on the world layer would reach the corpus beside it. Every
/// row keeps its item identifier, so any date here can be checked at its source.
///
/// See <c>Resources/WorldHistory/README.md</c> for the queries, when they were run, and why each
/// filter is there.
/// </summary>
internal sealed class WorldHistoryLoader(AppDbContext db, ILogger<WorldHistoryLoader> logger)
{
    private const string Source = "Wikidata, query.wikidata.org, CC0";

    /// <summary>
    /// The year the default reckoning calls 1 BCE. Every reckoning gets its own answer in
    /// <see cref="EventDate"/>; the event row itself still has to carry one, and this is whose.
    /// </summary>
    private const int DefaultReckoningZero = 3961;

    /// <summary>
    /// Wikidata has an item for the year 500 BC, and that item has a point in time. Two thirds of
    /// the first query's rows are these. They are not events.
    /// </summary>
    private static readonly HashSet<string> NotEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "year", "year BC", "calendar year", "century", "decade", "millennium",
        "Wikimedia list article", "Wikimedia category", "Wikimedia disambiguation page",
    };

    /// <summary>
    /// What each sort of thing is called on this timeline, so that a hundred Wikidata classes
    /// become a handful a reader can hold in their head.
    ///
    /// Two families, and which one a row falls into is a difference the reader should never have to
    /// guess at. <b>Something happened</b> — a battle, a treaty, an eruption — is a moment.
    /// <b>Something began to exist</b> — a city, a dynasty, a stele, a play — is dated by its
    /// inception, and the Merneptah Stele did not happen in 1200 BCE, it was cut then. Two thirds
    /// of this layer is the second kind, so one colour over both would say that a battle and a
    /// death mask are the same sort of fact.
    ///
    /// Matched on Wikidata class in order, first match winning, which is why <c>death mask</c> has
    /// to sit above <c>death</c>.
    /// </summary>
    private static readonly (string[] Contains, string Kind)[] Kinds =
    [
        // Something happened.
        (["naval battle", "battle", "siege", "last stand", "ambush", "military campaign", "war",
            "rebellion", "revolt", "coup", "conspiracy", "shipwreck"], "Battle"),
        (["treaty", "peace"], "Treaty"),
        (["synod", "council", "census", "law", "legal", "trial", "edict", "decree", "oration",
            "statute", "reform", "court"], "Message"),
        (["eruption", "earthquake", "flood", "famine", "plague", "epidemic", "eclipse", "impact"], "Destruction"),
        (["wedding", "marriage"], "Marriage"),
        (["festival", "games"], "Meeting"),

        // Something began to exist. Objects before events: a death mask is not a death.
        (["mask", "statue", "sculpture", "stele", "mosaic", "artefact", "artifact", "jewel"], "Artefact"),
        (["work", "text", "poem", "epic", "treatise", "writing system", "alphabet", "script",
            "syllabary", "abjad", "manuscript", "tablet"], "Work"),
        (["temple", "pyramid", "wall", "tomb", "monument", "building", "ruins", "tell", "site",
            "cave", "aqueduct", "theatre", "palace", "fortress", "church"], "Construction"),
        (["city", "polis", "settlement", "town", "village", "municipality", "comune", "commune",
            "colony", "country", "state", "kingdom", "empire", "dynasty", "province", "realm",
            "caliphate", "koinon", "legion", "school", "museum", "religion", "office",
            "organization", "position", "title"], "Founding"),

        // Neither: a stretch of time named as though it were a thing.
        (["culture", "period", "age", "periodization", "style", "horizon"], "Unique"),
    ];

    /// <summary>
    /// The classes whose span is a stretch of rule. Only <see cref="Spans"/> asks: a dynasty with a
    /// start and an end is a reign, and the same word on a row with one date is the day it began.
    /// </summary>
    private static readonly string[] Reigns =
        ["dynasty", "empire", "kingdom", "historical country", "state", "province", "caliphate"];

    /// <summary>
    /// Where the two layers are about the same event.
    ///
    /// Wikidata and Theographic both carry the passion, the circumcision, the return from Egypt and
    /// the council at Jerusalem, and they date them three to five years apart. As two rows that is
    /// the crucifixion drawn twice with nothing saying it is one event; as one row with a year
    /// picked it is a reading the corpus has quietly made on the reader's behalf. So the scripture
    /// row stands — that layer is where these belong — and Wikidata's year is written onto it as
    /// the disagreement it is.
    ///
    /// Keyed on the item identifier, never on the name: a name join between two datasets is the
    /// mistake this corpus has already made once. Each pair below was read on both sides.
    /// </summary>
    internal static readonly Dictionary<string, string> AlreadyInScripture = new(StringComparer.Ordinal)
    {
        ["http://www.wikidata.org/entity/Q51636"] = "crucifixionandburial",
        ["http://www.wikidata.org/entity/Q51624"] = "resurrectionandascension",
        ["http://www.wikidata.org/entity/Q13510036"] = "jesuscircumsized",
        ["http://www.wikidata.org/entity/Q619950"] = "jerusalemcouncil",
        ["http://www.wikidata.org/entity/Q7317265"] = "josephandmaryreturnfromegypt",
    };

    /// <summary>
    /// Dates the source has mistyped, dropped by item with the reason beside them.
    ///
    /// Not a plausibility rule. Anything that dropped what looks too old or too round would drop the
    /// founding of Rome and the Great Pyramid, which are what this layer is for. These are single
    /// rows read at Wikidata and found to disagree with the item's own article by an order of
    /// magnitude — a century typed into a year field.
    /// </summary>
    internal static readonly Dictionary<string, string> Miskeyed = new(StringComparer.Ordinal)
    {
        ["http://www.wikidata.org/entity/Q503387"] =
            "Rock-hewn Churches of Ivanovo, inception 0013-01-01, which is the 13th century written " +
            "as a year: the churches are 12th to 14th century, and the reign of Augustus is not a " +
            "plausible date for a Bulgarian rock church.",
    };

    public async Task<WorldOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        if (!Directory.Exists(folder))
        {
            logger.LogWarning("No world history: {Folder} is not there.", folder);
            return new WorldOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (await db.Events.AnyAsync(e => e.Realm == Realms.World, cancellationToken))
        {
            return new WorldOutcome(true, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var chronologies = await db.Chronologies.ToListAsync(cancellationToken);
        if (chronologies.Count == 0)
        {
            logger.LogWarning("No world history: the chronologies are not loaded yet.");
            return new WorldOutcome(true, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var counterparts = await Counterparts(cancellationToken);
        var taken = await db.Events.Select(e => e.Slug).ToHashSetAsync(cancellationToken);
        var events = new List<Event>();
        var years = new Dictionary<Event, int>();
        var beginnings = 0;
        var deferred = 0;

        foreach (var file in new[] { "wikidata-events.csv", "wikidata-inception.csv" })
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path))
            {
                continue;
            }

            var inception = file.Contains("inception", StringComparison.Ordinal);

            foreach (var item in Items(path, "time"))
            {
                if (Year(item.Time) is not { } year)
                {
                    continue;
                }

                if (AlreadyInScripture.TryGetValue(item.Uri, out var slug))
                {
                    if (counterparts.TryGetValue(slug, out var scripture))
                    {
                        scripture.Notes = Disagreeing(scripture, item, year);
                        deferred++;
                    }

                    continue;
                }

                var made = new Event
                {
                    Slug = Unique(Slugs.Of(item.Label), taken),
                    Name = item.Label,
                    Kind = Kind(item.Type, inception),
                    Description = Described(item.Type, item.Where, inception),
                    Realm = Realms.World,
                    Region = item.Where,
                    Uri = item.Uri,
                    YearFromCreation = DefaultReckoningZero + year,
                    BceYear = year <= 0 ? 1 - year : null,
                    Source = Source,
                };

                events.Add(made);
                years[made] = year;
                if (inception)
                {
                    beginnings++;
                }
            }
        }

        db.Events.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);

        var dates = new List<EventDate>(events.Count * chronologies.Count);
        foreach (var (made, year) in years)
        {
            foreach (var chronology in chronologies)
            {
                dates.Add(new EventDate
                {
                    EventId = made.Id,
                    ChronologyId = chronology.Id,
                    // Anchored to the common era, so every reckoning agrees on the historical year
                    // and disagrees only on how far it is from the creation. That difference is
                    // exactly what moves a pyramid to one side of the Flood or the other.
                    Year = chronology.LastYearBeforeTheCommonEra + year,
                    Citation = Era(year),
                });
            }
        }

        db.EventDates.AddRange(dates);

        var periods = Spans(folder);
        db.Periods.AddRange(periods);
        await db.SaveChangesAsync(cancellationToken);

        var outcome = new WorldOutcome(
            false, events.Count, beginnings, periods.Count, dates.Count, deferred, started.Elapsed);
        logger.LogInformation("Loaded world history: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The spans — wars, dynasties, empires, archaeological ages.
    ///
    /// Drawn on the second band rather than the era row: they are world history's periods, and the
    /// eras belong to the text this corpus is about. A reader comparing the two wants the Bronze
    /// Age beside the patriarchs, not instead of them.
    /// </summary>
    private static List<Period> Spans(string folder)
    {
        var path = Path.Combine(folder, "wikidata-spans.csv");
        if (!File.Exists(path))
        {
            return [];
        }

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        var periods = new List<Period>();

        foreach (var item in Items(path, "start", "end"))
        {
            if (Year(item.Time) is not { } from || Year(item.Until) is not { } to || to < from)
            {
                continue;
            }

            periods.Add(new Period
            {
                Slug = Unique($"world-{Slugs.Of(item.Label)}", slugs),
                Name = item.Label,
                Kind = Reigns.Any(word => item.Type.Contains(word, StringComparison.OrdinalIgnoreCase))
                    ? "reign"
                    : "span",
                // The second band. Level 0 is this corpus's own eras and stays that way.
                Level = 1,
                StartYear = DefaultReckoningZero + from,
                EndYear = DefaultReckoningZero + to,
                Realm = Realms.World,
                Region = item.Where,
                Uri = item.Uri,
                Notes = item.Type,
                Source = Source,
            });
        }

        return periods;
    }

    private sealed record Item(string Uri, string Label, string Time, string Until, string Type, string? Where);

    /// <summary>
    /// One row per item, from a result set that has several.
    ///
    /// The query asks for the type and the country, and an item with three types in two countries
    /// comes back six times. The first row wins: they are the same item, and which of its three
    /// types is named first is not worth a rule.
    /// </summary>
    private static IEnumerable<Item> Items(string path, string timeColumn, string? untilColumn = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var invented = Invented(path);

        foreach (var row in Csv.Read(path))
        {
            var uri = row.GetValueOrDefault("e", string.Empty);
            var label = row.GetValueOrDefault("eLabel", string.Empty).Trim();
            var type = row.GetValueOrDefault("kindLabel", string.Empty).Trim();

            // An item with no English label comes back as its own identifier, which is not a name.
            if (uri.Length == 0 || label.Length == 0 || label.StartsWith("Q", StringComparison.Ordinal)
                && label.Skip(1).All(char.IsDigit))
            {
                continue;
            }

            if (NotEvents.Contains(type) || invented.Contains(uri) || Miskeyed.ContainsKey(uri)
                || !seen.Add(uri))
            {
                continue;
            }

            yield return new Item(
                uri,
                label,
                row.GetValueOrDefault(timeColumn, string.Empty),
                untilColumn is null ? string.Empty : row.GetValueOrDefault(untilColumn, string.Empty),
                type,
                Blank(row.GetValueOrDefault("whereLabel", string.Empty)));
        }
    }

    /// <summary>
    /// Everything Wikidata marks as made up, by item rather than by row.
    ///
    /// The Galactic Republic is founded in 1032 BCE and the Rebel Alliance in 19 BCE, and both have
    /// enough Wikipedias to clear the notability floor. Row by row this cannot be caught: the
    /// Galactic Empire's first type is *galactic empire*, and only its second says *fictional
    /// government body*. So the file is read once for the identifiers, and any item that is called
    /// fictional anywhere is dropped everywhere.
    /// </summary>
    private static HashSet<string> Invented(string path)
    {
        var invented = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Csv.Read(path))
        {
            if (row.GetValueOrDefault("kindLabel", string.Empty)
                .Contains("fictional", StringComparison.OrdinalIgnoreCase))
            {
                invented.Add(row.GetValueOrDefault("e", string.Empty));
            }
        }

        return invented;
    }

    /// <summary>
    /// The year out of a Wikidata timestamp.
    ///
    /// **Astronomical, with a year zero.** Wikidata's own data model has no year zero, but the RDF
    /// these queries return is XSD <c>dateTime</c>, which does — so Marathon comes back as
    /// <c>-0489</c> and is 490 BCE, and the Great Pyramid as <c>-2559</c> for 2560 BCE. Assuming
    /// the internal convention instead put every world event a year late, which is invisible on a
    /// six-thousand-year axis and wrong in every citation. Checked against Marathon, Thermopylae,
    /// Gaugamela and Actium.
    /// </summary>
    internal static int? Year(string? timestamp)
    {
        var text = (timestamp ?? string.Empty).Trim();
        if (text.Length < 5)
        {
            return null;
        }

        var negative = text[0] == '-';
        var digits = negative ? text[1..].Split('-')[0] : text.Split('-')[0];
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }

        return negative ? -year : year;
    }

    /// <summary>
    /// The kind, and where the class is one nobody has mapped, the family it came from — so an
    /// inception nobody has classified still reads as a thing that began rather than as a moment.
    /// </summary>
    internal static string Kind(string type, bool inception)
    {
        var text = type.ToLowerInvariant();
        foreach (var (contains, kind) in Kinds)
        {
            if (contains.Any(word => text.Contains(word, StringComparison.Ordinal)))
            {
                return kind;
            }
        }

        return inception ? "Inception" : "Unique";
    }

    /// <summary>
    /// The scripture rows this loader has something to say about, read back by slug. Five of them,
    /// so the whole set is fetched rather than queried one at a time.
    /// </summary>
    private async Task<Dictionary<string, Event>> Counterparts(CancellationToken cancellationToken)
    {
        var slugs = AlreadyInScripture.Values.ToList();
        return await db.Events
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, cancellationToken);
    }

    /// <summary>
    /// Two datasets on one event, on the row that stays. Written out rather than resolved: the
    /// reader is told both years and which dataset holds each, and is left to weigh them.
    /// </summary>
    private static string Disagreeing(Event scripture, Item item, int year)
    {
        var held = (scripture.YearFromCreation ?? DefaultReckoningZero) - DefaultReckoningZero;
        var said =
            $"Wikidata has this event as \"{item.Label}\" at {Era(year)} ({item.Uri}), where this " +
            $"row holds {Era(held)}. One event under two datasets, {Math.Abs(year - held)} years " +
            "apart — so world history draws no second mark for it and the disagreement is here.";

        return string.IsNullOrWhiteSpace(scripture.Notes) ? said : $"{scripture.Notes} {said}";
    }

    private static string Era(int year) => year <= 0 ? $"{1 - year} BCE" : $"AD {year}";

    /// <summary>
    /// What the row says about itself, in a sentence, because the source sends no prose at all.
    /// Better an honest label than an empty description that reads as missing data.
    /// </summary>
    private static string Described(string type, string? where, bool inception)
    {
        var what = inception
            ? $"{Capitalised(type)}, dated by its inception"
            : Capitalised(type);

        return where is null ? $"{what}. From Wikidata." : $"{what}, in {where}. From Wikidata.";
    }

    private static string Capitalised(string value) =>
        value.Length == 0 ? "Event" : char.ToUpperInvariant(value[0]) + value[1..];

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
