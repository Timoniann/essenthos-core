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

namespace Essenthos.Core.Tests.Evidentia;

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

    /// <summary>The next class in the collection may count every text there is, so none of these stay behind.</summary>
    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        _db.Dispose();
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
    public async Task ATierAcceptedByRuleLeavesTheRunsOtherDecisionsWaiting()
    {
        var supplied = Absence(LinkRelation.Expands, English(2), EvidentiaDecisionRecorder.SafeTier,
            EvidentiaAbsenceRule.GermanUnwrittenArticle.Spelling);
        var god = Proposal(English(4), Hebrew(3), EvidentiaDecisionRecorder.SafeTier);

        var accepted = await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(
            Tier: EvidentiaDecisionRecorder.SafeTier, Kinds: [EvidentiaAbsenceRule.GermanUnwrittenArticle.Spelling]), Reviewer, null);

        accepted.Should().Be(1);
        (await _db.EvidentiaReviews.AsNoTracking().SingleAsync()).DecisionId.Should().Be(supplied.Id,
            "a run over a text its sources already link has its absences accepted without its proposals");
        god.Review.Should().BeNull();
    }

    [Fact]
    public void ARuleClaimNamesItsRun()
    {
        EvidentiaClaims.RunOf(EvidentiaClaims.RuleSource(26, "1.0.0+abc", "attached-word")).Should().Be(26);
        EvidentiaClaims.RunOf(NamesFirstPass.Source).Should().BeNull();
        EvidentiaClaims.RunOf("EVIDENTIA runner notes").Should().BeNull();
    }

    [Fact]
    public void AVerseReadWithTwoChaptersIsDecidedOnceAWord()
    {
        EvidentiaAnalysis At(long id, int position, string surface, int chapter) =>
            new(new EvidentiaToken(id, new EvidentiaAddress(18, chapter, chapter == 39 ? 27 : 1), position, surface, "spa"),
                surface, null, null, EvidentiaWordClass.Content, LanguagePackCapability.Normalisation);
        var behemoth = At(English(4), 4, "behemot", 39);
        var laterBehemoth = At(English(4), 4, "behemot", 40);
        var hand = At(English(5), 5, "mano", 39);
        var laterHand = At(English(5), 5, "mano", 40);
        var target = Analysis(Hebrew(3), _hebrew, 3, "בהמות");
        var proposal = new EvidentiaProposal(laterBehemoth, target, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.7,
            EvidentiaDecisionTrace.For(new EvidentiaCandidate(laterBehemoth, target, []), "review", "test"));
        var recorder = new EvidentiaDecisionRecorder(_run.Id);

        recorder.Record(new EvidentiaChapterDecisions(18, 39, [behemoth.Token, hand.Token],
            new HashSet<long> { behemoth.Token.Id, hand.Token.Id }, [], [], []));
        recorder.Record(new EvidentiaChapterDecisions(18, 40, [laterBehemoth.Token, laterHand.Token],
            new HashSet<long> { laterBehemoth.Token.Id, laterHand.Token.Id }, [], [], [proposal]));

        recorder.Decisions.Should().BeEquivalentTo(new object[]
        {
            new { SourceWordId = (long?)English(4), TargetWordId = (long?)Hebrew(3), CanonicalChapter = (short)40 },
            new { SourceWordId = (long?)English(5), TargetWordId = (long?)null, CanonicalChapter = (short)39 },
        }, options => options.WithStrictOrdering(), "the chapter that placed the word stands, and otherwise the first");
    }

    [Fact]
    public async Task AStoredRunsLexicalPlacementsAreWhatALaterPassReadsAsConfirmed()
    {
        var god = _db.WordAt(_hebrew, 1, 1, 3);
        god.StrongNumber = "H430";
        _db.SaveChanges();
        Proposal(English(4), Hebrew(3), "safe");
        var article = Proposal(English(2), Hebrew(3));
        await _db.EvidentiaDecisions.Where(decision => decision.Id == article.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(decision => decision.Kind, "attached-word"));
        var packs = new LanguagePackRegistry([new EnglishLanguagePack(), new OriginalLanguagePack()]);
        packs.TryAnalyse(new EvidentiaToken(0, default, 0, "God", "eng"), out var word).Should().BeTrue();
        packs.TryAnalyse(new EvidentiaToken(0, default, 0, "the", "eng"), out var the).Should().BeTrue();

        var options = await new EvidentiaRunner(_db, null!).WithConfirmed(
            new EvidentiaMeasurementOptions(ConfirmedByRuns: [_run.Id]));

        var confirmed = options.Confirmed!.For("eng", packs);
        confirmed.Of(word!, "H430").Should().Be(new ConfirmedRendering(Safe: 1, Placed: 1, FormPlaced: 1));
        confirmed.Of(the!).Should().BeEmpty("a word attached to its head says nothing of its own");
        var unknown = () => new EvidentiaRunner(_db, null!).WithConfirmed(
            new EvidentiaMeasurementOptions(ConfirmedByRuns: [_run.Id + 1000]));
        await unknown.Should().ThrowAsync<InvalidOperationException>().WithMessage("*evidentia-runs*");
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
            Provenance = new() { Source = "an existing loader" },
            Words = [new LinkWord { WordId = word, Side = relation == LinkRelation.Omits ? LinkSide.To : LinkSide.From }],
            Claims = [new LinkClaim { Method = LinkMethod.StatedBySource, Provenance = new() { Source = "an existing loader" }}],
        };
        _db.Links.Add(link);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return link.Id;
    }

    private EvidentiaRun LaterRun()
    {
        var run = new EvidentiaRun
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            StartedAt = _run.StartedAt.AddDays(1),
            FinishedAt = _run.StartedAt.AddDays(1),
            RuleVersion = "test",
            Configuration = JsonDocument.Parse("{}"),
            Scope = JsonDocument.Parse("{}"),
        };
        _db.EvidentiaRuns.Add(run);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return run;
    }

    private EvidentiaDecision Proposal(long source, long target, string tier = "review", int? run = null)
    {
        var decision = new EvidentiaDecision
        {
            RunId = run ?? _run.Id,
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
            Provenance = new() { Source = "an existing loader" },
            Words =
            [
                new LinkWord { WordId = source, Side = LinkSide.From },
                new LinkWord { WordId = target, Side = LinkSide.To },
            ],
            Claims = [new LinkClaim { Method = method, Confidence = confidence, Provenance = new() { Source = "an existing loader" }}],
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
        var link = await _db.Links.AsNoTracking().Include(row => row.Words).Include(row => row.Provenance).Include(row => row.Claims).ThenInclude(claim => claim.Provenance).SingleAsync();
        return (link.Id, link.Method, link.Confidence,
            link.Words.Select(word => (word.WordId, word.Side)).ToList(), [.. link.Claims]);
    }

    private long English(int position) => _db.WordAt(_english, 1, 1, position).Id;

    private long Hebrew(int position) => _db.WordAt(_hebrew, 1, 1, position).Id;

    private static EvidentiaAnalysis Analysis(long id, Text text, int position, string surface, int verse = 1) =>
        new(new EvidentiaToken(id, new EvidentiaAddress(1, 1, verse), position, surface, text.Language),
            surface.ToLowerInvariant(), null, null, EvidentiaWordClass.Content, LanguagePackCapability.Normalisation);

    private EvidentiaReviewQueue Queue() => new(_db);
}
