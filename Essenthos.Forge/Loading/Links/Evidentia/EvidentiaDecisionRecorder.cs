using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>Receives what a measurement decided, chapter by chapter, as it decides it.</summary>
internal interface IEvidentiaDecisionSink
{
    void Record(EvidentiaChapterDecisions chapter);
}

/// <param name="ContentSourceWordIds">The words coverage is counted over.</param>
/// <param name="Safe">The safe tier's proposals, which mark a final proposal as confirmed.</param>
/// <param name="Final">The proposals the measurement scores as its final tier.</param>
internal sealed record EvidentiaChapterDecisions(
    int CanonicalBook,
    int CanonicalChapter,
    IReadOnlyList<EvidentiaToken> Source,
    IReadOnlySet<long> ContentSourceWordIds,
    IReadOnlyList<EvidentiaCandidate> Candidates,
    IReadOnlyList<EvidentiaProposal> Safe,
    IReadOnlyList<EvidentiaProposal> Final);

/// <summary>
/// Turns a chapter's candidates and proposals into one decision per word, for storage.
///
/// <para>
/// **It reads what the resolvers chose and never chooses anything itself.** The alternatives and
/// the reason for an abstention are read off the candidate graph beside the choice: which edges the
/// word had, how they scored, and whether their target went to another word. That is everything a
/// reviewer needs to see why a word landed where it did, and none of it can change where it lands.
/// </para>
///
/// <para>
/// A word is recorded if it is a content word or if something was proposed for it. Function words
/// nothing proposed are left out: EVIDENTIA does not attempt them, and a row saying so for every
/// <em>the</em> in the Bible would be most of the table.
/// </para>
/// </summary>
/// <param name="verses">Only these verses are kept, when a run repeats part of an earlier one.</param>
internal sealed class EvidentiaDecisionRecorder(int runId, IReadOnlySet<EvidentiaAddress>? verses = null)
    : IEvidentiaDecisionSink
{
    public const string SafeTier = "safe";

    private const string ReviewTier = "review";

    public List<EvidentiaDecision> Decisions { get; } = [];

    /// <summary>Which evidence source stood behind each signal kind, over the whole run.</summary>
    public SortedDictionary<string, SortedSet<string>> EvidenceSources { get; } = new(StringComparer.Ordinal);

    public void Record(EvidentiaChapterDecisions chapter)
    {
        foreach (var candidate in chapter.Candidates)
        {
            foreach (var evidence in candidate.Evidence)
            {
                var kind = Spelling(evidence.Kind);
                if (!EvidenceSources.TryGetValue(kind, out var sources))
                {
                    EvidenceSources[kind] = sources = new SortedSet<string>(StringComparer.Ordinal);
                }

                sources.Add(SourceName(evidence.Source));
            }
        }

        Decisions.AddRange(Decide(runId, chapter, verses));
    }

    /// <summary>
    /// An evidence source names itself before its first semicolon and describes the edge after it —
    /// the key it was found under, the counts behind its share. The description is what the
    /// decision's columns already hold, and kept per source it made one run's configuration a
    /// megabyte of the same few names.
    /// </summary>
    private static string SourceName(string source) =>
        source.IndexOf(';') is var end and >= 0 ? source[..end] : source;

    public static IEnumerable<EvidentiaDecision> Decide(
        int runId,
        EvidentiaChapterDecisions chapter,
        IReadOnlySet<EvidentiaAddress>? verses = null)
    {
        var final = chapter.Final
            .GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.MaxBy(proposal => proposal.Confidence)!);
        var safe = chapter.Safe
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .ToHashSet();
        var taken = final.Values.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var candidatesBySource = chapter.Candidates
            .GroupBy(candidate => candidate.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Source.Token.Address.DistanceTo(candidate.Target.Token.Address))
                .ThenBy(candidate => candidate.Target.Token.Position)
                .ToList());

        foreach (var word in chapter.Source.DistinctBy(token => token.Id).OrderBy(token => token.Address.Verse)
                     .ThenBy(token => token.Position))
        {
            var content = chapter.ContentSourceWordIds.Contains(word.Id);
            var proposed = final.GetValueOrDefault(word.Id);
            if ((!content && proposed is null) || (verses is not null && !verses.Contains(word.Address)))
            {
                continue;
            }

            var candidates = candidatesBySource.GetValueOrDefault(word.Id) ?? [];
            yield return proposed is null
                ? Abstained(runId, word, content, candidates, taken)
                : Proposed(runId, word, content, proposed, candidates, taken, safe);
        }
    }

    private static EvidentiaDecision Proposed(
        int runId,
        EvidentiaToken word,
        bool content,
        EvidentiaProposal proposal,
        IReadOnlyList<EvidentiaCandidate> candidates,
        IReadOnlySet<long> taken,
        IReadOnlySet<(long, long)> safe)
    {
        var target = proposal.Target.Token.Id;
        var chosen = candidates.FirstOrDefault(candidate => candidate.Target.Token.Id == target);
        var alternatives = candidates.Where(candidate => candidate.Target.Token.Id != target)
            .Take(EvidentiaDecision.MaximumAlternatives)
            .ToList();
        var decision = New(runId, word, content, candidates.Count);
        decision.TargetWordId = target;
        decision.Kind = Spelling(proposal.Kind);
        decision.Tier = safe.Contains((word.Id, target)) ? SafeTier : proposal.Trace?.Tier ?? ReviewTier;
        decision.Rationale = proposal.Trace?.Rationale;
        decision.Confidence = (float)proposal.Confidence;
        decision.Score = chosen is null ? null : (float)chosen.Score;
        decision.Margin = chosen is null || alternatives.Count == 0
            ? null
            : (float)(Unclamped(chosen) - candidates
                .Where(candidate => candidate.Target.Token.Id != target)
                .Max(Unclamped));
        Signals(decision, proposal.Trace?.Evidence ?? chosen?.Evidence ?? []);
        Alternatives(decision, word, alternatives, taken);
        return decision;
    }

    /// <summary>
    /// A word placed nowhere. Its signals and score are its best candidate's, so the row still says
    /// what evidence the policy declined.
    /// </summary>
    private static EvidentiaDecision Abstained(
        int runId,
        EvidentiaToken word,
        bool content,
        IReadOnlyList<EvidentiaCandidate> candidates,
        IReadOnlySet<long> taken)
    {
        var decision = New(runId, word, content, candidates.Count);
        var best = candidates.FirstOrDefault();
        decision.Abstention = best is null
            ? EvidentiaAbstention.NoCandidate
            : taken.Contains(best.Target.Token.Id)
                ? EvidentiaAbstention.TargetTaken
                : EvidentiaAbstention.Declined;
        if (best is not null)
        {
            decision.Score = (float)best.Score;
            Signals(decision, best.Evidence);
        }

        Alternatives(decision, word, [.. candidates.Take(EvidentiaDecision.MaximumAlternatives)], taken);
        return decision;
    }

    /// <summary>
    /// The evidence summed without the ceiling a candidate's score carries. Two strong edges both
    /// read 1.00 once clamped, and a margin between them would say nothing.
    /// </summary>
    private static double Unclamped(EvidentiaCandidate candidate) => candidate.Evidence.Sum(evidence => evidence.Score);

    private static EvidentiaDecision New(int runId, EvidentiaToken word, bool content, int candidates) => new()
    {
        RunId = runId,
        SourceWordId = word.Id,
        CanonicalBook = (short)word.Address.Book,
        CanonicalChapter = (short)word.Address.Chapter,
        CanonicalVerse = (short)word.Address.Verse,
        Content = content,
        Candidates = (short)Math.Min(candidates, short.MaxValue),
    };

    private static void Signals(EvidentiaDecision decision, IEnumerable<EvidentiaEvidence> evidence)
    {
        foreach (var signal in evidence)
        {
            var score = (float)signal.Score;
            switch (signal.Kind)
            {
                case EvidentiaEvidenceKind.ExactCanonicalAddress:
                    decision.ExactAddress = (decision.ExactAddress ?? 0) + score;
                    break;
                case EvidentiaEvidenceKind.NeighbouringCanonicalAddress:
                    decision.NeighbouringAddress = (decision.NeighbouringAddress ?? 0) + score;
                    break;
                case EvidentiaEvidenceKind.MatchingNormalisedForm:
                    decision.MatchingForm = (decision.MatchingForm ?? 0) + score;
                    break;
                case EvidentiaEvidenceKind.SharedStrongNumber:
                    decision.SharedStrong = (decision.SharedStrong ?? 0) + score;
                    break;
                case EvidentiaEvidenceKind.DictionarySense:
                    decision.DictionarySense = (decision.DictionarySense ?? 0) + score;
                    break;
                case EvidentiaEvidenceKind.TargetGloss:
                    decision.TargetGloss = (decision.TargetGloss ?? 0) + score;
                    break;
                case EvidentiaEvidenceKind.KnownRendering:
                    decision.KnownRendering = (decision.KnownRendering ?? 0) + score;
                    if (signal.Support is { } support && decision.RenderingObservations is null)
                    {
                        decision.RenderingObservations = support.Observations;
                        decision.RenderingShare = (float)support.Share;
                        decision.RenderingNextShare = (float)support.NextShare;
                        decision.RenderingForm = support.MatchedForm is { } form ? Spelling(form) : null;
                    }

                    break;
                case EvidentiaEvidenceKind.Morphology:
                    decision.Morphology = (decision.Morphology ?? 0) + score;
                    break;
                case EvidentiaEvidenceKind.Syntax:
                    decision.Syntax = (decision.Syntax ?? 0) + score;
                    break;
                case EvidentiaEvidenceKind.StatisticalAligner:
                    decision.StatisticalAligner = (decision.StatisticalAligner ?? 0) + score;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(evidence), signal.Kind,
                        $"{signal.Kind} has no column on evidentia_decision. Add one, and its migration, before a " +
                        "source produces it.");
            }
        }
    }

    private static void Alternatives(
        EvidentiaDecision decision,
        EvidentiaToken word,
        IReadOnlyList<EvidentiaCandidate> alternatives,
        IReadOnlySet<long> taken)
    {
        if (alternatives.Count == 0)
        {
            return;
        }

        decision.AlternativeWordIds = [.. alternatives.Select(candidate => candidate.Target.Token.Id)];
        decision.AlternativeScores = [.. alternatives.Select(candidate => (float)candidate.Score)];
        decision.AlternativeDistances =
        [
            .. alternatives.Select(candidate =>
                (short)Math.Min(word.Address.DistanceTo(candidate.Target.Token.Address), short.MaxValue)),
        ];
        decision.AlternativeTaken = [.. alternatives.Select(candidate => taken.Contains(candidate.Target.Token.Id))];
    }

    public static string Spelling(EvidentiaProposalKind kind) => kind switch
    {
        EvidentiaProposalKind.UniqueSharedStrong => "unique-shared-strong",
        EvidentiaProposalKind.SharedStrongInOrder => "shared-strong-in-order",
        EvidentiaProposalKind.StableKnownRendering => "stable-known-rendering",
        EvidentiaProposalKind.ReviewKnownRendering => "review-known-rendering",
        EvidentiaProposalKind.GlobalStableKnownRendering => "global-stable-known-rendering",
        EvidentiaProposalKind.GlobalReviewKnownRendering => "global-review-known-rendering",
        EvidentiaProposalKind.UniqueTargetGlossReview => "unique-target-gloss-review",
        EvidentiaProposalKind.UniqueDictionarySenseReview => "unique-dictionary-sense-review",
        EvidentiaProposalKind.GlobalAssignmentReview => "global-assignment-review",
        EvidentiaProposalKind.AttachedWord => "attached-word",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
            $"{kind} has no stored spelling. Add it to EvidentiaDecisionRecorder.Spelling before a resolver emits it."),
    };

    private static string Spelling(EvidentiaEvidenceKind kind) => kind switch
    {
        EvidentiaEvidenceKind.ExactCanonicalAddress => "exact-address",
        EvidentiaEvidenceKind.NeighbouringCanonicalAddress => "neighbouring-address",
        EvidentiaEvidenceKind.MatchingNormalisedForm => "matching-form",
        EvidentiaEvidenceKind.SharedStrongNumber => "shared-strong",
        EvidentiaEvidenceKind.DictionarySense => "dictionary-sense",
        EvidentiaEvidenceKind.TargetGloss => "target-gloss",
        EvidentiaEvidenceKind.KnownRendering => "known-rendering",
        EvidentiaEvidenceKind.Morphology => "morphology",
        EvidentiaEvidenceKind.Syntax => "syntax",
        EvidentiaEvidenceKind.StatisticalAligner => "statistical-aligner",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"{kind} has no stored spelling."),
    };

    private static string Spelling(EvidentiaFormKind form) => form switch
    {
        EvidentiaFormKind.Surface => "surface",
        EvidentiaFormKind.Lemma => "lemma",
        EvidentiaFormKind.Normalised => "normalised",
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, $"{form} has no stored spelling."),
    };
}
