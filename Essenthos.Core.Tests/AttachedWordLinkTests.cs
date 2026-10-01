using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Migrations;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A word that writes another word's inflection is a link to that word's original, and says whose it
/// is. <em>did</em> of <em>did see</em> renders εἴδομεν as <em>see</em> does, so the two light up
/// together; what the link adds is that <em>did</em> goes with <em>see</em>, which is what the reader
/// names on hover and what the coverage counts apart.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class AttachedWordLinkTests : IDisposable
{
    private const string Reviewer = "a test reviewer";
    private const string AttachedTier = "attached";

    private readonly AppDbContext _db;
    private readonly Text _english;
    private readonly Text _greek;
    private readonly EvidentiaRun _run;

    public AttachedWordLinkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        _english = Corpus.Add(_db, "ENGT", TextKind.Translation, "eng",
            (1, 1, ["and", "we", "did", "see", "him", "go", "out"]));
        _greek = Corpus.Add(_db, "GRKT", TextKind.CriticalEdition, "grc",
            (1, 1, ["καὶ", "εἴδομεν", "αὐτὸν", "ἐξελθόντα"]));
        _db.SaveChanges();

        _run = new EvidentiaRun
        {
            FromTextId = _english.Id,
            ToTextId = _greek.Id,
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            RuleVersion = "test",
            Configuration = JsonDocument.Parse("{}"),
            Scope = JsonDocument.Parse("{}"),
        };
        _db.EvidentiaRuns.Add(_run);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        _db.Dispose();
    }

    private long And => English(1);
    private long We => English(2);
    private long Did => English(3);
    private long See => English(4);
    private long Go => English(6);
    private long Out => English(7);
    private long Kai => Greek(1);
    private long Eidomen => Greek(2);
    private long Exelthonta => Greek(4);

    [Fact]
    public async Task AWordOnItsHeadsCounterpartIsWrittenAsGoingWithItsHead()
    {
        Placed(See, Eidomen);
        Placed(Go, Exelthonta);
        Attached(Did, Eidomen, "AuxiliaryVerb of 'see'", head: (See, Eidomen));
        Attached(We, Eidomen, "SubjectPronoun of 'see'", head: (See, Eidomen));
        Attached(Out, Exelthonta, "PhrasalParticle of 'go'", head: (Go, Exelthonta));
        // On a part of its own: and renders the καί before its head's rendering, and is an ordinary link.
        Attached(And, Kai, "Conjunction of 'see'", head: (See, Eidomen));

        var outcome = await AcceptAndApply();

        outcome.Attached.Should().Be(3);
        (await Roles()).Should().BeEquivalentTo(new Dictionary<long, (LinkWordRole?, long?)>
        {
            [And] = (null, null),
            [We] = (LinkWordRole.Attached, See),
            [Did] = (LinkWordRole.Attached, See),
            [See] = (null, null),
            [Go] = (null, null),
            [Out] = (LinkWordRole.PhraseMember, Go),
        });
    }

    /// <summary>
    /// Decisions recorded before the head pair was kept name the head by its surface alone; the
    /// German and Spanish runs written that way are recognised by their rule and their rationale.
    /// </summary>
    [Fact]
    public async Task ADecisionWithoutItsHeadPairFindsTheHeadByItsSurfaceInTheVerse()
    {
        Placed(See, Eidomen);
        Attached(Did, Eidomen, "AuxiliaryVerb of 'see', by the parse of a model");
        Attached(And, Kai, "Conjunction of 'see'");

        await AcceptAndApply();

        var roles = await Roles();
        roles[Did].Should().Be((LinkWordRole.Attached, See));
        roles[And].Should().Be(((LinkWordRole?)null, (long?)null), "its head was placed on another word than it");
    }

    [Fact]
    public async Task AHeadTheRunDidNotPlaceIsFoundByTheLinkThatJoinsIt()
    {
        StatedLink(See, Eidomen);
        Attached(Did, Eidomen, "AuxiliaryVerb of 'see'");

        await AcceptAndApply();

        (await Roles())[Did].Should().Be((LinkWordRole.Attached, See));
    }

    /// <summary>The migration marks the links already written, the German and Spanish runs among them, as the Forge would.</summary>
    [Fact]
    public async Task TheMigrationMarksWhatWasWrittenBeforeIt()
    {
        Placed(See, Eidomen);
        Placed(Go, Exelthonta);
        Attached(Did, Eidomen, "AuxiliaryVerb of 'see'");
        Attached(We, Eidomen, "SubjectPronoun of 'see'", head: (See, Eidomen));
        Attached(Out, Exelthonta, "PhrasalParticle of 'go'");
        Attached(And, Kai, "Conjunction of 'see'");
        await AcceptAndApply();
        var marked = await Roles();
        await _db.LinkWords.ExecuteUpdateAsync(set => set
            .SetProperty(word => word.Role, (LinkWordRole?)null)
            .SetProperty(word => word.HeadWordId, (long?)null));

        var migration = new AWordThatWritesAnotherWordsInflectionGoesWithIt().UpOperations.OfType<SqlOperation>().Single();
        var updated = await _db.Database.ExecuteSqlRawAsync(migration.Sql);

        updated.Should().Be(3);
        (await Roles()).Should().BeEquivalentTo(marked);
    }

    [Fact]
    public async Task MarkingTwiceChangesNothing()
    {
        Placed(See, Eidomen);
        Attached(Did, Eidomen, "AuxiliaryVerb of 'see'");
        await AcceptAndApply();

        var again = await _db.Database.ExecuteSqlInterpolatedAsync(AttachedWords.Mark(null));

        again.Should().Be(0);
    }

    [Fact]
    public async Task TheReaderIsToldWhichWordAnAttachedWordGoesWith()
    {
        Placed(See, Eidomen);
        Placed(Go, Exelthonta);
        Attached(Did, Eidomen, "AuxiliaryVerb of 'see'", head: (See, Eidomen));
        Attached(Out, Exelthonta, "PhrasalParticle of 'go'", head: (Go, Exelthonta));
        await AcceptAndApply();

        var words = (await Endpoints.Texts.ReadByCanonicalVerse(_db, _english.Id, 1, 1, default))[1]
            .ToDictionary(word => word.Id);

        words[Did].Attached.Should().Be(new AttachedResponse("attached", See, "see"));
        words[Out].Attached.Should().Be(new AttachedResponse("phrase-member", Go, "go"));
        words[See].Attached.Should().BeNull();
        words[Did].OriginalWordIds.Should().Equal(words[See].OriginalWordIds, "it lights up with its head");
    }

    [Fact]
    public async Task CoverageCountsEachStateApart()
    {
        Placed(See, Eidomen);
        Placed(Go, Exelthonta);
        Attached(Did, Eidomen, "AuxiliaryVerb of 'see'", head: (See, Eidomen));
        Attached(Out, Exelthonta, "PhrasalParticle of 'go'", head: (Go, Exelthonta));
        Attached(And, Kai, "Conjunction of 'see'", head: (See, Eidomen));
        Supplied(We);
        await AcceptAndApply();

        var measures = await new CorpusCheck(_db, NullLogger<CorpusCheck>.Instance).Measure();
        var coverage = measures.Coverage.Single(row => row.Text == "ENGT");

        coverage.Should().BeEquivalentTo(new
        {
            Words = 7, Rendered = 5, Attached = 1, PhraseMember = 1, Linked = 3, StatedAbsent = 1, Silent = 1,
        });
        measures.Integrity.Should().OnlyContain(check => check.Found == 0);
        measures.Describe().Should().Contain("linked 3, attached 1, phrase member 1, supplied 1, unresolved 1");
    }

    [Fact]
    public async Task AHeadTakenOutOfItsTextLeavesItsWordRendering()
    {
        Placed(See, Eidomen);
        Attached(Did, Eidomen, "AuxiliaryVerb of 'see'", head: (See, Eidomen));
        await AcceptAndApply();

        await _db.Words.Where(word => word.Id == See).ExecuteDeleteAsync();

        var did = await _db.LinkWords.AsNoTracking().SingleAsync(word => word.WordId == Did);
        did.Should().BeEquivalentTo(new { Role = (LinkWordRole?)LinkWordRole.Attached, HeadWordId = (long?)null });
    }

    [Fact]
    public void ARecordedAttachedWordKeepsThePairItRestsOn()
    {
        var see = Analysis(See, 4, "see", "eng");
        var did = Analysis(Did, 3, "did", "eng");
        var eidomen = Analysis(Eidomen, 2, "εἴδομεν", "grc");
        var head = new EvidentiaProposal(see, eidomen, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.9,
            EvidentiaDecisionTrace.For(new EvidentiaCandidate(see, eidomen, []), "review", "test"));
        var attached = new EvidentiaProposal(did, eidomen, EvidentiaProposalKind.AttachedWord, 0.8,
            new EvidentiaDecisionTrace(AttachedTier, "AuxiliaryVerb of 'see'", []), head);
        var recorder = new EvidentiaDecisionRecorder(_run.Id);

        recorder.Record(new EvidentiaChapterDecisions(1, 1, [see.Token, did.Token],
            new HashSet<long> { See }, [new EvidentiaCandidate(see, eidomen, [])], [head], [head, attached]));

        recorder.Decisions.Single(decision => decision.SourceWordId == Did).Should().BeEquivalentTo(new
        {
            Kind = AttachedWords.DecisionKind,
            AnchorSourceWordId = (long?)See,
            AnchorTargetWordId = (long?)Eidomen,
        });
        recorder.Decisions.Single(decision => decision.SourceWordId == See).AnchorSourceWordId.Should().BeNull();
        EvidentiaStateScore.StateOf(attached).Should().Be(EvidentiaWordState.Attached,
            "the measurement and the stored role read the same word the same way");
    }

    private async Task<EvidentiaApplyOutcome> AcceptAndApply()
    {
        var queue = new EvidentiaReviewQueue(_db);
        await queue.AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier), Reviewer, null);
        await queue.AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: AttachedTier), Reviewer, null);
        return await new EvidentiaLinkWriter(_db, new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance))
            .Apply(_run.Id, write: true);
    }

    private async Task<Dictionary<long, (LinkWordRole?, long?)>> Roles() =>
        await _db.LinkWords.AsNoTracking()
            .Where(word => word.Side == LinkSide.From)
            .ToDictionaryAsync(word => word.WordId, word => (word.Role, word.HeadWordId));

    private void Placed(long source, long target) =>
        Decide(source, target, "global-review-known-rendering", EvidentiaDecisionRecorder.SafeTier, "test", null);

    private void Attached(long source, long target, string rationale, (long Source, long Target)? head = null) =>
        Decide(source, target, AttachedWords.DecisionKind, AttachedTier, rationale, head);

    private void Supplied(long source)
    {
        _db.EvidentiaDecisions.Add(new EvidentiaDecision
        {
            RunId = _run.Id, SourceWordId = source, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1,
            Kind = EvidentiaAbsenceRule.UnwrittenArticle.Spelling, Tier = EvidentiaDecisionRecorder.SafeTier, Rationale = "test",
            Confidence = 0.9f, Absence = LinkRelation.Expands,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private void Decide(long source, long target, string kind, string tier, string rationale, (long Source, long Target)? head)
    {
        _db.EvidentiaDecisions.Add(new EvidentiaDecision
        {
            RunId = _run.Id,
            SourceWordId = source,
            TargetWordId = target,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Content = true,
            Kind = kind,
            Tier = tier,
            Rationale = rationale,
            Confidence = 0.9f,
            Score = 1,
            Candidates = 1,
            AnchorSourceWordId = head?.Source,
            AnchorTargetWordId = head?.Target,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private void StatedLink(long source, long target)
    {
        _db.Links.Add(new Link
        {
            FromTextId = _english.Id,
            ToTextId = _greek.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Provenance = new() { Source = "a test table" },
            Words = [new LinkWord { WordId = source, Side = LinkSide.From }, new LinkWord { WordId = target, Side = LinkSide.To }],
            Claims = [new LinkClaim { Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test table" }}],
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private static EvidentiaAnalysis Analysis(long id, int position, string surface, string language) =>
        new(new EvidentiaToken(id, new EvidentiaAddress(1, 1, 1), position, surface, language),
            surface, null, null, EvidentiaWordClass.Content, LanguagePackCapability.Normalisation);

    private long English(int position) => _db.WordAt(_english, 1, 1, position).Id;

    private long Greek(int position) => _db.WordAt(_greek, 1, 1, position).Id;
}
