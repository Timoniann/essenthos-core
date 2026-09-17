using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>What one stored decision contributes to its verse's standing.</summary>
internal readonly record struct EvidentiaVerseWord(
    int RunId,
    EvidentiaAddress Address,
    bool Content,
    bool Proposed,
    bool Safe,
    EvidentiaAbstention? Abstention,
    bool PulledElsewhere,
    EvidentiaVerdict? Verdict);

/// <param name="Unplaced">Content words with no proposal, or whose proposal a reviewer rejected.</param>
/// <param name="ReviewTier">Content words placed only by a review-tier proposal nobody has approved.</param>
/// <param name="Confirmed">Content words placed by the safe tier or by a reviewer, and not rejected.</param>
/// <param name="PulledElsewhere">Words whose strongest candidate stands in another verse.</param>
/// <param name="Badness">
/// The share of content words the verse failed to place, with a review-tier placement counted as
/// half placed. Zero is a verse every content word of which the safe tier placed.
/// </param>
/// <param name="MayNotCorrespond">
/// Few confirmed words, and most of the rest finding their best evidence in another verse: either
/// the verse's parallel is not the verse the frame paired it with, or the witness verse lacks what
/// the evidence needs. Both are for a person to look at first, which is why it is its own flag.
/// </param>
internal sealed record EvidentiaProblemVerse(
    int RunId,
    EvidentiaAddress Address,
    int ContentWords,
    int Proposed,
    int Unplaced,
    int NoCandidate,
    int TargetTaken,
    int Declined,
    int ReviewTier,
    int Confirmed,
    int PulledElsewhere,
    double Badness,
    bool MayNotCorrespond)
{
    public string Summary() => (
        $"run {RunId} {Address.Book}:{Address.Chapter}:{Address.Verse}  badness {Badness:F2}"
        + (MayNotCorrespond ? "  [FEW CONFIRMED — may not correspond to its parallel]" : string.Empty)
        + $"\n  content words {ContentWords}: confirmed {Confirmed}, review tier only {ReviewTier}, unplaced {Unplaced} "
        + $"(no candidate {NoCandidate}, target taken {TargetTaken}, declined {Declined}); "
        + $"strongest candidate in another verse for {PulledElsewhere}");
}

/// <summary>
/// The owner's loop, from a stored run: which verses mapped worst, what was decided in them, and
/// whether a fix made them better.
/// </summary>
internal sealed class EvidentiaProblemVerses(AppDbContext db, EvidentiaRunner runner)
{
    public const int DefaultMinimumContentWords = 3;

    public const int DefaultTake = 20;

    /// <summary>What a review-tier placement is worth against a confirmed one.</summary>
    public const double ReviewTierWeight = 0.5;

    /// <summary>A verse shorter than this is not judged for correspondence; one missed word is a third of it.</summary>
    public const int CorrespondenceMinimumContentWords = 4;

    /// <summary>At or below this share of confirmed content words a verse may not correspond to its parallel.</summary>
    public const double FewConfirmedShare = 0.2;

    /// <summary>
    /// And at or above this share of its content words finding their strongest candidate in another
    /// verse. Few confirmed words alone flagged one verse in seven of the benchmark sample, which is
    /// a list of weak verses; evidence pointing elsewhere is what separates a verse that may belong
    /// to a different parallel from one the rules simply read badly.
    /// </summary>
    public const double PulledElsewhereShare = 0.5;

    public static IReadOnlyList<EvidentiaProblemVerse> Rank(
        IEnumerable<EvidentiaVerseWord> words,
        int minimumContentWords = DefaultMinimumContentWords)
    {
        return words
            .GroupBy(word => (word.RunId, word.Address))
            .Select(verse =>
            {
                var content = verse.Where(word => word.Content).ToList();
                var rejected = content.Count(word => word.Proposed && word.Verdict == EvidentiaVerdict.Rejected);
                var corrected = content.Count(word => !word.Proposed && word.Verdict == EvidentiaVerdict.Corrected);
                var confirmed = content.Count(word =>
                    word.Verdict is EvidentiaVerdict.Approved or EvidentiaVerdict.Corrected
                    || (word.Safe && word.Verdict is null));
                var reviewTier = content.Count(word => word.Proposed && !word.Safe && word.Verdict is null);
                var unplaced = content.Count(word => !word.Proposed) - corrected + rejected;
                var badness = content.Count == 0 ? 0 : (unplaced + ReviewTierWeight * reviewTier) / content.Count;
                return new EvidentiaProblemVerse(
                    verse.Key.RunId,
                    verse.Key.Address,
                    content.Count,
                    content.Count(word => word.Proposed),
                    unplaced,
                    content.Count(word => word.Abstention == EvidentiaAbstention.NoCandidate),
                    content.Count(word => word.Abstention == EvidentiaAbstention.TargetTaken),
                    content.Count(word => word.Abstention == EvidentiaAbstention.Declined),
                    reviewTier,
                    confirmed,
                    verse.Count(word => word.PulledElsewhere),
                    badness,
                    content.Count >= CorrespondenceMinimumContentWords
                    && (double)confirmed / content.Count <= FewConfirmedShare
                    && (double)content.Count(word => word.PulledElsewhere) / content.Count >= PulledElsewhereShare);
            })
            .Where(verse => verse.ContentWords >= minimumContentWords)
            .OrderByDescending(verse => verse.MayNotCorrespond)
            .ThenByDescending(verse => verse.Badness)
            .ThenByDescending(verse => verse.ContentWords)
            .ThenBy(verse => verse.RunId)
            .ThenBy(verse => (verse.Address.Book, verse.Address.Chapter, verse.Address.Verse))
            .ToList();
    }

    public async Task<IReadOnlyList<EvidentiaProblemVerse>> Ranked(
        IReadOnlyCollection<int> runIds,
        int minimumContentWords = DefaultMinimumContentWords,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.EvidentiaDecisions.AsNoTracking()
            .Where(decision => runIds.Contains(decision.RunId))
            .Select(decision => new
            {
                decision.RunId,
                decision.CanonicalBook,
                decision.CanonicalChapter,
                decision.CanonicalVerse,
                decision.Content,
                Proposed = decision.TargetWordId != null,
                Safe = decision.Tier == EvidentiaDecisionRecorder.SafeTier,
                decision.Abstention,
                Nearest = decision.AlternativeDistances == null ? (short?)null : decision.AlternativeDistances[0],
                Verdict = decision.Review == null ? (EvidentiaVerdict?)null : decision.Review.Verdict,
            })
            .ToListAsync(cancellationToken);
        return Rank(rows.Select(row => new EvidentiaVerseWord(
                row.RunId,
                new EvidentiaAddress(row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse),
                row.Content,
                row.Proposed,
                row.Safe,
                row.Abstention,
                !row.Proposed && row.Nearest is > 0,
                row.Verdict)),
            minimumContentWords);
    }

    /// <summary>The worst verses of the runs, each with every decision in it, for a person to work through.</summary>
    public async Task<string> Worst(
        IReadOnlyCollection<int> runIds,
        int take,
        int minimumContentWords = DefaultMinimumContentWords,
        bool flaggedOnly = false,
        CancellationToken cancellationToken = default)
    {
        var ranked = await Ranked(runIds, minimumContentWords, cancellationToken);
        var worst = ranked.Where(verse => !flaggedOnly || verse.MayNotCorrespond).Take(take).ToList();
        var report = new StringBuilder((
            $"EVIDENTIA problem verses, runs {string.Join(", ", runIds)}: {ranked.Count:N0} verses with at least " +
            $"{minimumContentWords} content words, {ranked.Count(verse => verse.MayNotCorrespond):N0} flagged; worst {worst.Count}\n"));
        var index = 0;
        foreach (var verse in worst)
        {
            report.Append('\n').Append(++index).Append(". ").Append(verse.Summary()).Append('\n');
            report.Append(await Evidence(verse.RunId, verse.Address, cancellationToken));
        }

        return report.ToString();
    }

    /// <summary>Repeats the worst verses of a run as a new run, and compares the two.</summary>
    public async Task<string> Rerun(int runId, int take, int minimumContentWords, CancellationToken cancellationToken = default)
    {
        var worst = (await Ranked([runId], minimumContentWords, cancellationToken)).Take(take)
            .Select(verse => verse.Address)
            .ToList();
        var outcome = await runner.Rerun(runId, worst, cancellationToken);
        return $"{outcome}\n\n{await Compare(runId, outcome.RunId, cancellationToken)}";
    }

    /// <summary>
    /// A later run against an earlier one, over the verses the later run covers: each verse's
    /// standing before and after, and every word whose decision changed.
    /// </summary>
    public async Task<string> Compare(int beforeRunId, int afterRunId, CancellationToken cancellationToken = default)
    {
        var after = (await Ranked([afterRunId], 0, cancellationToken))
            .ToDictionary(verse => verse.Address);
        var before = (await Ranked([beforeRunId], 0, cancellationToken))
            .Where(verse => after.ContainsKey(verse.Address))
            .ToDictionary(verse => verse.Address);
        var decisions = await db.EvidentiaDecisions.AsNoTracking()
            .Where(decision => decision.RunId == beforeRunId || decision.RunId == afterRunId)
            .Select(decision => new
            {
                decision.RunId,
                decision.SourceWordId,
                Source = decision.SourceWord!.Surface,
                decision.CanonicalBook,
                decision.CanonicalChapter,
                decision.CanonicalVerse,
                decision.TargetWordId,
                Target = decision.TargetWord == null ? null : decision.TargetWord.Surface,
                decision.Kind,
                decision.Tier,
                decision.Abstention,
            })
            .ToListAsync(cancellationToken);
        var covered = after.Keys.ToHashSet();
        var changed = decisions
            .Where(decision => covered.Contains(new EvidentiaAddress(
                decision.CanonicalBook, decision.CanonicalChapter, decision.CanonicalVerse)))
            .GroupBy(decision => decision.SourceWordId)
            .Select(group => (
                Before: group.FirstOrDefault(decision => decision.RunId == beforeRunId),
                After: group.FirstOrDefault(decision => decision.RunId == afterRunId)))
            .Where(pair => pair.Before?.TargetWordId != pair.After?.TargetWordId || pair.Before?.Tier != pair.After?.Tier)
            .ToList();

        var report = new StringBuilder((
            $"EVIDENTIA run {afterRunId} against run {beforeRunId}, over the {after.Count} verses run {afterRunId} covers\n"));
        double beforeTotal = 0, afterTotal = 0;
        foreach (var (address, now) in after.OrderBy(pair => (pair.Key.Book, pair.Key.Chapter, pair.Key.Verse)))
        {
            var then = before.GetValueOrDefault(address);
            beforeTotal += then?.Badness ?? 0;
            afterTotal += now.Badness;
            report.Append(
                $"{address.Book}:{address.Chapter}:{address.Verse}  badness {then?.Badness:F2} → {now.Badness:F2}; " +
                $"confirmed {then?.Confirmed} → {now.Confirmed}, review tier {then?.ReviewTier} → {now.ReviewTier}, " +
                $"unplaced {then?.Unplaced} → {now.Unplaced}" +
                (then?.MayNotCorrespond == now.MayNotCorrespond ? string.Empty : $"; flag {then?.MayNotCorrespond} → {now.MayNotCorrespond}") +
                "\n");
        }

        report.Append(
            $"mean badness {(after.Count == 0 ? 0 : beforeTotal / after.Count):F3} → {(after.Count == 0 ? 0 : afterTotal / after.Count):F3}; " +
            $"{changed.Count} word decisions changed\n");
        foreach (var (then, now) in changed)
        {
            var word = now ?? then!;
            report.Append(
                $"  {word.CanonicalBook}:{word.CanonicalChapter}:{word.CanonicalVerse} '{word.Source}': " +
                $"{Described(then?.Target, then?.Kind, then?.Tier, then?.Abstention)} → {Described(now?.Target, now?.Kind, now?.Tier, now?.Abstention)}\n");
        }

        return report.ToString();
    }

    private static string Described(string? target, string? kind, string? tier, EvidentiaAbstention? abstention) =>
        target is not null
            ? $"'{target}' ({tier} {kind})"
            : abstention is { } reason ? $"nothing ({EnumSpelling.Of(reason)})" : "not recorded";

    private async Task<string> Evidence(int runId, EvidentiaAddress address, CancellationToken cancellationToken)
    {
        var decisions = await db.EvidentiaDecisions.AsNoTracking()
            .Where(decision => decision.RunId == runId
                && decision.CanonicalBook == address.Book
                && decision.CanonicalChapter == address.Chapter
                && decision.CanonicalVerse == address.Verse)
            .Select(decision => new
            {
                decision,
                Verdict = decision.Review == null ? (EvidentiaVerdict?)null : decision.Review.Verdict,
                Reviewer = decision.Review == null ? null : decision.Review.Reviewer,
                decision.SourceWord!.VerseId,
                decision.SourceWord!.Position,
                Source = decision.SourceWord!.Surface,
                Target = decision.TargetWord == null ? null : decision.TargetWord.Surface,
                TargetStrong = decision.TargetWord == null ? null : decision.TargetWord.StrongNumber,
            })
            .OrderBy(row => row.Position)
            .ToListAsync(cancellationToken);
        if (decisions.Count == 0)
        {
            return string.Empty;
        }

        var verseIds = decisions.Select(row => row.VerseId).Distinct().ToList();
        var verseText = await db.Words.AsNoTracking()
            .Where(word => verseIds.Contains(word.VerseId))
            .OrderBy(word => word.VerseId)
            .ThenBy(word => word.Position)
            .Select(word => new { word.Id, word.Surface, word.Trailer })
            .ToListAsync(cancellationToken);
        var placed = decisions.Where(row => row.decision.TargetWordId is not null)
            .Select(row => row.decision.SourceWordId)
            .ToHashSet();
        var unplaced = decisions.Where(row => row.decision.Content && row.decision.TargetWordId is null)
            .Select(row => row.decision.SourceWordId)
            .ToHashSet();
        var alternativeIds = decisions.SelectMany(row => row.decision.AlternativeWordIds ?? []).Distinct().ToList();
        var alternatives = await db.Words.AsNoTracking()
            .Where(word => alternativeIds.Contains(word.Id))
            .ToDictionaryAsync(word => word.Id, word => word.Surface, cancellationToken);

        var report = new StringBuilder("  ");
        foreach (var word in verseText)
        {
            report.Append(placed.Contains(word.Id) ? $"[{word.Surface}]" : unplaced.Contains(word.Id) ? $"_{word.Surface}_" : word.Surface)
                .Append(word.Trailer);
        }

        report.Append("\n  ([placed], _unplaced content word_)\n");
        foreach (var row in decisions)
        {
            var decision = row.decision;
            report.Append($"  #{decision.Id} '{row.Source}' ");
            report.Append(decision.TargetWordId is null
                ? $"→ nothing ({EnumSpelling.Of(decision.Abstention!.Value)}, {decision.Candidates} candidates)"
                : (
                    $"→ '{row.Target}' [{row.TargetStrong ?? "no-strong"}] {decision.Tier} {decision.Kind} {decision.Confidence:F2}"));
            if (row.Verdict is { } verdict)
            {
                report.Append($" <{EnumSpelling.Of(verdict)} by {row.Reviewer}>");
            }

            if (decision.Candidates > 0)
            {
                report.Append("; ").Append(EvidentiaTrace.Signals(decision)).Append(EvidentiaTrace.Alternatives(decision, alternatives));
            }

            report.Append('\n');
        }

        return report.ToString();
    }
}
