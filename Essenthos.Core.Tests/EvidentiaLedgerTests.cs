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
/// The verdicts given on EVIDENTIA's proposals, kept in files so that the corpus can be rebuilt
/// without them. What these guard is that a rebuild gives back exactly what was decided, on the
/// words it was decided about although every id has changed, that a replay over a corpus already
/// holding them changes nothing, and that what cannot be replayed is said rather than guessed.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EvidentiaLedgerTests : IDisposable
{
    private const string Owner = "the project owner";
    private const float RuleConfidence = 0.62f;

    private readonly AppDbContext _db;
    private readonly string _resources = Directory.CreateTempSubdirectory("evidentia-ledger-").FullName;
    private Text _english = null!;
    private Text _hebrew = null!;
    private EvidentiaRun _run = null!;

    public EvidentiaLedgerTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        Seed();
        _run = new EvidentiaRun
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            RuleVersion = "1.0.0+test",
            Configuration = JsonDocument.Parse("""{"learn": "ENGT"}"""),
            Scope = JsonDocument.Parse("""{"books": ["1"]}"""),
        };
        _db.EvidentiaRuns.Add(_run);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        _db.Dispose();
        Directory.Delete(_resources, recursive: true);
    }

    [Fact]
    public async Task AColdRebuildGivesBackEveryVerdictOnTheWordsItNamed()
    {
        await Decide();
        var before = await Snapshot();
        var godBefore = English(4);

        await Rebuild();
        var outcome = await Ledger().Replay(_resources);

        English(4).Should().NotBe(godBefore, "a rebuild renumbers every word, which is why the ledger never names one by id");
        outcome.Conflicts.Should().BeEmpty();
        outcome.Reconstituted.Should().Be(1);
        outcome.Restored.Should().Be(4);
        (await Snapshot()).Should().Equal(before);

        var reviews = await _db.EvidentiaReviews.AsNoTracking().Include(review => review.Decision).ToListAsync();
        reviews.Select(review => (EnumSpelling.Of(review.Verdict), review.Examined, review.Reviewer)).Should().BeEquivalentTo(
        [
            ("approved", true, Owner),
            ("approved", false, Owner),
            ("approved", false, Owner),
            ("rejected", true, Owner),
        ]);
        var person = await _db.LinkClaims.AsNoTracking().SingleAsync(claim => claim.Method == LinkMethod.Manual);
        person.Source.Should().StartWith(Owner + ", reviewing EVIDENTIA run");
        person.Note.Should().Be("read against the verse");
    }

    [Fact]
    public async Task AReplayOverACorpusThatHoldsTheVerdictsChangesNothing()
    {
        await Decide();
        var before = await Snapshot();

        var outcome = await Ledger().Replay(_resources);

        outcome.AlreadyThere.Should().Be(4);
        outcome.Restored.Should().Be(0);
        outcome.Applied.Should().BeEmpty("nothing was missing, so nothing is written");
        (await Snapshot()).Should().Equal(before);
    }

    [Fact]
    public async Task AVerdictWhoseLinkSomethingDeletedIsWrittenAgain()
    {
        await Decide();
        var before = await Snapshot();
        var god = English(4);
        await _db.Links.Where(link => link.Words.Any(word => word.WordId == god)).ExecuteDeleteAsync();

        var outcome = await Ledger().Replay(_resources);

        outcome.Restored.Should().Be(1, "a redraw took the link and the person's claim on it with it");
        (await Snapshot()).Should().Equal(before);
    }

    [Fact]
    public async Task AWordThatNoLongerReadsTheSameIsReportedAndNotGuessedAt()
    {
        await Decide();

        await Rebuild(god: "Lord");
        var outcome = await Ledger().Replay(_resources);

        outcome.Conflicts.Should().ContainSingle().Which.Should().Contain("read 'God' and reads 'Lord' now");
        (await _db.EvidentiaReviews.CountAsync()).Should().Be(3, "the other verdicts are about words that read as they did");
        (await _db.Links.AnyAsync(link => link.Words.Any(word => word.WordId == English(4)))).Should().BeFalse();
    }

    [Fact]
    public async Task AVerdictTheCorpusHoldsDifferentlyIsReportedAndTheCorpusKept()
    {
        var god = Proposal(English(4), Hebrew(3));
        await Queue().Approve([god.Id], Owner, null);
        await Ledger().Export(_run.Id, _resources);
        await _db.EvidentiaReviews.ExecuteUpdateAsync(set => set.SetProperty(review => review.Verdict, EvidentiaVerdict.Rejected));

        var outcome = await Ledger().Replay(_resources);

        outcome.Conflicts.Should().ContainSingle().Which.Should().Contain("the corpus holds rejected and the ledger approved");
        (await _db.EvidentiaReviews.AsNoTracking().SingleAsync()).Verdict.Should().Be(EvidentiaVerdict.Rejected);
    }

    [Fact]
    public async Task AVerdictNotInTheLedgerIsWhatARefusedReleaseNames()
    {
        var god = Proposal(English(4), Hebrew(3));
        await Queue().Approve([god.Id], Owner, null);

        (await EvidentiaLedger.Unrecorded(_db, _resources)).Should().ContainSingle()
            .Which.Should().Contain($"evidentia-export {_run.Id}");

        await Ledger().Export(_run.Id, _resources);

        (await EvidentiaLedger.Unrecorded(_db, _resources)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLedgerNamesAWordByItsAddressAndSurfaceAndNeverByItsId()
    {
        await Decide();

        var folder = Path.Combine(EvidentiaLedger.Folder(_resources),
            EvidentiaLedger.RunFolder("ENGT", "HEBT", (await _db.EvidentiaRuns.AsNoTracking().SingleAsync()).StartedAt));
        var lines = await File.ReadAllLinesAsync(Path.Combine(folder, "01.tsv"));

        lines.Should().HaveCount(5, "a header and one line per verdict");
        lines.Should().Contain(line => line.StartsWith("1:1\t4\tGod\t\t3\tאלהים\t\tglobal-review-known-rendering\treview\t0.62\tapproved\t"));
        lines.Should().Contain(line => line.StartsWith("1:1\t2\tthe\t\t\t\texpands\tsupplied-article\tsafe\t0.62\tapproved\t"));
        lines.Should().NotContain(line => line.Split('\t').Contains(English(4).ToString()));
        File.ReadAllText(Path.Combine(folder, EvidentiaLedger.RunFile)).Should().Contain("\"reviewer\": \"the project owner\"");
    }

    /// <summary>
    /// One verdict of each kind: a proposal read and approved, a tier accepted unread with a proposal
    /// and an absence in it, and a proposal rejected. Applied and written to the ledger, as the verbs do.
    /// </summary>
    private async Task Decide()
    {
        var god = Proposal(English(4), Hebrew(3));
        Proposal(English(5), Hebrew(2), EvidentiaDecisionRecorder.SafeTier);
        var beginning = Proposal(English(3), Hebrew(2));
        Absence(English(2));
        await Queue().Approve([god.Id], Owner, "read against the verse");
        await Queue().AcceptTier(_run.Id, new EvidentiaQueueFilter(Tier: EvidentiaDecisionRecorder.SafeTier), Owner, null);
        await Queue().Reject([beginning.Id], Owner, "the verb is not the beginning");
        await Writer().Apply(_run.Id, write: true);
        await Ledger().Export(_run.Id, _resources);
        _db.ChangeTracker.Clear();
    }

    /// <summary>The corpus as a rebuild leaves it: the same texts, every row written again under new ids, no run.</summary>
    private async Task Rebuild(string god = "God")
    {
        await _db.Database.ExecuteSqlRawAsync("TRUNCATE text CASCADE");
        _db.ChangeTracker.Clear();
        Seed(god);
    }

    private void Seed(string god = "God")
    {
        _english = Corpus.Add(_db, "ENGT", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning", god, "created"]));
        _hebrew = Corpus.Add(_db, "HEBT", TextKind.CriticalEdition, "hbo", (1, 1, ["בראשית", "ברא", "אלהים"]));
        _db.SaveChanges();
    }

    /// <summary>Every link by the words it names and what claims it, so two corpora with different ids compare.</summary>
    private async Task<List<string>> Snapshot() =>
        (await _db.Links.AsNoTracking()
            .Include(link => link.Words).ThenInclude(word => word.Word)
            .Include(link => link.Claims)
            .ToListAsync())
        .Select(link =>
            $"{EnumSpelling.Of(link.Relation)} "
            + string.Join(",", link.Words.Where(word => word.Side == LinkSide.From).Select(word => word.Word!.Surface).Order())
            + " > "
            + string.Join(",", link.Words.Where(word => word.Side == LinkSide.To).Select(word => word.Word!.Surface).Order())
            + $" {EnumSpelling.Of(link.Method)} {link.Confidence} ["
            + string.Join(",", link.Claims.Select(claim => $"{EnumSpelling.Of(claim.Method)} {claim.Confidence}").Order())
            + "]")
        .Order(StringComparer.Ordinal)
        .ToList();

    private EvidentiaDecision Proposal(long source, long target, string tier = "review") =>
        Stored(new EvidentiaDecision
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
            Candidates = 1,
        });

    private EvidentiaDecision Absence(long word) =>
        Stored(new EvidentiaDecision
        {
            RunId = _run.Id,
            SourceWordId = word,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Absence = LinkRelation.Expands,
            Kind = "supplied-article",
            Tier = EvidentiaDecisionRecorder.SafeTier,
            Rationale = "the English article with no article under it",
            Confidence = RuleConfidence,
        });

    private EvidentiaDecision Stored(EvidentiaDecision decision)
    {
        _db.EvidentiaDecisions.Add(decision);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return decision;
    }

    private long English(int position) => _db.WordAt(_english, 1, 1, position).Id;

    private long Hebrew(int position) => _db.WordAt(_hebrew, 1, 1, position).Id;

    private EvidentiaReviewQueue Queue() => new(_db);

    private EvidentiaLinkWriter Writer() => new(_db, new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance));

    private EvidentiaLedger Ledger() => new(_db, Writer());
}
