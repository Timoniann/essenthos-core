using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.TextusReceptus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Glaux;

/// <param name="Bridged">
/// Words whose lemma reached a number only through the Attic-to-Koine bridge — GLAUx cites *become*
/// as γίγνομαι and the New Testament dictionary as γίνομαι, and joining the two lists as written
/// silently drops the whole class.
/// </param>
/// <param name="Ambiguous">
/// Lemmas that more than one Strong entry claims. Every candidate is written, because which of them
/// is right is a question the lexicon cannot answer and the corpus should not pretend to.
/// </param>
/// <param name="Unmatched">Words whose lemma no Greek entry answers, which is most of what is left.</param>
internal sealed record SeptuagintStrongOutcome(
    bool AlreadyLoaded,
    int Words,
    int Numbered,
    int Proposals,
    int Bridged,
    int Ambiguous,
    int Unmatched,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Septuagint's Strong numbers are already proposed"
            : $"{Numbered} of {Words} words reached a number in {Elapsed} ({(double)Numbered / Words:P1}), " +
              $"{Proposals} proposals: {Bridged} through the Attic-to-Koine bridge, {Ambiguous} words " +
              $"whose lemma more than one entry claims, {Unmatched} no entry answers";
}

/// <summary>
/// Strong numbers for the Septuagint, proposed rather than stated.
///
/// **Strong never numbered the Greek Old Testament.** The series catalogues New Testament
/// vocabulary, so a number on a Brenton word is always a claim that this word is the same lexeme as
/// some New Testament word — reasoning of ours, from a lemma GLAUx gave us, through a lexicon built
/// for a different book. It is good reasoning and it is not testimony, and the difference is the
/// whole reason these rows go to <c>word_strong</c> and not to <c>word.strong_number</c>. Anything
/// in the Septuagint's own vocabulary — and there is a great deal of it — correctly reaches no
/// number at all, which is a fact about Strong and not a gap in the load.
///
/// <para>
/// The join is lemma to lemma, both folded, because the two lists are written in different
/// conventions and neither is normalised the way the other is. Where a lemma is claimed by more than
/// one entry every candidate is written: the lexicon cannot say which, so neither can we, and a
/// reader who sees two numbers has been told the truth. That is what the table is for.
/// </para>
///
/// <para>
/// Confidence says what the reasoning was worth, not how common the word is. A lemma one entry
/// claims is the ordinary case and sits high; a lemma several claim is divided between them; a lemma
/// reached only by rewriting its citation form sits lower again, because the bridge is a rule about
/// two conventions rather than a fact about this word.
/// </para>
/// </summary>
internal sealed class SeptuagintStrongLoader(AppDbContext db, ILogger<SeptuagintStrongLoader> logger)
{
    /// <summary>A lemma one Greek entry claims, matched as GLAUx wrote it.</summary>
    internal const double Single = 0.9;

    /// <summary>
    /// A lemma reached by rewriting the citation form from one convention to the other. Below a
    /// direct match because the rule is about Attic and Koine practice, not about this word.
    /// </summary>
    internal const double BridgedConfidence = 0.75;

    /// <summary>
    /// A lemma several entries claim, divided between them: two candidates are 0.45 each. Below
    /// anything a single match earns however few the candidates, which is the point.
    /// </summary>
    internal static double Shared(int candidates) => Single / candidates;

    private const string Source =
        "the lemma GLAUx gives the word, matched against Strong's Greek entries by their own lemma";

    private const string Import =
        """
        COPY word_strong (word_id, number, method, confidence, source, note)
        FROM STDIN (FORMAT BINARY)
        """;

    public async Task<SeptuagintStrongOutcome> Load(CancellationToken cancellationToken = default)
    {
        var text = await db.Texts
            .Where(t => t.Slug == GlauxLemmaLoader.Septuagint)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (text == 0)
        {
            return new SeptuagintStrongOutcome(true, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (await db.WordStrongs.AnyAsync(w => w.Word!.TextId == text, cancellationToken))
        {
            logger.LogInformation("The Septuagint's Strong numbers are already proposed; nothing to do");
            return new SeptuagintStrongOutcome(true, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var entries = await Entries(cancellationToken);

        var words = await db.Words
            .Where(word => word.TextId == text && word.Lemma != null)
            .Select(word => new { word.Id, word.Lemma })
            .ToListAsync(cancellationToken);

        var proposals = new List<Proposal>(words.Count);
        int numbered = 0, bridged = 0, ambiguous = 0, unmatched = 0;

        foreach (var word in words)
        {
            var folded = GreekLetters.Bare(word.Lemma!);
            var (numbers, viaBridge) = Numbers(entries, folded);

            if (numbers.Count == 0)
            {
                unmatched++;
                continue;
            }

            numbered++;
            if (viaBridge)
            {
                bridged++;
            }

            if (numbers.Count > 1)
            {
                ambiguous++;
            }

            var confidence = viaBridge ? BridgedConfidence : Shared(numbers.Count);
            foreach (var number in numbers)
            {
                proposals.Add(new Proposal(word.Id, number, confidence, word.Lemma!));
            }
        }

        await Write(proposals, cancellationToken);

        var outcome = new SeptuagintStrongOutcome(
            false, words.Count, numbered, proposals.Count, bridged, ambiguous, unmatched,
            started.Elapsed);
        logger.LogInformation("Proposed Strong numbers for the Septuagint: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The numbers a folded lemma reaches, and whether it took the bridge to get there. The
    /// unchanged lemma always wins: the bridge is consulted only where the lexicon has nothing,
    /// so a rule can never displace a match GLAUx and Strong already agree on.
    /// </summary>
    internal static (List<string> Numbers, bool Bridged) Numbers(
        Dictionary<string, List<string>> entries,
        string folded)
    {
        if (entries.TryGetValue(folded, out var direct))
        {
            return (direct, false);
        }

        foreach (var candidate in GreekLemmaBridge.Candidates(folded))
        {
            if (entries.TryGetValue(candidate, out var found))
            {
                return (found, true);
            }
        }

        return ([], false);
    }

    /// <summary>
    /// Strong's Greek entries by their folded lemma. Hebrew entries are excluded rather than left to
    /// miss on their own: a Hebrew lemma folded by a Greek folder is not a Greek word, and a
    /// collision between the two would be silent and wrong in the worst possible way.
    /// </summary>
    private async Task<Dictionary<string, List<string>>> Entries(CancellationToken cancellationToken)
    {
        var rows = await db.StrongEntries
            .Where(entry => entry.Lemma != null && entry.StrongNumber.StartsWith("G"))
            .Select(entry => new { entry.StrongNumber, entry.Lemma })
            .ToListAsync(cancellationToken);

        var entries = new Dictionary<string, List<string>>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var folded = GreekLetters.Bare(row.Lemma!);
            if (folded.Length == 0)
            {
                continue;
            }

            if (!entries.TryGetValue(folded, out var numbers))
            {
                numbers = [];
                entries[folded] = numbers;
            }

            if (!numbers.Contains(row.StrongNumber, StringComparer.Ordinal))
            {
                numbers.Add(row.StrongNumber);
            }
        }

        return entries;
    }

    private async Task Write(List<Proposal> proposals, CancellationToken cancellationToken)
    {
        if (proposals.Count == 0)
        {
            return;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var method = EnumSpelling.Of(LinkMethod.Lexical);

        await using (var writer = await connection.BeginBinaryImportAsync(Import, cancellationToken))
        {
            foreach (var proposal in proposals)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(proposal.WordId, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(proposal.Number, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(proposal.Confidence, NpgsqlDbType.Double, cancellationToken);
                await writer.WriteAsync(Source, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync($"through the lemma {proposal.Lemma}", NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record Proposal(long WordId, string Number, double Confidence, string Lemma);
}
