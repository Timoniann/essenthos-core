using System.Globalization;
using System.Text;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Chinese Union Version's Old Testament linked to BHSA by the Open Hebrew Bible's mapping
/// (<see cref="OpenHebrewCuvMapping"/>): Eliran Wong's statement of which BHS word each of FHL's
/// spans renders. Where FHL's numbers only say which lexeme a span renders, and the match within the
/// verse has to guess which occurrence, the mapping names the word.
///
/// <para>
/// **Its links are <c>stated-by-source</c>**, as the King James's from the same project are, and
/// carry no confidence. Where a link of the pair already names exactly the same words, the mapping
/// is a claim on it instead of a second link, which is what makes the agreement countable.
/// </para>
///
/// <para>
/// **The file's Chinese is laid onto ours by its characters.** The spans are FHL's and so are the
/// words the corpus loaded from CrossWire's module, so in all but about a hundred verses the two
/// print the same letters in the same order and a span is the words its letters fall in. Where they
/// differ — the file prints FHL's notes inside the verse, the module beside it — the letters are
/// lined up first and only the ones both print carry a span over. The simplified text is FHL's
/// conversion of the same spans, so it takes the traditional text's words by their place in the
/// verse, wherever the two hold as many words.
/// </para>
/// </summary>
internal static class OpenHebrewCuvLinks
{
    public const string Credit = Sources.OpenHebrewCuvCredit;

    public static readonly string[] File = ["OpenHebrewBible", "CUV-OT-mapped-to-BHS.csv"];

    /// <param name="Words">Indexes into the verse's words, in order.</param>
    /// <param name="Positions">The running numbers the spans name.</param>
    internal sealed record Placement(IReadOnlyList<int> Words, IReadOnlyList<int> Positions);

    /// <summary>
    /// The spans of one verse laid onto its words: each span becomes the words its letters fall in,
    /// and spans sharing a word are one placement, since a word can be one rendering only once.
    /// </summary>
    /// <param name="realigned">Whether the two printed different letters and had to be lined up.</param>
    internal static List<Placement> Place(IReadOnlyList<OhbRun> runs, IReadOnlyList<string> words, out bool realigned)
    {
        var (fileLetters, fileOwners) = Letters(runs.Select(run => run.Text));
        var (ourLetters, ourOwners) = Letters(words);
        var spanWords = runs.Select(_ => new SortedSet<int>()).ToArray();

        realigned = fileLetters != ourLetters;
        foreach (var (file, ours) in realigned ? Lined(fileLetters, ourLetters) : Same(fileLetters.Length))
        {
            spanWords[fileOwners[file]].Add(ourOwners[ours]);
        }

        var parent = Enumerable.Range(0, runs.Count).ToArray();
        int Find(int run) => parent[run] == run ? run : parent[run] = Find(parent[run]);
        var claimedBy = new Dictionary<int, int>();
        for (var run = 0; run < runs.Count; run++)
        {
            if (runs[run].Positions.Count == 0)
            {
                continue;
            }

            foreach (var word in spanWords[run])
            {
                if (claimedBy.TryGetValue(word, out var other))
                {
                    parent[Find(run)] = Find(other);
                }
                else
                {
                    claimedBy[word] = run;
                }
            }
        }

        return
        [
            .. Enumerable.Range(0, runs.Count)
                .Where(run => runs[run].Positions.Count > 0)
                .GroupBy(Find)
                .Select(group => new Placement(
                    [.. group.SelectMany(run => spanWords[run]).Distinct().Order()],
                    [.. group.SelectMany(run => runs[run].Positions).Distinct()])),
        ];
    }

    private static (string Letters, List<int> Owners) Letters(IEnumerable<string> pieces)
    {
        var letters = new StringBuilder();
        var owners = new List<int>();
        var index = 0;
        foreach (var piece in pieces)
        {
            foreach (var rune in piece.EnumerateRunes())
            {
                if (Rune.IsLetterOrDigit(rune))
                {
                    letters.Append(rune.ToString());
                    for (var unit = 0; unit < rune.Utf16SequenceLength; unit++)
                    {
                        owners.Add(index);
                    }
                }
            }

            index++;
        }

        return (letters.ToString(), owners);
    }

    private static IEnumerable<(int, int)> Same(int length) => Enumerable.Range(0, length).Select(i => (i, i));

    /// <summary>The longest common run of letters, as pairs of positions in each string.</summary>
    private static List<(int, int)> Lined(string file, string ours)
    {
        var longest = new int[file.Length + 1, ours.Length + 1];
        for (var f = file.Length - 1; f >= 0; f--)
        {
            for (var o = ours.Length - 1; o >= 0; o--)
            {
                longest[f, o] = file[f] == ours[o]
                    ? longest[f + 1, o + 1] + 1
                    : Math.Max(longest[f + 1, o], longest[f, o + 1]);
            }
        }

        var pairs = new List<(int, int)>(Math.Min(file.Length, ours.Length));
        for (int f = 0, o = 0; f < file.Length && o < ours.Length;)
        {
            if (file[f] == ours[o])
            {
                pairs.Add((f, o));
                f++;
                o++;
            }
            else if (longest[f + 1, o] >= longest[f, o + 1])
            {
                f++;
            }
            else
            {
                o++;
            }
        }

        return pairs;
    }
}
