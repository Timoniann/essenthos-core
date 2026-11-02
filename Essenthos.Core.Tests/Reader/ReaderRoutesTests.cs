using System.Text.RegularExpressions;
using Essenthos.Core;
using Essenthos.Core.Pages;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The server answers 404 for an address <see cref="PageAddress.Parse"/> does not know, so it has to know
/// every address the reader's router serves. Nothing else fails when they part: the reader still draws the
/// page over the 404, and only a search engine sees it.
/// </summary>
public sealed partial class ReaderRoutesTests
{
    /// <summary>A catalogue of components for whoever works on them, linked from nowhere and no page to find.</summary>
    private static readonly string[] NotForASearchEngine = ["/dev/ui"];

    private static readonly IReadOnlyDictionary<string, string> Samples = new Dictionary<string, string>
    {
        ["$book"] = "mark",
        ["$chapter"] = "3",
        ["$number"] = "H430",
        ["$slug"] = "abram",
        ["$id"] = "12",
    };

    [Fact]
    public void TheServerKnowsEveryAddressTheRouterServes()
    {
        var unknown = RouterPaths()
            .Except(NotForASearchEngine)
            .Where(route => PageAddress.Parse(Sampled(route)).Kind == PageKind.NotFound)
            .ToList();

        unknown.Should().BeEmpty("each of these would answer 404 while the reader shows the page");
    }

    [Fact]
    public void TheRouterServesEverySectionTheServerKnows()
    {
        var routes = RouterPaths();

        PageSections.All.Select(section => section.Path).Except(routes).Should().BeEmpty();
    }

    [Theory]
    [InlineData("/settings")]
    [InlineData("/uk/settings")]
    [InlineData("/pages")]
    [InlineData("/lexicon/varied")]
    public void APageTheReaderGainedLaterIsAPageHereToo(string address)
    {
        PageAddress.Parse(address).Kind.Should().Be(PageKind.Section);
    }

    [Theory]
    [InlineData(200, 1, false)]
    [InlineData(200, RequestTiming.PageSampleEvery, true)]
    [InlineData(308, 1, true)]
    [InlineData(404, 3, true)]
    public void ThePageLogKeepsEveryOtherAnswerAndATenthOfThe200s(int status, long answered, bool said)
    {
        RequestTiming.SaysPage(status, answered).Should().Be(said);
    }

    [Fact]
    public void ACallerCannotForgeALineOfTheLog()
    {
        RequestTiming.Printable("evil\r\ninfo: forged", 100).Should().Be("evil??info: forged");
        RequestTiming.Printable(new string('a', 500), 160).Should().HaveLength(160);
        RequestTiming.Printable(null, 160).Should().Be("-");
    }

    private static string Sampled(string route) =>
        Samples.Aggregate(route, (address, sample) => address.Replace(sample.Key, sample.Value, StringComparison.Ordinal));

    /// <summary>The routes in the web repository's <c>src/router.tsx</c>, in the checkout beside this one.</summary>
    private static List<string> RouterPaths()
    {
        var router = Path.Combine("essenthos-web", "src", "router.tsx");
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, router)))
        {
            folder = folder.Parent;
        }

        folder.Should().NotBeNull($"{router} stands beside essenthos-core in the workspace, and this test reads it");
        var routes = RoutePath()
            .Matches(File.ReadAllText(Path.Combine(folder!.FullName, router)))
            .Select(match => match.Groups[1].Value)
            .ToList();
        routes.Should().Contain("/read/$book/$chapter", "the router's routes are written as this test reads them");
        return routes;
    }

    [GeneratedRegex("""^\s*path: "(/[^"]*)",?\s*$""", RegexOptions.Multiline)]
    private static partial Regex RoutePath();
}
