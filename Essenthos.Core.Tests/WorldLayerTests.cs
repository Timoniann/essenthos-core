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
