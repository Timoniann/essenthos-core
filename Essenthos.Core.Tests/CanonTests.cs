using Essenthos.Core.Endpoints;
using Essenthos.Core.Utils;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A canon decides which books exist, in what order and under what heading (DOC-0090). Getting one
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
    public void TheSeptuagintFileNamesResolve(string code, int ordinal)
    {
        // What Brenton's USFM files are called. TSK-0020 loads by these, so a code that does not
        // resolve is a book that silently fails to load.
        BookReferences.ResolveOrdinal(code).Should().Be(ordinal);
    }
}
