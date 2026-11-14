using System.Text.RegularExpressions;
using Essenthos.Core.Pages;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// What an event's and a period's page carry for a search engine: everything the reader's page shows
/// of them, in the page language's words wherever the corpus has them, and a title and description
/// that tell one language's copy from another's.
/// </summary>
public sealed class ChronologyPageTests
{
    private const string Origin = "https://essenthos.org";

    private const string Shell = """
        <!doctype html>
        <html lang="en"><head><title>Essenthos</title></head>
        <body><div id="root"></div></body></html>
        """;

    private static readonly PageWording Ukrainian = PageWording.Compiled("uk");

    [Fact]
    public void AnEventsPageGivesItsDatesItsVerseQuotedAndWhomItHappenedTo()
    {
        var view = new EventView("birtheli1", "Народження Ілія", "1214 до н. е.")
        {
            Description = "Now Eli was ninety-eight years old.",
            DescriptionInEnglish = true,
            Dates =
            [
                new ReckonedDate("Ussher", PageViews.Year(Ukrainian, 1214, false), null),
                new ReckonedDate("BibleData", PageViews.Year(Ukrainian, 1124, false), null),
            ],
            Passage = new PageLink("/read/1-samuel/4/15", "1 Самуїлова 4:15"),
            Quote = "А Ілій був віку дев'ятидесяти й восьми літ.",
            Person = new PageLink("/people/eli", "Ілій"),
            Source = "BibleData",
        };

        var page = PageViews.Event(SiteLanguage.Ukrainian, Ukrainian, view);
        var html = PageHtml.Render(Shell, page, SiteLanguage.Ukrainian, Origin);

        Title(html).Should().Be("Народження Ілія (1214 до н. е.) · Essenthos");
        page.Description.Should().StartWith("Народження Ілія (1214 до н. е.): ",
            "the source's English describes no page in another language");
        page.Body.Should().Contain("<p lang=\"en\">Now Eli was ninety-eight years old.</p>")
            .And.Contain("<h2>Коли це сталося</h2>")
            .And.Contain("<li>BibleData: 1124 до н. е.</li>")
            .And.Contain("<a href=\"/uk/read/1-samuel/4/15\">1 Самуїлова 4:15</a> А Ілій")
            .And.Contain("<a href=\"/uk/people/eli\">Ілій</a>")
            .And.Contain("Узято з BibleData");
        page.Canonical.Should().Be("/events/birtheli1");
    }

    [Fact]
    public void APeriodsPageGivesItsSpanInEachReckoningAndTheEventsInsideIt()
    {
        var english = PageWording.Compiled("en");
        var view = new PeriodView("period-oppression", "Oppression by the Philistines",
            PageViews.AxisSpan(english, 2806, 2866, 3961))
        {
            Notes = "One end of this is inferred from another figure rather than stated in the text.",
            Parent = new PageLink("/periods/era-judges", "The judges"),
            Spans =
            [
                new ReckonedSpan("Ussher", PageViews.AxisSpan(english, 2806, 2866, 3961)),
                new ReckonedSpan("Seder Olam", PageViews.AxisSpan(english, 3761, 3761, 3761)),
            ],
            Opens = new PageLink("/events/begin", "Beginning of the oppression"),
            Inside = [(new PageLink("/events/birthdavid", "Birth of David"), PageViews.AxisYear(english, 2921, 3961))],
            InsideTotal = 26,
            Nested = [new PageLink("/periods/ark", "The ark among the Philistines")],
        };

        var page = PageViews.Period(SiteLanguage.English, english, view);

        page.Title.Should().Be("Oppression by the Philistines (1156 BCE – 1096 BCE) · Essenthos");
        page.Description.Should().Be(
            "Oppression by the Philistines (1156 BCE – 1096 BCE). One end of this is inferred from another figure rather than stated in the text.");
        page.Body.Should().Contain("<nav><a href=\"/\">Essenthos</a> › <a href=\"/periods/era-judges\">The judges</a></nav>")
            .And.Contain("<li>Seder Olam: 1 BCE</li>")
            .And.Contain("<li>Opens at: <a href=\"/events/begin\">Beginning of the oppression</a></li>")
            .And.Contain("<h2>The events inside it (26)</h2>")
            .And.Contain("<a href=\"/events/birthdavid\">Birth of David</a> (1041 BCE)")
            .And.Contain("<a href=\"/periods/ark\">The ark among the Philistines</a>");
    }

    [Fact]
    public void AYearPastTheTurnIsOfTheCommonEra()
    {
        PageViews.AxisYear(Ukrainian, 3961, 3961).Should().Be("1 до н. е.");
        PageViews.AxisYear(Ukrainian, 3962, 3961).Should().Be("1 н. е.");
        PageViews.AxisYear(PageWording.Compiled("de"), 3920, 3961).Should().Be("42 v. u. Z.");
    }

    [Fact]
    public void WordingTheWebBuildDoesNotHaveYetIsTakenFromTheApisOwn()
    {
        var older = PageWording.Parse("uk", """{"event": {"description": "{{name}}: старе"}}""").Over(Ukrainian);

        older.Text("event.description", ("name", "X")).Should().Be("X: старе");
        older.Text("event.when").Should().Be("Коли це сталося");
    }

    private static string Title(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value);
}
