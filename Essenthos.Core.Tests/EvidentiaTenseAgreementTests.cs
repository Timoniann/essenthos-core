using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// An English verb form is not placed on a Greek verb whose tense or mood it cannot render: in Luke 13:30
/// <em>there are some who are last who will be first</em>, the <em>are</em> of <em>who are last</em> is
/// no rendering of ἔσονται.
/// </summary>
public class EvidentiaTenseAgreementTests
{
    private static readonly EvidentiaAddress Luke1330 = new(42, 13, 30);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void AnEnglishPresentIsNotTheGreekFuture()
    {
        var are = English(1, "are", "AUX", ("VerbForm", "Fin"), ("Tense", "Pres"), ("Mood", "Ind"));
        var esontai = Greek(11, "ἔσονται", ("tense", "future"), ("mood", "indicative"));
        var eisin = Greek(12, "εἰσὶν", ("tense", "present"), ("mood", "indicative"));

        EvidentiaTenseAgreement.Disagrees(are, esontai).Should().BeTrue();
        EvidentiaTenseAgreement.Disagrees(are, eisin).Should().BeFalse();
    }

    [Fact]
    public void AnEnglishPresentOfAnotherVerbIsLeftAlone()
    {
        var burst = English(1, "burst", "VERB", ("VerbForm", "Fin"), ("Tense", "Pres"), ("Mood", "Ind"));
        var rexei = Greek(11, "ῥήξει", ("tense", "future"), ("mood", "indicative"));

        EvidentiaTenseAgreement.Disagrees(burst, rexei).Should().BeFalse();
    }

    [Fact]
    public void AnEnglishParticipleOfAFutureIsLeftAlone()
    {
        var glorified = English(1, "glorified", "VERB", ("VerbForm", "Part"), ("Tense", "Past"));
        var future = Greek(11, "δοξασθήσεται", ("tense", "future"), ("mood", "indicative"));

        EvidentiaTenseAgreement.Disagrees(glorified, future).Should().BeFalse();
    }

    [Fact]
    public void BeingIsNotAFiniteVerb()
    {
        var being = English(1, "being", "AUX", ("VerbForm", "Ger"));
        var eisin = Greek(11, "εἰσὶν", ("tense", "present"), ("mood", "indicative"));
        var on = Greek(12, "ὤν", ("tense", "present"), ("mood", "participle"));

        EvidentiaTenseAgreement.Disagrees(being, eisin).Should().BeTrue();
        EvidentiaTenseAgreement.Disagrees(being, on).Should().BeFalse();
    }

    private static EvidentiaAnalysis English(long id, string surface, string partOfSpeech, params (string Name, string Value)[] features) =>
        Analysis(new EvidentiaToken(id, Luke1330, (int)id, surface, "eng", Lemma: surface is "are" or "being" ? "be" : surface, PartOfSpeech: partOfSpeech,
            Morphology: features.ToDictionary(feature => feature.Name, feature => feature.Value)));

    private static EvidentiaAnalysis Greek(long id, string surface, params (string Name, string Value)[] features) =>
        Analysis(new EvidentiaToken(id, Luke1330, (int)id, surface, "grc", StrongNumber: "G1510", PartOfSpeech: "verb",
            Morphology: features.Append(("pos", "verb")).ToDictionary(feature => feature.Item1, feature => feature.Item2)));

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }
}
