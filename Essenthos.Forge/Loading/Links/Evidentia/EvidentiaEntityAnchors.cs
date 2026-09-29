namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Which entity each word names, on either side of the pair. The translation's names may come from its
/// own annotations or from the name consensus read off its verses; the original's are its annotations.
/// </summary>
internal sealed record EvidentiaEntityNames(
    IReadOnlyDictionary<long, IReadOnlySet<int>> Source,
    IReadOnlyDictionary<long, IReadOnlySet<int>> Target);

/// <summary>
/// Places a word of the translation on the word of the original that names the same person, place,
/// people or thing in the same verse: <em>Nebuchadnezzar</em> on נְבוּכַדְנֶאצַּר because both are
/// annotated as the one king. A name is rendered by its sound or not at all, so which word renders it is
/// the verse's question only when the entity is named more than once, and then the two sides are paired
/// in the order they name it, only where they name it as often. A verse that names it in neither count
/// is left to the other tiers; so is a word annotated to two entities that land on different words.
/// </summary>
internal static class EvidentiaEntityAnchors
{
    /// <summary>
    /// What a proposal from two annotations is worth: the name consensus is measured at about 99% on
    /// the words it names, and the original's annotations are its own.
    /// </summary>
    public const double Confidence = 0.95;

    public static IReadOnlyList<EvidentiaProposal> Resolve(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        EvidentiaEntityNames names,
        int neighbourVerses)
    {
        if (names.Source.Count == 0 || names.Target.Count == 0)
        {
            return [];
        }

        var targetByVerse = target
            .DistinctBy(analysis => analysis.Token.Id)
            .Where(analysis => names.Target.ContainsKey(analysis.Token.Id))
            .GroupBy(analysis => analysis.Token.Address)
            .ToDictionary(verse => verse.Key, verse => verse.OrderBy(analysis => analysis.Token.Position).ToList());
        var pairs = new List<(EvidentiaAnalysis Source, EvidentiaAnalysis Target, int Entity)>();
        foreach (var verse in source
                     .DistinctBy(analysis => analysis.Token.Id)
                     .Where(analysis => names.Source.ContainsKey(analysis.Token.Id))
                     .GroupBy(analysis => analysis.Token.Address))
        {
            foreach (var entity in verse.SelectMany(analysis => names.Source[analysis.Token.Id]).Distinct())
            {
                var words = verse.Where(analysis => names.Source[analysis.Token.Id].Contains(entity))
                    .OrderBy(analysis => analysis.Token.Position)
                    .ToList();
                var originals = Naming(targetByVerse, verse.Key, entity, names);
                if (originals.Count == 0 && neighbourVerses > 0)
                {
                    originals = [.. targetByVerse.Keys
                        .Where(address => address.DistanceTo(verse.Key) is > 0 and var distance && distance <= neighbourVerses)
                        .SelectMany(address => Naming(targetByVerse, address, entity, names))];
                }

                if (words.Count != originals.Count)
                {
                    continue;
                }

                pairs.AddRange(words.Zip(originals, (word, original) => (word, original, entity)));
            }
        }

        var sourceUses = pairs.CountBy(pair => pair.Source.Token.Id).ToDictionary();
        var targetUses = pairs.CountBy(pair => pair.Target.Token.Id).ToDictionary();
        var clear = pairs
            .Where(pair => sourceUses[pair.Source.Token.Id] == 1 && targetUses[pair.Target.Token.Id] == 1)
            .ToList();
        return
        [
            .. clear.Select(pair => new EvidentiaProposal(
                pair.Source,
                pair.Target,
                EvidentiaProposalKind.SharedEntityInOrder,
                Confidence,
                new EvidentiaDecisionTrace(
                    "safe",
                    pair.Source.Token.Address == pair.Target.Token.Address
                        ? "both words name the same entity, in the order the verse names it"
                        : "both words name the same entity, the original's in the neighbouring verse",
                    []))),
        ];
    }

    private static List<EvidentiaAnalysis> Naming(
        Dictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetByVerse,
        EvidentiaAddress address,
        int entity,
        EvidentiaEntityNames names) =>
        targetByVerse.TryGetValue(address, out var verse)
            ? [.. verse.Where(analysis => names.Target[analysis.Token.Id].Contains(entity))]
            : [];
}
