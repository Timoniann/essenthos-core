using System.Globalization;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Every bar on the timeline of the kings held to the text: the length of each reign against the
/// years the Books of Kings give it, and the year each king began against the other kingdom's king
/// whose year the text dates it by — in every reckoning drawn from the event files, with Ussher's
/// Annals read where they replace a cell.
///
/// <para>
/// A reckoning counts whole years from an accession or from the new year after it, and Ussher's
/// year opens in the autumn, so two years apart is agreement. A co-regency, a rival reign or a
/// disputed one the records draw beside a reign may stretch it by its own length. Anything more is
/// either a wrong cell — Elah's two years drawn across sixty-six — or a reckoning reading the text
/// its own way, and the second is said in the records beside the king.
/// </para>
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class ReignBarTests
{
    /// <summary>How far apart two counts of the same reign may stand and still agree.</summary>
    private const int CountedApart = 2;

    private const int UssherZeroPoint = 4003;

    private static readonly string[] Reckonings = ["bibledata", UssherAnnalsLoader.Chronology, "shulman"];

    private static readonly Lazy<Drawn> Bars = new(Draw);

    private static readonly ReignDecision Decision = ReignLoader.Records();

    private static string Folder => Path.GetDirectoryName(TestResources.Path("BibleData2026", "BibleData-Event.csv"))!;

    [Fact]
    public void EveryReignIsAsLongAsTheTextSays()
    {
        var wrong = new List<string>();
        foreach (var ruler in Decision.Rulers.Where(r => r.Reigned is not null))
        {
            foreach (var reckoning in Reckonings.Where(one => ruler.Readings?.ContainsKey(one) != true))
            {
                if (Bars.Value.Of(ruler, reckoning) is not { } drawn)
                {
                    continue;
                }

                var length = drawn.To - drawn.From;
                if (Math.Abs(length - ruler.Reigned!.InYears) > CountedApart + drawn.SharedYears)
                {
                    wrong.Add($"{ruler.Slug} in {reckoning}: {drawn.From}-{drawn.To}, {length} years against " +
                              $"{ruler.Reigned.InYears:0.##} at {ruler.Reigned.Verse}");
                }
            }
        }

        wrong.Should().BeEmpty();
    }

    /// <summary>
    /// Where the text dates one accession twice — Hoshea in the twentieth year of Jotham and in the
    /// twelfth of Ahaz — a reckoning can keep one of them, and keeping either is enough.
    /// </summary>
    [Fact]
    public void EveryKingBeginsInTheYearTheOtherKingdomsKingDatesHimBy()
    {
        var rulers = Decision.Rulers.ToDictionary(r => r.Slug);
        var wrong = new List<string>();
        foreach (var accession in Decision.Statements.Where(s => s.Role == ReignRoles.Accession).GroupBy(s => s.Person))
        {
            var king = rulers[accession.Key];
            foreach (var reckoning in Reckonings.Where(one => king.Readings?.ContainsKey(one) != true))
            {
                var began = Bars.Value.Of(king, reckoning);
                var held = accession
                    .Select(s => (Statement: s, Ruler: Bars.Value.Of(rulers[s.Ruler], reckoning)))
                    .Where(one => one.Ruler is not null)
                    .ToList();
                if (began is null || held.Count == 0)
                {
                    continue;
                }

                var kept = held.Any(one =>
                    new[] { one.Ruler!.From, one.Ruler.OwnFrom }.Any(start =>
                        new[] { began.From, began.OwnFrom }.Any(at =>
                            Math.Abs(at - (start + one.Statement.Year!.Value - 1)) <= CountedApart)));
                if (!kept)
                {
                    wrong.Add($"{king.Slug} in {reckoning} begins {began.OwnFrom}, against " + string.Join(
                        " and ",
                        held.Select(one =>
                            $"year {one.Statement.Year} of {one.Statement.Ruler} ({one.Ruler!.OwnFrom}) at {one.Statement.Verse}")));
                }
            }
        }

        wrong.Should().BeEmpty();
    }

    [Fact]
    public void AReadingIsKeptOnlyForAReckoningAndAKingTheTextMeasures()
    {
        foreach (var ruler in Decision.Rulers.Where(r => r.Readings is not null))
        {
            ruler.Readings!.Keys.Should().BeSubsetOf(Reckonings, ruler.Slug);
            ruler.Readings.Values.Should().OnlyContain(why => !string.IsNullOrWhiteSpace(why), ruler.Slug);
            (ruler.Reigned is not null || Decision.Statements.Any(s => s.Role == ReignRoles.Accession && s.Person == ruler.Slug))
                .Should().BeTrue($"{ruler.Slug} keeps a reading of something the text does not say");
        }
    }

    /// <summary>
    /// A replacement names the year the dataset wrote; if the dataset is corrected at its source the
    /// entry stops applying, and this says so rather than letting the list go stale unseen.
    /// </summary>
    [Fact]
    public void EveryReplacedUssherYearIsTheOneTheDatasetStillWrites()
    {
        var cells = BibleDataLoader.EventRows(Folder)
            .ToDictionary(e => e.Slug, e => Number(e.Row["ussher_am_year"]), StringComparer.Ordinal);

        foreach (var (slug, dating) in UssherDatings.Read(Folder).Where(d => d.Value.Replaces is not null))
        {
            cells.Should().ContainKey(slug);
            cells[slug].Should().Be(dating.Replaces, $"{slug} replaces the year the dataset wrote");
        }
    }

    /// <summary>Elah reigned two years (1KI 16:8), and Ussher's column had drawn him across sixty-six.</summary>
    [Fact]
    public void ElahReignsTwoYearsInUssher()
    {
        var elah = Bars.Value.Of(Decision.Rulers.Single(r => r.Slug == "elah-2"), UssherAnnalsLoader.Chronology);

        elah.Should().NotBeNull();
        (elah!.From, elah.To).Should().Be((3074, 3075));
    }

    private sealed record Span(int From, int OwnFrom, int To, int SharedYears);

    private sealed class Drawn(IReadOnlyDictionary<string, IReadOnlyDictionary<string, (int From, int To)>> periods)
    {
        /// <summary>
        /// The ruler's bars in one reckoning, as the chart draws them: his own periods it dates, or
        /// where it dates none, the one kept for that.
        /// </summary>
        public Span? Of(RulerRecord ruler, string reckoning)
        {
            (int From, int To)? In(ReignRecord reign) =>
                periods.TryGetValue(reign.Period, out var years) && years.TryGetValue(reckoning, out var span)
                    ? span
                    : null;

            var chosen = ruler.Reigns.Where(r => !r.Fallback && In(r) is not null).ToList();
            if (chosen.Count == 0)
            {
                chosen = [.. ruler.Reigns.Where(r => r.Fallback && In(r) is not null)];
            }

            if (chosen.Count == 0)
            {
                return null;
            }

            var spans = chosen.Select(r => (Reign: r, Years: In(r)!.Value)).ToList();
            var own = spans.Where(s => !s.Reign.Shared).ToList();
            return new Span(
                spans.Min(s => s.Years.From),
                (own.Count > 0 ? own : spans).Min(s => s.Years.From),
                spans.Max(s => s.Years.To),
                spans.Where(s => s.Reign.Shared).Sum(s => s.Years.To - s.Years.From));
        }
    }

    /// <summary>
    /// Every period the event files make, with its years in each reckoning, dated as the loader and
    /// the endpoint date them: Ussher by the year he printed where the file gives it.
    /// </summary>
    private static Drawn Draw()
    {
        var annals = UssherDatings.Read(Folder);
        var rows = BibleDataLoader.EventRows(Folder).ToList();
        var events = rows.Select((one, at) => new Event
        {
            Id = at + 1,
            Slug = one.Slug,
            Name = one.Row["event_name"],
            Kind = BibleDataLoader.Blank(one.Row["event_type"]),
            Source = "a test",
        }).ToList();

        var years = rows.Select(one =>
        {
            var cell = Number(one.Row["ussher_am_year"]);
            int? ussher = annals.TryGetValue(one.Slug, out var dating) && dating.Supersedes(cell)
                ? EncyclopediaEndpoints.Placed(dating.Year, dating.StatedYear, UssherZeroPoint)
                : cell is { } year
                    ? EncyclopediaEndpoints.Placed(year, BibleDataLoader.UssherStatedYear(one.Row), UssherZeroPoint)
                    : null;
            return new Dictionary<string, int?>
            {
                ["bibledata"] = Number(one.Row["event_year_ah"]),
                [UssherAnnalsLoader.Chronology] = ussher,
                ["shulman"] = Number(one.Row["shulman_am_year"]),
            };
        }).ToList();

        var (periods, _) = Periods.From(events, "a test");
        return new Drawn(periods.ToDictionary(
            p => p.Slug,
            p => (IReadOnlyDictionary<string, (int, int)>)Reckonings
                .Where(r => years[p.StartEventId!.Value - 1][r] is not null && years[p.EndEventId!.Value - 1][r] is not null)
                .ToDictionary(r => r, r => (years[p.StartEventId!.Value - 1][r]!.Value, years[p.EndEventId!.Value - 1][r]!.Value)),
            StringComparer.Ordinal));
    }

    private static int? Number(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
