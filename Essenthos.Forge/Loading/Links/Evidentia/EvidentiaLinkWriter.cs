using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <param name="Written">Whether anything reached the database; false for a plan.</param>
/// <param name="Verdicts">Approved and corrected reviews not yet written.</param>
/// <param name="NewLinks">Links the verdicts created.</param>
/// <param name="OnExisting">Verdicts that became claims on a link naming exactly their words.</param>
/// <param name="Within">Verdicts that became claims on a link naming more words than theirs.</param>
/// <param name="Promoted">Existing links whose settled answer a verdict outranked.</param>
/// <param name="Withheld">Verdicts not written because the corpus places the word elsewhere with more standing.</param>
internal sealed record EvidentiaApplyOutcome(
    bool Written,
    int Verdicts,
    int NewLinks,
    int OnExisting,
    int Within,
    int Promoted,
    int Withheld,
    IReadOnlyList<string> Lines,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        (Written ? "EVIDENTIA verdicts written" : "EVIDENTIA verdicts NOT written (plan only; pass --write to write)")
        + $": {Verdicts:N0} approved or corrected; {NewLinks:N0} new links, {OnExisting:N0} claims on a link naming the same words, "
        + $"{Within:N0} on a link naming more, {Promoted:N0} links whose settled answer changed, {Withheld:N0} withheld; in {Elapsed}"
        + (Lines.Count == 0 ? string.Empty : "\n" + string.Join("\n", Lines));
}

/// <summary>
/// The one path from an EVIDENTIA run into the corpus: approved and corrected verdicts, written as
/// links and claims, settled by <see cref="ClaimStanding"/>.
///
/// <para>
/// **Only a verdict is written, never a proposal.** A decision with no review, or a rejected one,
/// has no path to a link. Each verdict becomes the claims it justifies: a proposal a person read
/// and approved is a <see cref="LinkMethod.Manual"/> claim with the rule's claim beside it; a tier
/// accepted unread is the rule's claim alone, with its confidence; a correction is a person's claim
/// about a pair the rule did not propose, so the rule has nothing to say about it.
/// </para>
///
/// <para>
/// **The corpus's existing links are settled against, not overwritten.** A link naming exactly the
/// same two words gains the claims, and its own answer changes only where it is an inference a new
/// claim outranks: a person agreeing with what a source states is kept as a claim beside the
/// statement, and the link goes on showing the source, which is the more informative of the two.
/// A link naming more words gains them with a note saying so and keeps its answer, because the
/// verdict speaks to one pair of its words and not to the link. And where a link that outranks the
/// verdict already places the source word on other words, a rule's claim is withheld: a heuristic
/// may not add a second rendering beside one a source states. A person's is not withheld — a person
/// reviewing the word decided knowing what the corpus says. Nothing here deletes a link.
/// </para>
/// </summary>
internal sealed class EvidentiaLinkWriter(AppDbContext db, VerseLinkLoader verseLinks)
{
    private const int Batch = 2_000;

    /// <summary>A decision's confidence is stored as a real; four digits is all of it that is not rounding noise.</summary>
    private const int ConfidenceDigits = 4;

    private const string WithinNote = "names one word pair of this link";

    public async Task<EvidentiaApplyOutcome> Apply(int runId, bool write, CancellationToken cancellationToken = default)
    {
        var elapsed = Stopwatch.StartNew();
        var run = await db.EvidentiaRuns.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"There is no EVIDENTIA run {runId}. List the stored runs with evidentia-runs.");

        var verdictIds = await db.EvidentiaReviews.AsNoTracking()
            .Where(review => review.Decision!.RunId == runId && review.AppliedAt == null)
            .Where(review => review.Verdict == EvidentiaVerdict.Approved || review.Verdict == EvidentiaVerdict.Corrected)
            .OrderBy(review => review.Id)
            .Select(review => review.Id)
            .ToListAsync(cancellationToken);

        var tally = new Tally();
        await using var transaction = write ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        foreach (var chunk in verdictIds.Chunk(Batch))
        {
            await Settle(run, chunk, write, tally, cancellationToken);
            db.ChangeTracker.Clear();
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            if (tally.NewLinks > 0)
            {
                tally.Lines.Add((await verseLinks.Load(cancellationToken)).ToString());
            }
        }

        return new EvidentiaApplyOutcome(write, verdictIds.Count, tally.NewLinks, tally.OnExisting, tally.Within,
            tally.Promoted, tally.Withheld, tally.Lines, elapsed.Elapsed);
    }

    private async Task Settle(EvidentiaRun run, long[] reviewIds, bool write, Tally tally, CancellationToken cancellationToken)
    {
        var reviews = await db.EvidentiaReviews
            .Include(review => review.Decision)
            .Where(review => reviewIds.Contains(review.Id))
            .OrderBy(review => review.Id)
            .ToListAsync(cancellationToken);
        var sourceWords = reviews.Select(review => review.Decision!.SourceWordId).Distinct().ToList();
        var touching = await db.Links
            .Include(link => link.Words)
            .Include(link => link.Claims)
            .AsSplitQuery()
            .Where(link => link.Words.Any(word => sourceWords.Contains(word.WordId)))
            .Where(link => (link.FromTextId == run.FromTextId && link.ToTextId == run.ToTextId)
                || (link.FromTextId == run.ToTextId && link.ToTextId == run.FromTextId))
            .ToListAsync(cancellationToken);
        var bySourceWord = touching
            .SelectMany(link => Sides(link, run).Source.Select(word => (word, link)))
            .GroupBy(pair => pair.word)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.link).Distinct().ToList());

        foreach (var review in reviews)
        {
            var decision = review.Decision!;
            var source = decision.SourceWordId;
            var target = review.CorrectedTargetWordId ?? decision.TargetWordId!.Value;
            var claims = Claims(run, review);
            var strongest = claims.MaxBy(claim => ClaimStanding.Of(claim.Method))!;
            var links = bySourceWord.GetValueOrDefault(source) ?? [];

            var exact = links.FirstOrDefault(link => Sides(link, run) is var (from, to)
                && from.SetEquals([source]) && to.SetEquals([target]));
            var within = exact is null
                ? links.FirstOrDefault(link => Sides(link, run) is var (from, to) && from.Contains(source) && to.Contains(target))
                : null;
            var elsewhere = links
                .Where(link => Sides(link, run) is var (_, to) && to.Count > 0 && !to.Contains(target))
                .MaxBy(link => ClaimStanding.Of(link.Method));

            if (exact is not null)
            {
                if (exact.Confidence is not null && ClaimStanding.Of(strongest.Method) > ClaimStanding.Of(exact.Method))
                {
                    Keep(exact, new LinkClaim
                    {
                        Method = exact.Method, Confidence = exact.Confidence, Source = exact.Source, Note = exact.Note,
                    });
                    exact.Method = strongest.Method;
                    exact.Confidence = strongest.Confidence;
                    exact.Source = strongest.Source;
                    exact.Note = strongest.Note;
                    tally.Promoted++;
                    tally.Lines.Add($"link {exact.Id}: settled answer now {EnumSpelling.Of(strongest.Method)} (review {review.Id})");
                }

                claims.ForEach(claim => Keep(exact, claim));
                review.Link = exact;
                tally.OnExisting++;
            }
            else if (within is not null)
            {
                claims.ForEach(claim => Keep(within, new LinkClaim
                {
                    Method = claim.Method, Confidence = claim.Confidence, Source = claim.Source,
                    Note = Joined(WithinNote, claim.Note),
                }));
                review.Link = within;
                tally.Within++;
            }
            else if (elsewhere is not null && ClaimStanding.Of(elsewhere.Method) > ClaimStanding.Of(strongest.Method))
            {
                review.Withheld = $"link {elsewhere.Id} ({EnumSpelling.Of(elsewhere.Method)}) places this word on other words";
                tally.Withheld++;
                tally.Lines.Add($"review {review.Id} withheld: {review.Withheld}");
                continue;
            }
            else
            {
                var link = new Link
                {
                    FromTextId = run.FromTextId,
                    ToTextId = run.ToTextId,
                    Relation = LinkRelation.Renders,
                    Method = strongest.Method,
                    Confidence = strongest.Confidence,
                    Source = strongest.Source,
                    Note = strongest.Note,
                    Words =
                    [
                        new LinkWord { WordId = source, Side = LinkSide.From },
                        new LinkWord { WordId = target, Side = LinkSide.To },
                    ],
                    Claims = [.. claims],
                };
                db.Links.Add(link);
                if (!bySourceWord.TryGetValue(source, out var standing))
                {
                    bySourceWord[source] = standing = [];
                }

                standing.Add(link);
                review.Link = link;
                tally.NewLinks++;
            }

            review.Withheld = null;
            review.AppliedAt = DateTimeOffset.UtcNow;
        }

        if (write)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Link sides as the run reads them: its source text's words, then its target's. A link stored
    /// the other way round between the same two texts is the same statement.
    /// </summary>
    private static (HashSet<long> Source, HashSet<long> Target) Sides(Link link, EvidentiaRun run)
    {
        var forward = link.FromTextId == run.FromTextId;
        var from = link.Words.Where(word => word.Side == LinkSide.From).Select(word => word.WordId).ToHashSet();
        var to = link.Words.Where(word => word.Side == LinkSide.To).Select(word => word.WordId).ToHashSet();
        return forward ? (from, to) : (to, from);
    }

    private static void Keep(Link link, LinkClaim claim)
    {
        if (!link.Claims.Any(held => held.Method == claim.Method && held.Source == claim.Source))
        {
            link.Claims.Add(claim);
        }
    }

    private static List<LinkClaim> Claims(EvidentiaRun run, EvidentiaReview review)
    {
        var decision = review.Decision!;
        var person = new LinkClaim
        {
            Method = LinkMethod.Manual,
            Confidence = null,
            Source = review.Verdict == EvidentiaVerdict.Corrected
                ? $"{review.Reviewer}, correcting EVIDENTIA run {run.Id} decision {decision.Id}"
                : $"{review.Reviewer}, reviewing EVIDENTIA run {run.Id} decision {decision.Id}",
            Note = review.Note,
        };
        if (review.Verdict == EvidentiaVerdict.Corrected)
        {
            return [person];
        }

        var rule = new LinkClaim
        {
            Method = LinkMethod.RuleBased,
            Confidence = decision.Confidence is { } confidence ? Math.Round(confidence, ConfidenceDigits) : null,
            Source = $"EVIDENTIA run {run.Id} ({run.RuleVersion}), {decision.Kind}",
            Note = Joined(
                ($"decision {decision.Id}, {decision.Tier} tier: {decision.Rationale}"),
                review.Examined ? null : $"accepted with its tier by {review.Reviewer}, not read one by one"),
        };
        return review.Examined ? [person, rule] : [rule];
    }

    private static string? Joined(string? first, string? second) =>
        (first, second) switch
        {
            (null, _) => second,
            (_, null) => first,
            _ => $"{first}; {second}",
        };

    private sealed class Tally
    {
        public int NewLinks;
        public int OnExisting;
        public int Within;
        public int Promoted;
        public int Withheld;
        public List<string> Lines { get; } = [];
    }
}
