using Essenthos.Core.Corpus;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>The brief list of texts keeps what a reader picks a text by and leaves its paragraphs out.</summary>
public sealed class CorpusBriefTests
{
    [Fact]
    public void TheBriefListKeepsWhatAReaderPicksATextByAndDropsItsParagraphs()
    {
        var corpus = new CorpusResponse(
            "KJV", "King James Version", "translation", "en", "ltr", true, "public-domain", null, null, null,
            new CoverageResponse(1, 66, [.. Enumerable.Range(1, 66)]))
        {
            Translators = "The translators of 1611",
            Edition = "Cambridge, 1769",
            Aliases = ["AV"],
            About = "A long paragraph.",
            Summary = new Dictionary<string, string> { ["en"] = "What it is." },
            Tagline = new Dictionary<string, string> { ["en"] = "One line." },
            Citation = "A citation.",
            RightsNote = "A note on rights.",
            TermsNote = new Dictionary<string, string> { ["en"] = "A note on terms." },
            PartSources = [],
        };

        var brief = ReadEndpoints.Brief(corpus);

        (brief.Id, brief.Name, brief.Language, brief.Translators, brief.Edition, brief.License)
            .Should().Be(("KJV", "King James Version", "en", "The translators of 1611", "Cambridge, 1769", "public-domain"));
        brief.Aliases.Should().Equal("AV");
        new object?[] { brief.About, brief.Summary, brief.Tagline, brief.Citation, brief.RightsNote, brief.TermsNote, brief.PartSources }
            .Should().AllSatisfy(value => value.Should().BeNull());
    }
}
