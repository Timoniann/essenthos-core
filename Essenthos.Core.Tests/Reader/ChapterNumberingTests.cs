using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A reader's address in its first text's numbering, and a chapter's number in the other numbering tradition, as the
/// server's copy of a chapter heads and titles it: the same answers the reader gives (essenthos-web numbering.test.ts,
/// otherNumbering.test.ts).
/// </summary>
public sealed class ChapterNumberingTests
{
    /// <summary>The Synodal: its 9 runs on into the frame's 10 from verse 22, its 50 opens with two verses of title.</summary>
    private static readonly NumberedAddressResponse[] Synodal =
    [
        new(9, 2, 9, 1),
        new(9, 22, 10, 1),
        new(49, 1, 50, 1),
        new(50, 1, 51, 1),
        new(50, 2, 51, 1),
        new(50, 3, 51, 1),
        new(90, 1, 91, 1),
        new(113, 1, 114, 1),
        new(113, 9, 115, 1),
        new(114, 1, 116, 1),
        new(115, 1, 116, 10),
    ];

    /// <summary>A Greek psalter, one lower from 10 to 147.</summary>
    private static NumberedAddressResponse[] Greek()
    {
        var addresses = new List<NumberedAddressResponse> { new(9, 22, 10, 1) };
        for (var frame = 11; frame <= 113; frame++)
        {
            addresses.Add(new(frame - 1, 1, frame, 1));
        }

        addresses.AddRange([new(113, 1, 114, 1), new(113, 9, 115, 1), new(114, 1, 116, 1), new(115, 1, 116, 10)]);
        for (var frame = 117; frame <= 146; frame++)
        {
            addresses.Add(new(frame - 1, 1, frame, 1));
        }

        addresses.AddRange([new(146, 1, 147, 1), new(147, 1, 147, 12)]);
        return [.. addresses];
    }

    [Fact]
    public void AnAddressInTheTextsNumberingOpensWhereTheFrameHoldsIt()
    {
        ChapterNumbering.InFrame(Synodal, 90, null).Should().Be((91, (int?)null));
        ChapterNumbering.InFrame(Synodal, 115, null).Should().Be((116, (int?)10));
        ChapterNumbering.InFrame(Synodal, 9, 22).Should().Be((10, (int?)1));
        ChapterNumbering.InFrame([], 23, 4).Should().Be((23, (int?)4));
    }

    [Fact]
    public void APlaceInTheFrameIsNamedByTheTextsNumberWithAVerseOnlyWhereItsChapterStartsInAnother()
    {
        ChapterNumbering.Own(Synodal, 91, null).Should().Be((90, (int?)null));
        ChapterNumbering.Own(Synodal, 51, null).Should().Be((50, (int?)null));
        ChapterNumbering.Own(Synodal, 10, null).Should().Be((9, (int?)22));
        ChapterNumbering.Own(Synodal, 115, null).Should().Be((113, (int?)9));
        ChapterNumbering.Own([], 51, null).Should().Be((51, (int?)null));
    }

    [Fact]
    public void AFramesChapterHoldsTheTextsChaptersWithTheVersesBeforeTheFirstItLists()
    {
        ChapterNumbering.Label(ChapterNumbering.OwnChapters(Synodal, 116)).Should().Be("114–115");
        ChapterNumbering.Label(ChapterNumbering.OwnChapters([new(3, 1, 2, 28), new(4, 1, 3, 1)], 2)).Should().Be("2–3");
        ChapterNumbering.Label(ChapterNumbering.OwnChapters(Synodal, 23)).Should().Be("23");
    }

    [Fact]
    public void AHebrewNumberedPsalmIsNamedByTheChurchsNumberAndLedToTheChurchsPsalmOfItsNumber()
    {
        ChapterNumbering.OtherOf("psalms", 50, 150, [50], Greek())
            .Should().Be(new ChapterNumbering.Other("greek", "49", (50, 51, null)));
        ChapterNumbering.OtherOf("psalms", 116, 150, [116], Greek())
            .Should().Be(new ChapterNumbering.Other("greek", "114–115", (116, 117, null)));
        ChapterNumbering.OtherOf("psalms", 115, 150, [115], Greek())
            .Should().Be(new ChapterNumbering.Other("greek", "113", (115, 116, 10)));
        ChapterNumbering.OtherOf("psalms", 9, 150, [9], Greek()).Should().BeNull();
    }

    [Fact]
    public void AGreekNumberedPsalmIsNamedByTheHebrewsNumber()
    {
        ChapterNumbering.OtherOf("psalms", 91, 150, [90], null)
            .Should().Be(new ChapterNumbering.Other("hebrewPsalms", "91", (90, 90, null)));
    }

    [Fact]
    public void JoelReadInTheEnglishNumberingIsNamedByTheHebrewsChapters()
    {
        NumberedAddressResponse[] hebrew = [new(3, 1, 2, 28), new(3, 5, 2, 32), new(4, 1, 3, 1)];
        ChapterNumbering.OtherOf("joel", 3, 3, [3], hebrew)
            .Should().Be(new ChapterNumbering.Other("hebrew", "4", (3, 2, 28)));
        ChapterNumbering.OtherOf("joel", 3, 3, [4], null)
            .Should().Be(new ChapterNumbering.Other("english", "3", null));
    }

    [Fact]
    public void APsalmsTitleCarriesBothItsNumbersAndItsHeadingTheLineToTheOther()
    {
        var page = PageViews.Chapter(SiteLanguage.Ukrainian, PageWording.Compiled("uk"),
            new ChapterView(19, "Псалми", 50, 150, [(1, "Бог богів")], "Біблія Огієнка")
            {
                OtherNumber = "49",
                Other = new OtherNumberingLine(
                    "У церковній нумерації (Септуагінта, Вульгата, слов'янська) це псалом 49.",
                    ("Шукаєте церковний псалом 50?", "Псалом 51", "/read/psalms/51")),
            });

        page.Title.Should().StartWith("Псалом 50 (49)");
        page.Body.Should().Contain("<h1>Псалми 50</h1><p class=\"reading-other\">У церковній нумерації (Септуагінта, Вульгата, слов&#39;янська) це псалом 49. Шукаєте церковний псалом 50? <a href=\"/uk/read/psalms/51\">→ Псалом 51</a></p>");
    }

    [Fact]
    public void AChapterAskedForInItsTextsNumberingIsHeadedSoAndKeepsTheFramesChapterAsItsCanonical()
    {
        var page = PageViews.Chapter(SiteLanguage.English, PageWording.Compiled("en"),
            new ChapterView(19, "Psalms", 91, 150, [(1, "Живущий")], "Синодальный перевод")
            {
                Heading = "90",
                AddressPath = "/read/psalms/90",
            });

        page.Body.Should().Contain("data-chapter=\"/read/psalms/90\"").And.Contain("<h1>Psalms 90</h1>");
        page.Canonical.Should().Be("/read/psalms/91");
    }
}
