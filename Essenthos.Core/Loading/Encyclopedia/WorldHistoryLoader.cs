using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record WorldOutcome(bool AlreadyLoaded, int Events, int Periods, int Dates, TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "world history is already loaded"
            : $"{Events} events and {Periods} spans of world history, with {Dates} dates, in {Elapsed}";
}

/// <summary>
/// What else was happening.
///
/// A chronology of one people, drawn alone, quietly implies that nothing else was going on. Putting
/// world history on the same axis is what makes the corpus answerable — and the disagreements are
/// the point rather than an embarrassment: the Great Pyramid is finished around 2560 BCE and the
/// Masoretic reckoning puts the Flood at 2304 BCE, so on that reckoning a surviving building
/// predates the Flood, and on the Septuagint's longer genealogies it does not. Both are drawn.
/// Nothing here is reconciled, hidden, or nudged to fit.
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
    /// Wikidata has an item for the year 500 BC, and that item has a point in time. Two thirds of
    /// the first query's rows are these. They are not events.
    /// </summary>
    private static readonly HashSet<string> NotEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "year", "year BC", "calendar year", "century", "decade", "millennium",
        "Wikimedia list article", "Wikimedia category", "Wikimedia disambiguation page",
    };

    /// <summary>
    /// What each sort of thing is called on this timeline, so that a hundred Wikidata types become
    /// a handful a reader can hold in their head. Anything unmatched keeps Wikidata's own word,
    /// which is honest and colours as the fallback.
    /// </summary>
    private static readonly (string[] Contains, string Kind)[] Kinds =
    [
        (["naval battle", "battle", "siege", "last stand", "ambush", "military campaign", "war", "rebellion", "revolt"], "Battle"),
        (["treaty", "peace", "synod", "council", "census", "law", "legal", "trial", "edict"], "Message"),
        (["eruption", "earthquake", "flood", "famine", "plague", "epidemic", "eclipse", "impact"], "Destruction"),
        (["dynasty", "empire", "kingdom", "historical country", "state", "province", "caliphate"], "Reign"),
        (["city", "polis", "settlement", "site", "temple", "pyramid", "wall", "tomb", "monument", "building"], "Construction"),
        (["work", "text", "poem", "epic", "treatise", "writing system", "alphabet", "script", "manuscript", "tablet"], "Message"),
        (["culture", "period", "age", "periodization", "style", "horizon"], "Unique"),
    ];

    public async Task<WorldOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        if (!Directory.Exists(folder))
        {
            logger.LogWarning("No world history: {Folder} is not there.", folder);
            return new WorldOutcome(true, 0, 0, 0, TimeSpan.Zero);
        }

        if (await db.Events.AnyAsync(e => e.Realm == Realms.World, cancellationToken))
        {
            return new WorldOutcome(true, 0, 0, 0, started.Elapsed);
        }

        var chronologies = await db.Chronologies.ToListAsync(cancellationToken);
        if (chronologies.Count == 0)
        {
            logger.LogWarning("No world history: the chronologies are not loaded yet.");
            return new WorldOutcome(true, 0, 0, 0, started.Elapsed);
        }

        var taken = await db.Events.Select(e => e.Slug).ToHashSetAsync(cancellationToken);
        var events = new List<Event>();
        var years = new Dictionary<Event, int>();

        foreach (var file in new[] { "wikidata-events.csv", "wikidata-inception.csv" })
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var item in Items(path, "time"))
            {
                if (Year(item.Time) is not { } year)
                {
                    continue;
                }

                var made = new Event
                {
                    Slug = Unique(Slugs.Of(item.Label), taken),
                    Name = item.Label,
                    Kind = Kind(item.Type),
                    Description = Described(item.Type, item.Where, file),
                    Realm = Realms.World,
                    Region = item.Where,
                    Uri = item.Uri,
                    YearFromCreation = 3961 + year,
                    BceYear = year <= 0 ? 1 - year : null,
                    Source = Source,
                };

                events.Add(made);
                years[made] = year;
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
                    Citation = year <= 0 ? $"{1 - year} BCE" : $"AD {year}",
                });
            }
        }

        db.EventDates.AddRange(dates);

        var periods = Spans(folder);
        db.Periods.AddRange(periods);
        await db.SaveChangesAsync(cancellationToken);

        var outcome = new WorldOutcome(false, events.Count, periods.Count, dates.Count, started.Elapsed);
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
                Kind = Kind(item.Type).ToLowerInvariant() == "reign" ? "reign" : "span",
                // The second band. Level 0 is this corpus's own eras and stays that way.
                Level = 1,
                StartYear = 3961 + from,
                EndYear = 3961 + to,
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

            if (NotEvents.Contains(type) || invented.Contains(uri) || !seen.Add(uri))
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

    private static string Kind(string type)
    {
        var text = type.ToLowerInvariant();
        foreach (var (contains, kind) in Kinds)
        {
            if (contains.Any(word => text.Contains(word, StringComparison.Ordinal)))
            {
                return kind;
            }
        }

        return "Unique";
    }

    /// <summary>
    /// What the row says about itself, in a sentence, because the source sends no prose at all.
    /// Better an honest label than an empty description that reads as missing data.
    /// </summary>
    private static string Described(string type, string? where, string file)
    {
        var what = file.Contains("inception", StringComparison.Ordinal)
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
