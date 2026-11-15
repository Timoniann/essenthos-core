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
/// The verdicts given on EVIDENTIA's proposals, kept in files so that the build reads them without a database
/// (evidentia-pairs). What these guard is that the ledger names a word by its address and surface, never by its
/// id, and that a verdict the ledger lacks is what a refused release names.
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
    /// and an absence in it, and a proposal rejected, written to the ledger as the verbs do.
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
        await Ledger().Export(_run.Id, _resources);
        _db.ChangeTracker.Clear();
    }

    private void Seed()
    {
        _english = Corpus.Add(_db, "ENGT", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning", "God", "created"]));
        _hebrew = Corpus.Add(_db, "HEBT", TextKind.CriticalEdition, "hbo", (1, 1, ["בראשית", "ברא", "אלהים"]));
        _db.SaveChanges();
    }

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

    private EvidentiaLedger Ledger() => new(_db);
}
