using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The one-line description the list of texts shows. It has to exist for every text in every
/// interface language, and it has to stay a line: the list is for comparing texts at a glance, and a
/// line that grows into a paragraph turns it back into the page of cards it replaced.
/// </summary>
public sealed class TextTaglinesTests
{
    private const int LongestLine = 150;

    [Fact]
    public void EveryTextHasALineInEveryInterfaceLanguage()
    {
        foreach (var definition in TextCorpus.Definitions)
        {
            var tagline = TextTaglines.For(definition.Slug);
            tagline.Should().NotBeNull($"{definition.Slug} has no line");
            tagline!.Keys.Should().BeEquivalentTo(TextSummaries.Languages, definition.Slug);
            tagline.Values.Should().OnlyContain(line => line.Length > 0, definition.Slug);
        }
    }

    [Fact]
    public void EachLineIsOneShortSentenceAndShorterThanTheSummary()
    {
        foreach (var definition in TextCorpus.Definitions)
        {
            var tagline = TextTaglines.For(definition.Slug)!;
            var summary = TextSummaries.For(definition.Slug)!;
            foreach (var (language, line) in tagline)
            {
                line.Length.Should().BeLessThanOrEqualTo(LongestLine, $"{definition.Slug} {language}: {line}");
                line.Should().EndWith(".", $"{definition.Slug} {language}");
                line.TrimEnd('.').Should().NotContain(". ", $"{definition.Slug} {language}: {line}");
                line.Length.Should().BeLessThan(summary[language].Length, $"{definition.Slug} {language}");
            }
        }
    }
}
