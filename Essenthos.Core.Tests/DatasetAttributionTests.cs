using Essenthos.Core.Endpoints;
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
            .Where(dataset => dataset.Licence.Contains("BY-SA", StringComparison.Ordinal))
            .Should().NotBeEmpty()
            .And.OnlyContain(dataset =>
                dataset.Obliges != null && dataset.Obliges.Contains("ShareAlike"));

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
    /// one does. RUL-0183 accepts the clause; accepting it is not the same as leaving it unsaid.
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
}
