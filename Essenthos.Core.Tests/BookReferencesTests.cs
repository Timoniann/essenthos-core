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
    [InlineData("67")]
    [InlineData("-1")]
    [InlineData("nope")]
    [InlineData("1 Maccabees")]
    public void RejectsAnythingOutsideTheCanon(string? book)
    {
        BookReferences.ResolveOrdinal(book).Should().BeNull();
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

    [Fact]
    public void FormatHintNamesTheAcceptedForms()
    {
        BookReferences.FormatHint("nope").Should().Contain("nope").And.Contain("genesis").And.Contain("/v1/books");
    }
}
