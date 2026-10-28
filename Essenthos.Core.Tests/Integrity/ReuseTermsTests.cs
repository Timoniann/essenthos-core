using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Integrity;

/// <summary>
/// What each text and dataset lets a reader do, in the one word a page turns into a sentence. It is
/// read from the licence's name, and a name nobody can recognise as a licence is never read as free.
/// </summary>
public sealed class ReuseTermsTests
{
    [Theory]
    [InlineData("CC-BY-SA-4.0", null, ReuseTerms.ShareAlike)]
    [InlineData("CC BY-NC 4.0", null, ReuseTerms.NonCommercial)]
    [InlineData("CC BY-NC-SA 4.0", null, ReuseTerms.NonCommercialShareAlike)]
    [InlineData("CC BY", null, ReuseTerms.Attribution)]
    [InlineData("CC0 1.0", null, ReuseTerms.Free)]
    [InlineData("Public domain (the Unlicense)", null, ReuseTerms.Free)]
    [InlineData("No licence stated", null, ReuseTerms.Own)]
    [InlineData("Copyrighted; Free non-commercial distribution", null, ReuseTerms.Own)]
    [InlineData("GPL", "share-alike", ReuseTerms.ShareAlike)]
    [InlineData("Everyone is permitted to copy", "public-domain", ReuseTerms.Free)]
    [InlineData("Everyone is permitted to copy", "unknown", ReuseTerms.Own)]
    public void ALicenceNameIsReadAsWhatItLetsAReaderDo(string licence, string? redistribution, string terms) =>
        ReuseTerms.Of(licence, redistribution).Should().Be(terms);

    [Fact]
    public void EveryTextAndDatasetHasTermsAPageCanSay()
    {
        foreach (var definition in TextCorpus.Definitions)
        {
            TextRights.Terms(definition.Slug, definition.Licence, EnumSpelling.Of(definition.Redistribution))
                .Should().BeOneOf(ReuseTerms.All, definition.Slug);
        }

        Datasets.All.Should().OnlyContain(dataset => ReuseTerms.All.Contains(dataset.Terms));
    }

    /// <summary>
    /// The copy the King James and the Synodal are read from lets them be passed on only free and
    /// unchanged, which a public-domain translation alone would not say; so neither is shown as free.
    /// </summary>
    [Theory]
    [InlineData("KJV")]
    [InlineData("RUSV")]
    public void ATextWhoseCopyAddsConditionsIsNotShownAsFree(string slug)
    {
        var definition = TextCorpus.Definitions.Single(d => d.Slug == slug);

        TextRights.Terms(slug, definition.Licence, EnumSpelling.Of(definition.Redistribution)).Should().Be(ReuseTerms.Own);
        TextRights.Note(slug)!["en"].Should().Contain("unchanged");
    }

    /// <summary>The fields reach the wire through the source-generated serializer, which is where a missing registration fails.</summary>
    [Fact]
    public void TheTermsAreSentWithEachTextAndDataset()
    {
        var corpus = new CorpusResponse("KRV", "Korean", "translation", "kor", "ltr", false, "Public Domain", null, null, 1961,
            new CoverageResponse(1, 66, [1]))
        {
            Terms = TextRights.Terms("KRV", "Public Domain", "public-domain"),
            TermsNote = TextRights.Note("KRV"),
            RightsHolderUrl = TextRights.HolderUrl("KRV"),
        };
        var text = System.Text.Json.JsonSerializer.Serialize(corpus, AppJsonSerializerContext.Default.CorpusResponse);
        text.Should().Contain("\"terms\":\"by\"").And.Contain("\"rightsHolderUrl\":\"https://www.bskorea.or.kr/\"")
            .And.Contain("\"termsNote\":{\"en\":");

        var dataset = new DatasetResponse("wikidata", "Wikidata", "the Wikidata contributors", "CC0", null, "https://query.wikidata.org",
            "", null, null, new DatasetCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), [])
        {
            Terms = ReuseTerms.Of("CC0"),
            RightsHolderUrl = "https://www.wikidata.org/",
        };
        var data = System.Text.Json.JsonSerializer.Serialize(dataset, AppJsonSerializerContext.Default.DatasetResponse);
        data.Should().Contain("\"terms\":\"free\"").And.Contain("\"rightsHolderUrl\":\"https://www.wikidata.org/\"");
    }

    [Fact]
    public void EveryListedTextIsATextTheCorpusLoadsAndEveryNoteIsInEveryLanguage()
    {
        var slugs = TextCorpus.Definitions.Select(d => d.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        TextRights.Slugs.Should().OnlyContain(slug => slugs.Contains(slug));
        foreach (var slug in TextRights.Slugs)
        {
            if (TextRights.Note(slug) is { } note)
            {
                note.Keys.Should().BeEquivalentTo(TextSummaries.Languages, slug);
                note.Values.Should().OnlyContain(line => line.Length > 0 && line.Length <= 260, slug);
            }

            if (TextRights.HolderUrl(slug) is { } url)
            {
                url.Should().StartWith("https://", slug);
            }
        }
    }
}
