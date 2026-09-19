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
