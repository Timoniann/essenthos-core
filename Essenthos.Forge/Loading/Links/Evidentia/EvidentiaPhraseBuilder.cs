namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Finds conservative contiguous phrase candidates from unambiguous, independently supported word edges.
/// A phrase is diagnostic output only; it never promotes or persists its member word links.
/// </summary>
internal static class EvidentiaPhraseBuilder
{
    private const double MinimumWordScore = EvidentiaDefaults.PhraseWordScore;

    public static IReadOnlyList<EvidentiaPhraseCandidate> Build(IReadOnlyList<EvidentiaCandidate> candidates)
    {
        var eligible = candidates.Where(candidate => candidate.Score >= MinimumWordScore
                && candidate.Source.IsContentWord && !candidate.PairsAContentWordWithAFunctionWord
                && !candidate.PlacesAnAuxiliaryWordOffItsKind
                && candidate.Source.Token.Address == candidate.Target.Token.Address)
            .ToList();
        var uniqueSource = eligible.GroupBy(candidate => candidate.Source.Token.Id)
            .Where(group => group.Count() == 1).Select(group => group.Single()).ToList();
        var unique = uniqueSource.GroupBy(candidate => candidate.Target.Token.Id)
            .Where(group => group.Count() == 1).Select(group => group.Single())
            .GroupBy(candidate => candidate.Source.Token.Address)
            .ToList();
        var phrases = new List<EvidentiaPhraseCandidate>();
        foreach (var verse in unique)
        {
            var ordered = verse.OrderBy(candidate => candidate.Source.Token.Position).ToList();
            for (var start = 0; start < ordered.Count - 1; start++)
            {
                var members = new List<EvidentiaCandidate> { ordered[start] };
                for (var next = start + 1; next < ordered.Count
                    && ordered[next].Source.Token.Position == members[^1].Source.Token.Position + 1
                    && ordered[next].Target.Token.Position == members[^1].Target.Token.Position + 1; next++)
                {
                    members.Add(ordered[next]);
                }
                if (members.Count >= 2)
                {
                    phrases.Add(new EvidentiaPhraseCandidate(
                        members.Select(member => member.Source.Token).ToList(),
                        members.Select(member => member.Target.Token).ToList(),
                        members.Average(member => member.Score),
                        "contiguous-unique-word-edges"));
                    start += members.Count - 1;
                }
            }
        }
        phrases.AddRange(Expansions(eligible, bySource: true));
        phrases.AddRange(Expansions(eligible, bySource: false));
        return phrases;
    }

    private static IEnumerable<EvidentiaPhraseCandidate> Expansions(IReadOnlyList<EvidentiaCandidate> candidates, bool bySource)
    {
        foreach (var group in candidates.GroupBy(candidate => bySource ? candidate.Source.Token.Id : candidate.Target.Token.Id))
        {
            var members = group.OrderBy(candidate => bySource ? candidate.Target.Token.Position : candidate.Source.Token.Position).ToList();
            var positions = members.Select(candidate => bySource ? candidate.Target.Token.Position : candidate.Source.Token.Position).ToList();
            if (members.Count is < 2 or > 3 || positions.Zip(positions.Skip(1)).Any(pair => pair.Second != pair.First + 1))
            {
                continue;
            }
            yield return new EvidentiaPhraseCandidate(
                bySource ? [members[0].Source.Token] : members.Select(member => member.Source.Token).ToList(),
                bySource ? members.Select(member => member.Target.Token).ToList() : [members[0].Target.Token],
                members.Average(member => member.Score),
                bySource ? "one-to-many-review-edges" : "many-to-one-review-edges");
        }
    }
}
