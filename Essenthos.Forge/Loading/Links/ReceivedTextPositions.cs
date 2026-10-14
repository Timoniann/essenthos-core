using System.Globalization;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading.Links;

/// <param name="Position">The place of the Greek word in its verse of the Received Text, counted from one.</param>
/// <param name="NextVerse">
/// The word stands in the verse after the one the English prints it in: Romans 1:3 ends with
/// <em>Jesus Christ our Lord</em>, which the Greek has at the end of 1:4, and the module writes
/// <c>src="13n"</c>.
/// </param>
/// <param name="Form">The Greek word as the module writes it beside the position, unaccented.</param>
internal readonly record struct StatedPosition(int Position, bool NextVerse, string Form);

/// <param name="Units">Tagged elements of the translation that state positions and were laid onto its words.</param>
/// <param name="Agreed">Of those, the ones whose every position holds the word the module names there.</param>
/// <param name="Disagreed">The ones where some position holds another word, and which say nothing.</param>
/// <param name="Unplaced">The ones whose verse the witness lacks, or whose position runs past its end.</param>
/// <param name="Absent">The ones on a word the pair already says has no counterpart.</param>
internal sealed record LaidPositions(
    IReadOnlyList<(IReadOnlyList<long> From, IReadOnlyList<long> To)> Links,
    int Units,
    int Agreed,
    int Disagreed,
    int Unplaced,
    int Absent)
{
    public double Share => Units == 0 ? 0 : (double)Agreed / Units;

    public override string ToString() =>
        $"{Units} elements stating positions: {Agreed} agree with the witness ({Share:P2}), {Disagreed} name "
        + $"another word at the place, {Unplaced} name a place the witness does not have, {Absent} stand on a "
        + "word the pair says has no counterpart";
}

/// <summary>
/// The positions CrossWire's King James modules write on each New Testament word: <c>src="4 5"</c>
/// beside <c>lemma.TR:ο lemma.TR:θεος</c> says that <em>God</em> in John 3:16 renders the fourth and
/// fifth Greek words of the verse in the KJV2003 project's Received Text. Unlike a Strong number,
/// which names a word of the dictionary, a position names the occurrence, so where a verse repeats a
/// number the position still says which of the repeated words is meant.
///
/// <para>
/// **A position is believed only where the witness agrees with the module about the word it holds.**
/// The KJV2003 project counted its own tokenisation of a Received Text, and the corpus holds Robinson's
/// Scrivener and Stephanus; wherever they divide a verse differently every position after the
/// difference points one word off. So each element's every position must hold, folded, the word
/// the module writes beside it, or the element says nothing.
/// </para>
/// </summary>
internal static class ReceivedTextPositions
{
    private const string FormPrefix = "lemma.TR:";
    private const char NextVerseMark = 'n';

    /// <summary>The vowels Greek elides at the end of a word before one beginning with a vowel.</summary>
    private const string Elidable = "αεοι";

    /// <summary>
    /// The positions and Greek forms of one element. They are written in the same order, one for
    /// one; an element where they are not (one in the whole module) or a position written some other
    /// way states nothing.
    /// </summary>
    public static IReadOnlyList<StatedPosition> Read(string? lemma, string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || lemma is null)
        {
            return [];
        }

        var forms = new List<string>(2);
        var continues = false;
        foreach (var token in lemma.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith(FormPrefix, StringComparison.Ordinal))
            {
                forms.Add(token[FormPrefix.Length..]);
                continues = true;
            }
            else if (continues && !token.Contains(':'))
            {
                // The module breaks a form with a space after zeta: lemma.TR:αφανιζ ουσιν.
                forms[^1] += token;
            }
            else
            {
                continues = false;
            }
        }

        var places = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (places.Length != forms.Count)
        {
            return [];
        }

        var positions = new StatedPosition[places.Length];
        for (var i = 0; i < places.Length; i++)
        {
            var place = places[i];
            var next = place.EndsWith(NextVerseMark);
            if (!int.TryParse(next ? place[..^1] : place, NumberStyles.None, CultureInfo.InvariantCulture, out var at)
                || at < 1)
            {
                return [];
            }

            positions[i] = new StatedPosition(at, next, forms[i]);
        }

        return positions;
    }

    /// <summary>
    /// Every element whose positions hold the words it names, as a link from the translation's words
    /// of the element to the witness's words at those positions.
    /// </summary>
    /// <param name="stated">The positions each element states, by its unit.</param>
    /// <param name="units">The unit of each translation word the module was laid onto, by word id.</param>
    /// <param name="witness">The witness's words, each verse in order, by its canonical address.</param>
    /// <param name="absent">Words of either text the pair already says have no counterpart.</param>
    public static LaidPositions Lay(
        IReadOnlyDictionary<int, StatedPositions> stated,
        IReadOnlyDictionary<long, int> units,
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), IReadOnlyList<CorpusWord>> witness,
        IReadOnlySet<long>? absent = null)
    {
        var links = new List<(IReadOnlyList<long>, IReadOnlyList<long>)>(stated.Count);
        int count = 0, agreed = 0, disagreed = 0, unplaced = 0, absentCount = 0;

        foreach (var element in units.GroupBy(word => word.Value).OrderBy(element => element.Key))
        {
            if (!stated.TryGetValue(element.Key, out var positions))
            {
                continue;
            }

            count++;
            var from = element.Select(word => word.Key).Order().ToList();
            var to = new List<long>(positions.Positions.Count);
            var verdict = Verdict.Agrees;
            foreach (var position in positions.Positions)
            {
                var address = (positions.Book, positions.Chapter, positions.Verse);
                if (position.NextVerse)
                {
                    address = Next(witness, address);
                }

                if (!witness.TryGetValue(address, out var words) || position.Position > words.Count)
                {
                    verdict = Verdict.Unplaced;
                    break;
                }

                var word = words[position.Position - 1];
                if (!Agrees(word.Text, position.Form))
                {
                    verdict = Verdict.Disagrees;
                    break;
                }

                to.Add(word.Id);
            }

            switch (verdict)
            {
                case Verdict.Unplaced:
                    unplaced++;
                    continue;
                case Verdict.Disagrees:
                    disagreed++;
                    continue;
            }

            if (absent is not null && (from.Any(absent.Contains) || to.Any(absent.Contains)))
            {
                absentCount++;
                continue;
            }

            agreed++;
            links.Add((from, to.Distinct().ToList()));
        }

        return new LaidPositions(links, count, agreed, disagreed, unplaced, absentCount);
    }

    /// <summary>
    /// Whether the witness writes the word the module names: the same letters, accents aside, or the
    /// same word with its last vowel elided in one of them — ἀλλ᾽ for ἀλλά, ἐπ᾽ for ἐπί.
    /// </summary>
    public static bool Agrees(string witness, string form)
    {
        var (left, right) = (GreekLetters.Bare(witness), GreekLetters.Bare(form));
        if (left == right)
        {
            return true;
        }

        var (shorter, longer) = left.Length < right.Length ? (left, right) : (right, left);
        return shorter.Length >= 2
               && longer.Length == shorter.Length + 1
               && longer.StartsWith(shorter, StringComparison.Ordinal)
               && Elidable.Contains(longer[^1]);
    }

    /// <summary>The verse after one, in the next chapter when the witness has no further verse in this one.</summary>
    private static (int, int, int) Next(
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), IReadOnlyList<CorpusWord>> witness,
        (int Book, int Chapter, int Verse) address) =>
        witness.ContainsKey((address.Book, address.Chapter, address.Verse + 1))
            ? (address.Book, address.Chapter, address.Verse + 1)
            : (address.Book, address.Chapter + 1, 1);

    private enum Verdict
    {
        Agrees,
        Disagrees,
        Unplaced,
    }
}
