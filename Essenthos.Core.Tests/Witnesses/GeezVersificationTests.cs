using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// The Ge'ez numbers a third of the chapters it shares with the Greek its own way. Its 1 Chronicles 7
/// runs to 44 where the Greek runs to 40, and its 7:34, the sons of Asher, is the Greek 7:30: the
/// reading in <c>Resources/Essenthos/versification/geez.tsv</c> says so, and the frame stands it there.
/// </summary>
public sealed class GeezVersificationTests
{
    private const int Numbers = 4;

    private const int FirstChronicles = 13;

    [Fact]
    public void TheReadingPutsFirstChroniclesSevenWhereTheGreekNumbersIt()
    {
        GeezVersification.ByVerse[(FirstChronicles, 7, 34)].Greek.Should().Equal((7, 30));
        GeezVersification.ByVerse[(FirstChronicles, 7, 41)].Greek.Should().Equal((7, 37));
        var third = GeezVersification.ByVerse[(FirstChronicles, 7, 3)];
        third.Relation.Should().Be("part-of");
        third.Greek.Should().Equal((7, 2));
        GeezVersification.ByVerse.Should().NotContainKey((FirstChronicles, 7, 1), "the chapter's first verse is where its number is");
    }

    [Fact]
    public void TheReadingLeavesTheBooksTheChurchDividesItsOwnWayToTheirMap()
    {
        GeezVersification.Lines.Should().NotBeEmpty()
            .And.OnlyContain(line => !GeezVerseMap.Books.Contains(line.Book))
            .And.OnlyContain(line => line.Share == null || (line.Share >= 0 && line.Share <= 1));
    }

    [Fact]
    public void ALineNamingNoGreekVerseMustSayTheGreekHasNone()
    {
        var parse = () => GeezVersification.Parse("13\t7:34\t-\tequals\t-\t0\n");

        parse.Should().Throw<FormatException>();
        GeezVersification.Parse("13\t7:9\t7:7;7:8-9\tspans\t0.75\t4\n").Single().Greek
            .Should().Equal((7, 7), (7, 8), (7, 9));
    }

    /// <summary>
    /// A verse the reading lists stands at the row of the Greek verse it answers; one the Greek has
    /// nothing for — the Ge'ez Numbers 1:4, the end of the Greek 1:3 — stands with the verse before it.
    /// </summary>
    [Fact]
    public void TheFrameStandsAListedVerseWhereTheReadingPutsIt()
    {
        PlacedVerse[] verses =
        [
            .. Enumerable.Range(1, 44).Select(verse => new PlacedVerse(verse, FirstChronicles, 7, verse, string.Empty, 50)),
            .. Enumerable.Range(1, 6).Select(verse => new PlacedVerse(100 + verse, Numbers, 1, verse, string.Empty, 50)),
        ];
        var rules = new VersificationRules(TestResources.Versification, [], new HashSet<TvtmsCorrection>(), new HashSet<TvtmsSupplement>(),
            new Dictionary<AdditionVerse, CanonicalReference>());

        var placed = CanonicalFrameLoader.Expected(rules, Versification.Septuagint, verses, read: GeezVersification.Placements)
            .Where(reference => reference.IsPrimary)
            .ToDictionary(reference => reference.VerseId, reference => (reference.Book, reference.Chapter, reference.Verse));

        placed[1].Should().Be((FirstChronicles, 7, 1));
        placed[34].Should().Be((FirstChronicles, 7, 30));
        placed[44].Should().Be((FirstChronicles, 7, 40));
        placed[104].Should().Be((Numbers, 1, 3));
        placed[105].Should().Be((Numbers, 1, 4));
    }
}

/// <summary>
/// Esther and the Psalter stand where the reading says they answer the Greek, not at their own numbers:
/// a name read at the Ge'ez's own number would stand in another chapter.
/// </summary>
public sealed class GeezPlacedByTheMapTests
{
    private const int Esther = 17;
    private const int Psalms = 19;

    private static IEnumerable<(int Chapter, int Verse)> Rows(int book, int chapter, int verse) =>
        GeezVersification.Placements[(book, chapter, verse)].Select(row => (row.Chapter, row.Verse));

    [Theory]
    [InlineData(Esther, 3, 1, 2, 1)]
    [InlineData(Esther, 1, 1, 11, 2)]
    [InlineData(Esther, 1, 17, 12, 6)]
    [InlineData(Esther, 4, 14, 13, 1)]
    [InlineData(Esther, 5, 16, 13, 8)]
    [InlineData(Esther, 5, 42, 14, 19)]
    [InlineData(Esther, 10, 18, 16, 18)]
    [InlineData(Esther, 12, 14, 10, 13)]
    [InlineData(Esther, 13, 1, 11, 1)]
    [InlineData(Psalms, 13, 11, 14, 7)]
    [InlineData(Psalms, 136, 11, 137, 8)]
    public void AVerseStandsAtTheRowItAnswers(int book, int chapter, int verse, int rowChapter, int rowVerse) =>
        Rows(book, chapter, verse).First().Should().Be((rowChapter, rowVerse));

    [Fact]
    public void AVerseThatHoldsSeveralStandsAtEachOfThem() =>
        Rows(Esther, 11, 6).Should().Equal((9, 7), (9, 8), (9, 9), (9, 10));

    [Fact]
    public void VersesAndRowsOfOneCountGoOneToOne()
    {
        Rows(Esther, 2, 7).Should().Equal((1, 7));
        Rows(Esther, 2, 8).Should().Equal((1, 8));
    }

    [Fact]
    public void ALineOfFewerRowsThanVersesLeavesEveryVerseARow()
    {
        Rows(Psalms, 7, 6).Should().NotBeEmpty();
        Rows(Psalms, 7, 9).Should().NotBeEmpty();
        Rows(Psalms, 7, 6).First().Should().Be((7, 6));
        Rows(Psalms, 7, 9).Last().Should().Be((7, 8));
    }

    [Fact]
    public void TheOtherBooksTheChurchDividesKeepTheirOwnNumbers() =>
        GeezVersification.Placements.Keys.Should().NotContain(key => key.Book == 18 || key.Book == 22 || key.Book == 75);
}
