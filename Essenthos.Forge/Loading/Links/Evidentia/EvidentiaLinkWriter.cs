using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <param name="Written">Whether anything reached the database; false for a plan.</param>
/// <param name="Verdicts">Approved and corrected reviews not yet written.</param>
/// <param name="NewLinks">Links the verdicts created.</param>
/// <param name="NewAbsences">Of those, the ones saying a word has no counterpart.</param>
/// <param name="OnExisting">Verdicts that became claims on a link naming exactly their words.</param>
/// <param name="Within">Verdicts that became claims on a link naming more words than theirs.</param>
/// <param name="Promoted">Existing links whose settled answer a verdict outranked.</param>
/// <param name="Withheld">
/// Verdicts not written because the corpus places the word elsewhere with more standing, or, for an
/// absence, gives the word a counterpart the absence does not outrank.
/// </param>
/// <param name="Withdrawn">Words taken out of an aligner's link so that a safe-tier absence could be written.</param>
/// <param name="Superseded">Absences an earlier run's rule wrote on a word this run's verdicts render, taken back.</param>
/// <param name="Attached">Words of the written links marked as going with another word of their own text.</param>
internal sealed record EvidentiaApplyOutcome(
    bool Written,
    int Verdicts,
    int NewLinks,
    int NewAbsences,
    int OnExisting,
    int Within,
    int Promoted,
    int Withheld,
    int Withdrawn,
    int Superseded,
    IReadOnlyList<string> Lines,
    TimeSpan Elapsed,
    int Attached = 0)
{
    public override string ToString() =>
        (Written ? "EVIDENTIA verdicts written" : "EVIDENTIA verdicts NOT written (plan only; pass --write to write)")
        + $": {Verdicts:N0} approved or corrected; {NewLinks:N0} new links ({NewAbsences:N0} of them absences), {OnExisting:N0} claims on a link naming the same words, "
        + $"{Within:N0} on a link naming more, {Promoted:N0} links whose settled answer changed, {Withheld:N0} withheld, "
        + $"{Withdrawn:N0} words withdrawn from the aligner's links for an absence, {Superseded:N0} earlier absences taken back for a rendering, "
        + $"{Attached:N0} words marked as going with another word; in {Elapsed}"
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
/// may not add a second rendering beside one a source states. A person's pair is not withheld — a person
/// reviewing the word decided knowing what the corpus says. Nothing here deletes a link.
/// </para>
///
/// <para>
/// **An absence is written as the corpus writes every other one**: a word the translation supplies
/// as an <see cref="LinkRelation.Expands"/> link naming it on the <c>from</c> side alone, a word of
/// the original it does not render as an <see cref="LinkRelation.Omits"/> link naming it on the
/// <c>to</c> side alone, with the same claims a pair would carry. It settles against the links that
/// already say something about that word: the same absence gains the claims, a wider absence gains
/// them with a note, and a link giving the word a counterpart keeps the absence out, a person's as
/// much as a rule's, except where every such link is only the aligner's and the absence is in the
/// safe tier by a rule measured at least as well as that tier. Then the aligner's guess gives way:
/// the word is taken out of its link, or the link is removed where the word was all of its side,
/// and an <see cref="EvidentiaWithdrawal"/> keeps what the link said under the verdict, so the run
/// and the rule are the recorded reason and the withdrawal can be undone. A stated, numbered,
/// read or reviewed link is never withdrawn from, and neither is a link putting a <em>the</em> said
/// to be supplied on an article the original does write: that is the premise of the absence
/// contradicted by the text, which happens when the noun it rests on was placed on the wrong word.
/// </para>
///
/// <para>
/// **A pair settles against an absence on either of its words the same way.** Where a link says the
/// word has no counterpart, the pair is withheld, a person's as much as a rule's, except where that
/// absence is an earlier run's rule and nobody else's: then the later verdict wins. The absence is
/// taken back with an <see cref="EvidentiaWithdrawal"/> under the verdict that renders the word, and
/// the review that wrote it is marked as superseded, so a replay of the ledger does not write it again.
/// </para>
///
/// <para>
/// **An attached word is a link like any other, and then says whose it is.** <em>did</em> of <em>did
/// see</em> is written on the word its rule placed it on, and where that is its head's own
/// counterpart, its membership of the link is marked with its role and the head it goes with
/// (<see cref="AttachedWords"/>), in the same transaction.
/// </para>
/// </summary>
internal sealed class EvidentiaLinkWriter(AppDbContext db, VerseLinkLoader verseLinks)
{
    private const int Batch = 2_000;

    /// <summary>A decision's confidence is stored as a real; four digits is all of it that is not rounding noise.</summary>
    private const int ConfidenceDigits = 4;

    private const string WithinNote = "names one word pair of this link";

    private const string WithinAbsenceNote = "names one word of this absence";

    private const string WithdrawnNote = "the aligner's link of this word was withdrawn for it";

    private const string SupersededNote = "an earlier run's absence on this word was taken back for it";

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
            tally.Attached = await db.Database.ExecuteSqlInterpolatedAsync(AttachedWords.Mark(runId), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (tally.NewLinks > 0)
            {
                tally.Lines.Add((await verseLinks.Load(cancellationToken)).ToString());
            }
        }

        return new EvidentiaApplyOutcome(write, verdictIds.Count, tally.NewLinks, tally.NewAbsences, tally.OnExisting, tally.Within,
            tally.Promoted, tally.Withheld, tally.Withdrawn, tally.Superseded, tally.Lines, elapsed.Elapsed, tally.Attached);
    }

    private async Task Settle(EvidentiaRun run, long[] reviewIds, bool write, Tally tally, CancellationToken cancellationToken)
    {
        var reviews = await db.EvidentiaReviews
            .Include(review => review.Decision)
            .Where(review => reviewIds.Contains(review.Id))
            .OrderBy(review => review.Id)
            .ToListAsync(cancellationToken);
        var claimed = reviews.Select(review => Claimed(review)).ToList();
        var words = claimed.Select(claim => claim.Word)
            .Concat(claimed.Where(claim => claim.Relation == LinkRelation.Renders).SelectMany(claim => claim.Target))
            .Distinct().ToList();
        var touching = await db.Links
            .Include(link => link.Words)
            .Include(link => link.Provenance)
            .Include(link => link.Claims).ThenInclude(held => held.Provenance)
            .AsSplitQuery()
            .Where(link => link.Words.Any(word => words.Contains(word.WordId)))
            .Where(link => (link.FromTextId == run.FromTextId && link.ToTextId == run.ToTextId)
                || (link.FromTextId == run.ToTextId && link.ToTextId == run.FromTextId))
            .ToListAsync(cancellationToken);
        var bySourceWord = touching
            .SelectMany(link => Sides(link, run).Source.Select(word => (word, link)))
            .GroupBy(pair => pair.word)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.link).Distinct().ToList());
        var byTargetWord = touching
            .SelectMany(link => Sides(link, run).Target.Select(word => (word, link)))
            .GroupBy(pair => pair.word)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.link).Distinct().ToList());
        var linked = touching.SelectMany(link => link.Words.Select(word => word.WordId)).Distinct().ToList();
        var articles = (await db.Words.AsNoTracking()
            .Where(word => linked.Contains(word.Id) && EvidentiaAbsences.ArticleNumbers.Contains(word.StrongNumber!))
            .Select(word => word.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        var ruleAbsences = touching.Where(link => link.Relation != LinkRelation.Renders && OnlyARule(link))
            .Select(link => (long?)link.Id).ToList();
        var writtenBy = (await db.EvidentiaReviews
            .Where(review => ruleAbsences.Contains(review.LinkId))
            .ToListAsync(cancellationToken))
            .ToLookup(review => review.LinkId!.Value);

        var removed = new HashSet<Link>();
        foreach (var (review, claim) in reviews.Zip(claimed))
        {
            var claims = Claims(run, review);
            var strongest = claims.MaxBy(held => ClaimStanding.Of(held.Method))!;
            var index = claim.OnSource ? bySourceWord : byTargetWord;

            if (claim.Relation == LinkRelation.Renders
                && Absences(claim, run, bySourceWord, byTargetWord, removed) is { Count: > 0 } absences)
            {
                if (absences.FirstOrDefault(absence => !OnlyARule(absence)) is { } stands)
                {
                    review.Withheld = $"link {stands.Id} ({EnumSpelling.Of(stands.Method)}) says this word has no counterpart";
                    tally.Withheld++;
                    tally.Lines.Add($"review {review.Id} withheld: {review.Withheld}");
                    continue;
                }

                foreach (var absence in absences)
                {
                    foreach (var earlier in writtenBy[absence.Id])
                    {
                        earlier.Withheld = $"review {review.Id} of run {run.Id} renders this word, so this absence was taken back";
                    }

                    Withdraw(absence, absence.Words.Single().WordId, review, removed);
                    tally.Superseded++;
                    tally.Lines.Add($"link {absence.Id}: {EnumSpelling.Of(absence.Relation)} taken back, review {review.Id} renders its word");
                }

                claims.Where(held => held.Method == LinkMethod.RuleBased).ToList()
                    .ForEach(held => held.Provenance = Noted(held.Provenance!, SupersededNote));
            }
            var links = (index.GetValueOrDefault(claim.Word) ?? []).Where(link => !removed.Contains(link)).ToList();

            var exact = links.FirstOrDefault(link => Sides(link, run) is var (from, to)
                && from.SetEquals(claim.Source) && to.SetEquals(claim.Target));
            var within = exact is null
                ? links.FirstOrDefault(link => Sides(link, run) is var (from, to)
                    && from.IsSupersetOf(claim.Source) && to.IsSupersetOf(claim.Target)
                    && (claim.Relation == LinkRelation.Renders || Counterparts(link, run, claim).Count == 0))
                : null;

            if (exact is null && within is null && claim.Relation != LinkRelation.Renders
                && OutranksTheAligner(review.Decision!)
                && links.Where(link => Counterparts(link, run, claim).Count > 0).ToList() is { Count: > 0 } guesses
                && guesses.All(OnlyTheAligner)
                && !(review.Decision!.Kind == EvidentiaAbsenceRule.UnwrittenArticle.Spelling
                    && guesses.Any(guess => Counterparts(guess, run, claim).Overlaps(articles))))
            {
                foreach (var guess in guesses)
                {
                    Withdraw(guess, claim.Word, review, removed);
                    index[claim.Word].Remove(guess);
                    links.Remove(guess);
                    tally.Withdrawn++;
                }

                claims.Where(held => held.Method == LinkMethod.RuleBased).ToList()
                    .ForEach(held => held.Provenance = Noted(held.Provenance!, WithdrawnNote));
            }

            var elsewhere = links
                .Where(link => Counterparts(link, run, claim) is { Count: > 0 } counterparts
                    && (claim.Relation != LinkRelation.Renders || !counterparts.IsSupersetOf(claim.Target)))
                .MaxBy(link => ClaimStanding.Of(link.Method));

            if (exact is not null)
            {
                if (exact.Confidence is not null && ClaimStanding.Of(strongest.Method) > ClaimStanding.Of(exact.Method))
                {
                    Keep(exact, new LinkClaim
                    {
                        Method = exact.Method, Confidence = exact.Confidence, Provenance = exact.Provenance,
                    });
                    exact.Method = strongest.Method;
                    exact.Confidence = strongest.Confidence;
                    exact.Provenance = strongest.Provenance;
                    tally.Promoted++;
                    tally.Lines.Add($"link {exact.Id}: settled answer now {EnumSpelling.Of(strongest.Method)} (review {review.Id})");
                }

                claims.ForEach(held => Keep(exact, held));
                review.Link = exact;
                tally.OnExisting++;
            }
            else if (within is not null)
            {
                claims.ForEach(held => Keep(within, new LinkClaim
                {
                    Method = held.Method,
                    Confidence = held.Confidence,
                    Provenance = new Provenance
                    {
                        Source = held.Provenance!.Source,
                        Note = Joined(
                            claim.Relation == LinkRelation.Renders ? WithinNote : WithinAbsenceNote, held.Provenance.Note),
                    },
                }));
                review.Link = within;
                tally.Within++;
            }
            else if (elsewhere is not null && Withheld(claim, strongest, elsewhere))
            {
                review.Withheld = claim.Relation == LinkRelation.Renders
                    ? $"link {elsewhere.Id} ({EnumSpelling.Of(elsewhere.Method)}) places this word on other words"
                    : $"link {elsewhere.Id} ({EnumSpelling.Of(elsewhere.Method)}) gives this word a counterpart";
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
                    Relation = claim.Relation,
                    Method = strongest.Method,
                    Confidence = strongest.Confidence,
                    Provenance = strongest.Provenance,
                    Words =
                    [
                        .. claim.Source.Select(word => new LinkWord { WordId = word, Side = LinkSide.From }),
                        .. claim.Target.Select(word => new LinkWord { WordId = word, Side = LinkSide.To }),
                    ],
                    Claims = [.. claims],
                };
                db.Links.Add(link);
                if (!index.TryGetValue(claim.Word, out var standing))
                {
                    index[claim.Word] = standing = [];
                }

                standing.Add(link);
                review.Link = link;
                tally.NewLinks++;
                tally.NewAbsences += claim.Relation == LinkRelation.Renders ? 0 : 1;
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
    /// The correspondence a verdict states, in the run's orientation: a pair for a proposal or a
    /// correction, one word with nothing opposite it for an absence. The corpus's links are looked up
    /// by the pair's source word, or by the absent word.
    /// </summary>
    private static Correspondence Claimed(EvidentiaReview review)
    {
        var decision = review.Decision!;
        if (review.Verdict == EvidentiaVerdict.Corrected || decision.Absence is null)
        {
            var source = decision.SourceWordId!.Value;
            return new Correspondence(LinkRelation.Renders, source, true, [source],
                [review.CorrectedTargetWordId ?? decision.TargetWordId!.Value]);
        }

        return decision.Absence == LinkRelation.Expands
            ? new Correspondence(LinkRelation.Expands, decision.SourceWordId!.Value, true, [decision.SourceWordId.Value], [])
            : new Correspondence(LinkRelation.Omits, decision.TargetWordId!.Value, false, [], [decision.TargetWordId.Value]);
    }

    /// <summary>The words a link sets opposite the claim's own word.</summary>
    private static HashSet<long> Counterparts(Link link, EvidentiaRun run, Correspondence claim) =>
        Sides(link, run) is var (from, to) && claim.OnSource ? to : from;

    /// <summary>
    /// Whether a link that gives the word other counterparts keeps the verdict out. A pair is kept
    /// out by a link that outranks it, and a person's pair by none. An absence is kept out by any
    /// link still giving the word a counterpart, whoever stated either: a second rendering beside a
    /// first is two answers a reader can weigh, but a word shown as supplied and as rendered at once
    /// is a contradiction. Only the aligner's guess is ever withdrawn to resolve it, and only for a
    /// safe-tier absence; where a person holds any other link wrong, it is the link that has to be
    /// corrected.
    /// </summary>
    private static bool Withheld(Correspondence claim, LinkClaim strongest, Link elsewhere) =>
        claim.Relation != LinkRelation.Renders
        || ClaimStanding.Of(elsewhere.Method) > ClaimStanding.Of(strongest.Method);

    /// <summary>
    /// Whether an absence may take its word out of the aligner's links: accepted or approved in the
    /// safe tier, by a rule whose own measured precision reaches that tier's.
    /// </summary>
    private static bool OutranksTheAligner(EvidentiaDecision decision) =>
        decision.Tier == EvidentiaDecisionRecorder.SafeTier
        && EvidentiaAbsenceRule.Named(decision.Kind) is { OutranksTheAligner: true };

    /// <summary>
    /// The links saying a word of the pair has no counterpart: its translation word supplied, or its
    /// original word not rendered.
    /// </summary>
    private static List<Link> Absences(
        Correspondence claim,
        EvidentiaRun run,
        Dictionary<long, List<Link>> bySourceWord,
        Dictionary<long, List<Link>> byTargetWord,
        HashSet<Link> removed) =>
    [
        .. claim.Source.SelectMany(word => bySourceWord.GetValueOrDefault(word) ?? [])
            .Concat(claim.Target.SelectMany(word => byTargetWord.GetValueOrDefault(word) ?? []))
            .Where(link => !removed.Contains(link) && Sides(link, run) is var (from, to) && (from.Count == 0 || to.Count == 0))
            .Distinct(),
    ];

    /// <summary>A link only EVIDENTIA's rules claim and no person read, which a later verdict may take back.</summary>
    private static bool OnlyARule(Link link) =>
        link.Method == LinkMethod.RuleBased && link.Claims.All(held => held.Method == LinkMethod.RuleBased);

    /// <summary>A link nothing but the aligner claims: no source stated it, no number, reading or person.</summary>
    private static bool OnlyTheAligner(Link link) =>
        link.Method == LinkMethod.Aligner && link.Claims.All(held => held.Method == LinkMethod.Aligner);

    /// <summary>
    /// Takes the word out of an aligner's link, or an earlier rule's absence, and records everything
    /// the link said. The link goes with it where the word was the only one on its side, since a
    /// rendering with one side empty would be an absence nobody claimed.
    /// </summary>
    private void Withdraw(Link link, long word, EvidentiaReview review, HashSet<Link> removed)
    {
        var named = link.Words.Single(held => held.WordId == word);
        var alone = link.Words.Count(held => held.Side == named.Side) == 1;
        var claims = link.Claims.OrderBy(held => held.Id).ToList();
        db.EvidentiaWithdrawals.Add(new EvidentiaWithdrawal
        {
            Review = review,
            WordId = word,
            Link = alone ? null : link,
            FromTextId = link.FromTextId,
            ToTextId = link.ToTextId,
            Relation = link.Relation,
            Method = link.Method,
            Confidence = link.Confidence,
            Source = link.Provenance!.Source,
            Note = link.Provenance.Note,
            FromWordIds = [.. link.Words.Where(held => held.Side == LinkSide.From).Select(held => held.WordId).Order()],
            ToWordIds = [.. link.Words.Where(held => held.Side == LinkSide.To).Select(held => held.WordId).Order()],
            ClaimSources = [.. claims.Select(held => held.Provenance!.Source)],
            ClaimConfidences = [.. claims.Select(held => held.Confidence)],
            ClaimNotes = [.. claims.Select(held => held.Provenance!.Note)],
            WithdrawnAt = DateTimeOffset.UtcNow,
        });

        if (alone)
        {
            db.Links.Remove(link);
            removed.Add(link);
        }
        else
        {
            link.Words.Remove(named);
            db.LinkWords.Remove(named);
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
        if (!link.Claims.Any(held => held.Method == claim.Method && held.Provenance!.Source == claim.Provenance!.Source))
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
            Provenance = new Provenance
            {
                Source = review.Verdict == EvidentiaVerdict.Corrected
                    ? $"{review.Reviewer}, correcting EVIDENTIA run {run.Id} decision {decision.Id}"
                    : $"{review.Reviewer}, reviewing EVIDENTIA run {run.Id} decision {decision.Id}",
                Note = review.Note,
            },
        };
        if (review.Verdict == EvidentiaVerdict.Corrected)
        {
            return [person];
        }

        var rule = new LinkClaim
        {
            Method = LinkMethod.RuleBased,
            Confidence = decision.Confidence is { } confidence ? Math.Round(confidence, ConfidenceDigits) : null,
            Provenance = new Provenance
            {
                Source = $"EVIDENTIA run {run.Id} ({run.RuleVersion}), {decision.Kind}",
                Note = Joined(
                    ($"decision {decision.Id}, {decision.Tier} tier: {decision.Rationale}"),
                    review.Examined ? null : $"accepted with its tier by {review.Reviewer}, not read one by one"),
            },
        };
        return review.Examined ? [person, rule] : [rule];
    }

    /// <summary>
    /// The same source with something added to its note. A new provenance rather than an edit,
    /// because the row the claim points at may be every other claim's with that source and note.
    /// </summary>
    private static Provenance Noted(Provenance provenance, string note) =>
        new() { Source = provenance.Source, Note = Joined(provenance.Note, note) };

    private static string? Joined(string? first, string? second) =>
        (first, second) switch
        {
            (null, _) => second,
            (_, null) => first,
            _ => $"{first}; {second}",
        };

    /// <param name="Word">The word the verdict is about, and which the corpus's links to it are found by.</param>
    /// <param name="OnSource">Whether that word is of the run's source text.</param>
    private sealed record Correspondence(
        LinkRelation Relation,
        long Word,
        bool OnSource,
        HashSet<long> Source,
        HashSet<long> Target);

    private sealed class Tally
    {
        public int NewLinks;
        public int NewAbsences;
        public int OnExisting;
        public int Within;
        public int Promoted;
        public int Withheld;
        public int Withdrawn;
        public int Superseded;
        public int Attached;
        public List<string> Lines { get; } = [];
    }
}
