using System.Text.Json;
using System.Text.RegularExpressions;
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
/// Two runs over one verse that between them take every road a verdict can take into the corpus: a
/// new pair and a new absence, a claim on a link naming the same words and on one naming more, a
/// settled answer outranked, a rule withheld by a stated placement elsewhere, an aligner's word
/// withdrawn for an absence, a later run's rendering taking back an earlier run's absences, a later
/// placement replacing an earlier run's, a correction and a rejection. What it guards is that the
/// rows written are exactly these, with every source and note, and that a rebuild replaying the
/// ledger writes them again.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EvidentiaApplyEquivalenceTests : IDisposable
{
    private const string Owner = "the project owner";
    private const float RuleConfidence = 0.62f;
    private const string Safe = EvidentiaDecisionRecorder.SafeTier;
    private const string Review = "review";

    private readonly AppDbContext _db;
    private readonly string _resources = Directory.CreateTempSubdirectory("evidentia-equivalence-").FullName;
    private Text _english = null!;
    private Text _hebrew = null!;

    public EvidentiaApplyEquivalenceTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        Seed();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        _db.Dispose();
        Directory.Delete(_resources, recursive: true);
    }

    [Fact]
    public async Task EveryRoadWritesTheRowsItAlwaysWrote()
    {
        var (earlier, later) = await Decide();

        earlier.Should().Be("10 verdicts: 4 new (3 absences), 3 on the same words, 1 on more, 2 promoted, 2 withheld, 1 withdrawn, 0 superseded, 0 replaced");
        later.Should().Be("4 verdicts: 2 new (0 absences), 2 on the same words, 0 on more, 0 promoted, 0 withheld, 0 withdrawn, 2 superseded, 1 replaced");
        (await Snapshot()).Should().Equal(Expected);
    }

    [Fact]
    public async Task ARebuildReplayingTheLedgerWritesTheSameRows()
    {
        await Decide();
        await Ledger().ExportAll(_resources);
        (await Snapshot()).Should().Equal(Expected);

        await _db.Database.ExecuteSqlRawAsync("TRUNCATE text CASCADE");
        _db.ChangeTracker.Clear();
        Seed();
        var outcome = await Ledger().Replay(_resources);

        outcome.Conflicts.Should().BeEmpty();
        outcome.Reconstituted.Should().Be(2);
        var replayed = await Snapshot();
        replayed.Should().Equal([.. Expected.Append(ReplayedOutOfOrder).Order(StringComparer.Ordinal)]);
        (await Ledger().Replay(_resources)).Restored.Should().Be(0, "a second replay finds every verdict in the corpus as recorded");
        (await Snapshot()).Should().Equal(replayed);
    }

    private static readonly string[] Expected =
    [
        """
        link expands E2 > - rule-based 0.62 EVIDENTIA run # (earlier), supplied-article | decision #, safe tier: nothing under it; accepted with its tier by the project owner, not read one by one; the aligner's link of this word was withdrawn for it shape True roles [] claims [rule-based 0.62 EVIDENTIA run # (earlier), supplied-article | decision #, safe tier: nothing under it; accepted with its tier by the project owner, not read one by one; the aligner's link of this word was withdrawn for it; rule-based 0.62 EVIDENTIA run # (later), supplied-article | decision #, safe tier: nothing under it; accepted with its tier by the project owner, not read one by one]
        """,
        """
        link renders E1,E3 > H1 aligner 0.5 an existing aligner loader | - shape True roles [] claims [aligner 0.5 an existing aligner loader | -]
        """,
        """
        link renders E10,E9 > H7 stated-by-source  an existing stated-by-source loader | - shape True roles [] claims [rule-based 0.62 EVIDENTIA run # (earlier), global-review-known-rendering | names one word pair of this link; decision #, safe tier: global known-rendering assignment; lexical score #.#; accepted with its tier by the project owner, not read one by one; stated-by-source  an existing stated-by-source loader | -]
        """,
        """
        link renders E11 > H9 rule-based 0.62 EVIDENTIA run # (later), global-review-known-rendering | decision #, safe tier: global known-rendering assignment; lexical score #.#; accepted with its tier by the project owner, not read one by one; an earlier run's placement of this word was taken back for it shape True roles [] claims [rule-based 0.62 EVIDENTIA run # (later), global-review-known-rendering | decision #, safe tier: global known-rendering assignment; lexical score #.#; accepted with its tier by the project owner, not read one by one; an earlier run's placement of this word was taken back for it]
        """,
        """
        link renders E4 > H3 stated-by-source  an existing stated-by-source loader | - shape True roles [] claims [manual  the project owner, reviewing EVIDENTIA run # decision # | read against the verse; rule-based 0.62 EVIDENTIA run # (earlier), global-review-known-rendering | decision #, review tier: global known-rendering assignment; lexical score #.#; rule-based 0.62 EVIDENTIA run # (later), global-review-known-rendering | decision #, safe tier: global known-rendering assignment; lexical score #.#; accepted with its tier by the project owner, not read one by one; stated-by-source  an existing stated-by-source loader | -]
        """,
        """
        link renders E5 > H2 rule-based 0.62 EVIDENTIA run # (earlier), global-review-known-rendering | decision #, safe tier: global known-rendering assignment; lexical score #.#; accepted with its tier by the project owner, not read one by one shape True roles [] claims [aligner 0.6 an existing aligner loader | -; rule-based 0.62 EVIDENTIA run # (earlier), global-review-known-rendering | decision #, safe tier: global known-rendering assignment; lexical score #.#; accepted with its tier by the project owner, not read one by one]
        """,
        """
        link renders E6 > H4 rule-based 0.62 EVIDENTIA run # (later), global-review-known-rendering | decision #, safe tier: global known-rendering assignment; lexical score #.#; accepted with its tier by the project owner, not read one by one; an earlier run's absence on this word was taken back for it shape True roles [] claims [rule-based 0.62 EVIDENTIA run # (later), global-review-known-rendering | decision #, safe tier: global known-rendering assignment; lexical score #.#; accepted with its tier by the project owner, not read one by one; an earlier run's absence on this word was taken back for it]
        """,
        """
        link renders E7 > H5 strong-number 0.9 an existing strong-number loader | - shape True roles [] claims [strong-number 0.9 an existing strong-number loader | -]
        """,
        """
        link renders E8 > H6 manual  the project owner, correcting EVIDENTIA run # decision # | the conjunction shape True roles [] claims [aligner 0.4 an existing aligner loader | -; manual  the project owner, correcting EVIDENTIA run # decision # | the conjunction]
        """,
        """
        review earlier global-review-known-rendering E10 > H7 approved examined False corrected - applied True withheld - link renders E10,E9 > H7
        """,
        """
        review earlier global-review-known-rendering E11 > H8 approved examined False corrected - applied True withheld review # of run # places this word elsewhere, so this placement was taken back link -
        """,
        """
        review earlier global-review-known-rendering E3 > H2 rejected examined True corrected - applied False withheld - link -
        """,
        """
        review earlier global-review-known-rendering E4 > H3 approved examined True corrected - applied True withheld - link renders E4 > H3
        """,
        """
        review earlier global-review-known-rendering E5 > H2 approved examined False corrected - applied True withheld - link renders E5 > H2
        """,
        """
        review earlier global-review-known-rendering E7 > H4 approved examined False corrected - applied False withheld link # (strong-number) places this word on other words link -
        """,
        """
        review earlier global-review-known-rendering E8 > H4 corrected examined True corrected H6 applied True withheld - link renders E8 > H6
        """,
        """
        review earlier supplied-article E2 > - approved examined False corrected - applied True withheld - link expands E2 > -
        """,
        """
        review earlier supplied-article E6 > - approved examined False corrected - applied True withheld review # of run # renders this word, so this absence was taken back link -
        """,
        """
        review earlier supplied-article E9 > - approved examined False corrected - applied False withheld link # (stated-by-source) gives this word a counterpart link -
        """,
        """
        review earlier unrendered-conjunction - > H4 approved examined False corrected - applied True withheld review # of run # renders this word, so this absence was taken back link -
        """,
        """
        review later global-review-known-rendering E11 > H9 approved examined False corrected - applied True withheld - link renders E11 > H9
        """,
        """
        review later global-review-known-rendering E4 > H3 approved examined False corrected - applied True withheld - link renders E4 > H3
        """,
        """
        review later global-review-known-rendering E6 > H4 approved examined False corrected - applied True withheld - link renders E6 > H4
        """,
        """
        review later supplied-article E2 > - approved examined False corrected - applied True withheld - link expands E2 > -
        """,
        """
        withdrawal E11 for later global-review-known-rendering E11: link - renders E11 > H8 rule-based 0.62 EVIDENTIA run # (earlier), global-review-known-rendering | decision #, safe tier: global known-rendering assignment; lexical score #.#; accepted with its tier by the project owner, not read one by one claims [EVIDENTIA run # (earlier), global-review-known-rendering] [0.62] [decision #, safe tier: global known-rendering assignment; lexical score #.#; accepted with its tier by the project owner, not read one by one]
        """,
        """
        withdrawal E2 for earlier supplied-article E2: link - renders E2 > H1 aligner 0.35 an existing aligner loader | - claims [an existing aligner loader] [0.35] [-]
        """,
        """
        withdrawal E6 for later global-review-known-rendering E6: link - expands E6 > - rule-based 0.62 EVIDENTIA run # (earlier), supplied-article | decision #, safe tier: nothing under it; accepted with its tier by the project owner, not read one by one claims [EVIDENTIA run # (earlier), supplied-article] [0.62] [decision #, safe tier: nothing under it; accepted with its tier by the project owner, not read one by one]
        """,
        """
        withdrawal H4 for later global-review-known-rendering E6: link - omits - > H4 rule-based 0.62 EVIDENTIA run # (earlier), unrendered-conjunction | decision #, safe tier: nothing under it; accepted with its tier by the project owner, not read one by one claims [EVIDENTIA run # (earlier), unrendered-conjunction] [0.62] [decision #, safe tier: nothing under it; accepted with its tier by the project owner, not read one by one]
        """,
    ];

    /// <summary>
    /// What a replay adds to <see cref="Expected"/>. The ledger gives a run's verdicts back in the order
    /// of its words, not the order they were given, so the earlier run's pair on the fourth Hebrew word
    /// now comes after its absence on the same word, takes that absence back and is then withheld; the
    /// absence stays, because its own verdict still names it, and the later run takes it back again.
    /// </summary>
    private const string ReplayedOutOfOrder =
        """
        withdrawal H4 for earlier global-review-known-rendering E7: link - omits - > H4 rule-based 0.62 EVIDENTIA run # (earlier), unrendered-conjunction | decision #, safe tier: nothing under it; accepted with its tier by the project owner, not read one by one claims [EVIDENTIA run # (earlier), unrendered-conjunction] [0.62] [decision #, safe tier: nothing under it; accepted with its tier by the project owner, not read one by one]
        """;

    private async Task<(string Earlier, string Later)> Decide()
    {
        var earlier = Run("earlier", DateTimeOffset.UtcNow.AddDays(-1));
        var read = Proposal(earlier, 4, 3, Review);
        Proposal(earlier, 5, 2, Safe);
        Proposal(earlier, 7, 4, Safe);
        Proposal(earlier, 10, 7, Safe);
        Absence(earlier, LinkRelation.Expands, 2, Safe, "supplied-article");
        Absence(earlier, LinkRelation.Expands, 6, Safe, "supplied-article");
        Absence(earlier, LinkRelation.Omits, 4, Safe, "unrendered-conjunction");
        var corrected = Proposal(earlier, 8, 4, Review);
        var rejected = Proposal(earlier, 3, 2, Review);
        Proposal(earlier, 11, 8, Safe);
        Absence(earlier, LinkRelation.Expands, 9, Review, "supplied-article");
        await Queue().Approve([read.Id], Owner, "read against the verse");
        await Queue().Correct(corrected.Id, Hebrew(6), Owner, "the conjunction");
        await Queue().Reject([rejected.Id], Owner, "the verb is not the beginning");
        await Queue().AcceptTier(earlier.Id, new EvidentiaQueueFilter(Tier: Safe), Owner, null);
        await Queue().AcceptTier(earlier.Id, new EvidentiaQueueFilter(Tier: Review), Owner, null);
        var first = await Writer().Apply(earlier.Id, write: true);

        var later = Run("later", DateTimeOffset.UtcNow);
        Proposal(later, 6, 4, Safe);
        Proposal(later, 11, 9, Safe);
        Proposal(later, 4, 3, Safe);
        Absence(later, LinkRelation.Expands, 2, Safe, "supplied-article");
        await Queue().AcceptTier(later.Id, new EvidentiaQueueFilter(Tier: Safe), Owner, null);
        var second = await Writer().Apply(later.Id, write: true);
        _db.ChangeTracker.Clear();
        return (Tally(first), Tally(second));
    }

    private static string Tally(EvidentiaApplyOutcome outcome) =>
        $"{outcome.Verdicts} verdicts: {outcome.NewLinks} new ({outcome.NewAbsences} absences), {outcome.OnExisting} on the same words, "
        + $"{outcome.Within} on more, {outcome.Promoted} promoted, {outcome.Withheld} withheld, {outcome.Withdrawn} withdrawn, "
        + $"{outcome.Superseded} superseded, {outcome.Replaced} replaced";

    /// <summary>
    /// The corpus as a cold load leaves it before any verdict: each text, and the links its other
    /// sources and the aligner state.
    /// </summary>
    private void Seed()
    {
        _english = Corpus.Add(_db, "ENGT", TextKind.Translation, "eng",
            (1, 1, ["In", "the", "beginning", "God", "created", "the", "heaven", "and", "the", "earth", "void"]));
        _hebrew = Corpus.Add(_db, "HEBT", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בראשית", "ברא", "אלהים", "את", "השמים", "ואת", "הארץ", "תהו", "ובהו"]));
        _db.SaveChanges();
        Existing([2], [1], LinkMethod.Aligner, 0.35);
        Existing([1, 3], [1], LinkMethod.Aligner, 0.5);
        Existing([4], [3], LinkMethod.StatedBySource, null);
        Existing([5], [2], LinkMethod.Aligner, 0.6);
        Existing([7], [5], LinkMethod.StrongNumber, 0.9);
        Existing([8], [6], LinkMethod.Aligner, 0.4);
        Existing([9, 10], [7], LinkMethod.StatedBySource, null);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private void Existing(int[] from, int[] to, LinkMethod method, double? confidence) =>
        _db.Links.Add(new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Provenance = new Provenance { Source = $"an existing {EnumSpelling.Of(method)} loader" },
            Words =
            [
                .. from.Select(position => new LinkWord { WordId = English(position), Side = LinkSide.From }),
                .. to.Select(position => new LinkWord { WordId = Hebrew(position), Side = LinkSide.To }),
            ],
            Claims =
            [
                new LinkClaim
                {
                    Method = method, Confidence = confidence,
                    Provenance = new Provenance { Source = $"an existing {EnumSpelling.Of(method)} loader" },
                },
            ],
        });

    private EvidentiaRun Run(string version, DateTimeOffset startedAt)
    {
        var run = new EvidentiaRun
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            StartedAt = startedAt,
            FinishedAt = startedAt,
            RuleVersion = version,
            Configuration = JsonDocument.Parse("{}"),
            Scope = JsonDocument.Parse("""{"books": ["1"]}"""),
        };
        _db.EvidentiaRuns.Add(run);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return run;
    }

    private EvidentiaDecision Proposal(EvidentiaRun run, int source, int target, string tier) =>
        Stored(new EvidentiaDecision
        {
            RunId = run.Id,
            SourceWordId = English(source),
            TargetWordId = Hebrew(target),
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

    private EvidentiaDecision Absence(EvidentiaRun run, LinkRelation relation, int position, string tier, string kind) =>
        Stored(new EvidentiaDecision
        {
            RunId = run.Id,
            SourceWordId = relation == LinkRelation.Expands ? English(position) : null,
            TargetWordId = relation == LinkRelation.Omits ? Hebrew(position) : null,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Absence = relation,
            Kind = kind,
            Tier = tier,
            Rationale = "nothing under it",
            Confidence = RuleConfidence,
        });

    private EvidentiaDecision Stored(EvidentiaDecision decision)
    {
        _db.EvidentiaDecisions.Add(decision);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return decision;
    }

    /// <summary>
    /// Every link, review and withdrawal by the words it names and what it says, with every number in
    /// a source or note blotted out, so two corpora with different ids compare.
    /// </summary>
    private async Task<List<string>> Snapshot()
    {
        var words = await _db.Words.AsNoTracking()
            .Where(word => word.TextId == _english.Id || word.TextId == _hebrew.Id)
            .ToDictionaryAsync(word => word.Id, word => $"{(word.TextId == _english.Id ? "E" : "H")}{word.Position}");
        string Named(IEnumerable<long> ids) => ids.Any() ? string.Join(",", ids.Select(id => words[id]).Order(StringComparer.Ordinal)) : "-";
        var links = await _db.Links.AsNoTracking()
            .Include(link => link.Words).Include(link => link.Provenance)
            .Include(link => link.Claims).ThenInclude(claim => claim.Provenance)
            .ToListAsync();
        var shapes = links.ToDictionary(link => link.Id, link =>
            $"{EnumSpelling.Of(link.Relation)} {Named(link.Words.Where(word => word.Side == LinkSide.From).Select(word => word.WordId))}"
            + $" > {Named(link.Words.Where(word => word.Side == LinkSide.To).Select(word => word.WordId))}");
        var lines = links.Select(link =>
            $"link {shapes[link.Id]} {EnumSpelling.Of(link.Method)} {link.Confidence} {Blotted(link.Provenance!.Source)} | {Blotted(link.Provenance.Note)}"
            + " shape " + (link.Fingerprint == LinkShape.Of(
                link.Words.Where(word => word.Side == LinkSide.From).Select(word => word.WordId),
                link.Words.Where(word => word.Side == LinkSide.To).Select(word => word.WordId)))
            + " roles [" + string.Join(",", link.Words.Where(word => word.Role is not null)
                .Select(word => $"{words[word.WordId]} {word.Role} {(word.HeadWordId is { } head ? words[head] : "-")}").Order(StringComparer.Ordinal)) + "]"
            + " claims [" + string.Join("; ", link.Claims
                .Select(claim => $"{EnumSpelling.Of(claim.Method)} {claim.Confidence} {Blotted(claim.Provenance!.Source)} | {Blotted(claim.Provenance.Note)}")
                .Order(StringComparer.Ordinal)) + "]")
            .ToList();

        var reviews = await _db.EvidentiaReviews.AsNoTracking().Include(review => review.Decision).ThenInclude(decision => decision!.Run)
            .ToListAsync();
        lines.AddRange(reviews.Select(review =>
            $"review {review.Decision!.Run!.RuleVersion} {review.Decision.Kind} "
            + $"{(review.Decision.SourceWordId is { } source ? words[source] : "-")} > {(review.Decision.TargetWordId is { } target ? words[target] : "-")} "
            + $"{EnumSpelling.Of(review.Verdict)} examined {review.Examined} corrected {(review.CorrectedTargetWordId is { } named ? words[named] : "-")} "
            + $"applied {review.AppliedAt is not null} withheld {Blotted(review.Withheld)} link {(review.LinkId is { } link ? shapes[link] : "-")}"));

        var withdrawals = await _db.EvidentiaWithdrawals.AsNoTracking().Include(row => row.Review).ThenInclude(review => review!.Decision)
            .ThenInclude(decision => decision!.Run).ToListAsync();
        lines.AddRange(withdrawals.Select(row =>
            $"withdrawal {words[row.WordId]} for {row.Review!.Decision!.Run!.RuleVersion} {row.Review.Decision.Kind} "
            + $"{(row.Review.Decision.SourceWordId is { } source ? words[source] : "-")}: link {(row.LinkId is { } link ? shapes[link] : "-")} "
            + $"{EnumSpelling.Of(row.Relation)} {Named(row.FromWordIds)} > {Named(row.ToWordIds)} {EnumSpelling.Of(row.Method)} {row.Confidence} "
            + $"{Blotted(row.Source)} | {Blotted(row.Note)} claims [{string.Join("; ", row.ClaimSources.Select(Blotted))}] "
            + $"[{string.Join("; ", row.ClaimConfidences)}] [{string.Join("; ", row.ClaimNotes.Select(Blotted))}]"));

        return [.. lines.Order(StringComparer.Ordinal)];
    }

    private static string Blotted(string? text) => text is null ? "-" : Regex.Replace(text, "[0-9]+", "#");

    private long English(int position) => _db.WordAt(_english, 1, 1, position).Id;

    private long Hebrew(int position) => _db.WordAt(_hebrew, 1, 1, position).Id;

    private EvidentiaReviewQueue Queue() => new(_db);

    private EvidentiaLinkWriter Writer() => new(_db, new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance));

    private EvidentiaLedger Ledger() => new(_db, Writer());
}
