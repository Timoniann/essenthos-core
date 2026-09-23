using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A grammatical word follows the word it belongs to, onto the place the witness's convention gives
/// it: a Hebrew article with its noun, <em>and</em> on the ו before the word it joins, an auxiliary on
/// its verb.
/// </summary>
public class EvidentiaAttachedWordTests
{
    private static readonly EvidentiaAddress Genesis122 = new(1, 1, 22);
    private static readonly EvidentiaAddress Mark114 = new(41, 1, 14);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void AnEnglishArticleGoesWithTheHebrewNounItsNounWasPlacedOn()
    {
        var the = English(1, Genesis122, 1, "the", "DET");
        var waters = English(2, Genesis122, 2, "waters", "NOUN");
        var article = Hebrew(11, Genesis122, 1, "הַ", "H9009", "art");
        var water = Hebrew(12, Genesis122, 2, "מַּיִם", "H4325", "subs");

        var attached = Attach([the, waters], [article, water], (waters, water));

        attached.Should().Equal((1L, 12L));
    }

    [Fact]
    public void AndGoesOnTheVavWrittenBeforeTheWordItJoinsPastAPrepositionAndAnArticle()
    {
        var and = English(1, Genesis122, 1, "and", "CCONJ");
        var birds = English(2, Genesis122, 2, "birds", "NOUN");
        var vav = Hebrew(11, Genesis122, 1, "וּ", "H9000", "conj");
        var preposition = Hebrew(12, Genesis122, 2, "בְ", "H9003", "prep");
        var article = Hebrew(13, Genesis122, 3, "הָ", "H9009", "art");
        var bird = Hebrew(14, Genesis122, 4, "עֹוף", "H5775", "subs");

        var attached = Attach([and, birds], [vav, preposition, article, bird], (birds, bird));

        attached.Should().Equal((1L, 11L));
    }

    [Fact]
    public void AndIsNotPlacedWhereNoConjunctionStandsBeforeTheWordItJoins()
    {
        var and = English(1, Genesis122, 1, "and", "CCONJ");
        var said = English(2, Genesis122, 2, "said", "VERB");
        var god = Hebrew(11, Genesis122, 1, "אֱלֹהִים", "H430", "subs");
        var say = Hebrew(12, Genesis122, 2, "לֵאמֹר", "H559", "verb");

        Attach([and, said], [god, say], (said, say)).Should().BeEmpty();
    }

    [Fact]
    public void AnAuxiliaryGoesOnItsVerbButNotAcrossANegation()
    {
        var will = English(1, Genesis122, 1, "will", "AUX");
        var rule = English(2, Genesis122, 2, "rule", "VERB");
        var must = English(3, Genesis122, 3, "must", "AUX");
        var not = English(4, Genesis122, 4, "not", "PART");
        var eat = English(5, Genesis122, 5, "eat", "VERB");
        var ruleVerb = Hebrew(11, Genesis122, 1, "יִמְשָׁל", "H4910", "verb");
        var negation = Hebrew(12, Genesis122, 2, "לֹא", "H3808", "nega");
        var eatVerb = Hebrew(13, Genesis122, 3, "תֹאכַל", "H398", "verb");

        var attached = Attach([will, rule, must, not, eat], [ruleVerb, negation, eatVerb], (rule, ruleVerb), (eat, eatVerb));

        attached.Should().Equal((1L, 11L));
    }

    [Fact]
    public void AnArticleWhoseNounWasNotPlacedIsNotPlacedEither()
    {
        var the = English(1, Genesis122, 1, "the", "DET");
        var waters = English(2, Genesis122, 2, "waters", "NOUN");
        var water = Hebrew(12, Genesis122, 2, "מַּיִם", "H4325", "subs");

        Attach([the, waters], [water]).Should().BeEmpty();
    }

    [Fact]
    public void AgainstGreekOnlyAndIsAttachedAndOnlyOnKai()
    {
        var and = English(1, Mark114, 1, "and", "CCONJ");
        var the = English(2, Mark114, 2, "the", "DET");
        var gospel = English(3, Mark114, 3, "gospel", "NOUN");
        var kai = Greek(11, Mark114, 1, "καὶ", "G2532", "conj", "καί");
        var article = Greek(12, Mark114, 2, "τὸ", "G3588", "det");
        var gospelNoun = Greek(13, Mark114, 3, "εὐαγγέλιον", "G2098", "noun");

        Attach([and, the, gospel], [kai, article, gospelNoun], (gospel, gospelNoun)).Should().Equal((1L, 11L));
    }

    [Fact]
    public void AndIsNotPlacedOnAGreekConjunctionThatSubordinates()
    {
        var and = English(1, Mark114, 1, "And", "CCONJ");
        var satan = English(2, Mark114, 2, "Satan", "PROPN");
        var ei = Greek(11, Mark114, 1, "εἰ", "G1487", "conj", "εἰ");
        var satanNoun = Greek(12, Mark114, 2, "Σατανᾶς", "G4567", "noun");

        Attach([and, satan], [ei, satanNoun], (satan, satanNoun)).Should().BeEmpty();
    }

    private static IReadOnlyList<(long Source, long Target)> Attach(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (EvidentiaToken Source, EvidentiaToken Target)[] placed)
    {
        var proposals = placed
            .Select(pair => new EvidentiaProposal(
                Analysis(pair.Source), Analysis(pair.Target), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8))
            .ToList();
        return EvidentiaAttachedWords.Resolve(
                EvidentiaAuxiliaryWords.Mark([.. source.Select(Analysis)]),
                [.. target.Select(Analysis)],
                proposals)
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .ToList();
    }

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaToken English(long id, EvidentiaAddress address, int position, string surface, string partOfSpeech) =>
        new(id, address, position, surface, "eng", PartOfSpeech: partOfSpeech);

    private static EvidentiaToken Hebrew(long id, EvidentiaAddress address, int position, string surface, string strong, string partOfSpeech) =>
        new(id, address, position, surface, "hbo", StrongNumber: strong, PartOfSpeech: partOfSpeech);

    private static EvidentiaToken Greek(
        long id, EvidentiaAddress address, int position, string surface, string strong, string partOfSpeech, string? lemma = null) =>
        new(id, address, position, surface, "grc", Lemma: lemma, StrongNumber: strong, PartOfSpeech: partOfSpeech);
}
