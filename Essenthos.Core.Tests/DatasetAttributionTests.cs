using Essenthos.Core.Corpus;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the sources page says about the datasets beside the texts, where one file is not one work.
/// </summary>
public sealed class DatasetAttributionTests
{
    private static Datasets.Dataset Of(string id) => Datasets.All.Single(dataset => dataset.Id == id);

    /// <summary>
    /// The Hebrew lexicon file declares three works in its own header and assigns each a part of
    /// the entry: Strong's dictionaries, which are long out of copyright, and the Theological
    /// Wordbook of the Old Testament, which is not. One author and one licence over the whole file
    /// puts the wrong name on the TWOT reference of 6,070 entries and the wrong terms on all of it.
    /// </summary>
    [Fact]
    public void TheLexiconAttributesTheWordbookBoundIntoTheSameFile()
    {
        var strong = Of("strong");

        strong.Author.Should().Be("James Strong");
        strong.Licence.Should().Be("Public Domain");

        var twot = strong.Contains.Should().ContainSingle().Which;
        twot.Name.Should().Be("Theological Wordbook of the Old Testament");
        twot.Author.Should().Contain("Archer").And.Contain("Harris");
        twot.Licence.Should().Contain("Moody Bible Institute");
        twot.Covers.Should().Contain("TWOT");
    }

    /// <summary>
    /// A second work is the exception, not the shape. Every other dataset is one work under one
    /// licence, and a stray entry here would mean somebody had described a file they had not read.
    /// </summary>
    [Fact]
    public void NoOtherDatasetDeclaresASecondWork() =>
        Datasets.All
            .Where(dataset => dataset.Contains is not null)
            .Should().ContainSingle().Which.Id.Should().Be("strong");

    /// <summary>
    /// Share-alike is the one clause this project treats as a decision rather than a detail, and a
    /// licence name does not say what it costs. Every source carrying it has to say, in words, what
    /// the corpus owes on anything published from it — otherwise the obligation is discoverable
    /// only by someone who already knows what the four letters mean.
    /// </summary>
    [Fact]
    public void EveryShareAlikeSourceSaysWhatItObliges() =>
        Datasets.All
            .Where(dataset => dataset.Licence?.Contains("BY-SA", StringComparison.Ordinal) == true)
            .Should().NotBeEmpty()
            .And.OnlyContain(dataset =>
                dataset.Obliges != null && dataset.Obliges.Contains("ShareAlike"));

    /// <summary>
    /// Every source we carry states its licence where it is declared. Ours alone does not: what our
    /// own work is published under is the owner's to set for the site, in one place.
    /// </summary>
    [Fact]
    public void OnlyOurOwnWorkLeavesItsLicenceToTheSiteSetting() =>
        Datasets.All
            .Where(dataset => dataset.Licence is null || dataset.LicenceUrl is null)
            .Should().ContainSingle().Which.Id.Should().Be(Datasets.Own);

    /// <summary>
    /// unfoldingWord's condition is not in any Creative Commons licence: a derivative work must
    /// remove their trademark. It reaches the reader only if it is written down beside the licence
    /// that does not contain it.
    /// </summary>
    [Fact]
    public void TheUkrainianInterlinearCarriesTheTrademarkCondition()
    {
        var door43 = Of("unfoldingword");

        door43.Licence.Should().Be("CC BY-SA 4.0");
        door43.Obliges.Should().Contain("ShareAlike").And.Contain("trademark");
    }

    /// <summary>
    /// A NonCommercial source restricts what may be published from it as surely as a share-alike
    /// one does. This project is not commercial and accepts the clause; accepting it is not the
    /// same as leaving it unsaid.
    /// </summary>
    [Fact]
    public void TheNonCommercialMappingSaysSo() =>
        Of("openhebrewbible").Obliges.Should().Contain("NonCommercial");

    /// <summary>
    /// Every declared work is followable: a reader given a licence name and no link has been told
    /// the name of an obligation and not how to meet it.
    /// </summary>
    [Fact]
    public void EveryDeclaredWorkCanBeFollowed() =>
        Datasets.All
            .SelectMany(dataset => dataset.Contains ?? [])
            .Should().OnlyContain(work =>
                !string.IsNullOrWhiteSpace(work.Name)
                && !string.IsNullOrWhiteSpace(work.Author)
                && !string.IsNullOrWhiteSpace(work.Licence)
                && work.LicenceUrl.StartsWith("https://"));

    /// <summary>
    /// A model's reading of a verse is this project's, and the rows say so the way they begin — not
    /// the way a description of the method would.
    /// </summary>
    [Fact]
    public void AModelsReadingOfAVerseIsClaimedAsOurOwn() =>
        Datasets.Of("a reading of the verse by claude-sonnet-5, prompt sense-3, run to 2026-09-05")
            .Should().Be(Datasets.Own);

    /// <summary>
    /// A row nobody claims is listed as undeclared on the sources page, and the claim is an ordinal
    /// prefix match. The strings are taken from the loaders that write them, so a slug respelt or a
    /// method renamed in one place fails here rather than on the page.
    /// </summary>
    [Theory]
    [InlineData("luther1912-strong", "BHSA")]
    [InlineData("luther1912-strong", "NESTLE1904")]
    public void LuthersStrongPairingsAreCreditedToTheTagging(string dataset, string against) =>
        Datasets.Of(TaggedTextLinkLoader.Source(EbibleTextSource.Luther, against)).Should().Be(dataset);

    [Fact]
    public void TheSeptuagintLetterAlignmentIsClaimedAsOurOwn() =>
        Datasets.Of(SeptuagintLinkLoader.Source).Should().Be(Datasets.Own);

    /// <summary>
    /// A parsing is neither an entity nor a link, so a dataset that contributes only parsings is
    /// credited only if its declaration says it does and claims the string its rows carry.
    /// </summary>
    [Theory]
    [InlineData(MaculaAnnotationLoader.Source, "macula")]
    [InlineData(MorphGntParsingLoader.Source, "morphgnt")]
    public void EachSecondAnalysisOfTheGreekIsCredited(string source, string dataset)
    {
        Datasets.Of(source).Should().Be(dataset);
        Of(dataset).Parsings.Should().BeTrue();
    }

    /// <summary>MACULA's licence has one condition, and it is this exact string.</summary>
    [Fact]
    public void MaculaIsCitedInTheWordsItsLicenceRequires() =>
        Of("macula").Citation.Should().Be(
            "MACULA Greek Linguistic Datasets, available at https://github.com/Clear-Bible/macula-greek/");

    /// <summary>
    /// The English gloss on every Greek word is a second publisher's work inside the same text, and
    /// the only thing that makes it countable — and so creditable — is the text it names.
    /// </summary>
    [Fact]
    public void TheGreekGlossesAreCreditedToTheInterlinearThatMadeThem()
    {
        var berean = Of("berean-interlinear");

        berean.WordGlosses.Should().Be(NestleTextSource.Slug);
        berean.Author.Should().Contain("Bible Hub");
    }
}
