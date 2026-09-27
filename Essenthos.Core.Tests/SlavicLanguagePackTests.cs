using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Ukrainian and Russian each answer for their own function words. A word that only carries grammar
/// in one language is a content word in the other, and filtering one by the other's list costs the
/// link its anchor.
/// </summary>
public class SlavicLanguagePackTests
{
    private static readonly LanguagePackRegistry Packs =
        new([new UkrainianLanguagePack(), new RussianLanguagePack()]);

    [Theory]
    [InlineData("та", "ukr", true)]
    [InlineData("та", "rus", false)]
    [InlineData("он", "rus", true)]
    [InlineData("он", "ukr", false)]
    [InlineData("і", "ukr", true)]
    [InlineData("и", "rus", true)]
    [InlineData("и", "ukr", false)]
    public void EachLanguageIsFilteredByItsOwnFunctionWords(string surface, string language, bool function)
    {
        Packs.TryAnalyse(Word(surface, language), out var analysis).Should().BeTrue();

        analysis.WordClass.Should().Be(function ? EvidentiaWordClass.Function : EvidentiaWordClass.Content);
    }

    [Fact]
    public void EachPackClaimsOneLanguage()
    {
        new UkrainianLanguagePack().Supports("rus").Should().BeFalse();
        new RussianLanguagePack().Supports("ukr").Should().BeFalse();
    }

    private static EvidentiaToken Word(string surface, string language) =>
        new(surface.GetHashCode(), new EvidentiaAddress(2, 2, 1), 1, surface, language);
}
