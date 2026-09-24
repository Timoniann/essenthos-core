using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The path from an EVIDENTIA proposal to the corpus. What these guard is that nothing heuristic is
/// stored as though a source had said it, at the one place a rule's output could become a link:
/// nothing gets there without a verdict, a verdict writes the claim it justifies and no stronger
/// one, and the corpus's own links are settled against rather than overwritten.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EvidentiaReviewTests : IDisposable
{
    private const string Reviewer = "a test reviewer";
    private const float RuleConfidence = 0.62f;

    private readonly AppDbContext _db;
    private readonly Text _english;
    private readonly Text _hebrew;
    private readonly EvidentiaRun _run;

    public EvidentiaReviewTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        _english = Corpus.Add(_db, "ENGT", TextKind.Translation, "eng",
            (1, 1, ["In", "the", "beginning", "God", "created"]));
        _hebrew = Corpus.Add(_db, "HEBT", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בראשית", "ברא", "אלהים"]));
        _db.SaveChanges();

        _run = new EvidentiaRun
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            RuleVersion = "test",
            Configuration = JsonDocument.Parse("{}"),
            Scope = JsonDocument.Parse("{}"),
        };
        _db.EvidentiaRuns.Add(_run);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task NothingReachesTheCorpusWithoutAVerdict()
    {
        var god = Proposal(English(4), Hebrew(3));
        var created = Proposal(English(5), Hebrew(2));
        await Queue().Reject([created.Id], Reviewer, "wrong occurrence");
        var before = await Counts();

        var outcome = await Writer().Apply(_run.Id, write: true);

        outcome.Verdicts.Should().Be(0, "a proposal nobody reviewed and a rejected one are not verdicts to write");
        (await Counts()).Should().Be(before);
        (await _db.EvidentiaReviews.AsNoTracking().SingleAsync()).AppliedAt.Should().BeNull();
        god.Review.Should().BeNull();
    }

    [Fact]
    public async Task APlanWritesNothingEvenForAnApprovedProposal()
    {
        var god = Proposal(English(4), Hebrew(3));
        await Queue().Approve([god.Id], Reviewer, null);
        var before = await Counts();

        var outcome = await Writer().Apply(_run.Id, write: false);

        outcome.NewLinks.Should().Be(1, "the plan says what would be written");
        outcome.Written.Should().BeFalse();
        (await Counts()).Should().Be(before);
        (await _db.EvidentiaReviews.AsNoTracking().SingleAsync()).AppliedAt.Should().BeNull();
    }

    [Fact]
    public async Task AProposalAPersonReadBecomesTheirLinkWithTheRuleBesideIt()
    {
        var god = Proposal(English(4), Hebrew(3));
        await Queue().Approve([god.Id], Reviewer, "checked against the verse");

        var outcome = await Writer().Apply(_run.Id, write: true);

        outcome.NewLinks.Should().Be(1);
        var link = await StoredLink();
        link.Method.Should().Be(LinkMethod.Manual);
        link.Confidence.Should().BeNull();
        link.Words.Should().BeEquivalentTo(
            [(English(4), LinkSide.From), (Hebrew(3), LinkSide.To)],
            options => options.WithoutStrictOrdering());
        link.Claims.Select(claim => claim.Method).Should().BeEquivalentTo([LinkMethod.Manual, LinkMethod.RuleBased]);
        var rule = link.Claims.Single(claim => claim.Method == LinkMethod.RuleBased);
        rule.Confidence.Should().BeApproximately(RuleConfidence, 0.0001);
        rule.Source.Should().Contain($"EVIDENTIA run {_run.Id}").And.Contain("global-review-known-rendering");
        (await _db.EvidentiaReviews.AsNoTracking().SingleAsync()).LinkId.Should().Be(link.Id);
    }

    [Fact]
    public async Task ATierAcceptedUnreadIsTheRulesClaimAloneWithItsConfidence()
    {
        Proposal(English(4), Hebrew(3), tier: EvidentiaDecisionRecorder.SafeTier);
        Proposal(English(5), Hebrew(2), tier: "review");

        var accepted = await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier),
            Reviewer, null);
        await Writer().Apply(_run.Id, write: true);

        accepted.Should().Be(1, "only the named tier is accepted");
        var link = await StoredLink();
        link.Method.Should().Be(LinkMethod.RuleBased, "nobody read it, so no person may be said to state it");
        link.Confidence.Should().BeApproximately(RuleConfidence, 0.0001);
        link.Claims.Should().ContainSingle().Which.Note.Should().Contain("not read one by one");
    }

    [Fact]
    public async Task ACorrectionIsAPersonsClaimAboutTheWordTheyNamed()
    {
        var created = Proposal(English(5), Hebrew(3));
        await Queue().Correct(created.Id, Hebrew(2), Reviewer, "the verb, not the subject");

        await Writer().Apply(_run.Id, write: true);

        var link = await StoredLink();
        link.Method.Should().Be(LinkMethod.Manual);
        link.Words.Should().Contain((Hebrew(2), LinkSide.To));
        link.Claims.Should().ContainSingle(claim => claim.Method == LinkMethod.Manual,
            "the rule proposed a different word, so it has nothing to say about this one");
    }

    [Fact]
    public async Task AStatedLinkKeepsShowingItsSourceAndGainsTheRulesClaim()
    {
        var stated = ExistingLink(English(4), Hebrew(3), LinkMethod.StatedBySource, null);
        Proposal(English(4), Hebrew(3), tier: EvidentiaDecisionRecorder.SafeTier);
        await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier), Reviewer, null);

        var outcome = await Writer().Apply(_run.Id, write: true);

        outcome.OnExisting.Should().Be(1);
        outcome.NewLinks.Should().Be(0, "one correspondence is one link however many methods say it");
        var link = await StoredLink();
        link.Id.Should().Be(stated);
        link.Method.Should().Be(LinkMethod.StatedBySource);
        link.Claims.Select(claim => claim.Method).Should().BeEquivalentTo([LinkMethod.StatedBySource, LinkMethod.RuleBased]);
    }

    [Fact]
    public async Task ARuleOutranksTheAlignerOnTheSameWords()
    {
        var guessed = ExistingLink(English(4), Hebrew(3), LinkMethod.Aligner, 0.41);
        Proposal(English(4), Hebrew(3), tier: EvidentiaDecisionRecorder.SafeTier);
        await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier), Reviewer, null);

        var outcome = await Writer().Apply(_run.Id, write: true);

        outcome.Promoted.Should().Be(1);
        var link = await StoredLink();
        link.Id.Should().Be(guessed);
        link.Method.Should().Be(LinkMethod.RuleBased);
        link.Claims.Select(claim => claim.Method).Should().BeEquivalentTo([LinkMethod.Aligner, LinkMethod.RuleBased],
            "the aligner's guess stays as a claim; it stops being the link's answer");
    }

    [Fact]
    public async Task ARuleIsWithheldWhereASourcePlacesTheWordElsewhereAndAPersonIsNot()
    {
        ExistingLink(English(5), Hebrew(2), LinkMethod.StatedBySource, null);
        var unread = Proposal(English(5), Hebrew(3), tier: EvidentiaDecisionRecorder.SafeTier);
        await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier), Reviewer, null);

        var withheld = await Writer().Apply(_run.Id, write: true);

        withheld.Withheld.Should().Be(1);
        withheld.NewLinks.Should().Be(0);
        (await _db.EvidentiaReviews.AsNoTracking().SingleAsync()).Withheld.Should().Contain("stated-by-source");

        await Queue().Approve([unread.Id], Reviewer, "the source's link is the other occurrence");
        var read = await Writer().Apply(_run.Id, write: true);

        read.NewLinks.Should().Be(1, "a person reviewing the word decided knowing what the source says");
        (await _db.Links.AsNoTracking().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task TheTraceReadsBackAsItWasDecided()
    {
        var source = Analysis(English(4), _english, 1, "God");
        var chosen = Analysis(Hebrew(3), _hebrew, 3, "אלהים");
        var other = Analysis(Hebrew(1), _hebrew, 1, "בראשית", verse: 2);
        var unplaced = Analysis(English(5), _english, 5, "created");
        EvidentiaEvidence[] evidence =
        [
            new(EvidentiaEvidenceKind.ExactCanonicalAddress, 0.30, "canonical-frame"),
            new(EvidentiaEvidenceKind.KnownRendering, 0.54, "known-rendering:ENGT→HEBT; key=surface 'god'",
                new EvidentiaEvidenceSupport(2977, 0.76, 0.08, EvidentiaFormKind.Surface)),
            new(EvidentiaEvidenceKind.Morphology, 0.08, "universal-pos:noun; matching-features:1"),
        ];
        var best = new EvidentiaCandidate(source, chosen, evidence);
        var runnerUp = new EvidentiaCandidate(source, other,
            [new EvidentiaEvidence(EvidentiaEvidenceKind.DictionarySense, 0.34, "strong-entry:eng")]);
        var declined = new EvidentiaCandidate(unplaced, chosen,
            [new EvidentiaEvidence(EvidentiaEvidenceKind.TargetGloss, 0.20, "target-gloss:source")]);
        var proposal = new EvidentiaProposal(source, chosen, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.69,
            EvidentiaDecisionTrace.For(best, "review", "global known-rendering assignment; lexical score 0.54"));
        var recorder = new EvidentiaDecisionRecorder(_run.Id);
        recorder.Record(new EvidentiaChapterDecisions(1, 1, [source.Token, unplaced.Token],
            new HashSet<long> { source.Token.Id, unplaced.Token.Id }, [best, runnerUp, declined], [proposal], [proposal]));

        await new EvidentiaRunner(_db, null!).Write(recorder.Decisions, CancellationToken.None);

        var stored = await _db.EvidentiaDecisions.AsNoTracking().OrderBy(decision => decision.Id).ToListAsync();
        stored.Should().HaveCount(2);
        var placed = stored[0];
        placed.Should().BeEquivalentTo(new
        {
            SourceWordId = English(4),
            TargetWordId = (long?)Hebrew(3),
            CanonicalBook = (short)1,
            CanonicalChapter = (short)1,
            CanonicalVerse = (short)1,
            Content = true,
            Kind = "global-review-known-rendering",
            Tier = EvidentiaDecisionRecorder.SafeTier,
            Rationale = "global known-rendering assignment; lexical score 0.54",
            Candidates = (short)2,
            RenderingObservations = (int?)2977,
            RenderingForm = "surface",
            AlternativeWordIds = new[] { Hebrew(1) },
            AlternativeDistances = new short[] { 1 },
            AlternativeTaken = new[] { false },
        });
        placed.Confidence.Should().BeApproximately(0.69f, 0.0001f);
        placed.ExactAddress.Should().BeApproximately(0.30f, 0.0001f);
        placed.KnownRendering.Should().BeApproximately(0.54f, 0.0001f);
        placed.Morphology.Should().BeApproximately(0.08f, 0.0001f);
        placed.DictionarySense.Should().BeNull("the chosen edge carried no dictionary sense");
        placed.RenderingShare.Should().BeApproximately(0.76f, 0.0001f);
        placed.Margin.Should().BeApproximately(0.92f - 0.34f, 0.0001f);
        placed.AlternativeScores.Should().ContainSingle().Which.Should().BeApproximately(0.34f, 0.0001f);

        var abstained = stored[1];
        abstained.TargetWordId.Should().BeNull();
        abstained.Abstention.Should().Be(EvidentiaAbstention.TargetTaken, "its only candidate's word went to 'God'");
        abstained.TargetGloss.Should().BeApproximately(0.20f, 0.0001f);
        abstained.AlternativeTaken.Should().Equal(true);
    }

    [Fact]
    public async Task AnAbsenceIsWrittenOnTheSideTheRelationSaysWithTheRulesClaim()
    {
        Absence(LinkRelation.Expands, English(2), EvidentiaDecisionRecorder.SafeTier);
        Absence(LinkRelation.Omits, Hebrew(2), EvidentiaDecisionRecorder.SafeTier);

        var accepted = await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier),
            Reviewer, null);
        var outcome = await Writer().Apply(_run.Id, write: true);

        accepted.Should().Be(2, "an absence waits in the queue like a proposal");
        outcome.NewLinks.Should().Be(2);
        outcome.NewAbsences.Should().Be(2);
        var links = await _db.Links.AsNoTracking().Include(row => row.Words).Include(row => row.Claims)
            .OrderBy(row => row.Id).ToListAsync();
        links.Select(link => (link.Relation, link.Words.Single().WordId, link.Words.Single().Side)).Should().Equal(
            (LinkRelation.Expands, English(2), LinkSide.From),
            (LinkRelation.Omits, Hebrew(2), LinkSide.To));
        links.Should().AllSatisfy(link =>
        {
            link.FromTextId.Should().Be(_english.Id);
            link.Method.Should().Be(LinkMethod.RuleBased, "a rule said it and nobody read it");
            link.Confidence.Should().BeApproximately(RuleConfidence, 0.0001);
            link.Claims.Should().ContainSingle().Which.Source.Should().Contain($"EVIDENTIA run {_run.Id}");
        });
        links[0].Source.Should().EndWith("supplied-article");
        links[1].Source.Should().EndWith("unrendered-conjunction");
    }

    [Fact]
    public async Task AnAbsenceTheCorpusAlreadyStatesGainsTheClaimAndARenderingKeepsAnyAbsenceOut()
    {
        var stated = ExistingAbsence(LinkRelation.Omits, Hebrew(2));
        ExistingLink(English(2), Hebrew(1), LinkMethod.StatedBySource, null);
        Absence(LinkRelation.Omits, Hebrew(2), EvidentiaDecisionRecorder.SafeTier);
        var supplied = Absence(LinkRelation.Expands, English(2), EvidentiaDecisionRecorder.SafeTier);
        await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier), Reviewer, null);

        var unread = await Writer().Apply(_run.Id, write: true);

        unread.OnExisting.Should().Be(1, "the Berean already says the word is not rendered");
        unread.NewLinks.Should().Be(0);
        unread.Withheld.Should().Be(1, "a source gives 'the' a counterpart, and an absence may not stand beside it");
        var claims = await _db.LinkClaims.AsNoTracking().Where(claim => claim.LinkId == stated).ToListAsync();
        claims.Select(claim => claim.Method).Should().BeEquivalentTo([LinkMethod.StatedBySource, LinkMethod.RuleBased]);
        (await _db.Links.AsNoTracking().SingleAsync(link => link.Id == stated)).Method.Should().Be(LinkMethod.StatedBySource);

        await Queue().Approve([supplied.Id], Reviewer, "the original has no article here");
        var read = await Writer().Apply(_run.Id, write: true);

        read.Withheld.Should().Be(1, "a word shown as supplied and as rendered at once is a contradiction, whoever approved it");
        read.NewLinks.Should().Be(0);
        (await _db.EvidentiaReviews.AsNoTracking().SingleAsync(review => review.DecisionId == supplied.Id))
            .Withheld.Should().Contain("stated");
    }

    [Fact]
    public async Task ASafeTierAbsenceTakesItsWordOutOfTheAlignersLinkAndKeepsWhatItSaid()
    {
        var guess = ExistingLink(English(2), Hebrew(1), LinkMethod.Aligner, 0.35);
        var kept = ExistingLink(English(4), Hebrew(3), LinkMethod.Aligner, 0.9);
        var supplied = Absence(LinkRelation.Expands, English(2), EvidentiaDecisionRecorder.SafeTier);
        await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier), Reviewer, null);

        var plan = await Writer().Apply(_run.Id, write: false);
        (await _db.Links.AsNoTracking().AnyAsync(link => link.Id == guess)).Should().BeTrue("a plan withdraws nothing");
        var outcome = await Writer().Apply(_run.Id, write: true);

        plan.Withdrawn.Should().Be(1);
        outcome.Withdrawn.Should().Be(1);
        outcome.NewAbsences.Should().Be(1);
        outcome.Withheld.Should().Be(0);
        var links = await _db.Links.AsNoTracking().Include(row => row.Words).Include(row => row.Claims).ToListAsync();
        links.Select(link => link.Id).Should().NotContain(guess, "'the' was all of the link's English side");
        links.Select(link => link.Id).Should().Contain(kept);
        var absence = links.Single(link => link.Relation == LinkRelation.Expands);
        absence.Words.Should().ContainSingle().Which.WordId.Should().Be(English(2));
        absence.Claims.Single().Note.Should().Contain("withdrawn");

        var withdrawal = await _db.EvidentiaWithdrawals.AsNoTracking().Include(row => row.Review).SingleAsync();
        withdrawal.Review!.DecisionId.Should().Be(supplied.Id, "the verdict names the run and the rule that withdrew it");
        withdrawal.LinkId.Should().BeNull();
        (withdrawal.WordId, withdrawal.Relation, withdrawal.Method, withdrawal.Confidence, withdrawal.Source)
            .Should().Be((English(2), LinkRelation.Renders, LinkMethod.Aligner, 0.35, "an existing loader"));
        withdrawal.FromWordIds.Should().Equal(English(2));
        withdrawal.ToWordIds.Should().Equal(Hebrew(1));
        withdrawal.ClaimSources.Should().Equal("an existing loader");
        withdrawal.ClaimConfidences.Should().Equal(0.35);
    }

    [Fact]
    public async Task AnAbsenceGivesWayWhereAnythingButTheAlignerRendersTheWordOrItIsNotSafe()
    {
        ExistingLink(English(2), Hebrew(1), LinkMethod.Aligner, 0.35);
        ExistingLink(English(2), Hebrew(3), LinkMethod.StatedBySource, null);
        var guess = ExistingLink(English(3), Hebrew(1), LinkMethod.Aligner, 0.4);
        ExistingLink(English(5), Hebrew(2), LinkMethod.Aligner, 0.5);
        Absence(LinkRelation.Expands, English(2), EvidentiaDecisionRecorder.SafeTier);
        Absence(LinkRelation.Expands, English(3), "review");
        Absence(LinkRelation.Omits, Hebrew(2), EvidentiaDecisionRecorder.SafeTier, "unrendered-something-unmeasured");
        await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier), Reviewer, null);
        await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: "review"), Reviewer, null);

        var outcome = await Writer().Apply(_run.Id, write: true);

        outcome.Withdrawn.Should().Be(0, "a stated link stands beside the aligner's, and the others are not safe-tier absences by a measured rule");
        outcome.Withheld.Should().Be(3);
        (await _db.Links.AsNoTracking().AnyAsync(link => link.Id == guess)).Should().BeTrue();
        (await _db.EvidentiaWithdrawals.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ASuppliedTheTheAlignerPutsOnAWrittenArticleIsNotWithdrawn()
    {
        var article = Hebrew(1);
        await _db.Words.Where(word => word.Id == article).ExecuteUpdateAsync(set => set.SetProperty(word => word.StrongNumber, "H9009"));
        var guess = ExistingLink(English(2), article, LinkMethod.Aligner, 0.98);
        Absence(LinkRelation.Expands, English(2), EvidentiaDecisionRecorder.SafeTier);
        await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier), Reviewer, null);

        var outcome = await Writer().Apply(_run.Id, write: true);

        outcome.Withdrawn.Should().Be(0, "the original writes the article the absence says it does not");
        outcome.Withheld.Should().Be(1);
        (await _db.Links.AsNoTracking().AnyAsync(link => link.Id == guess)).Should().BeTrue();
    }

    [Fact]
    public async Task AWordTakenOutOfAWiderAlignerLinkLeavesTheRestOfIt()
    {
        var wide = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.5,
            Source = "an existing loader",
            Words =
            [
                new LinkWord { WordId = English(1), Side = LinkSide.From },
                new LinkWord { WordId = English(3), Side = LinkSide.From },
                new LinkWord { WordId = Hebrew(1), Side = LinkSide.To },
                new LinkWord { WordId = Hebrew(2), Side = LinkSide.To },
            ],
            Claims = [new LinkClaim { Method = LinkMethod.Aligner, Confidence = 0.5, Source = "an existing loader" }],
        };
        _db.Links.Add(wide);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        Absence(LinkRelation.Omits, Hebrew(2), EvidentiaDecisionRecorder.SafeTier);
        await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier), Reviewer, null);

        var outcome = await Writer().Apply(_run.Id, write: true);

        outcome.Withdrawn.Should().Be(1);
        outcome.NewAbsences.Should().Be(1);
        var remaining = await _db.Links.AsNoTracking().Include(row => row.Words).SingleAsync(link => link.Id == wide.Id);
        remaining.Words.Select(word => word.WordId).Should().BeEquivalentTo([English(1), English(3), Hebrew(1)]);
        var withdrawal = await _db.EvidentiaWithdrawals.AsNoTracking().SingleAsync();
        withdrawal.LinkId.Should().Be(wide.Id);
        withdrawal.ToWordIds.Should().Equal(new[] { Hebrew(1), Hebrew(2) }.Order());
    }

    [Fact]
    public async Task AnUnrenderedWordHasNoTranslationWordToCorrect()
    {
        var omitted = Absence(LinkRelation.Omits, Hebrew(2), EvidentiaDecisionRecorder.SafeTier);
        var supplied = Absence(LinkRelation.Expands, English(2), EvidentiaDecisionRecorder.SafeTier);

        var refused = () => Queue().Correct(omitted.Id, Hebrew(1), Reviewer, null);
        await Queue().Correct(supplied.Id, Hebrew(1), Reviewer, "it renders the article of the next word");
        await Writer().Apply(_run.Id, write: true);

        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage("*evidentia-reject*");
        var link = await StoredLink();
        link.Words.Should().BeEquivalentTo([(English(2), LinkSide.From), (Hebrew(1), LinkSide.To)],
            "a corrected supplied word is a pair, written as one");
    }

    [Fact]
    public async Task ARunKeepsItsAbsencesWithTheTierAndConfidenceOfThePairTheyRestOn()
    {
        var the = Analysis(English(2), _english, 2, "the");
        var god = Analysis(English(4), _english, 4, "God");
        var elohim = Analysis(Hebrew(3), _hebrew, 3, "אלהים");
        var created = Analysis(Hebrew(2), _hebrew, 2, "ברא");
        var anchor = new EvidentiaProposal(god, elohim, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.965,
            EvidentiaDecisionTrace.For(new EvidentiaCandidate(god, elohim, []), "review", "test"));
        var recorder = new EvidentiaDecisionRecorder(_run.Id);
        recorder.Record(new EvidentiaChapterDecisions(1, 1, [the.Token, god.Token], new HashSet<long> { god.Token.Id }, [],
            [anchor], [anchor],
            [
                new EvidentiaAbsence(the, EvidentiaAbsenceRule.UnwrittenArticle, anchor),
                new EvidentiaAbsence(created, EvidentiaAbsenceRule.Conjunction, anchor),
            ]));

        await new EvidentiaRunner(_db, null!).Write(recorder.Decisions, CancellationToken.None);

        var stored = await _db.EvidentiaDecisions.AsNoTracking().Where(decision => decision.Absence != null)
            .OrderBy(decision => decision.Id).ToListAsync();
        stored.Should().BeEquivalentTo(new object[]
        {
            new
            {
                SourceWordId = (long?)English(2), TargetWordId = (long?)null, Absence = (LinkRelation?)LinkRelation.Expands,
                Kind = "supplied-article", Tier = EvidentiaDecisionRecorder.SafeTier,
                AnchorSourceWordId = (long?)English(4), AnchorTargetWordId = (long?)Hebrew(3), Abstention = (EvidentiaAbstention?)null,
            },
            new
            {
                SourceWordId = (long?)null, TargetWordId = (long?)Hebrew(2), Absence = (LinkRelation?)LinkRelation.Omits,
                Kind = "unrendered-conjunction", Tier = EvidentiaDecisionRecorder.SafeTier,
                AnchorSourceWordId = (long?)English(4), AnchorTargetWordId = (long?)Hebrew(3), Abstention = (EvidentiaAbstention?)null,
            },
        }, options => options.WithStrictOrdering());
        stored[0].Confidence.Should().BeApproximately(0.965f, 0.0001f);
        stored[1].Confidence.Should().BeApproximately((float)EvidentiaAbsenceRule.Conjunction.Confidence, 0.0001f,
            "the rule's own record is below the pair's confidence here");
        stored[0].Rationale.Should().Contain("'God' → 'אלהים'");
    }

    private EvidentiaDecision Absence(LinkRelation relation, long word, string tier, string? kind = null)
    {
        var decision = new EvidentiaDecision
        {
            RunId = _run.Id,
            SourceWordId = relation == LinkRelation.Expands ? word : null,
            TargetWordId = relation == LinkRelation.Omits ? word : null,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Absence = relation,
            Kind = kind ?? (relation == LinkRelation.Expands ? "supplied-article" : "unrendered-conjunction"),
            Tier = tier,
            Rationale = "test",
            Confidence = RuleConfidence,
        };
        _db.EvidentiaDecisions.Add(decision);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return decision;
    }

    private long ExistingAbsence(LinkRelation relation, long word)
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = relation,
            Method = LinkMethod.StatedBySource,
            Source = "an existing loader",
            Words = [new LinkWord { WordId = word, Side = relation == LinkRelation.Omits ? LinkSide.To : LinkSide.From }],
            Claims = [new LinkClaim { Method = LinkMethod.StatedBySource, Source = "an existing loader" }],
        };
        _db.Links.Add(link);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return link.Id;
    }

    private EvidentiaDecision Proposal(long source, long target, string tier = "review")
    {
        var decision = new EvidentiaDecision
        {
            RunId = _run.Id,
            SourceWordId = source,
            TargetWordId = target,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Content = true,
            Kind = "global-review-known-rendering",
            Tier = tier,
            Rationale = "global known-rendering assignment; lexical score 0.47",
            Confidence = RuleConfidence,
            Score = 1,
            Candidates = 1,
            KnownRendering = 0.47f,
        };
        _db.EvidentiaDecisions.Add(decision);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return decision;
    }

    private long ExistingLink(long source, long target, LinkMethod method, double? confidence)
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Source = "an existing loader",
            Words =
            [
                new LinkWord { WordId = source, Side = LinkSide.From },
                new LinkWord { WordId = target, Side = LinkSide.To },
            ],
            Claims = [new LinkClaim { Method = method, Confidence = confidence, Source = "an existing loader" }],
        };
        _db.Links.Add(link);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return link.Id;
    }

    private async Task<(int Links, int Words, int Claims)> Counts() =>
        (await _db.Links.CountAsync(), await _db.LinkWords.CountAsync(), await _db.LinkClaims.CountAsync());

    private async Task<(long Id, LinkMethod Method, double? Confidence, List<(long, LinkSide)> Words, List<LinkClaim> Claims)> StoredLink()
    {
        var link = await _db.Links.AsNoTracking().Include(row => row.Words).Include(row => row.Claims).SingleAsync();
        return (link.Id, link.Method, link.Confidence,
            link.Words.Select(word => (word.WordId, word.Side)).ToList(), [.. link.Claims]);
    }

    private long English(int position) => _db.WordAt(_english, 1, 1, position).Id;

    private long Hebrew(int position) => _db.WordAt(_hebrew, 1, 1, position).Id;

    private static EvidentiaAnalysis Analysis(long id, Text text, int position, string surface, int verse = 1) =>
        new(new EvidentiaToken(id, new EvidentiaAddress(1, 1, verse), position, surface, text.Language),
            surface.ToLowerInvariant(), null, null, EvidentiaWordClass.Content, LanguagePackCapability.Normalisation);

    private EvidentiaReviewQueue Queue() => new(_db);

    private EvidentiaLinkWriter Writer() => new(_db, new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance));
}
