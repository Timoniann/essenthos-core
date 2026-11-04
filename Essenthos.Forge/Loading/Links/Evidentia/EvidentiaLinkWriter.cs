using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

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
/// <param name="Replaced">
/// Placements an earlier run's rule wrote for a word this run's verdicts place on other words, taken back.
/// </param>
/// <param name="Reading">Of the time, how much went on reading the verdicts and the links they settle against.</param>
/// <param name="Writing">Of the time, how much went on writing what they settled.</param>
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
    int Attached = 0,
    int Replaced = 0,
    TimeSpan Reading = default,
    TimeSpan Writing = default)
{
    public override string ToString() =>
        (Written ? "EVIDENTIA verdicts written" : "EVIDENTIA verdicts NOT written (plan only; pass --write to write)")
        + $": {Verdicts:N0} approved or corrected; {NewLinks:N0} new links ({NewAbsences:N0} of them absences), {OnExisting:N0} claims on a link naming the same words, "
        + $"{Within:N0} on a link naming more, {Promoted:N0} links whose settled answer changed, {Withheld:N0} withheld, "
        + $"{Withdrawn:N0} words withdrawn from the aligner's links for an absence, {Superseded:N0} earlier absences taken back for a rendering, "
        + $"{Replaced:N0} earlier runs' placements taken back for one elsewhere, "
        + $"{Attached:N0} words marked as going with another word; in {Elapsed} ({Reading} reading, {Writing} writing)"
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
/// **A later run of the same two texts replaces an earlier run's placement of a word.** Where a pair
/// verdict places its word on other words than a link already does, and that link is an earlier
/// EVIDENTIA run's rule and nobody else's — no source, number, reading or person — the earlier
/// placement is taken back with an <see cref="EvidentiaWithdrawal"/> under the new verdict, and the
/// review that wrote it is marked as superseded, as an absence is. Otherwise re-running a text with
/// new rules would leave the old rules' placement standing as a second rendering beside the new one
/// (PRB-1097). A word the later run does not decide keeps its link, and nothing is taken back for a
/// verdict that is itself withheld.
/// </para>
///
/// <para>
/// **An attached word is a link like any other, and then says whose it is.** <em>did</em> of <em>did
/// see</em> is written on the word its rule placed it on, and where that is its head's own
/// counterpart, its membership of the link is marked with its role and the head it goes with
/// (<see cref="AttachedWords"/>), in the same transaction.
/// </para>
///
/// <para>
/// **The verdicts are settled in memory and written together.** Each batch reads the links of the
/// pair its words are on, decides every verdict against them in order, and then writes what it
/// decided as a few statements: the new links, their words and every new claim by COPY, the settled
/// answers, the words taken out of links, the withdrawals, the reviews, and last the links taken back.
/// A link a verdict of the same batch wrote and a later one takes back stays written, because the
/// verdict that wrote it still names it.
/// </para>
/// </summary>
internal sealed class EvidentiaLinkWriter(AppDbContext db, VerseLinkLoader verseLinks)
{
    private const int Batch = 2_000;

    /// <summary>A decision's confidence is stored as a real; four digits is all of it that is not rounding noise.</summary>
    private const int ConfidenceDigits = 4;

    /// <summary>The source of the rule's claim: the run, the rules' version and the kind of decision.</summary>
    internal static string RuleSource(int run, string ruleVersion, string kind) =>
        $"{Sources.EvidentiaRunPrefix} {run} ({ruleVersion}), {kind}";

    internal const string WithinNote = "names one word pair of this link";

    internal const string WithinAbsenceNote = "names one word of this absence";

    private const string WithdrawnNote = "the aligner's link of this word was withdrawn for it";

    private const string SupersededNote = "an earlier run's absence on this word was taken back for it";

    private const string ReplacedNote = "an earlier run's placement of this word was taken back for it";

    /// <summary>The links of the run's two texts, either way round, naming any of the words.</summary>
    private const string Touching =
        """
        SELECT DISTINCT l.id
        FROM unnest(@words) AS w(id)
        JOIN link_word lw ON lw.word_id = w.id
        JOIN link l ON l.id = lw.link_id
        WHERE (l.from_text_id = @from AND l.to_text_id = @to) OR (l.from_text_id = @to AND l.to_text_id = @from)
        """;

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, provenance_id, fingerprint)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport = "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    private const string ClaimImport =
        "COPY link_claim (link_id, method, confidence, provenance_id) FROM STDIN (FORMAT BINARY)";

    private const string WithdrawalImport =
        """
        COPY evidentia_withdrawal (
            review_id, word_id, link_id, from_text_id, to_text_id, relation, method, confidence, source, note,
            from_word_ids, to_word_ids, claim_sources, claim_confidences, claim_notes, withdrawn_at)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string Promote =
        """
        UPDATE link l SET method = d.method, confidence = d.confidence, provenance_id = d.provenance_id
        FROM unnest(@links, @methods, @confidences, @provenances) AS d(id, method, confidence, provenance_id)
        WHERE l.id = d.id
        """;

    private const string Unlink =
        """
        DELETE FROM link_word lw
        USING unnest(@links, @words, @sides) AS d(link_id, word_id, side)
        WHERE lw.link_id = d.link_id AND lw.word_id = d.word_id AND lw.side = d.side
        """;

    private const string Reviewed =
        """
        UPDATE evidentia_review r SET withheld = d.withheld, applied_at = d.applied_at, link_id = d.link_id
        FROM unnest(@reviews, @withheld, @applied, @links) AS d(id, withheld, applied_at, link_id)
        WHERE r.id = d.id
        """;

    private const string TakenBack = "DELETE FROM link WHERE id = ANY(@links)";

    /// <param name="joinVerses">
    /// Whether the verse links are drawn again for what the run wrote. A replay of several runs draws
    /// them once after the last (<see cref="JoinVerses"/>), because no verdict is settled against them.
    /// </param>
    public async Task<EvidentiaApplyOutcome> Apply(
        int runId, bool write, CancellationToken cancellationToken = default, bool joinVerses = true)
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
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = write ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            foreach (var chunk in verdictIds.Chunk(Batch))
            {
                var changes = await Settle(run, chunk, tally, cancellationToken);
                if (transaction is not null)
                {
                    var writing = Stopwatch.StartNew();
                    await Write(changes, (NpgsqlTransaction)transaction.GetDbTransaction(), cancellationToken);
                    tally.Writing += writing.Elapsed;
                }
            }

            if (transaction is not null)
            {
                tally.Attached = await db.Database.ExecuteSqlInterpolatedAsync(AttachedWords.Mark(runId), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                if (tally.NewLinks > 0 && joinVerses)
                {
                    tally.Lines.Add(await JoinVerses(cancellationToken));
                }
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return new EvidentiaApplyOutcome(write, verdictIds.Count, tally.NewLinks, tally.NewAbsences, tally.OnExisting, tally.Within,
            tally.Promoted, tally.Withheld, tally.Withdrawn, tally.Superseded, tally.Lines, elapsed.Elapsed, tally.Attached, tally.Replaced,
            tally.Reading, tally.Writing);
    }

    /// <summary>
    /// Draws the verse links the links written since call for; says what it did. A run writes no
    /// aligner link, turns aligner links into rule-based ones rather than the other way, and takes
    /// no verse link away, so it can make no crossing for <see cref="VerseLinkLoader.Load"/>'s sweep
    /// to find, and none is looked for (<c>swept</c> empty).
    /// </summary>
    public async Task<string> JoinVerses(CancellationToken cancellationToken = default) =>
        (await verseLinks.Load(cancellationToken, swept: NothingSwept)).ToString();

    private static readonly IReadOnlySet<int> NothingSwept = new HashSet<int>();

    /// <summary>
    /// Decides a batch of verdicts, in the order they were given, against the links of the pair as the
    /// batch finds them, and says what is to be written. Nothing here writes.
    /// </summary>
    private async Task<Changes> Settle(EvidentiaRun run, long[] reviewIds, Tally tally, CancellationToken cancellationToken)
    {
        var reading = Stopwatch.StartNew();
        var reviews = await db.EvidentiaReviews.AsNoTracking()
            .Include(review => review.Decision)
            .Where(review => reviewIds.Contains(review.Id))
            .OrderBy(review => review.Id)
            .ToListAsync(cancellationToken);
        var claimed = reviews.Select(review => Claimed(review)).ToList();
        var words = claimed.Select(claim => claim.Word)
            .Concat(claimed.Where(claim => claim.Relation == LinkRelation.Renders).SelectMany(claim => claim.Target))
            .Distinct().ToArray();
        var touchingIds = await TouchingIds(run, words, cancellationToken);
        var touching = await db.Links.AsNoTracking()
            .Include(link => link.Words)
            .Include(link => link.Provenance)
            .Include(link => link.Claims).ThenInclude(held => held.Provenance)
            .AsSplitQuery()
            .Where(link => touchingIds.Contains(link.Id))
            .OrderBy(link => link.Id)
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
        var takeable = touching.Where(link => (link.Relation != LinkRelation.Renders && OnlyARule(link)) || OnlyAnEarlierRun(link, run))
            .Select(link => (long?)link.Id).ToList();
        var writtenBy = (await db.EvidentiaReviews.AsNoTracking()
            .Where(review => takeable.Contains(review.LinkId))
            .ToListAsync(cancellationToken))
            .ToLookup(review => review.LinkId!.Value);
        tally.Reading += reading.Elapsed;

        var changes = new Changes(reviews, touching);
        var removed = changes.Removed;
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
                        changes.TakenBackFrom.Add(earlier);
                    }

                    Withdraw(absence, absence.Words.Single().WordId, review, changes);
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
                    Withdraw(guess, claim.Word, review, changes);
                    index[claim.Word].Remove(guess);
                    links.Remove(guess);
                    tally.Withdrawn++;
                }

                claims.Where(held => held.Method == LinkMethod.RuleBased).ToList()
                    .ForEach(held => held.Provenance = Noted(held.Provenance!, WithdrawnNote));
            }

            var replaced = claim.Relation == LinkRelation.Renders
                ? links.Where(link => OnlyAnEarlierRun(link, run)
                    && Counterparts(link, run, claim) is { Count: > 0 } counterparts && !counterparts.IsSupersetOf(claim.Target)).ToList()
                : [];
            var elsewhere = links
                .Where(link => !replaced.Contains(link)
                    && Counterparts(link, run, claim) is { Count: > 0 } counterparts
                    && (claim.Relation != LinkRelation.Renders || !counterparts.IsSupersetOf(claim.Target)))
                .MaxBy(link => ClaimStanding.Of(link.Method));

            if (exact is null && within is null && elsewhere is not null && Withheld(claim, strongest, elsewhere))
            {
                review.Withheld = claim.Relation == LinkRelation.Renders
                    ? $"link {elsewhere.Id} ({EnumSpelling.Of(elsewhere.Method)}) places this word on other words"
                    : $"link {elsewhere.Id} ({EnumSpelling.Of(elsewhere.Method)}) gives this word a counterpart";
                tally.Withheld++;
                tally.Lines.Add($"review {review.Id} withheld: {review.Withheld}");
                continue;
            }

            foreach (var earlier in replaced)
            {
                foreach (var by in writtenBy[earlier.Id])
                {
                    by.Withheld = $"review {review.Id} of run {run.Id} places this word elsewhere, so this placement was taken back";
                    changes.TakenBackFrom.Add(by);
                }

                Withdraw(earlier, claim.Word, review, changes);
                index[claim.Word].Remove(earlier);
                tally.Replaced++;
                tally.Lines.Add($"link {earlier.Id}: an earlier run's placement taken back, review {review.Id} places its word elsewhere");
            }

            if (replaced.Count > 0)
            {
                claims.Where(held => held.Method == LinkMethod.RuleBased).ToList()
                    .ForEach(held => held.Provenance = Noted(held.Provenance!, ReplacedNote));
            }

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
                    changes.Promoted.Add(exact);
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
                changes.Created.Add(link);
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

        return changes;
    }

    private async Task<long[]> TouchingIds(EvidentiaRun run, long[] words, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Touching, (NpgsqlConnection)db.Database.GetDbConnection(),
            db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
        command.Parameters.AddWithValue("words", words);
        command.Parameters.AddWithValue("from", run.FromTextId);
        command.Parameters.AddWithValue("to", run.ToTextId);
        var ids = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt64(0));
        }

        return [.. ids];
    }

    /// <summary>
    /// Writes what a batch settled: the sources first, then the new links with their words and claims
    /// and every claim added to a link already there, the settled answers that changed, the words
    /// taken out of links that stay, the withdrawals and the reviews, and the links taken back last,
    /// so that a review or withdrawal naming one of them is let go by the database as it goes.
    /// </summary>
    private static async Task Write(Changes changes, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        var standing = changes.Touching.Where(link => !changes.Removed.Contains(link)).ToList();
        var claims = changes.Created.SelectMany(link => link.Claims.Select(held => (Link: link, Claim: held)))
            .Concat(standing.SelectMany(link => link.Claims.Where(held => held.Id == 0).Select(held => (Link: link, Claim: held))))
            .ToList();
        var promoted = changes.Promoted.Where(link => link.Id != 0 && !changes.Removed.Contains(link)).ToList();

        var unnamed = changes.Created.Select(link => link.Provenance!)
            .Concat(claims.Select(pair => pair.Claim.Provenance!))
            .Concat(promoted.Select(link => link.Provenance!))
            .Where(provenance => provenance.Id == 0)
            .ToList();
        var provenance = await ProvenanceIds.Of(
            connection, transaction, unnamed.Select(held => (held.Source, held.Note)), cancellationToken);
        unnamed.ForEach(held => held.Id = provenance[(held.Source, held.Note)]);

        if (changes.Created.Count > 0)
        {
            var first = await LinkWriter.ReserveIds(connection, transaction, changes.Created.Count, cancellationToken);
            for (var n = 0; n < changes.Created.Count; n++)
            {
                changes.Created[n].Id = first + n;
            }

            await CopyLinks(connection, changes.Created, cancellationToken);
        }

        await CopyClaims(connection, claims, cancellationToken);
        if (promoted.Count > 0)
        {
            await using var command = new NpgsqlCommand(Promote, connection, transaction);
            command.Parameters.AddWithValue("links", promoted.Select(link => link.Id).ToArray());
            command.Parameters.AddWithValue("methods", promoted.Select(link => EnumSpelling.Of(link.Method)).ToArray());
            command.Parameters.Add(new NpgsqlParameter<double?[]>("confidences", promoted.Select(link => link.Confidence).ToArray()));
            command.Parameters.AddWithValue("provenances", promoted.Select(link => link.Provenance!.Id).ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var unlinked = changes.Unlinked.Where(pair => !changes.Removed.Contains(pair.Link)).ToList();
        if (unlinked.Count > 0)
        {
            await using var command = new NpgsqlCommand(Unlink, connection, transaction);
            command.Parameters.AddWithValue("links", unlinked.Select(pair => pair.Link.Id).ToArray());
            command.Parameters.AddWithValue("words", unlinked.Select(pair => pair.Word.WordId).ToArray());
            command.Parameters.AddWithValue("sides", unlinked.Select(pair => EnumSpelling.Of(pair.Word.Side)).ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await CopyWithdrawals(connection, changes.Withdrawals, cancellationToken);

        var reviews = changes.Reviews
            .Select(review => (review.Id, review.Withheld, review.AppliedAt, LinkId: review.Link?.Id ?? review.LinkId))
            .Concat(changes.TakenBackFrom.Where(review => changes.Reviews.All(settled => settled.Id != review.Id))
                .Select(review => (review.Id, review.Withheld, review.AppliedAt, review.LinkId)))
            .ToList();
        await using (var command = new NpgsqlCommand(Reviewed, connection, transaction))
        {
            command.Parameters.AddWithValue("reviews", reviews.Select(review => review.Id).ToArray());
            command.Parameters.Add(new NpgsqlParameter<string?[]>("withheld", reviews.Select(review => review.Withheld).ToArray()));
            command.Parameters.Add(new NpgsqlParameter<DateTimeOffset?[]>("applied", reviews.Select(review => review.AppliedAt).ToArray()));
            command.Parameters.Add(new NpgsqlParameter<long?[]>("links", reviews.Select(review => review.LinkId).ToArray()));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // A link a verdict of this batch wrote stays, taken back or not: its own verdict still names it.
        var created = changes.Created.ToHashSet();
        var takenBack = changes.Removed.Where(link => !created.Contains(link)).Select(link => link.Id).ToArray();
        if (takenBack.Length > 0)
        {
            await using var command = new NpgsqlCommand(TakenBack, connection, transaction);
            command.Parameters.AddWithValue("links", takenBack);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task CopyLinks(NpgsqlConnection connection, List<Link> links, CancellationToken cancellationToken)
    {
        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            foreach (var link in links)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(link.Id, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(link.FromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(link.ToTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(EnumSpelling.Of(link.Relation), NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(EnumSpelling.Of(link.Method), NpgsqlDbType.Text, cancellationToken);
                await Nullable(writer, link.Confidence, NpgsqlDbType.Double, cancellationToken);
                await writer.WriteAsync(link.Provenance!.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(
                    LinkShape.Of(
                        link.Words.Where(word => word.Side == LinkSide.From).Select(word => word.WordId),
                        link.Words.Where(word => word.Side == LinkSide.To).Select(word => word.WordId)),
                    NpgsqlDbType.Uuid, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
        {
            foreach (var link in links)
            {
                foreach (var word in link.Words)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(link.Id, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(word.WordId, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(EnumSpelling.Of(word.Side), NpgsqlDbType.Text, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }
    }

    private static async Task CopyClaims(
        NpgsqlConnection connection,
        List<(Link Link, LinkClaim Claim)> claims,
        CancellationToken cancellationToken)
    {
        if (claims.Count == 0)
        {
            return;
        }

        await using var writer = await connection.BeginBinaryImportAsync(ClaimImport, cancellationToken);
        foreach (var (link, claim) in claims)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(link.Id, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(EnumSpelling.Of(claim.Method), NpgsqlDbType.Text, cancellationToken);
            await Nullable(writer, claim.Confidence, NpgsqlDbType.Double, cancellationToken);
            await writer.WriteAsync(claim.Provenance!.Id, NpgsqlDbType.Integer, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }

    private static async Task CopyWithdrawals(
        NpgsqlConnection connection,
        List<(EvidentiaWithdrawal Row, Link? Link)> withdrawals,
        CancellationToken cancellationToken)
    {
        if (withdrawals.Count == 0)
        {
            return;
        }

        await using var writer = await connection.BeginBinaryImportAsync(WithdrawalImport, cancellationToken);
        foreach (var (row, link) in withdrawals)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(row.ReviewId, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(row.WordId, NpgsqlDbType.Bigint, cancellationToken);
            await Nullable(writer, link?.Id, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(row.FromTextId, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(row.ToTextId, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(EnumSpelling.Of(row.Relation), NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(EnumSpelling.Of(row.Method), NpgsqlDbType.Text, cancellationToken);
            await Nullable(writer, row.Confidence, NpgsqlDbType.Double, cancellationToken);
            await writer.WriteAsync(row.Source, NpgsqlDbType.Text, cancellationToken);
            await Nullable(writer, row.Note, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(row.FromWordIds, NpgsqlDbType.Array | NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(row.ToWordIds, NpgsqlDbType.Array | NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(row.ClaimSources, NpgsqlDbType.Array | NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(row.ClaimConfidences, NpgsqlDbType.Array | NpgsqlDbType.Double, cancellationToken);
            await writer.WriteAsync(row.ClaimNotes, NpgsqlDbType.Array | NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(row.WithdrawnAt, NpgsqlDbType.TimestampTz, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }

    private static async Task Nullable<T>(NpgsqlBinaryImporter writer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await writer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await writer.WriteAsync(value, type, cancellationToken);
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

    /// <summary>
    /// A placement only an earlier EVIDENTIA run's rules claim: every claim a rule's, and every rule an
    /// earlier run's. The links looked at are already the run's two texts', so it is this pair's run.
    /// </summary>
    private static bool OnlyAnEarlierRun(Link link, EvidentiaRun run) =>
        link.Relation == LinkRelation.Renders
        && OnlyARule(link)
        && link.Claims.Count > 0
        && link.Claims.All(held => RunOf(held.Provenance!.Source) is { } earlier && earlier < run.Id);

    /// <summary>The run a rule's claim names in its source, as <see cref="RuleSource"/> writes it; null for any other source.</summary>
    internal static int? RunOf(string source)
    {
        const string prefix = Sources.EvidentiaRunPrefix + " ";
        if (!source.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = source.AsSpan(prefix.Length);
        var end = rest.IndexOf(' ');
        return int.TryParse(end < 0 ? rest : rest[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    /// <summary>A link nothing but the aligner claims: no source stated it, no number, reading or person.</summary>
    private static bool OnlyTheAligner(Link link) =>
        link.Method == LinkMethod.Aligner && link.Claims.All(held => held.Method == LinkMethod.Aligner);

    /// <summary>
    /// Takes the word out of an aligner's link, or an earlier rule's absence, and records everything
    /// the link said. The link goes with it where the word was the only one on its side, since a
    /// rendering with one side empty would be an absence nobody claimed.
    /// </summary>
    private static void Withdraw(Link link, long word, EvidentiaReview review, Changes changes)
    {
        var named = link.Words.Single(held => held.WordId == word);
        var alone = link.Words.Count(held => held.Side == named.Side) == 1;
        var claims = link.Claims.OrderBy(held => held.Id).ToList();
        changes.Withdrawals.Add((new EvidentiaWithdrawal
        {
            ReviewId = review.Id,
            WordId = word,
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
        }, alone ? null : link));

        if (alone)
        {
            changes.Removed.Add(link);
        }
        else
        {
            link.Words.Remove(named);
            changes.Unlinked.Add((link, named));
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
                Source = $"{PersonSource(review.Reviewer, run.Id, review.Verdict == EvidentiaVerdict.Corrected)} decision {decision.Id}",
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
                Source = RuleSource(run.Id, run.RuleVersion, decision.Kind),
                Note = $"decision {decision.Id}, {RuleEvidence(decision.Tier, decision.Rationale, review.Examined, review.Reviewer)}",
            },
        };
        return review.Examined ? [person, rule] : [rule];
    }

    /// <summary>
    /// What a rule's claim says of its decision beyond the decision's row: the tier, the rationale, and
    /// whether the tier was accepted unread. The database's claim puts the decision's id before it.
    /// </summary>
    internal static string RuleEvidence(string? tier, string? rationale, bool examined, string reviewer) =>
        Joined($"{tier} tier: {rationale}", examined ? null : $"accepted with its tier by {reviewer}, not read one by one")!;

    /// <summary>The source of a person's claim on a verdict, which the database's claim follows with the decision's id.</summary>
    internal static string PersonSource(string reviewer, int run, bool corrected) =>
        corrected ? $"{reviewer}, correcting EVIDENTIA run {run}" : $"{reviewer}, reviewing EVIDENTIA run {run}";

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

    /// <summary>What a batch settled, to be written. <see cref="Touching"/> is what it read, as it left it.</summary>
    private sealed class Changes(List<EvidentiaReview> reviews, List<Link> touching)
    {
        public List<EvidentiaReview> Reviews { get; } = reviews;

        public List<Link> Touching { get; } = touching;

        public List<Link> Created { get; } = [];

        public HashSet<Link> Removed { get; } = [];

        public HashSet<Link> Promoted { get; } = [];

        public List<(Link Link, LinkWord Word)> Unlinked { get; } = [];

        public List<(EvidentiaWithdrawal Row, Link? Link)> Withdrawals { get; } = [];

        /// <summary>Earlier reviews whose link a verdict of this batch took back.</summary>
        public HashSet<EvidentiaReview> TakenBackFrom { get; } = [];
    }

    private sealed class Tally
    {
        public TimeSpan Reading;
        public TimeSpan Writing;
        public int NewLinks;
        public int NewAbsences;
        public int OnExisting;
        public int Within;
        public int Promoted;
        public int Withheld;
        public int Withdrawn;
        public int Superseded;
        public int Replaced;
        public int Attached;
        public List<string> Lines { get; } = [];
    }
}
