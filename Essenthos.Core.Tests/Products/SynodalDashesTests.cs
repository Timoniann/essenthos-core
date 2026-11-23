using Essenthos.Core.Conversion;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.XmlBible;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// The Synodal's «--» printed as the edition's em dash (PRB-1994), with a space on either side, in the trailers only;
/// the hyphenated words bible4u broke at «-- » are left as it prints them.
/// </summary>
public class SynodalDashesTests
{
    [Theory]
    [InlineData("--", false, " — ")]
    [InlineData("; --", false, "; — ")]
    [InlineData(", --", false, ", — ")]
    [InlineData(" --", false, " — ")]
    [InlineData("--'", false, " — '")]
    [InlineData(", --", true, ", —")]
    [InlineData("--", true, " —")]
    [InlineData("?' --", true, "?' —")]
    public void TwoHyphensAreAnEmDashWithASpaceOnEitherSide(string trailer, bool last, string dashed) =>
        SynodalDashes.Dashed(trailer, last).Should().Be(dashed);

    [Theory]
    [InlineData("-- ")]
    [InlineData(", ")]
    [InlineData(", — ")]
    [InlineData(" ")]
    public void ATrailerWithoutADoubleHyphenOrABrokenHyphenatedWordIsLeftAsItIs(string trailer) =>
        SynodalDashes.Dashed(trailer, false).Should().BeSameAs(trailer);

    /// <summary>Genesis 1:29 and 8:21 and Leviticus 11:38 as bible4u's file prints them, through the bible4u tokeniser.</summary>
    [Fact]
    public void TheStageChangesTheSynodalsTrailersAndNothingElse()
    {
        var text = Converted(Bible4uTextSource.Synodal,
            "и семя; --вам [сие] будет в пищу;",
            "помышление сердца человеческого--зло от юности его, --",
            "упадет на него что-- нибудь от трупа их");
        var before = text.Verses.Select(verse => verse.Words.Select(word => word.Surface).ToList()).ToList();

        new SynodalDashes().Apply(text, string.Empty);

        text.Verses.Select(verse => verse.Words.Select(word => word.Surface).ToList()).Should().BeEquivalentTo(before, options => options.WithStrictOrdering());
        Printed(text.Verses[0]).Should().Be("и семя; — вам сие будет в пищу;");
        Printed(text.Verses[1]).Should().Be("помышление сердца человеческого — зло от юности его, —");
        Printed(text.Verses[2]).Should().Be("упадет на него что-- нибудь от трупа их");
        text.Verses[0].Words.Single(word => word.Surface == "сие").Supplied.Should().Be(1);
        text.Provenance.Should().ContainSingle(note => note.Key == "dashes");
    }

    [Fact]
    public void NoOtherTextIsDashed() =>
        new SynodalDashes().AppliesTo("UKR1871").Should().BeFalse();

    private static string Printed(ConvertedVerse verse) => string.Concat(verse.Words.Select(word => word.Surface + word.Trailer));

    private static ConvertedText Converted(string slug, params string[] verses) => TextConverter.From(new TextSource(
        new TextDefinition(slug, "A test text", null, TextKind.Translation, "ru", TextDirection.LeftToRight, Versification.English,
            1876, "https://example.org", null, "Public Domain", null, Redistribution.PublicDomain, null),
        [
            new BookDraft(1, 1, "Genesis", "gen",
            [
                new ChapterDraft(1, [.. verses.Select((verse, at) => new VerseDraft(at + 1,
                    [.. VerseWords.Parse(verse).Select(token => new WordDraft(token.Word, token.Trailer, SuppliedSpan: token.SuppliedSpan))]))]),
            ]),
        ]));
}
