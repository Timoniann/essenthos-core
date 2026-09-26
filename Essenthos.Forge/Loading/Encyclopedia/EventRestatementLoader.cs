using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Located">Events given the verse their source names their location at.</param>
/// <param name="Stated">Ussher's dates given the year of the common era he printed.</param>
/// <param name="Filled">BibleData's events given the date Ussher's Annals give them, where its own column is blank.</param>
/// <param name="Dated">Annals given a date of his they had none of, from the two columns that agree.</param>
/// <param name="Described">World events whose description no longer names today's country.</param>
/// <param name="Placed">World events whose countries, today's and of the time, were written again.</param>
/// <param name="Ordered">Reckonings given the default and the place in the list the encyclopedia declares.</param>
internal sealed record EventRestatementOutcome(
    int Located,
    int Stated,
    int Filled,
    int Dated,
    int Described,
    int Placed,
    int Ordered,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        Located + Stated + Filled + Dated + Described + Placed + Ordered == 0
            ? "every event already carries what its source states"
            : $"{Located} event locations given the verse their source names them at, {Stated} of Ussher's " +
              $"dates given the year he printed, {Filled} events dated from his Annals where BibleData's column " +
              $"for him is blank, {Dated} of his Annals dated from the two columns that agree, " +
              $"{Described} world descriptions without today's country, {Placed} world events with their " +
              $"countries written again and {Ordered} reckonings put in the declared order, in {Elapsed}";
}

/// <summary>
/// What the event sources state that a database loaded before it was read does not hold.
///
/// The loaders read it on a fresh load, and each is guarded on its own rows, so on a corpus that
/// already has events they do nothing — and reloading the encyclopedia to reach three columns
/// would renumber every record the rest of the corpus names. This reads the same files with the
/// loaders' own code and writes only what is missing or was written differently: the verse at
/// which BibleData names each event's location, Ussher's year of the common era as he printed it,
/// his years for the events BibleData leaves blank in his column and <see cref="UssherDatings"/>
/// finds in his Annals, the Annals his anno mundi column contradicts dated from the two columns
/// that agree, the world layer's descriptions without the modern country in them, and which
/// reckoning is the default and in what order they are offered. On a fresh load every count is
/// zero.
/// </summary>
internal sealed class EventRestatementLoader(AppDbContext db, ILogger<EventRestatementLoader> logger)
{
    private const string CommonEra = "AD";

    public async Task<EventRestatementOutcome> Load(
        string bibleData,
        string worldHistory,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var ussher = await db.Chronologies.SingleOrDefaultAsync(
            c => c.Slug == UssherAnnalsLoader.Chronology, cancellationToken);

        var (located, stated, filled) = ussher is null || !Directory.Exists(bibleData)
            ? (0, 0, 0)
            : await BibleData(bibleData, ussher, cancellationToken);
        var (restated, dated) = ussher is null || !File.Exists(Path.Combine(bibleData, UssherAnnalsLoader.File))
            ? (0, 0)
            : await Annals(bibleData, ussher, cancellationToken);
        var (described, placed) = await World(worldHistory, cancellationToken);
        var ordered = await Order(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var outcome = new EventRestatementOutcome(
            located, stated + restated, filled, dated, described, placed, ordered, started.Elapsed);
        logger.LogInformation("Restated the events: {Outcome}", outcome);
        return outcome;
    }

    private async Task<(int Located, int Stated, int Filled)> BibleData(
        string folder,
        Chronology ussher,
        CancellationToken cancellationToken)
    {
        var events = await db.Events
            .Where(e => e.Source == BibleDataLoader.Source)
            .ToDictionaryAsync(e => e.Slug, cancellationToken);
        var dates = await db.EventDates
            .Where(d => d.ChronologyId == ussher.Id && d.Event!.Source == BibleDataLoader.Source)
            .ToDictionaryAsync(d => d.EventId, cancellationToken);

        var frame = BibleDataLoader.ReferenceTable.Read(folder);
        var annals = UssherDatings.Read(folder);
        var located = 0;
        var stated = 0;
        var filled = 0;

        foreach (var (slug, row) in BibleDataLoader.EventRows(folder))
        {
            if (!events.TryGetValue(slug, out var one))
            {
                continue;
            }

            if (one.LocationBook is null && BibleDataLoader.LocationVerse(row, frame) is { } verse)
            {
                (one.LocationBook, one.LocationChapter, one.LocationVerse) = verse;
                located++;
            }

            if (dates.TryGetValue(one.Id, out var date) && date.StatedYear is null
                && BibleDataLoader.UssherStatedYear(row) is { } year)
            {
                date.StatedYear = year;
                stated++;
            }

            if (!dates.ContainsKey(one.Id) && annals.TryGetValue(slug, out var dating))
            {
                db.EventDates.Add(new EventDate
                {
                    EventId = one.Id,
                    ChronologyId = ussher.Id,
                    Year = dating.Year,
                    StatedYear = dating.StatedYear,
                    Citation = dating.Citation,
                });
                filled++;
            }
        }

        return (located, stated, filled);
    }

    /// <summary>The default reckoning, and the order the reckonings are offered in, as the encyclopedia declares them.</summary>
    private async Task<int> Order(CancellationToken cancellationToken)
    {
        var declared = BibleDataLoader.Reckonings.ToDictionary(r => r.Slug, StringComparer.Ordinal);
        var ordered = 0;
        foreach (var chronology in await db.Chronologies.ToListAsync(cancellationToken))
        {
            if (declared.TryGetValue(chronology.Slug, out var one)
                && (chronology.IsDefault != one.Default || chronology.Position != one.Position))
            {
                (chronology.IsDefault, chronology.Position) = (one.Default, one.Position);
                ordered++;
            }
        }

        return ordered;
    }

    private async Task<(int Stated, int Dated)> Annals(
        string folder,
        Chronology ussher,
        CancellationToken cancellationToken)
    {
        var events = await db.Events
            .Where(e => e.Source == UssherAnnalsLoader.Source)
            .ToDictionaryAsync(e => e.Slug, cancellationToken);
        if (events.Count == 0)
        {
            return (0, 0);
        }

        var dates = await db.EventDates
            .Where(d => d.ChronologyId == ussher.Id && d.Event!.Source == UssherAnnalsLoader.Source)
            .ToDictionaryAsync(d => d.EventId, cancellationToken);

        var stated = 0;
        var dated = 0;

        foreach (var row in Csv.Read(Path.Combine(folder, UssherAnnalsLoader.File)))
        {
            if (!string.Equals(row["gc_bc_ad"], CommonEra, StringComparison.Ordinal)
                || !int.TryParse(row["gc_year"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                || !events.TryGetValue($"ussher-{row["paragraph_nr"]}", out var one))
            {
                continue;
            }

            var reckoned = UssherAnnalsLoader.Reckoned(row, year, ussher.LastYearBeforeTheCommonEra);
            if (UssherAnnalsLoader.Stated(row, year, reckoned is not null) is not { } printed)
            {
                continue;
            }

            if (dates.TryGetValue(one.Id, out var date))
            {
                if (date.StatedYear is null)
                {
                    date.StatedYear = printed;
                    stated++;
                }

                continue;
            }

            db.EventDates.Add(new EventDate
            {
                EventId = one.Id,
                ChronologyId = ussher.Id,
                Year = reckoned,
                StatedYear = printed,
                Citation = $"¶{row["paragraph_nr"]}",
            });
            dated++;

            if (reckoned is null && one.Notes is { } notes)
            {
                one.Notes = notes.Replace(
                    UssherAnnalsLoader.Unread(row["am_year"], year),
                    UssherAnnalsLoader.Contradiction(row["am_year"], printed, year),
                    StringComparison.Ordinal);
            }
        }

        return (stated, dated);
    }

    private async Task<(int Described, int Placed)> World(string folder, CancellationToken cancellationToken)
    {
        var events = await db.Events
            .Where(e => e.Realm == Realms.World && e.Source == WorldHistoryLoader.Source)
            .ToListAsync(cancellationToken);
        if (events.Count == 0)
        {
            return (0, 0);
        }

        var regions = Directory.Exists(folder)
            ? WorldHistoryLoader.StatedRegions(folder)
            : [];
        var described = 0;
        var placed = 0;

        foreach (var one in events)
        {
            var without = WorldHistoryLoader.WithoutTheCountry(one.Description, one.Region);
            if (!string.Equals(without, one.Description, StringComparison.Ordinal))
            {
                one.Description = without;
                described++;
            }

            if (one.Uri is { } uri && regions.TryGetValue(uri, out var region)
                && (one.Region != region.Today || one.RegionAtTheTime != region.Then))
            {
                (one.Region, one.RegionAtTheTime) = region;
                placed++;
            }
        }

        return (described, placed);
    }
}
