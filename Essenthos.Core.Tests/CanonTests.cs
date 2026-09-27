using Essenthos.Core.Corpus;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Utils;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A canon decides which books exist, in what order and under what heading. Getting one
/// wrong is not a crash — it is a book quietly missing from a reading order, or listed twice, or
/// numbered as something else. Nothing else would catch that, so it is caught here.
/// </summary>
public class CanonTests
{
    /// <summary>
    /// The Tanakh in BHSA's own order, read out of <c>book.position</c> for the BHSA text. The
    /// canon claims to be this order; this is the list it must equal. Taken from the database
    /// rather than from a book, because the claim being tested is that the corpus already holds it.
    /// </summary>
    private static readonly string[] BhsaOrder =
    [
        "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy",
        "Joshua", "Judges", "1 Samuel", "2 Samuel", "1 Kings", "2 Kings",
        "Isaiah", "Jeremiah", "Ezekiel",
        "Hosea", "Joel", "Amos", "Obadiah", "Jonah", "Micah", "Nahum", "Habakkuk", "Zephaniah",
        "Haggai", "Zechariah", "Malachi",
        "Psalms", "Job", "Proverbs", "Ruth", "Song of Solomon", "Ecclesiastes", "Lamentations",
        "Esther", "Daniel", "Ezra", "Nehemiah", "1 Chronicles", "2 Chronicles",
    ];

    [Fact]
    public void TheTanakhIsBhsaOwnOrder()
    {
        var tanakh = Canons.Find("tanakh")!;

        tanakh.Ordinals.Select(BookReferences.Name).Should().Equal(BhsaOrder);
    }

    [Fact]
    public void TheTanakhHoldsTheThirtyNineAndNothingElse()
    {
        var tanakh = Canons.Find("tanakh")!;

        tanakh.Ordinals.Order().Should().Equal(Enumerable.Range(1, 39));
    }

    [Fact]
    public void TheDefaultIsTheSixtySixInTheirUsualOrder()
    {
        var protestant = Canons.Find(null)!;

        protestant.Slug.Should().Be(Canons.Default);
        protestant.Ordinals.Should().Equal(Enumerable.Range(1, 66));
    }

    [Theory]
    [InlineData("protestant")]
    [InlineData("tanakh")]
    [InlineData("catholic")]
    [InlineData("orthodox")]
    [InlineData("septuagint")]
    [InlineData("ethiopian")]
    [InlineData("synodal")]
    public void NoCanonListsABookTwice(string slug)
    {
        var canon = Canons.Find(slug)!;

        canon.Ordinals.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("protestant")]
    [InlineData("tanakh")]
    [InlineData("catholic")]
    [InlineData("orthodox")]
    [InlineData("septuagint")]
    [InlineData("ethiopian")]
    [InlineData("synodal")]
    public void EveryOrdinalACanonNamesIsABookThatExists(string slug)
    {
        var canon = Canons.Find(slug)!;

        foreach (var ordinal in canon.Ordinals)
        {
            BibleBookAbbreviation.GetByOrdinal(ordinal).Should()
                .NotBeNull($"canon {slug} lists ordinal {ordinal}");
            BookReferences.IsInCanon(ordinal).Should().BeTrue($"canon {slug} lists ordinal {ordinal}");
        }
    }

    [Theory]
    [InlineData("catholic")]
    [InlineData("orthodox")]
    public void AWiderCanonHoldsAllSixtySix(string slug)
    {
        var canon = Canons.Find(slug)!;

        canon.Ordinals.Should().Contain(Enumerable.Range(1, 66));
    }

    [Fact]
    public void EveryBookIsUnderExactlyOneHeading()
    {
        foreach (var canon in Canons.List)
        {
            foreach (var ordinal in canon.Ordinals)
            {
                canon.Sections.Count(section => section.Ordinals.Contains(ordinal)).Should()
                    .Be(1, $"{BookReferences.Name(ordinal)} in canon {canon.Slug}");
            }
        }
    }

    [Fact]
    public void TheCatholicCanonPrintsTobitAndJudithBetweenNehemiahAndEsther()
    {
        var names = Canons.Find("catholic")!.Ordinals.Select(BookReferences.Name).ToList();

        names.SkipWhile(name => name != "Nehemiah").Take(5).Should()
            .Equal("Nehemiah", "Tobit", "Judith", "Esther", "Job");
    }

    [Fact]
    public void RuthSitsInDifferentPlacesInDifferentCanons()
    {
        // The reason a section cannot be a column on a book. Both of these are true at once.
        Canons.SectionOf(Canons.Find("tanakh")!, 8).Should().Be("ketuvim");
        Canons.SectionOf(Canons.Find("protestant")!, 8).Should().Be("old-testament");
    }

    [Fact]
    public void ACanonThatOmitsABookSaysSoRatherThanGuessing()
    {
        Canons.SectionOf(Canons.Find("tanakh")!, 40).Should().BeNull();
        Canons.SectionOf(Canons.Find("protestant")!, 70).Should().BeNull();
    }

    [Fact]
    public void OnlyTheHebrewScripturesAreNotCalledABible()
    {
        Canons.Find("tanakh")!.Collection.Should().Be("Scripture");
        Canons.Find("septuagint")!.Collection.Should().Be("Scripture");
        Canons.Find("protestant")!.Collection.Should().Be("Bible");
        Canons.Find("catholic")!.Collection.Should().Be("Bible");
        Canons.Find("orthodox")!.Collection.Should().Be("Bible");
        Canons.Find("ethiopian")!.Collection.Should().Be("Bible");
    }

    /// <summary>
    /// The Ethiopian canon is eighty-one books, and the count is the point: the church counts
    /// Proverbs as two books and the Maccabees not at all, so a list that reached eighty-one by
    /// another road would name different books.
    /// </summary>
    [Fact]
    public void TheEthiopianCanonIsFiftyFourAndTwentySeven()
    {
        var ethiopian = Canons.Find("ethiopian")!;

        ethiopian.BookCount.Should().Be(81);
        ethiopian.Sections.Select(section => section.Ordinals.Count).Should().Equal(54, 27);
        ethiopian.Ordinals.Should().Contain([85, 86, 87, 88, 89, 90, 91, 92]);
        ethiopian.Ordinals.Should().NotContain([20, 73, 74, 79, 82], "Proverbs is Messale and Tagsas, "
            + "Meqabyan stands where the Maccabees would, and Manasseh and Psalm 151 are inside other books");
    }

    /// <summary>
    /// The Synodal's seventy-seven, with the eleven books it marks as non-canonical standing where it
    /// prints them and marked by the heading they stand under. Its second book of Ezra is the Greek 1
    /// Esdras and its third the Latin 2 Esdras, so both are there and neither is Ezra.
    /// </summary>
    [Fact]
    public void TheSynodalMarksItsElevenNonCanonicalBooksWhereItPrintsThem()
    {
        var synodal = Canons.Find(Canons.Synodal)!;

        synodal.BookCount.Should().Be(77);
        synodal.Ordinals.Should().Contain(Enumerable.Range(1, 66)).And.OnlyHaveUniqueItems();
        synodal.Ordinals.Where(ordinal => Canons.SectionOf(synodal, ordinal) == "non-canonical")
            .Should().Equal(68, 70, 71, 75, 72, 76, 67, 73, 74, 80, 69);
        synodal.Ordinals.SkipWhile(ordinal => ordinal != 16).Skip(1).First().Should().Be(68, "2 Ezra follows Nehemiah");
        synodal.Ordinals.SkipWhile(ordinal => ordinal != 44).Skip(1).First().Should().Be(59, "James follows Acts");
    }

    [Fact]
    public void AnUnknownCanonIsNotSilentlyTheDefault()
    {
        Canons.Find("vulgate").Should().BeNull();
        Canons.Find("").Should().NotBeNull("an absent parameter means the default");
    }

    [Theory]
    [InlineData(70, "Tobit")]
    [InlineData(76, "Letter of Jeremiah")]
    [InlineData(77, "Susanna")]
    [InlineData(80, "3 Maccabees")]
    [InlineData(84, "Psalms of Solomon")]
    [InlineData(85, "1 Enoch")]
    [InlineData(86, "Jubilees")]
    [InlineData(89, "3 Meqabyan")]
    [InlineData(90, "4 Baruch")]
    [InlineData(92, "Tagsas")]
    public void TheDeuterocanonResolvesByItsOwnSlug(int ordinal, string name)
    {
        BookReferences.Name(ordinal).Should().Be(name);
        BookReferences.ResolveOrdinal(BookReferences.Slug(ordinal)).Should().Be(ordinal);
    }

    [Theory]
    [InlineData("LJE", 76)]
    [InlineData("SUS", 77)]
    [InlineData("BEL", 78)]
    [InlineData("MAN", 79)]
    [InlineData("3MA", 80)]
    [InlineData("4MA", 81)]
    [InlineData("TOB", 70)]
    [InlineData("WIS", 75)]
    [InlineData("ENO", 85)]
    [InlineData("JUB", 86)]
    [InlineData("1MQ", 87)]
    [InlineData("4BA", 90)]
    public void TheSeptuagintFileNamesResolve(string code, int ordinal)
    {
        // What Brenton's USFM files are called. The Septuagint loads by these, so a code that
        // does not resolve is a book that silently fails to load.
        BookReferences.ResolveOrdinal(code).Should().Be(ordinal);
    }
}
