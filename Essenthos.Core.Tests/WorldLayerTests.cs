using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Telling a moment from a thing that began.
///
/// Two thirds of the world layer is dated by inception — cities, dynasties, statues, plays — and
/// the Merneptah Stele did not happen in 1200 BCE, it was cut then. The kind is the only field a
/// client reads to decide how to draw a mark, so it is the field that has to carry the difference.
/// </summary>
public class WorldLayerTests
{
    [Theory]
    [InlineData("battle", "Battle")]
    [InlineData("naval battle", "Battle")]
    [InlineData("peace treaty", "Treaty")]
    [InlineData("treaty", "Treaty")]
    [InlineData("synod", "Message")]
    [InlineData("Plinian eruption", "Destruction")]
    public void NamesWhatHappened(string type, string kind) =>
        WorldHistoryLoader.Kind(type, inception: false).Should().Be(kind);

    [Theory]
    [InlineData("city", "Founding")]
    [InlineData("historical country", "Founding")]
    [InlineData("Egyptian dynasty", "Founding")]
    [InlineData("museum", "Founding")]
    [InlineData("Egyptian pyramids", "Construction")]
    [InlineData("archaeological site", "Construction")]
    [InlineData("dramatic work", "Work")]
    [InlineData("religious text", "Work")]
    [InlineData("writing system", "Work")]
    [InlineData("stele", "Artefact")]
    [InlineData("colossal statue", "Artefact")]
    public void NamesWhatBegan(string type, string kind) =>
        WorldHistoryLoader.Kind(type, inception: true).Should().Be(kind);

    [Fact]
    public void ReadsADeathMaskAsAnObjectRatherThanADeath() =>
        WorldHistoryLoader.Kind("death mask", inception: true).Should().Be("Artefact");

    [Fact]
    public void KeepsAnUnmappedClassInTheFamilyItCameFrom()
    {
        WorldHistoryLoader.Kind("hydrological phenomenon", inception: true).Should().Be("Inception");
        WorldHistoryLoader.Kind("hydrological phenomenon", inception: false).Should().Be("Unique");
    }

    [Fact]
    public void NamesEveryItemTheScriptureLayerAlreadyCarries()
    {
        // The exclusions are Wikidata identifiers, and an identifier that has left the source is a
        // suppression that no longer suppresses anything.
        var items = Uris();

        foreach (var uri in WorldHistoryLoader.AlreadyInScripture.Keys.Concat(WorldHistoryLoader.Miskeyed.Keys))
        {
            items.Should().Contain(uri);
        }
    }

    /// <summary>
    /// A battle of 480 BCE in Sicily, filed under Italy, the Kingdom of Italy and Magna Graecia: the
    /// first is today's, the second ended in 1946 and began two millennia too late, and only the
    /// third existed then. The years decide it, not the names.
    /// </summary>
    [Fact]
    public void NamesTodaysCountryAndTheOneOfTheTimeApart()
    {
        var (today, then) = WorldHistoryLoader.Regions(
            [
                new("Kingdom of Italy", 1861, 1946, Historical: true),
                new("Italy", 1946, null, Historical: false),
                new("Magna Graecia", -800, -200, Historical: true),
            ],
            year: -479,
            lifetimes: true);

        today.Should().Be("Italy");
        then.Should().Be("Magna Graecia");
    }

    /// <summary>
    /// A stele of about 1200 BCE that Wikidata files under the Khedivate of Egypt, a state of
    /// 1867-1914: not the country of the time, and not anybody's country today.
    /// </summary>
    [Fact]
    public void AStateThatDidNotExistYetIsNeitherCountry() =>
        WorldHistoryLoader.Regions([new("Khedivate of Egypt", 1867, 1914, Historical: true)], -1199, lifetimes: true)
            .Should().Be((null, null));

    /// <summary>A country with no dissolution date is still not today's if Wikidata calls it historical.</summary>
    [Fact]
    public void AHistoricalCountryWithNoEndIsNotTodays() =>
        WorldHistoryLoader.Regions([new("Ancient Greece", -1100, null, Historical: true)], -490, lifetimes: true)
            .Should().Be((null, "Ancient Greece"));

    /// <summary>
    /// A file fetched before the queries asked for the years: the first country named, as the
    /// corpus always read it, and no claim about the time.
    /// </summary>
    [Fact]
    public void WithoutTheYearsTheFirstCountryIsAllThereIs() =>
        WorldHistoryLoader.Regions(
                [new("Khedivate of Egypt", null, null, Historical: false), new("Egypt", null, null, Historical: false)],
                -1199,
                lifetimes: false)
            .Should().Be(("Khedivate of Egypt", null));

    [Fact]
    public void GathersACountryNamedOnSeveralRowsIntoOneSpan() =>
        WorldHistoryLoader.Gathered(
            [
                new("Ancient Rome", -752, null, Historical: true),
                new("Ancient Rome", -509, 476, Historical: false),
                new("Italy", 1861, null, Historical: false),
            ])
            .Should().Equal(
                new WorldHistoryLoader.Country("Ancient Rome", -752, 476, Historical: true),
                new WorldHistoryLoader.Country("Italy", 1861, null, Historical: false));

    /// <summary>
    /// The sentence a row says about itself does not name a country: a stele "in the Khedivate of
    /// Egypt" read as a historical claim. The country is a field.
    /// </summary>
    [Fact]
    public void TheDescriptionNamesNoCountry()
    {
        WorldHistoryLoader.Described("stele", inception: true).Should().Be("Stele, dated by its inception. From Wikidata.");
        WorldHistoryLoader.Described("battle", inception: false).Should().Be("Battle. From Wikidata.");
        WorldHistoryLoader.WithoutTheCountry(
                "Stele, dated by its inception, in Khedivate of Egypt. From Wikidata.", "Khedivate of Egypt")
            .Should().Be("Stele, dated by its inception. From Wikidata.");
        WorldHistoryLoader.WithoutTheCountry("Battle. From Wikidata.", "Italy").Should().Be("Battle. From Wikidata.");
    }

    private static HashSet<string> Uris()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Resources", "WorldHistory");
        var uris = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in new[] { "wikidata-events.csv", "wikidata-inception.csv", "wikidata-spans.csv" })
        {
            var path = Path.Combine(folder, file);
            if (File.Exists(path))
            {
                uris.UnionWith(Essenthos.Core.Loading.Encyclopedia.Csv.Read(path).Select(row => row["e"]));
            }
        }

        uris.Should().NotBeEmpty("the Wikidata exports are copied to the output folder beside the tests");
        return uris;
    }
}
