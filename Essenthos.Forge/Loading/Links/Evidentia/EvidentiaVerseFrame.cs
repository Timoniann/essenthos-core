namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Where in the original a word of the translation is expected, read from the words the joint assignment
/// has already placed: between the renderings of its nearest placed neighbours on either side, or along
/// the verse's diagonal past the last of them. The passes that place a word by the one free candidate
/// its verse leaves - a residual rendering, a dictionary sense, a gloss, a counterpart - do not look at
/// position at all, and their worst mistakes stand at the other end of the verse from where the
/// sentence puts the word.
///
/// Only the anchors that keep their order are read, as in the assignment's own frame, so one misplaced
/// neighbour does not move every expectation after it.
/// </summary>
internal sealed class EvidentiaVerseFrame
{
    /// <summary>
    /// How far, as a share of the original verse, a placement may stand from where its neighbours put it.
    /// Past this the placements of these passes were right about half the time on the benchmark passages,
    /// where the passes average above nine in ten.
    /// </summary>
    public const double MaximumDistance = 0.6;

    private readonly Dictionary<EvidentiaAddress, Verse> verses;

    private EvidentiaVerseFrame(Dictionary<EvidentiaAddress, Verse> verses) => this.verses = verses;

    public static EvidentiaVerseFrame Of(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IEnumerable<EvidentiaProposal> anchors)
    {
        var sourceExtent = source.GroupBy(analysis => analysis.Token.Address)
            .ToDictionary(verse => verse.Key, verse => verse.Max(analysis => analysis.Token.Position));
        var targetExtent = target.GroupBy(analysis => analysis.Token.Address)
            .ToDictionary(verse => verse.Key, verse => verse.Max(analysis => analysis.Token.Position));
        var anchorsByVerse = anchors
            .Where(proposal => proposal.Source.Token.Address == proposal.Target.Token.Address)
            .GroupBy(proposal => proposal.Source.Token.Address)
            .ToDictionary(verse => verse.Key, verse => InOrder([
                .. verse.Select(proposal => (Source: proposal.Source.Token.Position, Target: proposal.Target.Token.Position))
                    .Distinct()
                    .OrderBy(anchor => anchor.Source),
            ]));
        var verses = new Dictionary<EvidentiaAddress, Verse>();
        foreach (var (address, sourceCount) in sourceExtent)
        {
            if (targetExtent.TryGetValue(address, out var targetCount))
            {
                verses[address] = new Verse(anchorsByVerse.GetValueOrDefault(address) ?? [], sourceCount, targetCount);
            }
        }

        return new EvidentiaVerseFrame(verses);
    }

    /// <summary>How far the placement stands from where its placed neighbours put it, as a share of the original verse.</summary>
    public double Distance(EvidentiaProposal proposal) =>
        proposal.Source.Token.Address == proposal.Target.Token.Address
        && verses.TryGetValue(proposal.Source.Token.Address, out var verse)
            ? verse.Distance(proposal.Source.Token.Position, proposal.Target.Token.Position)
            : 0;

    /// <summary>The same for a pair nothing has proposed yet.</summary>
    public double Distance(EvidentiaAnalysis source, EvidentiaAnalysis target) =>
        source.Token.Address == target.Token.Address && verses.TryGetValue(source.Token.Address, out var verse)
            ? verse.Distance(source.Token.Position, target.Token.Position)
            : 0;

    public IReadOnlyList<EvidentiaProposal> Near(IEnumerable<EvidentiaProposal> proposals) =>
        [.. proposals.Where(proposal => Distance(proposal) < MaximumDistance)];

    public EvidentiaResolution Near(EvidentiaResolution resolution) =>
        resolution with { Proposals = Near(resolution.Proposals) };

    /// <summary>
    /// The longest run of anchors whose renderings keep their order: a single word whose only rendering
    /// stands at the other end of the verse would otherwise pull every neighbour after it.
    /// </summary>
    internal static List<(int Source, int Target)> InOrder(List<(int Source, int Target)> anchors)
    {
        if (anchors.Count < 3)
        {
            return anchors;
        }

        var length = new int[anchors.Count];
        var previous = new int[anchors.Count];
        var best = 0;
        for (var i = 0; i < anchors.Count; i++)
        {
            length[i] = 1;
            previous[i] = -1;
            for (var j = 0; j < i; j++)
            {
                if (anchors[j].Source < anchors[i].Source && anchors[j].Target < anchors[i].Target && length[j] + 1 > length[i])
                {
                    length[i] = length[j] + 1;
                    previous[i] = j;
                }
            }

            if (length[i] > length[best])
            {
                best = i;
            }
        }

        var chain = new List<(int Source, int Target)>();
        for (var i = best; i >= 0; i = previous[i])
        {
            chain.Add(anchors[i]);
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>Where a word at <paramref name="source"/> is expected between the anchors on either side of it.</summary>
    internal static double Expected(IReadOnlyList<(int Source, int Target)> anchors, int source, double slope)
    {
        var after = -1;
        for (var i = 0; i < anchors.Count; i++)
        {
            if (anchors[i].Source > source)
            {
                after = i;
                break;
            }
        }

        var before = (after < 0 ? anchors.Count : after) - 1;
        while (before >= 0 && anchors[before].Source >= source)
        {
            before--;
        }

        return (before >= 0, after >= 0) switch
        {
            (true, true) => anchors[before].Target + (source - anchors[before].Source)
                * (double)(anchors[after].Target - anchors[before].Target) / (anchors[after].Source - anchors[before].Source),
            (true, false) => anchors[before].Target + (source - anchors[before].Source) * slope,
            (false, true) => anchors[after].Target - (anchors[after].Source - source) * slope,
            _ => 1 + (source - 1) * slope,
        };
    }

    private sealed class Verse(List<(int Source, int Target)> anchors, int sourceCount, int targetCount)
    {
        private readonly double slope = sourceCount <= 1 ? 1 : (double)(targetCount - 1) / (sourceCount - 1);

        public double Distance(int source, int target) =>
            Math.Abs(target - Expected(anchors, source, slope)) / Math.Max(1, targetCount - 1);
    }
}
