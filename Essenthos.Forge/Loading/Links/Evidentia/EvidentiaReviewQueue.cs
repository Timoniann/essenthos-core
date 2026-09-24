using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>Which pending proposals a listing or a tier acceptance reaches.</summary>
internal sealed record EvidentiaQueueFilter(
    string? Tier = null,
    int? Book = null,
    int? Chapter = null,
    int? Verse = null,
    int Take = EvidentiaReviewQueue.DefaultTake);

/// <summary>
/// The editor's side of a run: what is waiting, and the verdicts that settle it.
///
/// <para>
/// A proposal is waiting exactly when it has no review, so nothing has to be enqueued and nothing
/// can fall out of the queue. A verdict is recorded here and goes no further — writing it into the
/// corpus is <see cref="EvidentiaLinkWriter"/>'s, and only on an explicit command.
/// </para>
/// </summary>
internal sealed class EvidentiaReviewQueue(AppDbContext db)
{
    public const int DefaultTake = 50;

    public async Task<string> Pending(int runId, EvidentiaQueueFilter filter, CancellationToken cancellationToken = default)
    {
        await Run(runId, cancellationToken);
        var pending = Scoped(runId, filter).Where(decision => decision.Review == null);
        var total = await pending.CountAsync(cancellationToken);
        var rows = await pending
            .OrderBy(decision => decision.CanonicalBook)
            .ThenBy(decision => decision.CanonicalChapter)
            .ThenBy(decision => decision.CanonicalVerse)
            .ThenBy(decision => decision.SourceWordId == null)
            .ThenBy(decision => decision.SourceWord!.Position)
            .ThenBy(decision => decision.TargetWord!.Position)
            .Take(filter.Take)
            .Select(decision => new
            {
                decision,
                Source = decision.SourceWord == null ? null : decision.SourceWord.Surface,
                Target = decision.TargetWord == null ? null : decision.TargetWord.Surface,
                TargetStrong = decision.TargetWord == null ? null : decision.TargetWord.StrongNumber,
            })
            .ToListAsync(cancellationToken);
        var alternatives = await Surfaces(
            rows.SelectMany(row => row.decision.AlternativeWordIds ?? []), cancellationToken);

        var report = new StringBuilder(
            $"EVIDENTIA run {runId}: {total:N0} proposals waiting{Describe(filter)}; showing {rows.Count:N0}\n");
        foreach (var row in rows)
        {
            report.Append(
                $"#{row.decision.Id} {row.decision.CanonicalBook}:{row.decision.CanonicalChapter}:{row.decision.CanonicalVerse} ");
            if (row.decision.Absence is { } absence)
            {
                report.Append(absence == LinkRelation.Expands
                        ? $"'{row.Source}' supplied, nothing in the original "
                        : $"'{row.Target}' [{row.TargetStrong ?? "no-strong"}] not rendered by the translation ")
                    .Append($"({EnumSpelling.Of(absence)}) {row.decision.Tier} {row.decision.Kind} {row.decision.Confidence:F2}; ")
                    .Append(row.decision.Rationale)
                    .Append('\n');
                continue;
            }

            report.Append(
                    $"'{row.Source}' →'{row.Target}' [{row.TargetStrong ?? "no-strong"}] ")
                .Append(
                    $"{row.decision.Tier} {row.decision.Kind} {row.decision.Confidence:F2}; ")
                .Append(EvidentiaTrace.Signals(row.decision))
                .Append(EvidentiaTrace.Alternatives(row.decision, alternatives))
                .Append('\n');
        }

        return report.ToString();
    }

    /// <summary>A person read these proposals and says they are right.</summary>
    public Task<int> Approve(IReadOnlyCollection<long> decisionIds, string reviewer, string? note,
        CancellationToken cancellationToken = default) =>
        Settle(decisionIds, reviewer, note, EvidentiaVerdict.Approved, null, cancellationToken);

    public Task<int> Reject(IReadOnlyCollection<long> decisionIds, string reviewer, string? note,
        CancellationToken cancellationToken = default) =>
        Settle(decisionIds, reviewer, note, EvidentiaVerdict.Rejected, null, cancellationToken);

    /// <summary>
    /// The source word renders a different target word. It may correct an abstention as well as a
    /// proposal: a word the run placed nowhere is still a word a person can place, and so is a word
    /// the run said the translation supplies. A word of the original the run said is not rendered has
    /// no source word to place, so that decision is approved or rejected, never corrected.
    /// </summary>
    public async Task<int> Correct(long decisionId, long targetWordId, string reviewer, string? note,
        CancellationToken cancellationToken = default)
    {
        var decision = await db.EvidentiaDecisions.AsNoTracking()
            .Where(row => row.Id == decisionId)
            .Select(row => new { row.Run!.ToTextId, row.SourceWordId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"There is no EVIDENTIA decision {decisionId}. List them with evidentia-queue.");
        if (decision.SourceWordId is null)
        {
            throw new InvalidOperationException(
                $"Decision {decisionId} says a word of the original is not rendered, so there is no word of the translation to place. " +
                "Reject it with evidentia-reject and place the word that does render it through that word's own decision.");
        }

        var inTarget = await db.Words.AsNoTracking()
            .AnyAsync(word => word.Id == targetWordId && word.TextId == decision.ToTextId, cancellationToken);
        if (!inTarget)
        {
            throw new InvalidOperationException(
                $"Word {targetWordId} is not a word of the run's target text. A correction names a word of the text the run mapped onto.");
        }

        return await Settle([decisionId], reviewer, note, EvidentiaVerdict.Corrected, targetWordId, cancellationToken);
    }

    /// <summary>
    /// Every waiting proposal of one tier, accepted as a tier. Nobody read them one by one, and the
    /// review says so: what they become is the rule's own claim, not a person's.
    /// </summary>
    public async Task<int> AcceptTier(int runId, EvidentiaQueueFilter filter, string reviewer, string? note,
        CancellationToken cancellationToken = default)
    {
        await Run(runId, cancellationToken);
        if (filter.Tier is null)
        {
            throw new InvalidOperationException(
                "Accepting a tier needs --tier. Accepting every proposal of a run at once is not a tier decision; " +
                "name the tier whose rule you are accepting, usually --tier safe.");
        }

        Reviewer(reviewer);
        var now = DateTimeOffset.UtcNow;
        var ids = await Scoped(runId, filter with { Take = int.MaxValue })
            .Where(decision => decision.Review == null)
            .Select(decision => decision.Id)
            .ToListAsync(cancellationToken);
        db.EvidentiaReviews.AddRange(ids.Select(id => new EvidentiaReview
        {
            DecisionId = id,
            Verdict = EvidentiaVerdict.Approved,
            Examined = false,
            Reviewer = reviewer,
            ReviewedAt = now,
            Note = note,
        }));
        await db.SaveChangesAsync(cancellationToken);
        return ids.Count;
    }

    private async Task<int> Settle(
        IReadOnlyCollection<long> decisionIds,
        string reviewer,
        string? note,
        EvidentiaVerdict verdict,
        long? correctedTarget,
        CancellationToken cancellationToken)
    {
        Reviewer(reviewer);
        if (decisionIds.Count == 0)
        {
            throw new InvalidOperationException("Name at least one decision id; evidentia-queue lists them.");
        }

        var decisions = await db.EvidentiaDecisions
            .Include(decision => decision.Review)
            .Where(decision => decisionIds.Contains(decision.Id))
            .ToListAsync(cancellationToken);
        var missing = decisionIds.Except(decisions.Select(decision => decision.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"No EVIDENTIA decision has id {string.Join(", ", missing)}. List the waiting ones with evidentia-queue.");
        }

        foreach (var decision in decisions)
        {
            if (decision.TargetWordId is null && decision.Absence is null && verdict != EvidentiaVerdict.Corrected)
            {
                throw new InvalidOperationException(
                    $"Decision {decision.Id} placed its word nowhere, so there is nothing to {(verdict == EvidentiaVerdict.Approved ? "approve" : "reject")}. " +
                    "Place the word with evidentia-correct instead.");
            }

            if (decision.Review is { AppliedAt: not null } applied)
            {
                throw new InvalidOperationException(
                    $"Decision {decision.Id} was already written to link {applied.LinkId}. A verdict that reached the corpus " +
                    "is changed by correcting the link, not by reviewing the proposal again.");
            }

            var review = decision.Review ?? new EvidentiaReview { DecisionId = decision.Id, Reviewer = reviewer };
            review.Verdict = verdict;
            review.Examined = true;
            review.CorrectedTargetWordId = correctedTarget;
            review.Reviewer = reviewer;
            review.ReviewedAt = DateTimeOffset.UtcNow;
            review.Note = note;
            review.Withheld = null;
            if (decision.Review is null)
            {
                db.EvidentiaReviews.Add(review);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return decisions.Count;
    }

    private IQueryable<EvidentiaDecision> Scoped(int runId, EvidentiaQueueFilter filter) =>
        db.EvidentiaDecisions.AsNoTracking()
            .Where(decision => decision.RunId == runId && (decision.TargetWordId != null || decision.Absence != null))
            .Where(decision => filter.Tier == null || decision.Tier == filter.Tier)
            .Where(decision => filter.Book == null || decision.CanonicalBook == filter.Book)
            .Where(decision => filter.Chapter == null || decision.CanonicalChapter == filter.Chapter)
            .Where(decision => filter.Verse == null || decision.CanonicalVerse == filter.Verse);

    private async Task Run(int runId, CancellationToken cancellationToken)
    {
        var finished = await db.EvidentiaRuns.AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => (bool?)(run.FinishedAt != null))
            .SingleOrDefaultAsync(cancellationToken);
        if (finished is null)
        {
            throw new InvalidOperationException($"There is no EVIDENTIA run {runId}. List the stored runs with evidentia-runs.");
        }

        if (finished == false)
        {
            throw new InvalidOperationException(
                $"EVIDENTIA run {runId} never finished, so its decisions are partial. Run the scope again rather than reviewing it.");
        }
    }

    private async Task<IReadOnlyDictionary<long, string>> Surfaces(IEnumerable<long> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        return await db.Words.AsNoTracking()
            .Where(word => wanted.Contains(word.Id))
            .ToDictionaryAsync(word => word.Id, word => word.Surface, cancellationToken);
    }

    private static void Reviewer(string reviewer)
    {
        if (string.IsNullOrWhiteSpace(reviewer))
        {
            throw new InvalidOperationException(
                "A verdict names who gave it. Pass --reviewer with a name; it is what the link's source will say.");
        }
    }

    private static string Describe(EvidentiaQueueFilter filter) =>
        (filter.Tier is null ? string.Empty : $" in the {filter.Tier} tier")
        + (filter.Book is null ? string.Empty : $" at {filter.Book}")
        + (filter.Chapter is null ? string.Empty : $":{filter.Chapter}")
        + (filter.Verse is null ? string.Empty : $":{filter.Verse}");
}

/// <summary>How a stored decision is shown to a person reading it.</summary>
internal static class EvidentiaTrace
{
    public static string Signals(EvidentiaDecision decision)
    {
        var signals = new (string Name, float? Score)[]
        {
            ("exact-address", decision.ExactAddress),
            ("neighbouring-address", decision.NeighbouringAddress),
            ("matching-form", decision.MatchingForm),
            ("shared-strong", decision.SharedStrong),
            ("dictionary-sense", decision.DictionarySense),
            ("target-gloss", decision.TargetGloss),
            ("known-rendering", decision.KnownRendering),
            ("morphology", decision.Morphology),
            ("syntax", decision.Syntax),
            ("statistical-aligner", decision.StatisticalAligner),
        };
        var present = signals.Where(signal => signal.Score is not null)
            .OrderByDescending(signal => signal.Score)
            .Select(signal => ($"{signal.Name} {signal.Score:F2}"));
        var support = decision.RenderingObservations is { } seen
            ? (
                $" (seen {seen}×, share {decision.RenderingShare:F2}, next {decision.RenderingNextShare:F2}, by {decision.RenderingForm ?? "unknown form"})")
            : string.Empty;
        return ($"score {decision.Score:F2}")
               + (decision.Margin is { } margin ? ($", margin {margin:F2}") : string.Empty)
               + $"; {string.Join(", ", present)}{support}";
    }

    public static string Alternatives(EvidentiaDecision decision, IReadOnlyDictionary<long, string> surfaces)
    {
        if (decision.AlternativeWordIds is not { Length: > 0 } ids)
        {
            return "; no other candidate";
        }

        return "; over " + string.Join(", ", ids.Select((id, index) =>
            (
                $"'{surfaces.GetValueOrDefault(id, $"#{id}")}' {decision.AlternativeScores![index]:F2}")
            + (decision.AlternativeDistances![index] == 0
                ? string.Empty
                : decision.AlternativeDistances[index] == short.MaxValue
                    ? " in another chapter"
                    : $" {decision.AlternativeDistances[index]} verse(s) away")
            + (decision.AlternativeTaken![index] ? " (taken)" : string.Empty)));
    }
}
