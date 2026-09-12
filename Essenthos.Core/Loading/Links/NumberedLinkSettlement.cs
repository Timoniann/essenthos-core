namespace Essenthos.Core.Loading.Links;

/// <summary>What a Strong-number match says about one link another method already drew.</summary>
internal enum SettledVerdict
{
    /// <summary>A match names the same words, so the guess becomes a second claim on it.</summary>
    Confirmed,

    /// <summary>
    /// The numbers give this translated word to other witness words, and give the witness word to
    /// other translated words. The match outranks the guess, which is removed.
    /// </summary>
    Contradicted,

    /// <summary>
    /// The numbers tag this translated word and say nothing of the witness word — an article, a
    /// preposition, a particle no number is written for. Nothing contradicts the guess, and it stays.
    /// </summary>
    Beside,

    /// <summary>The numbers do not tag this translated word at all, so they have nothing to say.</summary>
    Untouched,
}

/// <param name="Link">The link the other method drew.</param>
/// <param name="From">Its translated words.</param>
/// <param name="To">Its witness words.</param>
internal sealed record DrawnLink(long Link, IReadOnlyList<long> From, IReadOnlyList<long> To);

/// <param name="Draft">
/// For a confirmed link, the match it is folded into, by its index among the matches; -1 otherwise.
/// </param>
/// <param name="Exact">Whether that match names exactly the same words, rather than more of them.</param>
internal readonly record struct Settled(long Link, SettledVerdict Verdict, int Draft, bool Exact);

/// <summary>
/// Where a translation already has links from the aligner, or from a source, and Strong numbers
/// now reach the same witness: which of the two answers stands for each word.
///
/// <para>
/// **The standing is the one <c>ClaimStanding</c> states**, applied to a pair of texts rather than
/// to a card. A source's statement outranks a number, so a match never overwrites testimony: where
/// a source already says what a word renders, the match is not written, and where it names the same
/// words it is recorded as a second claim on the source's link. A number outranks the aligner, so
/// where the two name the same words the aligner's link becomes a claim on the match, and where the
/// numbers give both of its words elsewhere it is removed. Two links about one word pair would read
/// as contended when nothing is, and a guess the numbers refute would keep lighting the wrong word.
/// </para>
///
/// <para>
/// **A guess is only contradicted where the numbers speak to both of its ends.** Russian renders a
/// Hebrew preposition with a case ending and the Greek article with nothing, and no Strong number
/// is written over either — so the aligner joining <em>землю</em> to the article as well as to אֶרֶץ
/// adds something the numbers are silent about, and stays. It is removed only when the witness word
/// is one some other translated word's number reaches: then the numbers name a different rendering
/// for it, and the method that read a printed number outranks the one that learned from the text.
/// </para>
/// </summary>
internal static class NumberedLinkSettlement
{
    /// <param name="matches">The Strong-number matches, by index.</param>
    /// <param name="drawn">The other method's links between the same two texts.</param>
    public static List<Settled> Settle(
        IReadOnlyList<(IReadOnlyList<long> From, IReadOnlyList<long> To)> matches,
        IReadOnlyList<DrawnLink> drawn)
    {
        var byTranslated = new Dictionary<long, List<int>>(matches.Count * 2);
        var reached = new HashSet<long>(matches.Count * 2);
        for (var i = 0; i < matches.Count; i++)
        {
            foreach (var word in matches[i].From)
            {
                if (!byTranslated.TryGetValue(word, out var holding))
                {
                    holding = [];
                    byTranslated[word] = holding;
                }

                holding.Add(i);
            }

            reached.UnionWith(matches[i].To);
        }

        var settled = new List<Settled>(drawn.Count);
        foreach (var link in drawn)
        {
            settled.Add(Verdict(matches, byTranslated, reached, link));
        }

        return settled;
    }

    private static Settled Verdict(
        IReadOnlyList<(IReadOnlyList<long> From, IReadOnlyList<long> To)> matches,
        Dictionary<long, List<int>> byTranslated,
        HashSet<long> reached,
        DrawnLink link)
    {
        if (!link.From.All(byTranslated.ContainsKey))
        {
            return new Settled(link.Link, SettledVerdict.Untouched, -1, false);
        }

        var candidates = link.From.SelectMany(word => byTranslated[word]).Distinct();
        foreach (var at in candidates)
        {
            var (from, to) = matches[at];
            if (link.From.All(from.Contains) && link.To.All(to.Contains))
            {
                var exact = from.Count == link.From.Count && to.Count == link.To.Count;
                return new Settled(link.Link, SettledVerdict.Confirmed, at, exact);
            }
        }

        return link.To.Count > 0 && link.To.All(reached.Contains)
            ? new Settled(link.Link, SettledVerdict.Contradicted, -1, false)
            : new Settled(link.Link, SettledVerdict.Beside, -1, false);
    }
}
