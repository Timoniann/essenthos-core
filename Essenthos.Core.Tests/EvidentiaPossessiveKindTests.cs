using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A German or Spanish possessive writes the original's genitive pronoun or suffix. The index learned
/// <em>seine</em> on every form of αὐτός, so the case decides: on αὐτοῦ it stands, on αὐτῷ it is
/// somebody else's word; and <em>zu meinem Herrn</em> puts it on the noun, never on אֶל.
/// </summary>
public class EvidentiaPossessiveKindTests
{
    private static readonly EvidentiaAddress Matthew825 = new(40, 8, 25);

    private static readonly LanguagePackRegistry Packs =
        new([new GermanLanguagePack(), new SpanishLanguagePack(), new OriginalLanguagePack()]);

    [Theory]
    [InlineData("seine", "deu", "PRON", "Yes", "genitive", false)]
    [InlineData("seine", "deu", "PRON", "Yes", "dative", true)]
    [InlineData("Seinen", "deu", "PRON", "Yes", "accusative", true)]
    [InlineData("dein", "deu", "DET", null, "nominative", true)]
    [InlineData("euer", "deu", "ADJ", null, "genitive", false)]
    [InlineData("nuestro", "spa", "DET", "Yes", "dative", true)]
    [InlineData("nuestro", "spa", "DET", "Yes", "genitive", false)]
    [InlineData("ihm", "deu", "PRON", null, "dative", false)]
    public void APossessiveIsOffItsKindOnAGreekPronounOfAnotherCase(
        string surface, string language, string partOfSpeech, string? possessive, string targetCase, bool off)
    {
        var source = Translation(surface, language, partOfSpeech, possessive);
        var target = Original(new EvidentiaToken(11, Matthew825, 1, "αὐτοῦ", "grc", StrongNumber: "G846", PartOfSpeech: "pron",
            Morphology: new Dictionary<string, string> { ["case"] = targetCase, ["number"] = "singular" }));

        EvidentiaAuxiliaryWords.PlacesOffItsKind(source, target).Should().Be(off);
    }

    [Theory]
    [InlineData("prep", true)]
    [InlineData("conj", true)]
    [InlineData("art", true)]
    [InlineData("subs", false)]
    public void APossessiveIsOffItsKindOnAHebrewGrammaticalWord(string partOfSpeech, bool off)
    {
        var source = Translation("meinem", "deu", "PRON", "Yes");
        var target = Original(new EvidentiaToken(11, new EvidentiaAddress(1, 44, 22), 1, "אֶל", "hbo", StrongNumber: "H413",
            PartOfSpeech: partOfSpeech));

        EvidentiaAuxiliaryWords.PlacesOffItsKind(source, target).Should().Be(off);
    }

    [Fact]
    public void AConjunctionStaysAContentWordAndIsNotHeldToAnyKind()
    {
        var sondern = Translation("sondern", "deu", "CCONJ", null);
        var alla = Original(new EvidentiaToken(11, Matthew825, 1, "ἀλλὰ", "grc", StrongNumber: "G235", PartOfSpeech: "conj"));

        sondern.IsContentWord.Should().BeTrue();
        EvidentiaAuxiliaryWords.PlacesOffItsKind(sondern, alla).Should().BeFalse();
    }

    private static EvidentiaAnalysis Translation(string surface, string language, string partOfSpeech, string? possessive)
    {
        var features = new Dictionary<string, string>();
        if (possessive is not null)
        {
            features["Poss"] = possessive;
            features["PronType"] = "Prs";
        }

        Packs.TryAnalyse(new EvidentiaToken(1, Matthew825, 1, surface, language, PartOfSpeech: partOfSpeech,
            Morphology: features.Count == 0 ? null : features), out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaAnalysis Original(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }
}
