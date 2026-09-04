using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class BookReferencesTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("66", 66)]
    [InlineData("genesis", 1)]
    [InlineData("GENESIS", 1)]
    [InlineData("ruth", 8)]
    [InlineData("1-samuel", 9)]
    [InlineData("1 Samuel", 9)]
    [InlineData("song-of-solomon", 22)]
    [InlineData("revelation", 66)]
    public void ResolvesOrdinalsAndSlugs(string book, int expected)
    {
        BookReferences.ResolveOrdinal(book).Should().Be(expected);
    }

    [Theory]
    [InlineData("Gen", 1)]
    [InlineData("Numeri", 4)]
    [InlineData("Samuel_I", 9)]
    [InlineData("Judices", 7)]
    [InlineData("Canticum", 22)]
    public void ResolvesTheNamesTheCorpusFilesUse(string book, int expected)
    {
        BookReferences.ResolveOrdinal(book).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("85")]
    [InlineData("-1")]
    [InlineData("nope")]
    [InlineData("The Shepherd of Hermas")]
    public void RejectsAnythingThatIsNotABookItKnows(string? book)
    {
        BookReferences.ResolveOrdinal(book).Should().BeNull();
    }

    [Theory]
    [InlineData("67", 67)]
    [InlineData("1 Maccabees", 73)]
    [InlineData("Tobit", 70)]
    public void ResolvesTheDeuterocanonToo(string book, int ordinal)
    {
        // This used to be the rejection list. The frame stopped at 66 and the deuterocanon was
        // "outside the canon" — but whose canon was never asked, and the answer differs by reader
        // (DOC-0090). A reference resolving is not a claim that any loaded text has the book.
        BookReferences.ResolveOrdinal(book).Should().Be(ordinal);
    }

    [Theory]
    [InlineData(1, "genesis")]
    [InlineData(8, "ruth")]
    [InlineData(9, "1-samuel")]
    [InlineData(22, "song-of-solomon")]
    [InlineData(66, "revelation")]
    public void SlugsAreUrlSafeAndRoundTrip(int ordinal, string slug)
    {
        BookReferences.Slug(ordinal).Should().Be(slug);
        BookReferences.ResolveOrdinal(slug).Should().Be(ordinal);
    }

    [Fact]
    public void EverySlugInTheCanonIsDistinctAndResolvable()
    {
        var slugs = BookReferences.Ordinals.Select(BookReferences.Slug).ToList();
        slugs.Should().OnlyHaveUniqueItems();
        slugs.Should().OnlyContain(s => s.All(c => char.IsLetterOrDigit(c) || c == '-'));
        foreach (var ordinal in BookReferences.Ordinals)
        {
            BookReferences.ResolveOrdinal(BookReferences.Slug(ordinal)).Should().Be(ordinal);
        }
    }

    [Theory]
    [InlineData(1, "old")]
    [InlineData(39, "old")]
    [InlineData(40, "new")]
    [InlineData(66, "new")]
    public void TestamentSplitsAtMalachi(int ordinal, string testament)
    {
        BookReferences.Testament(ordinal).Should().Be(testament);
    }

    /// <summary>
    /// The book codes of USFM 3.0, in canonical order. Every USFM file the corpus reads names its
    /// book with one of these, and a file whose book does not resolve is skipped without being
    /// read — so a gap here is data that silently never loads rather than an error anyone sees.
    /// </summary>
    private static readonly string[] UsfmBookCodes =
    [
        "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH",
        "2CH", "EZR", "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK",
        "DAN", "HOS", "JOL", "AMO", "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL",
        "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH",
        "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN", "JUD",
        "REV",
    ];

    [Fact]
    public void EveryUsfmBookCodeResolvesToItsOwnBook()
    {
        for (var at = 0; at < UsfmBookCodes.Length; at++)
        {
            BookReferences.ResolveOrdinal(UsfmBookCodes[at]).Should().Be(at + 1,
                $"USFM names that book {UsfmBookCodes[at]}");
        }
    }

    [Fact]
    public void FormatHintNamesTheAcceptedForms()
    {
        BookReferences.FormatHint("nope").Should().Contain("nope").And.Contain("genesis").And.Contain("/v1/books");
    }
}
