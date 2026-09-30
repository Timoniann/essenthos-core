using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A word the first pass left unplaced goes on the one free word of its verse that the text's own
/// placements, counted over the whole text, say its form renders.
/// </summary>
public class EvidentiaSecondPassTests
{
    private const string Beast = "H929";
    private const string Herd = "H4735";

    private static readonly EvidentiaAddress Genesis1 = new(1, 1, 24);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void AnUnplacedWordGoesOnTheOneFreeLexemeItsFormWasPlacedOnElsewhere()
    {
        var source = new[] { English(1, 1, "livestock"), English(2, 2, "crept") };
        var target = new[] { Hebrew(11, 1, "בְּהֵמָה", Beast), Hebrew(12, 2, "רֶמֶשׂ", "H7431") };

        Placed(source, target, Renderings(("livestock", Beast, 5))).Should().Equal((1L, 11L));
    }

    [Fact]
    public void ThePairMustHaveBeenPlacedOftenEnoughAndHoldTheFormsShare()
    {
        var source = new[] { English(1, 1, "livestock") };
        var target = new[] { Hebrew(11, 1, "בְּהֵמָה", Beast) };

        Placed(source, target, Renderings(("livestock", Beast, EvidentiaSecondPass.MinimumPlacements - 1)))
            .Should().BeEmpty("a pair seen less often is a coincidence as easily as a habit");
        Placed(source, target, Renderings(("livestock", Beast, 5), ("livestock", Herd, 5)))
            .Should().BeEmpty("half of the form's placements went to another lexeme");
    }

    [Fact]
    public void TheFormIsReadUnderThePacksKey()
    {
        var source = new[] { English(1, 1, "beasts") };
        var target = new[] { Hebrew(11, 1, "בְּהֵמָה", Beast) };

        Placed(source, target, Renderings(("beast", Beast, 3), ("Beast’s", Beast, 2))).Should().Equal((1L, 11L));
    }

    [Fact]
    public void AWordAlreadyPlacedAndALexemeAlreadyTakenAreLeftAlone()
    {
        var source = new[] { English(1, 1, "livestock"), English(2, 2, "cattle") };
        var target = new[] { Hebrew(11, 1, "בְּהֵמָה", Beast) };
        var cattle = new EvidentiaProposal(
            Analysis(source[1]), Analysis(target[0]), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.5);

        Placed(source, target, Renderings(("livestock", Beast, 5)), cattle).Should().BeEmpty();
    }

    [Fact]
    public void TheLexemeMustStandFreeOnceAndBeClaimedByOneWord()
    {
        var source = new[] { English(1, 1, "livestock"), English(2, 2, "and"), English(3, 3, "livestock") };
        var twice = new[] { Hebrew(11, 1, "בְּהֵמָה", Beast), Hebrew(12, 2, "בְּהֵמָה", Beast) };
        var once = new[] { Hebrew(11, 1, "בְּהֵמָה", Beast) };

        Placed(source, once, Renderings(("livestock", Beast, 5)))
            .Should().BeEmpty("which of the two renders the one word is not something the counts say");
        Placed(source, twice, Renderings(("livestock", Beast, 5)))
            .Should().BeEmpty("nor which occurrence each of them renders");
        Placed([source[0]], twice, Renderings(("livestock", Beast, 5))).Should().BeEmpty();
    }

    [Fact]
    public void AFormThePassesPlacedIsReadAsWrittenBeforeItsNormalisation()
    {
        var source = new[] { English(1, 1, "here") };
        var target = new[] { Hebrew(11, 1, "הֵנָּה", "H2008"), Hebrew(12, 2, "הִיא", "H1931") };

        Placed(source, target, Renderings(("her", "H1931", 40), ("here", "H2008", 4))).Should().Equal((1L, 11L));
    }

    [Fact]
    public void ThePairTheAlignerNamesTooNeedsOnePlacementAndTheOneItLinksElsewhereIsRefused()
    {
        var source = new[] { English(1, 1, "livestock") };
        var target = new[] { Hebrew(11, 1, "בְּהֵמָה", Beast), Hebrew(12, 2, "רֶמֶשׂ", "H7431") };
        var once = Renderings(("livestock", Beast, 1), ("livestock", Herd, 1));
        var often = Renderings(("livestock", Beast, 5));

        Placed(source, target, once).Should().BeEmpty();
        Placed(source, target, once, EvidentiaAlignerPairs.Of([(1L, 11L)])).Should().Equal((1L, 11L));
        Placed(source, target, often, EvidentiaAlignerPairs.Of([(1L, 12L)]))
            .Should().BeEmpty("the aligner links the word elsewhere in the verse");
        Placed(source, target, often, EvidentiaAlignerPairs.Of([(2L, 12L)]))
            .Should().Equal([(1L, 11L)], "an aligner that says nothing of the word leaves the counts to stand alone");
    }

    [Fact]
    public void WhatAPassLearnsLeavesOutAttachedWordsAndItsOwnConfirmedPlacements()
    {
        var livestock = Analysis(English(1, 1, "livestock"));
        var the = Analysis(English(2, 2, "the"));
        var beast = Analysis(Hebrew(11, 1, "בְּהֵמָה", Beast));
        var learnt = new EvidentiaConfirmedRenderings();

        learnt.Learn(
            [
                new EvidentiaProposal(livestock, beast, EvidentiaProposalKind.UniqueContextGlossReview, 0.4),
                new EvidentiaProposal(the, beast, EvidentiaProposalKind.AttachedWord, 0.4),
                new EvidentiaProposal(livestock, beast, EvidentiaProposalKind.ConfirmedRendering, 0.4),
            ],
            new HashSet<(long, long)> { (1, 11) });

        learnt.For("eng", Packs).Of(livestock, Beast).Should().Be(new ConfirmedRendering(Safe: 1, Placed: 1, FormPlaced: 1));
        learnt.Pairs.Should().Be(1);
    }

    [Fact]
    public void TheRenderingsAreWrittenAndReadBackAsTheyWere()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("evidentia-confirmed").FullName, "8.tsv");
        try
        {
            var written = new EvidentiaConfirmedRenderings();
            written.Add("Livestock", Beast, 2, 5);
            written.Add("livestock", Herd, 0, 1);
            written.Write(path);

            var read = EvidentiaConfirmedRenderings.Read([path, Path.GetDirectoryName(path)!]).For("eng", Packs);

            read.Of(Analysis(English(1, 1, "livestock")), Beast)
                .Should().Be(new ConfirmedRendering(Safe: 4, Placed: 10, FormPlaced: 12), "a file and the folder it stands in are read as one count");
            File.WriteAllText(path, "livestock H929 5");
            var unreadable = () => EvidentiaConfirmedRenderings.Read([path]);
            unreadable.Should().Throw<FormatException>().WithMessage("*separated by tabs*");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static EvidentiaConfirmedIndex Renderings(params (string Form, string StrongNumber, int Placed)[] pairs)
    {
        var renderings = new EvidentiaConfirmedRenderings();
        foreach (var (form, number, placed) in pairs)
        {
            renderings.Add(form, number, 0, placed);
        }

        return renderings.For("eng", Packs);
    }

    private static List<(long, long)> Placed(
        EvidentiaToken[] source,
        EvidentiaToken[] target,
        EvidentiaConfirmedIndex confirmed,
        params EvidentiaProposal[] reserved) =>
        Placed(source, target, confirmed, aligner: null, reserved);

    private static List<(long, long)> Placed(
        EvidentiaToken[] source,
        EvidentiaToken[] target,
        EvidentiaConfirmedIndex confirmed,
        EvidentiaAlignerPairs? aligner,
        params EvidentiaProposal[] reserved)
    {
        List<EvidentiaAnalysis> sourceAnalyses = [.. source.Select(Analysis)];
        List<EvidentiaAnalysis> targetAnalyses = [.. target.Select(Analysis)];
        return
        [
            .. EvidentiaSecondPass.Resolve(
                    sourceAnalyses, targetAnalyses, reserved, confirmed,
                    EvidentiaVerseFrame.Of(sourceAnalyses, targetAnalyses, reserved), aligner)
                .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id)),
        ];
    }

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaToken English(long id, int position, string surface) =>
        new(id, Genesis1, position, surface, "eng", PartOfSpeech: "NOUN");

    private static EvidentiaToken Hebrew(long id, int position, string surface, string strongNumber) =>
        new(id, Genesis1, position, surface, "hbo", StrongNumber: strongNumber, PartOfSpeech: "subs");
}
