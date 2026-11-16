using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading.Links;

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
internal static class SamaritanLinks
{
    internal const string Source =
        "the consonants both Hebrew witnesses write, aligned within each verse";

    /// <summary>
    /// Verses the Masoretic numbers and the Samaritan does not, looked for among the words the
    /// Samaritan has and the Masoretic has not in the same book. Where one verse's surplus holds
    /// most of such a run, the surplus is aligned against the run instead of standing as a plus with
    /// no matching minus: in place where it is the verse just before the run, and as
    /// <c>transposes</c> where it is anywhere else, which is Exodus 26:35 holding the altar of incense.
    /// <para>
    /// "Most" is counted in words of two letters or more. A one-letter word (the conjunction, the
    /// article, a preposition) pairs with its like in any passage, so a long surplus elsewhere could
    /// hold half a run on those alone: the Gerizim command the Samaritan writes in Deuteronomy 5:21
    /// matched Deuteronomy 34:2-3 on <c>ו</c>, <c>ה</c> and <c>את</c>. The Samaritan's 34:1 does not
    /// hold 34:2-3 either: it names other bounds, and shares only a word or two with them.
    /// </para>
    /// </summary>
    internal static (int Placed, int Moved, List<(int Book, int Expanded, int Omitted)> Changed) Relocate(
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
            var content = runWords.Where(Counts).Select(w => w.Id).ToHashSet();
            ((int Book, int Chapter, int Verse) Host, List<HebrewDraft> Pairs, int Matched)? best = null;

            foreach (var (host, surplus) in expandedAt.Where(entry => entry.Key.Book == run[0].Book))
            {
                var ids = surplus.SelectMany(d => d.From).ToHashSet();
                var trial = new List<HebrewDraft>();
                Pair([.. here[host].Where(w => ids.Contains(w.Id))], runWords, trial);
                var matched = trial
                    .Where(d => d.Relation is LinkRelation.Equals or LinkRelation.Renders)
                    .Sum(d => d.To.Count(content.Contains));
                if (best is null || matched > best.Value.Matched)
                {
                    best = (host, trial, matched);
                }
            }

            if (best is not { } found || found.Matched * MostOfTheRun < content.Count)
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

    /// <summary>The shortest word a run is counted in.</summary>
    private const int ShortestCounted = 2;

    private static bool Counts(HebrewWord word) => word.Form.Letters.Length >= ShortestCounted;

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
    internal static void Pair(List<HebrewWord> left, List<HebrewWord> right, List<HebrewDraft> drafts)
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

    internal sealed record HebrewWord(long Id, WitnessForm Form);

    internal sealed record HebrewDraft(
        LinkRelation Relation,
        List<long> From,
        List<long> To,
        double Confidence);
}
