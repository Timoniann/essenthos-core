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

namespace Essenthos.Core.Tests.Evidentia;

/// <summary>
/// A word that writes another word's inflection is a link to that word's original, and says whose it
/// is. <em>did</em> of <em>did see</em> renders εἴδομεν as <em>see</em> does, so the two light up
/// together; what the link adds is that <em>did</em> goes with <em>see</em>. A stored run records the pair the
/// attached word rests on, which the ledger and the build's evidentia files read.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class AttachedWordLinkTests : IDisposable
{
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

    private long Did => English(3);
    private long See => English(4);
    private long Eidomen => Greek(2);

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

    private static EvidentiaAnalysis Analysis(long id, int position, string surface, string language) =>
        new(new EvidentiaToken(id, new EvidentiaAddress(1, 1, 1), position, surface, language),
            surface, null, null, EvidentiaWordClass.Content, LanguagePackCapability.Normalisation);

    private long English(int position) => _db.WordAt(_english, 1, 1, position).Id;

    private long Greek(int position) => _db.WordAt(_greek, 1, 1, position).Id;
}
