namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Reads a verse of the original that the frame places beside a translation's verse as part of it. Every tier
/// places a word in its own verse; where the two texts divide a passage differently, the frame says so by
/// placing one verse at several canonical addresses (a psalm's title printed inside its first verse), and the
/// verses of both texts joined through the addresses they share are one passage. Where that passage holds one
/// verse of the translation, the original's verses in it are read as that verse: given its address, and their
/// words numbered on from one verse to the next in the order they stand. Nothing is joined that the frame does
/// not join, and a passage holding several verses of the translation is left as it is, since which of them a
/// word of the original belongs to is what the tiers would have to decide.
/// </summary>
internal static class EvidentiaVerseMap
{
    public static IReadOnlyDictionary<long, EvidentiaToken> Read(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target)
    {
        var sourceVerses = source.Select(token => (token.Address, token.Span)).DistinctBy(verse => verse.Address).ToList();
        var targetVerses = target.GroupBy(token => token.Address).ToList();
        var parent = new Dictionary<EvidentiaAddress, EvidentiaAddress>();
        EvidentiaAddress Find(EvidentiaAddress address)
        {
            while (parent.TryGetValue(address, out var up) && !up.Equals(address))
            {
                address = up;
            }

            return address;
        }

        void Join(IEnumerable<EvidentiaAddress> addresses)
        {
            var list = addresses.ToList();
            foreach (var address in list)
            {
                parent.TryAdd(address, address);
            }

            foreach (var address in list.Skip(1))
            {
                var one = Find(list[0]);
                var two = Find(address);
                if (!one.Equals(two))
                {
                    parent[two] = one;
                }
            }
        }

        foreach (var (address, span) in sourceVerses)
        {
            Join(span ?? [address]);
        }

        foreach (var verse in targetVerses)
        {
            Join(verse.First().Span ?? [verse.Key]);
        }

        var sourceByPassage = sourceVerses.GroupBy(verse => Find(verse.Address)).ToDictionary(group => group.Key, group => group.ToList());
        var moved = new Dictionary<long, EvidentiaToken>();
        foreach (var passage in targetVerses.GroupBy(verse => Find(verse.Key)))
        {
            if (!sourceByPassage.TryGetValue(passage.Key, out var verses) || verses.Count != 1)
            {
                continue;
            }

            var reading = verses[0].Address;
            var ordered = passage.OrderBy(verse => verse.Key.Sequence ?? verse.Key.Verse).ThenBy(verse => verse.Key.Verse).ToList();
            if (ordered.Count == 1 && ordered[0].Key.Equals(reading))
            {
                continue;
            }

            var before = 0;
            foreach (var verse in ordered)
            {
                foreach (var token in verse.OrderBy(token => token.Position))
                {
                    moved[token.Id] = token with
                    {
                        Address = reading,
                        Position = before + token.Position,
                        Home = token.Printed,
                    };
                }

                before += verse.Max(token => token.Position);
            }
        }

        return moved;
    }
}
