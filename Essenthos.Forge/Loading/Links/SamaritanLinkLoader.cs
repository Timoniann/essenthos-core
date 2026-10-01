using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Expanded">
/// Words the Samaritan has and the Masoretic has not. This and <paramref name="Omitted"/> are the
/// numbers this text was loaded for: the corpus has never before been able to say, in Hebrew, that
/// one witness carries a word another does not.
/// </param>
/// <param name="Omitted">Words the Masoretic has and the Samaritan has not.</param>
/// <param name="Unpaired">
/// Verses one text numbers and the other does not — the altar of incense, which the Samaritan sets
/// after Exodus 26:35 rather than at Exodus 30, and Deuteronomy 34:2-3, which it writes as part of
/// 34:1. No link is written for either: the words are not missing, they are somewhere else, and an
/// <c>omits</c> there would be a false statement rather than an incomplete one.
/// </param>
internal sealed record SamaritanLinkOutcome(
    bool AlreadyLoaded,
    int Verses,
    int Links,
    int Identical,
    int Differing,
    int Expanded,
    int Omitted,
    int Unpaired,
    IReadOnlyList<(string Book, int Expanded, int Omitted)> ByBook,
    TimeSpan Elapsed)
{
    /// <summary>
    /// Words the Samaritan writes somewhere other than where the Masoretic does, paired with the
    /// Masoretic words where they stand there — the altar of incense, which is Exodus 30:1-10 in one
    /// and part of 26:35 in the other.
    /// </summary>
    public int Transposed { get; init; }

    public override string ToString() =>
        AlreadyLoaded
            ? "the Samaritan Pentateuch is already linked to BHSA"
            : $"{Links} links over {Verses} verses in {Elapsed}: {Identical} where the two witnesses write the " +
              $"same consonants, {Differing} where they write the same word differently, {Expanded} the " +
              $"Samaritan has and the Masoretic has not, {Omitted} the Masoretic has and the Samaritan has " +
              $"not, {Transposed} it writes elsewhere, over {Unpaired} verses one numbers and the other does " +
              "not. Plus and minus per book: " +
              string.Join("; ", ByBook.Select(b => $"{b.Book} +{b.Expanded} -{b.Omitted}"));
}

/// <summary>
/// The Samaritan Pentateuch against BHSA, word for word.
///
/// Nobody states this correspondence — there is no Samaritan-to-Masoretic word mapping anywhere —
/// so every link here is an inference, says <c>lexical</c>, and carries a confidence. What makes
/// the inference cheap is that both datasets come out of the same ETCBC encoding practice and cut
/// words into the same morphemes: <c>בראשית</c> is <c>ב</c> then <c>ראשית</c> on both sides. So the
/// two verses can be laid against each other letter for letter rather than guessed at.
///
/// <para>
/// The pairing is an alignment over the consonants of one verse against the consonants of the same
/// verse in the other text, and its scoring is the whole of the honesty here. Two words pair when
/// they are the same consonants, or when they are the same lexeme, or when they differ by a letter
/// or two — which is the Samaritan writing plene where the Masoretic writes defective, and is by
/// far the commonest difference between them. Two words that are none of those never pair: the
/// score for it is set below the cost of leaving both unpaired, so the alignment prefers to say
/// <em>this one has a word the other has not, twice</em> over inventing a correspondence.
/// </para>
///
/// <para>
/// Which side lacks the word is the relation's to carry, and a link with one empty side cannot say
/// it for itself: <c>expands</c> names words on the <c>from</c> side alone, which is the Samaritan,
/// and <c>omits</c> names words on the <c>to</c> side alone, which is BHSA.
/// </para>
/// </summary>
internal sealed class SamaritanLinkLoader(AppDbContext db, ILogger<SamaritanLinkLoader> logger)
{
    private const string Source =
        "the consonants both Hebrew witnesses write, aligned within each verse";

    public async Task<SamaritanLinkOutcome> Load(
        string fromSlug,
        string toSlug,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        if (from.Versification != to.Versification || from.Versification == Versification.Unknown)
        {
            throw new InvalidOperationException(
                $"\"{fromSlug}\" numbers its verses as {from.Versification} and \"{toSlug}\" as " +
                $"{to.Versification}. This joins the two on the numbering they both use, so it needs both " +
                "to say what that numbering is and needs it to be the same one; a pair that follows two " +
                "schemes needs a loader that reads verse_reference instead.");
        }

        if (await db.Links.AnyAsync(l => l.FromTextId == from.Id && l.ToTextId == to.Id, cancellationToken))
        {
            logger.LogInformation("{From} and {To} are already linked; nothing to do", fromSlug, toSlug);
            return new SamaritanLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var here = await Words(from.Id, cancellationToken);
        var there = await Words(to.Id, cancellationToken);

        var drafts = new List<HebrewDraft>(130_000);
        var byBook = new SortedDictionary<int, (int Expanded, int Omitted)>();
        var verses = 0;
        var unpaired = 0;
        var expandedAt = new Dictionary<(int Book, int Chapter, int Verse), List<HebrewDraft>>();

        foreach (var (address, left) in here.OrderBy(entry => entry.Key.Book)
                     .ThenBy(entry => entry.Key.Chapter).ThenBy(entry => entry.Key.Verse))
        {
            if (!there.TryGetValue(address, out var right))
            {
                unpaired++;
                continue;
            }

            verses++;
            var before = drafts.Count;
            Pair(left, right, drafts);
            var added = drafts.Skip(before).Where(d => d.Relation == LinkRelation.Expands).ToList();
            if (added.Count > 0)
            {
                expandedAt[address] = added;
            }

            var expanded = 0;
            var omitted = 0;
            for (var i = before; i < drafts.Count; i++)
            {
                expanded += drafts[i].Relation == LinkRelation.Expands ? 1 : 0;
                omitted += drafts[i].Relation == LinkRelation.Omits ? 1 : 0;
            }

            var counted = byBook.GetValueOrDefault(address.Book);
            byBook[address.Book] = (counted.Expanded + expanded, counted.Omitted + omitted);
        }

        unpaired += there.Keys.Count(address => !here.ContainsKey(address));
        var (placed, moved, changed) = Relocate(here, there, expandedAt, drafts);
        unpaired -= placed;
        foreach (var (book, expanded, omitted) in changed)
        {
            var counted = byBook.GetValueOrDefault(book);
            byBook[book] = (counted.Expanded + expanded, counted.Omitted + omitted);
        }

        await Write(from.Id, to.Id, drafts, cancellationToken);

        var outcome = new SamaritanLinkOutcome(
            false,
            verses,
            drafts.Count,
            drafts.Count(d => d.Relation == LinkRelation.Equals),
            drafts.Count(d => d.Relation == LinkRelation.Renders),
            drafts.Count(d => d.Relation == LinkRelation.Expands),
            drafts.Count(d => d.Relation == LinkRelation.Omits),
            unpaired,
            [.. byBook.Select(entry => (
                BibleBookAbbreviation.GetByOrdinal(entry.Key)?.FullName.Full ?? entry.Key.ToString(),
                entry.Value.Expanded,
                entry.Value.Omitted))],
            started.Elapsed)
        {
            Transposed = moved,
        };

        logger.LogInformation("Linked {From} to {To}: {Outcome}", fromSlug, toSlug, outcome);
        return outcome;
    }

    /// <summary>
    /// Verses the Masoretic numbers and the Samaritan does not, looked for among the words the
    /// Samaritan has and the Masoretic has not in the same book. Where one verse's surplus holds
    /// most of such a run, the surplus is aligned against the run instead of standing as a plus with
    /// no matching minus: in place where it is the verse just before the run — Deuteronomy 34:1,
    /// which the Samaritan writes with 34:2-3 inside it — and as <c>transposes</c> where it is
    /// anywhere else, which is Exodus 26:35 holding the altar of incense.
    /// </summary>
    private static (int Placed, int Moved, List<(int Book, int Expanded, int Omitted)> Changed) Relocate(
        Dictionary<(int Book, int Chapter, int Verse), List<HebrewWord>> here,
        Dictionary<(int Book, int Chapter, int Verse), List<HebrewWord>> there,
        Dictionary<(int Book, int Chapter, int Verse), List<HebrewDraft>> expandedAt,
        List<HebrewDraft> drafts)
    {
        var changed = new List<(int Book, int Expanded, int Omitted)>();
        var placed = 0;
        var moved = 0;
        var replaced = new HashSet<HebrewDraft>(ReferenceEqualityComparer.Instance);

        foreach (var run in Runs(there.Keys.Where(address => !here.ContainsKey(address))))
        {
            var runWords = run.SelectMany(address => there[address]).ToList();
            ((int Book, int Chapter, int Verse) Host, List<HebrewDraft> Pairs, int Matched)? best = null;

            foreach (var (host, surplus) in expandedAt.Where(entry => entry.Key.Book == run[0].Book))
            {
                var ids = surplus.SelectMany(d => d.From).ToHashSet();
                var trial = new List<HebrewDraft>();
                Pair([.. here[host].Where(w => ids.Contains(w.Id))], runWords, trial);
                var matched = trial
                    .Where(d => d.Relation is LinkRelation.Equals or LinkRelation.Renders)
                    .Sum(d => d.To.Count);
                if (best is null || matched > best.Value.Matched)
                {
                    best = (host, trial, matched);
                }
            }

            if (best is not { } found || found.Matched * MostOfTheRun < runWords.Count)
            {
                continue;
            }

            var inPlace = found.Host.Chapter == run[0].Chapter && found.Host.Verse == run[0].Verse - 1;
            var surplusDrafts = expandedAt[found.Host];
            replaced.UnionWith(surplusDrafts);
            expandedAt.Remove(found.Host);

            foreach (var draft in found.Pairs)
            {
                var corresponds = draft.Relation is LinkRelation.Equals or LinkRelation.Renders;
                drafts.Add(corresponds && !inPlace ? draft with { Relation = LinkRelation.Transposes } : draft);
                moved += corresponds && !inPlace ? draft.From.Count : 0;
            }

            placed += run.Count;
            changed.Add((
                run[0].Book,
                found.Pairs.Count(d => d.Relation == LinkRelation.Expands) - surplusDrafts.Count,
                found.Pairs.Count(d => d.Relation == LinkRelation.Omits)));
        }

        drafts.RemoveAll(replaced.Contains);
        return (placed, moved, changed);
    }

    /// <summary>A match on at least half the run's words, so a stray plus elsewhere never claims it.</summary>
    private const int MostOfTheRun = 2;

    private static List<List<(int Book, int Chapter, int Verse)>> Runs(
        IEnumerable<(int Book, int Chapter, int Verse)> addresses)
    {
        var runs = new List<List<(int Book, int Chapter, int Verse)>>();
        foreach (var address in addresses.Order())
        {
            if (runs.Count > 0 && runs[^1][^1] is var last
                && last.Book == address.Book && last.Chapter == address.Chapter && last.Verse + 1 == address.Verse)
            {
                runs[^1].Add(address);
            }
            else
            {
                runs.Add([address]);
            }
        }

        return runs;
    }

    /// <summary>
    /// One verse of each witness laid against the other. The alignment decides what corresponds to
    /// what and how sure it is; this only turns its answer into rows, and the ids it hands back are
    /// positions within the two verses.
    /// </summary>
    private static void Pair(List<HebrewWord> left, List<HebrewWord> right, List<HebrewDraft> drafts)
    {
        var forms = WitnessAlignment.Pair(
            [.. left.Select(w => w.Form)], [.. right.Select(w => w.Form)]);

        foreach (var pairing in forms)
        {
            drafts.Add(new HebrewDraft(
                pairing.Relation,
                [.. pairing.From.Select(at => left[at].Id)],
                [.. pairing.To.Select(at => right[at].Id)],
                pairing.Confidence));
        }
    }

    /// <summary>
    /// Both witnesses' words, keyed by the address each gives the verse. They follow the same
    /// numbering, so this is the texts' own addresses rather than the shared frame — which is the
    /// stronger join here: the frame collapses BHSA's Numbers 25:19 and 26:1 onto one canonical
    /// address, and the two texts number that pair identically.
    /// </summary>
    private async Task<Dictionary<(int Book, int Chapter, int Verse), List<HebrewWord>>> Words(
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Words
            .Where(w => w.TextId == textId)
            .Select(w => new
            {
                Book = w.Verse!.Book!.CanonicalOrdinal,
                Chapter = w.Verse!.ChapterNumber,
                Verse = w.Verse!.Number,
                w.Id,
                w.Position,
                w.Surface,
                w.Lemma,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.Book, r.Chapter, r.Verse))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(r => r.Position)
                    .Select(r => new HebrewWord(
                        r.Id,
                        new WitnessForm(
                            HebrewLetters.Of(r.Surface), HebrewLetters.Of(r.Lemma ?? string.Empty))))
                    .ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        List<HebrewDraft> drafts,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await LinkWriter.Write(
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction(),
            [
                .. drafts.Select(draft => new NewLink(
                    fromTextId, toTextId, draft.Relation, LinkMethod.Lexical, draft.Confidence, Source, null,
                    draft.From, draft.To)),
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Database.Entities.Text> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
        ?? throw new InvalidOperationException(
            $"The text \"{slug}\" must be loaded before it can be linked. This reads its words; it does not " +
            "create them.");

    private sealed record HebrewWord(long Id, WitnessForm Form);

    private sealed record HebrewDraft(
        LinkRelation Relation,
        List<long> From,
        List<long> To,
        double Confidence);
}
