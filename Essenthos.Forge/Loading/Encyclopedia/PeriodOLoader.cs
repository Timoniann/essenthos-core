using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record PeriodOOutcome(
    bool AlreadyLoaded,
    int Authorities,
    int Periods,
    int OutsideTheWindow,
    int Elsewhere,
    int Unplaced,
    int OpenEnded,
    int Licensed,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the periods of the lands are already loaded, or PeriodO has not been fetched"
            : $"{Periods} periods of the lands under {Authorities} authorities; left out: " +
              $"{OutsideTheWindow} outside the years, {Elsewhere} mostly elsewhere, {Unplaced} " +
              $"placed nowhere, {OpenEnded} with an open end, {Licensed} under an authority's own " +
              $"licence; in {Elapsed}";
}

/// <summary>
/// The archaeological and historical periods of the lands around the Bible — the Late Bronze Age of
/// the Levant, Egypt's New Kingdom, the Old Babylonian period — as the scholars who define them date
/// them.
///
/// <b>PeriodO, public domain.</b> A gazetteer of period definitions, each under the published work
/// it was read from. That is the point of taking it rather than a list of ages: the same name under
/// five authorities is five rows here, each with its own dates and its own citation, and a reader is
/// shown the disagreement rather than an average nobody published.
///
/// Taken as the source gives it. The years stay astronomical, as PeriodO writes them, and each end
/// keeps its range where the source gives one. What is ours is only the choice of what to take — a
/// period of these lands that touches 4000 BCE to AD 150 — and the region it is drawn under, read
/// off the places the source says it covers. <c>Resources/PeriodO/LICENCE.md</c> has the rest.
/// </summary>
internal sealed class PeriodOLoader(AppDbContext db, ILogger<PeriodOLoader> logger)
{
    internal const string Source = "PeriodO, data.perio.do, public domain";

    private const string FileName = "d.json";

    /// <summary>The years taken, astronomical: 4000 BCE is <c>-3999</c>.</summary>
    internal const int FirstYear = -3999;

    internal const int LastYear = 150;

    /// <summary>
    /// The lands, each as the Wikidata items PeriodO names for it. Cyprus and the Middle East at large
    /// are deliberately in none: the one is its own sequence, the other names all of them at once.
    /// </summary>
    internal static readonly (string Region, string[] Items)[] Regions =
    [
        ("levant", ["Q801", "Q810", "Q822", "Q858", "Q219060", "Q81483", "Q39760", "Q36678"]),
        ("egypt", ["Q79", "Q11768"]),
        ("mesopotamia", ["Q796", "Q11767", "Q47690", "Q10914393", "Q35355"]),
        ("anatolia", ["Q43", "Q51614"]),
        ("persia", ["Q794"]),
        ("aegean", ["Q41", "Q34374", "Q83958"]),
        ("rome", ["Q2277", "Q1747689", "Q220", "Q17167"]),
    ];

    /// <summary>
    /// The words a source uses for each land when it says in its own words where a period is. They
    /// decide before the places do: a period of <em>Anatolia</em> that lists Iraq and Syria among
    /// its places is Anatolia's, and counting the places would give it to the Levant, which has
    /// the most modern states to count.
    /// </summary>
    internal static readonly (string Region, Regex Words)[] Described =
    [
        ("levant", Words("levant", "canaan", "palestine", "israel", "judah", "judaea", "judea", "syria",
            "lebanon", "jordan", "phoenicia", "holy land", "transjordan", "negev")),
        ("egypt", Words("egypt")),
        ("mesopotamia", Words("mesopotamia", "babylonia", "assyria", "sumer", "akkad", "iraq")),
        ("anatolia", Words("anatolia", "asia minor", "turkey", "lydia", "phrygia")),
        ("persia", Words("persia", "iran", "elam")),
        ("aegean", Words("aegean", "greece", "crete", "cyclades", "attica", "peloponnese", "macedonia")),
        ("rome", Words("rome", "roman empire", "italy")),
    ];

    private static Regex Words(params string[] words) =>
        new(
            $@"\b({string.Join('|', words.Select(Regex.Escape))})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> RegionOfItem = Regions
        .SelectMany(r => r.Items.Select(item => (item, r.Region)))
        .ToDictionary(pair => pair.item, pair => pair.Region, StringComparer.Ordinal);

    public async Task<PeriodOOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var path = Path.Combine(resources, "PeriodO", FileName);
        if (!File.Exists(path))
        {
            logger.LogWarning(
                "No periods of the lands: {Path} is not there. Run scripts/fetch-periodo.ps1.", path);
            return new PeriodOOutcome(true, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (await db.PeriodDefinitions.AnyAsync(cancellationToken))
        {
            return new PeriodOOutcome(true, 0, 0, 0, 0, 0, 0, 0, started.Elapsed);
        }

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var read = Read(document.RootElement);

        db.PeriodAuthorities.AddRange(read.Authorities);
        await db.SaveChangesAsync(cancellationToken);

        var outcome = read.Outcome with { Elapsed = started.Elapsed };
        logger.LogInformation("Loaded the periods of the lands: {Outcome}", outcome);
        return outcome;
    }

    internal sealed record Reading(List<PeriodAuthority> Authorities, PeriodOOutcome Outcome);

    /// <summary>Everything worth taking out of a PeriodO dataset, with a count of what was not and why.</summary>
    internal static Reading Read(JsonElement dataset)
    {
        var authorities = new List<PeriodAuthority>();
        int periods = 0, outside = 0, elsewhere = 0, unplaced = 0, open = 0, licensed = 0;

        if (!dataset.TryGetProperty("authorities", out var all))
        {
            throw new InvalidDataException(
                "The PeriodO file has no \"authorities\". It is not the dataset d.json this reads; " +
                "fetch it again with scripts/fetch-periodo.ps1.");
        }

        foreach (var entry in all.EnumerateObject())
        {
            var authority = entry.Value;
            var source = authority.TryGetProperty("source", out var s) ? s : default;
            var taken = new List<PeriodDefinition>();
            var ownLicence = source.ValueKind == JsonValueKind.Object && CarriesItsOwnLicence(source);

            if (authority.TryGetProperty("periods", out var definitions))
            {
                foreach (var definition in definitions.EnumerateObject())
                {
                    var period = definition.Value;
                    if (Bounds(Property(period, "start")) is not { } start
                        || Bounds(Property(period, "stop")) is not { } stop)
                    {
                        open++;
                        continue;
                    }

                    if (stop.Latest < FirstYear || start.Earliest > LastYear)
                    {
                        outside++;
                        continue;
                    }

                    var items = Covered(period);
                    if (items.Count == 0)
                    {
                        unplaced++;
                        continue;
                    }

                    if (RegionOf(items.Select(item => item.Id).ToList(), Text(period, "spatialCoverageDescription"))
                        is not { } region)
                    {
                        elsewhere++;
                        continue;
                    }

                    if (ownLicence)
                    {
                        licensed++;
                        continue;
                    }

                    taken.Add(new PeriodDefinition
                    {
                        PeriodoId = definition.Name,
                        Label = Text(period, "label") ?? definition.Name,
                        LanguageTag = Text(period, "languageTag"),
                        Labels = Labels(period),
                        Region = region,
                        Coverage = JsonSerializer.Serialize(
                            items.Select(item => new Dictionary<string, string>
                            {
                                ["id"] = item.Id,
                                ["label"] = item.Label,
                            }).ToList()),
                        CoverageDescription = Text(period, "spatialCoverageDescription"),
                        StartLabel = Text(Property(period, "start"), "label"),
                        StartEarliest = start.Earliest,
                        StartLatest = start.Latest,
                        StopLabel = Text(Property(period, "stop"), "label"),
                        StopEarliest = stop.Earliest,
                        StopLatest = stop.Latest,
                        Broader = Text(period, "broader"),
                        Note = Text(period, "note"),
                        EditorialNote = Text(period, "editorialNote"),
                        Source = Source,
                    });
                }
            }

            if (taken.Count == 0)
            {
                continue;
            }

            periods += taken.Count;
            authorities.Add(Authority(entry.Name, source, taken));
        }

        return new Reading(
            authorities,
            new PeriodOOutcome(false, authorities.Count, periods, outside, elsewhere, unplaced, open, licensed,
                TimeSpan.Zero));
    }

    /// <summary>
    /// Which land a period is drawn with, and only where at least half of what it covers is these
    /// lands at all — a period of all Europe that happens to name Greece is not a period of the
    /// Aegean. Where the source's own words for where name exactly one land, that land; otherwise
    /// the one most of its places belong to, a tie going to the land listed first.
    /// </summary>
    internal static string? RegionOf(IReadOnlyList<string> items, string? description = null)
    {
        var counted = new Dictionary<string, int>(StringComparer.Ordinal);
        var ours = 0;
        foreach (var item in items)
        {
            if (RegionOfItem.TryGetValue(item, out var region))
            {
                counted[region] = counted.GetValueOrDefault(region) + 1;
                ours++;
            }
        }

        if (ours == 0 || ours * 2 < items.Count)
        {
            return null;
        }

        var named = description is null
            ? []
            : Described.Where(land => land.Words.IsMatch(description)).Select(land => land.Region).ToList();
        if (named.Count == 1)
        {
            return named[0];
        }

        var most = counted.Values.Max();
        return Regions.First(r => counted.GetValueOrDefault(r.Region) == most).Region;
    }

    internal readonly record struct Range(int Earliest, int Latest);

    /// <summary>
    /// One end of a period: a year, or the earliest and latest it may be. PeriodO writes years as
    /// ISO 8601 does, astronomically and as strings, so <c>"-1549"</c> is 1550 BCE. An end with
    /// only one of its bounds is open, and not taken.
    /// </summary>
    internal static Range? Bounds(JsonElement end)
    {
        if (end.ValueKind != JsonValueKind.Object || !end.TryGetProperty("in", out var within))
        {
            return null;
        }

        if (Year(within, "year") is { } year)
        {
            return new Range(year, year);
        }

        return Year(within, "earliestYear") is { } earliest && Year(within, "latestYear") is { } latest
            ? new Range(Math.Min(earliest, latest), Math.Max(earliest, latest))
            : null;
    }

    private static int? Year(JsonElement within, string name)
    {
        if (!within.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                value.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private sealed record Place(string Id, string Label);

    private static List<Place> Covered(JsonElement period)
    {
        var places = new List<Place>();
        if (!period.TryGetProperty("spatialCoverage", out var coverage) || coverage.ValueKind != JsonValueKind.Array)
        {
            return places;
        }

        foreach (var place in coverage.EnumerateArray())
        {
            var id = Text(place, "id");
            if (id is null)
            {
                continue;
            }

            places.Add(new Place(id[(id.LastIndexOf('/') + 1)..], Text(place, "label") ?? string.Empty));
        }

        return places;
    }

    /// <summary>The first label PeriodO has in each language, as a JSON object.</summary>
    private static string? Labels(JsonElement period)
    {
        if (!period.TryGetProperty("localizedLabels", out var localized) || localized.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var labels = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var language in localized.EnumerateObject())
        {
            if (language.Value.ValueKind == JsonValueKind.Array
                && language.Value.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.String } first
                && !string.IsNullOrWhiteSpace(first.GetString()))
            {
                labels[language.Name] = first.GetString()!.Trim();
            }
        }

        return labels.Count == 0 ? null : JsonSerializer.Serialize(labels);
    }

    /// <summary>
    /// Whether the authority says its own terms. The dataset is dedicated to the public domain, but a
    /// contributor can copy in a vocabulary published under a licence of its own, and one does:
    /// THANADOS's, under CC BY 4.0. The dedication cannot waive a licence the contributor did not
    /// hold, so such an authority is left out rather than loaded under the dataset's terms.
    /// </summary>
    internal static bool CarriesItsOwnLicence(JsonElement source)
    {
        var said = source.GetRawText();
        return said.Contains("Licensed under", StringComparison.OrdinalIgnoreCase)
            || said.Contains("Creative Commons", StringComparison.OrdinalIgnoreCase);
    }

    private static PeriodAuthority Authority(string id, JsonElement source, List<PeriodDefinition> periods)
    {
        var within = source.ValueKind == JsonValueKind.Object && source.TryGetProperty("partOf", out var part)
            ? part
            : default;

        var title = Text(source, "title") ?? Text(within, "title");
        var creators = Names(source, "creators") is { Count: > 0 } own ? own : Names(within, "creators");
        var year = Published(source) ?? Published(within);
        var locator = Text(source, "locator");
        var uri = Text(source, "url") ?? Text(source, "id") ?? Text(within, "url") ?? Text(within, "id");

        return new PeriodAuthority
        {
            PeriodoId = id,
            Attribution = Attributed(creators, title, year),
            Citation = Text(source, "citation") ?? Cited(creators, title, year, locator),
            Title = title,
            Creators = creators.Count == 0 ? null : string.Join("; ", creators),
            YearPublished = year,
            Locator = locator,
            Uri = uri,
            Periods = periods,
        };
    }

    /// <summary>
    /// Who and what, in the form a date is followed by: surnames, the title without its subtitle
    /// and the year — <c>King &amp; Stager, Life in biblical Israel (2001)</c>. A catalogue writes a
    /// person as <c>Mazar, Amihai, (1942- ).,</c>, and only the part before the first comma is a name.
    /// </summary>
    internal static string Attributed(IReadOnlyList<string> creators, string? title, int? year)
    {
        var who = creators.Select(Surname).Where(name => name.Length > 0).ToList();
        var by = who.Count switch
        {
            0 => null,
            1 => who[0],
            2 => $"{who[0]} & {who[1]}",
            _ => $"{who[0]} et al.",
        };

        var what = string.Join(", ", new[] { by, Short(title) }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (what.Length == 0)
        {
            what = "PeriodO";
        }

        return year is { } published ? $"{what} ({published})" : what;
    }

    /// <summary>A title without its subtitle: a catalogue's <c>Ancient cities : the archaeology of urban life</c>.</summary>
    private static string? Short(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var colon = title.IndexOf(':');
        var main = colon >= 3 ? title[..colon] : title;
        return main.Trim().TrimEnd('.', ' ', '/');
    }

    private static string Surname(string name)
    {
        var comma = name.IndexOf(',');
        return (comma > 0 ? name[..comma] : name).Trim().TrimEnd('.');
    }

    private static string Cited(IReadOnlyList<string> creators, string? title, int? year, string? locator)
    {
        var parts = new List<string>();
        if (creators.Count > 0)
        {
            parts.Add(string.Join("; ", creators));
        }

        if (year is { } published)
        {
            parts.Add($"({published})");
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            parts.Add(title);
        }

        if (!string.IsNullOrWhiteSpace(locator))
        {
            parts.Add(locator);
        }

        return parts.Count == 0 ? "PeriodO" : string.Join(". ", parts);
    }

    private static List<string> Names(JsonElement source, string property)
    {
        var names = new List<string>();
        if (source.ValueKind != JsonValueKind.Object
            || !source.TryGetProperty(property, out var people)
            || people.ValueKind != JsonValueKind.Array)
        {
            return names;
        }

        foreach (var person in people.EnumerateArray())
        {
            if (Text(person, "name") is { } name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static int? Published(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("yearPublished", out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? Text(JsonElement element, string name)
    {
        var value = Property(element, name);
        return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
    }
}
