using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The bands behind the dots.
///
/// The dataset records no periods — it records 140 events whose name begins *Beginning of* and 131
/// whose name ends *ended*, which is the same thing written twice and left to the reader to join.
/// So they are joined here: 125 of the 132 openings find their close, and each pair becomes one
/// named stretch of time. Add the lifespans, which are a birth and a death saying the same thing
/// in another vocabulary, and the eras, which are the long anchors everything else sits inside.
///
/// Nothing here invents a year. Every period points at the two events that open and close it, and
/// the years come from whichever chronology is asked for — so the bands move with the dots when a
/// reader switches to Ussher, instead of quietly staying where the base reckoning left them.
/// </summary>
internal static partial class Periods
{
    /// <summary>
    /// The eras, each anchored to two events in the dataset rather than to a number.
    ///
    /// These are the periods that make the whole 3,500-year span legible at a glance, and every
    /// division is one this dataset already draws: the Flood opens and closes, the wandering opens
    /// and closes, Saul's reign begins and Solomon's ends. Where a boundary is a judgement — the
    /// judges end when the monarchy begins — the anchor says which event was chosen.
    /// </summary>
    private static readonly (string Slug, string Name, string From, string To, string? Notes)[] Eras =
    [
        ("era-antediluvian", "From the Creation to the Flood", "creation", "beginflood", null),
        ("era-flood", "The Flood", "beginflood", "endflood", null),
        ("era-after-the-flood", "From the Flood to Abraham", "endflood", "birthabram1", null),
        ("era-patriarchs", "The patriarchs", "birthabram1", "jacobwenttoegypt", null),
        ("era-egypt", "Israel in Egypt", "jacobwenttoegypt", "theexodus", null),
        ("era-wilderness", "The wilderness", "beginwanderingwilderness", "endwanderingwilderness", null),
        ("era-conquest", "The conquest of Canaan", "beginconquestoftheland", "endconquestoftheland", null),
        ("era-judges", "The judges", "endconquestoftheland", "beginsaul1reign",
            "Closed at the beginning of Saul's reign, which is where the monarchy starts rather " +
            "than where the book of Judges stops."),
        ("era-united-kingdom", "The united kingdom", "beginsaul1reign", "endsolomon1reign", null),
        ("era-divided-kingdom", "The divided kingdom", "endsolomon1reign", "beginseventyyearexileinbabylon",
            "Israel falls to Assyria within this era, 137 years before Judah falls to Babylon."),
        ("era-exile", "The exile in Babylon", "beginseventyyearexileinbabylon", "endseventyyearexileinbabylon", null),
        ("era-return", "The return and the Persian period", "endseventyyearexileinbabylon", "endartaxerxes1reign",
            "Where this dataset's narrative stops. It records nothing between Artaxerxes and the " +
            "New Testament, and no New Testament events at all — the jubilees after this point are " +
            "counted forward, not narrated."),
    ];

    /// <summary>
    /// The eras the New Testament needs, which no dataset here draws.
    ///
    /// BibleData stops at Artaxerxes and Theographic names no eras at all, so the four centuries of
    /// silence and the two ages after them would otherwise be an unlabelled gap on the axis — which
    /// is where a reader most needs to be told what they are looking at. Each is anchored to two
    /// events, one of which is the last thing the Old Testament dataset narrates.
    /// </summary>
    private static readonly (string Slug, string Name, string From, string To, string? Notes)[] NewTestamentEras =
    [
        ("era-between-the-testaments", "Between the testaments", "endartaxerxes1reign", "birthofjesus",
            "Four centuries neither dataset narrates. BibleData's method is arithmetic over the " +
            "genealogies and reign lengths, and those stop; Theographic begins again with Luke's " +
            "synchronisms. The gap is real, not a loading failure."),
        ("era-life-of-christ", "The life of Christ", "birthofjesus", "resurrectionandascension", null),
        ("era-apostolic", "The apostolic age", "theholyspiritcomes", "paulsfirstromanimprisonment",
            "Ends where the account in Acts ends, not where the age does."),
    ];

    /// <summary>
    /// Which row a kind is drawn on. An era is the ground, the spans of rule and captivity that
    /// order the narrative sit above it, and the lives and ministries — of which there are many
    /// more, overlapping constantly — sit above those.
    ///
    /// A band, not a depth. Every period here hangs off an era whatever its level, so a level-2
    /// life is a child of a level-0 era and not of anything at level 1; the number says which row
    /// to draw on, and <see cref="Period.Parent"/> says what it belongs to.
    /// </summary>
    private static int LevelOf(string kind) => kind switch
    {
        "era" => 0,
        "reign" or "co-regency" or "judgeship" or "oppression" or "captivity" => 1,
        _ => 2,
    };

    public static (List<Period> Made, int Unpaired) From(IReadOnlyList<Event> events, string source)
    {
        var byId = events.Where(e => e.Id != 0).ToDictionary(e => e.Slug, e => e);
        var periods = new List<Period>(400);
        var eras = new List<Period>();

        foreach (var era in Eras)
        {
            if (!byId.TryGetValue(era.From, out var from) || !byId.TryGetValue(era.To, out var to))
            {
                continue;
            }

            var made = new Period
            {
                Slug = era.Slug,
                Name = era.Name,
                Kind = "era",
                Level = 0,
                StartEventId = from.Id,
                EndEventId = to.Id,
                StartYear = from.YearFromCreation,
                EndYear = to.YearFromCreation,
                Notes = era.Notes,
                Source = source,
            };

            eras.Add(made);
            periods.Add(made);
        }

        var (spans, unpaired) = Spans(events, source);
        periods.AddRange(spans);
        periods.AddRange(Lives(events, source));

        Nest(periods, eras);

        return (periods, unpaired);
    }

    /// <summary>
    /// The New Testament's bands: the three eras, and the missionary journeys the source groups
    /// its events into.
    /// </summary>
    /// <param name="bySlug">
    /// Every event that can anchor one of these, which is the New Testament's own plus the last
    /// event the Old Testament dataset narrates.
    /// </param>
    /// <param name="journeys">
    /// The groups the source marks with <c>partOf</c>, each as the first and last event in it.
    /// </param>
    public static List<Period> ForTheNewTestament(
        IReadOnlyDictionary<string, Event> bySlug,
        IEnumerable<(string Name, string From, string To)> journeys,
        string source)
    {
        var made = new List<Period>();
        var eras = new List<Period>();

        foreach (var era in NewTestamentEras)
        {
            if (Between(bySlug, era.Slug, era.Name, "era", 0, era.From, era.To, era.Notes, source) is { } band)
            {
                eras.Add(band);
                made.Add(band);
            }
        }

        foreach (var (name, from, to) in journeys)
        {
            if (Between(bySlug, $"period-{Slugs.Of(name)}", name, "travel", 1, from, to, null, source) is { } band)
            {
                made.Add(band);
            }
        }

        Nest(made, eras);

        return made;
    }

    /// <summary>
    /// The era each band belongs to, which is the era it opens in.
    ///
    /// Not the era that contains it. The eras are contiguous and 42 of these bands run past the
    /// close of the one they start in — Noah outlives the Flood, and the 430 years from the promise
    /// to the covenant end two eras after they begin — so for those there is no containing era to
    /// point at, and a band that changed parent halfway through would draw as two. Where a band does
    /// leave its era the row says so, because a client that reads the parent as nesting would
    /// otherwise draw a child outside its parent with nothing to say that this is expected.
    /// </summary>
    private static void Nest(IEnumerable<Period> periods, IReadOnlyList<Period> eras)
    {
        foreach (var period in periods.Where(p => p.Level > 0))
        {
            period.Parent = eras.FirstOrDefault(
                era => era.StartYear <= period.StartYear && period.StartYear <= era.EndYear);

            if (period.Parent is { EndYear: { } closes } era && period.EndYear > closes)
            {
                period.Notes = Appended(
                    period.Notes,
                    $"Runs {period.EndYear - closes} years past the close of \"{era.Name}\", the era " +
                    "it opens in. The parent of a band is where it begins, not what contains it.");
            }
        }
    }

    private static string Appended(string? notes, string sentence) =>
        string.IsNullOrWhiteSpace(notes) ? sentence : $"{notes} {sentence}";

    private static Period? Between(
        IReadOnlyDictionary<string, Event> bySlug,
        string slug,
        string name,
        string kind,
        int level,
        string from,
        string to,
        string? notes,
        string source)
    {
        if (!bySlug.TryGetValue(from, out var opens) || !bySlug.TryGetValue(to, out var closes))
        {
            return null;
        }

        return new Period
        {
            Slug = slug,
            Name = name,
            Kind = kind,
            Level = level,
            StartEventId = opens.Id,
            EndEventId = closes.Id,
            StartYear = opens.YearFromCreation,
            EndYear = closes.YearFromCreation,
            Notes = notes,
            Source = source,
        };
    }

    /// <summary>Every opening that finds its close.</summary>
    private static (List<Period> Made, int Unpaired) Spans(IReadOnlyList<Event> events, string source)
    {
        var closes = events
            .Where(e => e.Kind == "End")
            .GroupBy(Key)
            .ToDictionary(g => g.Key, g => g.First());

        var made = new List<Period>();
        var unpaired = 0;

        foreach (var opens in events.Where(e => e.Kind == "Begin"))
        {
            if (!closes.TryGetValue(Key(opens), out var closed))
            {
                unpaired++;
                continue;
            }

            var kind = KindOf(opens.Slug, opens.Name);
            made.Add(new Period
            {
                Slug = $"period-{Key(opens)}",
                Name = Title(opens.Name),
                Kind = kind,
                Level = LevelOf(kind),
                EntityId = opens.EntityId,
                StartEventId = opens.Id,
                EndEventId = closed.Id,
                StartYear = opens.YearFromCreation,
                EndYear = closed.YearFromCreation,
                Notes = opens.Name.Contains('*') ? Inferred : null,
                Source = source,
            });
        }

        return (made, unpaired);
    }

    /// <summary>
    /// A birth and a death are a period written in another vocabulary. Fifty-seven people have
    /// both, and their lifespans are what turn *Birth of Methuselah* into the 969 years the reader
    /// came to see.
    /// </summary>
    private static List<Period> Lives(IReadOnlyList<Event> events, string source)
    {
        var deaths = events
            .Where(e => e.Kind == "Death")
            .GroupBy(Key)
            .ToDictionary(g => g.Key, g => g.First());

        var made = new List<Period>();
        foreach (var born in events.Where(e => e.Kind == "Birth"))
        {
            if (!deaths.TryGetValue(Key(born), out var died))
            {
                continue;
            }

            made.Add(new Period
            {
                Slug = $"life-{Key(born)}",
                Name = born.Name.Replace("Birth of ", string.Empty, StringComparison.Ordinal).Replace("*", string.Empty),
                Kind = "life",
                Level = 2,
                EntityId = born.EntityId ?? died.EntityId,
                StartEventId = born.Id,
                EndEventId = died.Id,
                StartYear = born.YearFromCreation,
                EndYear = died.YearFromCreation,
                Notes = born.Name.Contains('*') || died.Name.Contains('*') ? Inferred : null,
                Source = source,
            });
        }

        return made;
    }

    private const string Inferred =
        "The dataset marks one end of this with an asterisk, meaning the year is inferred from " +
        "another figure rather than stated in the text.";

    /// <summary>
    /// What the two halves of a pair have in common. The dataset writes the marker at either end
    /// of the identifier — <c>begin_asa1_reign</c> but <c>absalom_denied…_begin</c> — so both are
    /// stripped, and what is left is the thing itself.
    /// </summary>
    private static string Key(Event e)
    {
        var slug = e.Slug;
        foreach (var marker in (string[])["begin", "end", "birth", "death"])
        {
            if (slug.StartsWith(marker, StringComparison.Ordinal))
            {
                slug = slug[marker.Length..];
            }
            else if (slug.EndsWith(marker, StringComparison.Ordinal))
            {
                slug = slug[..^marker.Length];
            }
        }

        return slug;
    }

    private static string KindOf(string slug, string name)
    {
        var text = $"{slug} {name}".ToLowerInvariant();
        return text switch
        {
            _ when text.Contains("coregency") || text.Contains("co-regency") => "co-regency",
            _ when text.Contains("reign") => "reign",
            _ when text.Contains("judge") => "judgeship",
            _ when text.Contains("prophes") || text.Contains("prophet") || text.Contains("ministry") => "ministry",
            _ when text.Contains("oppress") || text.Contains("bondage") || text.Contains("servitude") => "oppression",
            _ when text.Contains("captivity") || text.Contains("exile") => "captivity",
            _ when text.Contains("construction") || text.Contains("built") => "construction",
            _ when text.Contains("famine") || text.Contains("plenty") || text.Contains("drought") => "provision",
            _ when text.Contains("siege") || text.Contains("war") || text.Contains("invasion") => "war",
            _ => "span",
        };
    }

    /// <summary>
    /// The name of the stretch, from the name of its opening.
    ///
    /// The source writes an event, not a period — <em>Beginning of the 400 years of oppression</em>,
    /// <em>Asa's reign as king over Judah begins</em> — and a band labelled *…begins* reads as a
    /// moment rather than a duration. Both shapes are stripped, from the front and from the back,
    /// because the dataset uses both about equally.
    /// </summary>
    private static string Title(string name)
    {
        var text = name.Replace("*", string.Empty).Trim();
        text = Opening().Replace(text, string.Empty);
        text = Closing().Replace(text, string.Empty);
        return text.Length == 0 ? name : char.ToUpperInvariant(text[0]) + text[1..];
    }

    [GeneratedRegex(@"^(?:The )?Beginning of (?:the )?", RegexOptions.IgnoreCase)]
    private static partial Regex Opening();

    [GeneratedRegex(@"\s+(?:began|begins|started|starts)\b\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex Closing();
}
