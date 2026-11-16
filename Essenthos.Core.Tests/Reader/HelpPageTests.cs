using Essenthos.Core.Pages;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The help pages are written in full by the server from the reader's help files, so that an assistant
/// fetching one without running its script can still tell a reader where to press.
/// </summary>
public sealed class HelpPageTests
{
    private static readonly PageWording Ukrainian = PageWording.Parse("uk", """
        {
          "help": {
            "eyebrow": "Довідка", "title": "Як користуватися Essenthos", "lead": "Короткі сторінки.",
            "topics": "Що ви хочете зробити?", "steps": "Що натиснути", "tips": "Варто знати",
            "open": "Відкрити сторінку", "all": "Уся довідка", "picture": "{{title}}: як це виглядає",
            "keys": "На Mac замість Ctrl натискайте ⌘.",
            "pages": {
              "map": {
                "title": "Знайти місце на карті", "summary": "Кожне місце.", "page": "/map",
                "steps": { "1": "Відкрийте «Карту».", "2": "Введіть назву.", "10": "Десятий крок." },
                "tips": { "1": "Кнопки праворуч." }
              },
              "search": {
                "title": "Шукати в тексті", "summary": "Одне поле.", "page": "/search",
                "steps": { "1": "Натисніть Ctrl K." }, "tips": { "1": "Є й інші поля." }
              }
            }
          }
        }
        """).Over(PageWording.Compiled("uk"));

    [Theory]
    [InlineData("/help", null)]
    [InlineData("/uk/help", null)]
    [InlineData("/help/map", "map")]
    [InlineData("/de/help/bookmarks", "bookmarks")]
    public void AHelpAddressIsAHelpPage(string address, string? topic)
    {
        var parsed = PageAddress.Parse(address);

        parsed.Kind.Should().Be(PageKind.Help);
        parsed.Slug.Should().Be(topic);
    }

    [Fact]
    public void ATopicIsWrittenWithEveryStepInOrderAndItsPicture()
    {
        var uk = SiteLanguage.Prefixed("uk")!;
        var page = PageViews.Help(uk, Ukrainian, PageAddress.Parse("/uk/help/map"))!;

        page.Heading.Should().Be("Знайти місце на карті");
        page.Canonical.Should().Be("/help/map");
        page.Body.Should().ContainAll("<h2>Що натиснути</h2>", "<h2>Варто знати</h2>", "Кнопки праворуч.",
            "src=\"/help/uk/map.webp\"", "alt=\"Знайти місце на карті: як це виглядає\"",
            "href=\"/uk/map\"", "href=\"/uk/help\"");
        page.Body.IndexOf("Введіть назву.", StringComparison.Ordinal).Should()
            .BeLessThan(page.Body.IndexOf("Десятий крок.", StringComparison.Ordinal), "step 10 comes after step 2");
    }

    [Fact]
    public void TheIndexListsEveryTopicInTheOrderTheFilesGive()
    {
        var page = PageViews.Help(SiteLanguage.Prefixed("uk")!, Ukrainian, PageAddress.Parse("/uk/help"))!;

        page.Heading.Should().Be("Як користуватися Essenthos");
        page.Body.Should().Contain("href=\"/uk/help/map\"").And.Contain("href=\"/uk/help/search\"");
        page.Body.IndexOf("/uk/help/map", StringComparison.Ordinal).Should()
            .BeLessThan(page.Body.IndexOf("/uk/help/search", StringComparison.Ordinal));
    }

    [Fact]
    public void ATopicTheFilesDoNotHoldOrAShellWithoutHelpIsNoPage()
    {
        PageViews.Help(SiteLanguage.English, Ukrainian, PageAddress.Parse("/help/nothing")).Should().BeNull();
        PageViews.Help(SiteLanguage.English, PageWording.Compiled("en"), PageAddress.Parse("/help")).Should().BeNull();
    }
}
