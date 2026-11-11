using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.TextusReceptus;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Stephanus 1550 against Scrivener 1894, from the file that holds both.
///
/// Nothing is aligned here and nothing is guessed. Robinson's composite is one token stream with a
/// choice at 261 places, so the two editions are the same words everywhere else — the file itself
/// says which word corresponds to which, and where it offers a choice it says that too. Every link
/// carries <c>stated-by-source</c> and no confidence.
///
/// The 52 groups where one side is empty are the interesting ones. They are written with words on
/// one side and nothing on the other, which is what the link table was built to be able to say:
/// an absence recorded rather than a hole left. Which edition lacks the word is the relation's
/// to carry — <c>omits</c> where Stephanus does, <c>expands</c> where Scrivener does.
/// </summary>
internal static class PrintedEditionLinks
{
    internal const string Source = "byztxt/greektext-textus-receptus, the variant groups of the composite";

    /// <summary>
    /// Lays the two readings of one verse against each other by the segment each word came from.
    /// A segment is one word outside a variant group and one group inside it, counted the same way
    /// on both sides, so this needs no alignment and admits no guess.
    /// </summary>
    internal static void Pair(
        UtrVerse first,
        UtrVerse second,
        List<long> left,
        List<long> right,
        List<EditionDraft> drafts)
    {
        var bySegment = first.Words
            .Select((word, at) => (word, at))
            .GroupBy(entry => entry.word.Segment)
            .ToDictionary(group => group.Key, group => group.ToList());

        var otherBySegment = second.Words
            .Select((word, at) => (word, at))
            .GroupBy(entry => entry.word.Segment)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var segment in bySegment.Keys.Union(otherBySegment.Keys).Order())
        {
            var here = bySegment.GetValueOrDefault(segment, []);
            var there = otherBySegment.GetValueOrDefault(segment, []);

            // A segment present on one side only is a word one edition prints and the other does
            // not. Stored positively, which is what turns a difference between editions from a gap
            // into something a reader can be shown — and which way round it stands is the relation's
            // to say, since a row with one empty side cannot say it on its own.
            var relation = here.Count == 0
                ? LinkRelation.Omits
                : there.Count == 0
                    ? LinkRelation.Expands
                    : Same(here, there) ? LinkRelation.Equals : LinkRelation.Renders;

            drafts.Add(new EditionDraft(
                relation,
                [.. here.Select(entry => left[entry.at])],
                [.. there.Select(entry => right[entry.at])]));
        }
    }

    private static bool Same(
        List<(UtrWord word, int at)> here,
        List<(UtrWord word, int at)> there) =>
        here.Count == there.Count
        && here.Select(entry => entry.word.Surface).SequenceEqual(there.Select(entry => entry.word.Surface));

    internal sealed record EditionDraft(LinkRelation Relation, List<long> From, List<long> To);
}
