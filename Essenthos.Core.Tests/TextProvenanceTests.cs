using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What each text of the corpus says about itself, checked rather than trusted.
///
/// The question these answer is one a reader asked and the corpus could not: *which* Ukrainian
/// Bible is this. The row said "Ukrainian Bible", 1962, no translator — and 1962 is when a
/// translation finished in 1940 was first printed, so even the one number there was pointed at the
/// wrong thing.
///
/// A text is loaded once and read for years, so a missing attribution is not caught by using the
/// product. These are the check that it is there, and they are deliberately about every text rather
/// than the one that prompted them: the next text loaded should fail here until somebody has
/// established who made it.
/// </summary>
public sealed class TextProvenanceTests
{
    private static TextDefinition[] All =>
    [
        BhsaTextSource.Definition,
        NestleTextSource.Definition,
        SeptuagintTextSource.Definition(),
        TextusReceptusTextSource.Definition(Edition.Scrivener1894),
        TextusReceptusTextSource.Definition(Edition.Stephanus1550),
        ByzantineTextSource.Definition,
        SamaritanTextSource.Definition,
        BereanTextSource.Definition,
        .. Bible4uTextSource.Definitions.Values,
    ];

    private static TextDefinition Of(string slug) => All.Single(definition => definition.Slug == slug);

    [Fact]
    public void TheCorpusHoldsElevenTexts() => All.Should().HaveCount(11);

    /// <summary>
    /// Every text says what it is. A licence and a year identify a file, not an edition, and the
    /// columns beside them cannot hold the sentence that actually answers the reader's question.
    /// </summary>
    [Theory]
    [InlineData("bhsa")]
    [InlineData("nestle1904")]
    [InlineData("lxx-brenton")]
    [InlineData("scrivener1894")]
    [InlineData("stephanus1550")]
    [InlineData("robinsonpierpont2018")]
    [InlineData("sp")]
    [InlineData("bsb")]
    [InlineData("kjv")]
    [InlineData("rusv")]
    [InlineData("ukr")]
    public void EveryTextSaysWhatItIs(string slug) =>
        Of(slug).About.Should().NotBeNullOrWhiteSpace();

    /// <summary>
    /// Somebody is named for every text: whoever put it into its language, or whoever established
    /// the edition. Both may be present and one must be — a text with neither is one nobody has
    /// looked into, which is the state the whole corpus was in.
    /// </summary>
    [Theory]
    [InlineData("bhsa")]
    [InlineData("nestle1904")]
    [InlineData("lxx-brenton")]
    [InlineData("scrivener1894")]
    [InlineData("stephanus1550")]
    [InlineData("robinsonpierpont2018")]
    [InlineData("sp")]
    [InlineData("bsb")]
    [InlineData("kjv")]
    [InlineData("rusv")]
    [InlineData("ukr")]
    public void EveryTextNamesWhoMadeIt(string slug)
    {
        var definition = Of(slug);
        var named = !string.IsNullOrWhiteSpace(definition.Translators)
                    || !string.IsNullOrWhiteSpace(definition.Editors);

        named.Should().BeTrue(
            $"\"{slug}\" names neither a translator nor an editor. Establish who made it and set one of "
            + "them; leave both null only where nobody is known, and say so in About.");
    }

    /// <summary>
    /// Every translation names whoever translated it, as a person or as the body that did it. The
    /// King James has no single translator and naming one would be as false as naming none, which
    /// is why the field takes a body — not why it may be left empty.
    /// </summary>
    [Theory]
    [InlineData("kjv")]
    [InlineData("rusv")]
    [InlineData("ukr")]
    [InlineData("bsb")]
    public void EveryTranslationNamesItsTranslators(string slug)
    {
        var definition = Of(slug);

        definition.Kind.Should().Be(TextKind.Translation);
        definition.Translators.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>The question that started this, asked of the row that could not answer it.</summary>
    [Fact]
    public void TheUkrainianTextIsOhienkosAndSaysSo()
    {
        var ukr = Of("ukr");

        ukr.Name.Should().Be("Ohienko Bible");
        ukr.Translators.Should().Contain("Ohienko");
        ukr.Edition.Should().Contain("1962");
        ukr.Citation.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The rights on the Ukrainian text are contested and the row has to say so. Three sources call
    /// it public domain by copying one another; the Society whose notice is on the same text has
    /// never been asked. A reader deciding whether to republish must meet that, not discover it.
    /// </summary>
    [Fact]
    public void TheUkrainianTextCarriesTheNoticeThatDisputesItsLicence()
    {
        var ukr = Of("ukr");

        ukr.RightsNote.Should().Contain("British and Foreign Bible Society");
        ukr.RightsHolder.Should().Contain("British and Foreign Bible Society");
    }

    /// <summary>
    /// A publication year alone says the wrong thing about a revised text. Every digital King James
    /// is the modern standard text, and a row reading 1611 invites a reader to quote it as the 1611
    /// printing, which it is not.
    /// </summary>
    [Fact]
    public void TheKingJamesSaysWhichEditionItIs()
    {
        var kjv = Of("kjv");

        kjv.PublishedYear.Should().Be(1611);
        kjv.EditionYear.Should().Be(1769);
        kjv.Edition.Should().Contain("not the 1611 printing");
    }

    /// <summary>
    /// An edition year is set only where it differs from publication. Repeating the same number in
    /// both fields would make "this is a revision" indistinguishable from "nobody looked".
    /// </summary>
    [Fact]
    public void AnEditionYearIsOnlySetWhereItDiffersFromPublication() =>
        All.Where(definition => definition.EditionYear is not null)
            .Should().OnlyContain(definition => definition.EditionYear != definition.PublishedYear);
}
