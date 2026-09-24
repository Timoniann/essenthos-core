namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// The measurement by word rather than by pair, which is how the reader shows it: each word of the
/// translation is placed, or said to be supplied, and each word of the original is rendered by some
/// word, or said to be unrendered. A word counts once whichever it is, so a prefix the translation
/// renders with a word of its own (<em>In</em> on בְּ) and one it does not render (a ו with no
/// <em>and</em>) are both counted as done, and a word placed on the stem is not also asked for the
/// prefix.
///
/// <para>The answer keys write a written word whole: the Berean puts <em>said</em> on both וַ and
/// יֹּאמֶר, and <em>And God</em> on אֱלֹהִים. Its <em>And</em> and its וַ are then each folded into
/// another word's link, and neither link says the one renders the other. Where a verse folds as many
/// conjunctions on each side, the two are paired in order and a placement on that pair is counted
/// right; where the numbers differ nothing can say which renders which, and the placement is judged
/// by the key as written. A key that leaves the prefixes out of its links altogether, as the King
/// James's does, is satisfied by <em>the</em> on the article of a word its link names. Articles and
/// prepositions are not otherwise paired: a folded <em>the</em> is as often
/// supplied beside an article the translation leaves unrendered as it is that article's rendering.</para>
///
/// <para>An absence is judged by the key's own absences where it states them, and otherwise by its
/// links alone, never by the other proposals: a word is wrongly said to be supplied or unrendered when
/// its link names a word of its own kind on the other side (<em>the</em> of <em>the waters</em> with
/// הַ), when it is a conjunction in a verse whose key folds a conjunction on the other side (<em>And
/// God</em> with the וַ of <em>said</em>), or when it is the only word of the translation its link
/// gives a word of meaning; a word of the original is wrongly unrendered when a word of its link has
/// nothing else in the link to render. <em>the</em> of <em>the surface</em> is supplied: פְּנֵי is also
/// <em>surface</em>'s.</para>
/// </summary>
internal static class EvidentiaWordScore
{
    public static EvidentiaWordMeasure Of(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaProposal> proposals,
        IReadOnlyList<EvidentiaAbsence> absences,
        EvidentiaGold gold,
        out IReadOnlyDictionary<long, bool?> absenceVerdicts)
    {
        var sourceById = source.DistinctBy(word => word.Token.Id).ToDictionary(word => word.Token.Id);
        var verses = sourceById.Values.Select(word => word.Token.Address).ToHashSet();
        var targetById = target.DistinctBy(word => word.Token.Id)
            .Where(word => verses.Contains(word.Token.Address))
            .ToDictionary(word => word.Token.Id);
        var goldTargets = Union(gold.Links.SelectMany(link => link.SourceWords.Select(word => (word, link.TargetWords))));
        var goldSources = Union(gold.Links.SelectMany(link => link.TargetWords.Select(word => (word, link.SourceWords))));

        var orphanSources = Orphans(goldTargets, sourceById, targetById);
        var orphanTargets = Orphans(goldSources, targetById, sourceById);
        int Position(long id) => targetById.TryGetValue(id, out var word) ? word.Token.Position : -1;
        var paired = new HashSet<(long, long)>();
        var unpairedSources = new HashSet<long>();
        var unpairedTargets = new HashSet<long>();
        foreach (var (key, sources) in orphanSources)
        {
            var targets = orphanTargets.GetValueOrDefault(key) ?? [];
            // The Berean's And God follows the verb its וַ is written on: וַיֹּאמֶר אֱלֹהִים.
            var adjacent = sources
                .Select(source => (Source: source, Target: targets.FirstOrDefault(target =>
                    goldTargets[source].Select(Position).Where(position => position >= 0).DefaultIfEmpty(-1).Min()
                    == goldSources[target].SelectMany(other => goldTargets[other]).Select(Position).Max() + 1)))
                .Where(pair => pair.Target != 0)
                .DistinctBy(pair => pair.Target)
                .ToList();
            paired.UnionWith(adjacent.Select(pair => (pair.Source, pair.Target)));
            var restSources = sources.Except(adjacent.Select(pair => pair.Source)).ToList();
            var restTargets = targets.Except(adjacent.Select(pair => pair.Target)).ToList();
            if (restSources.Count == restTargets.Count)
            {
                paired.UnionWith(restSources.Zip(restTargets, (one, two) => (one, two)));
            }
            else
            {
                unpairedSources.UnionWith(restSources);
                unpairedTargets.UnionWith(restTargets);
            }
        }

        foreach (var (key, targets) in orphanTargets.Where(pair => !orphanSources.ContainsKey(pair.Key)))
        {
            unpairedTargets.UnionWith(targets);
        }

        var pairedSources = paired.Select(pair => pair.Item1).ToHashSet();
        var pairedTargets = paired.Select(pair => pair.Item2).ToHashSet();

        var byPlace = targetById.Values.ToDictionary(word => (word.Token.Address, word.Token.Position));

        // The King James's key leaves the prefixes to no one - the heaven is on שָּׁמַיִם alone - so the
        // on the הַ written on that word is the finer placement, not another one.
        bool OnAPrefixOfItsLink(long from, long to)
        {
            if (!targetById.TryGetValue(to, out var prefix) || !IsPrefix(prefix) || !sourceById.TryGetValue(from, out var english)
                || EvidentiaAbsences.FunctionClass(english) is not { } kind || EvidentiaAbsences.FunctionClass(prefix) != kind
                || !goldTargets.TryGetValue(from, out var linked))
            {
                return false;
            }

            for (var word = prefix; word.Token.Trailer.Length == 0
                 && byPlace.TryGetValue((word.Token.Address, word.Token.Position + 1), out var next); word = next)
            {
                if (linked.Contains(next.Token.Id))
                {
                    return true;
                }
            }

            return false;
        }

        bool Accepted(long from, long to) =>
            gold.Pairs.Contains((from, to)) || paired.Contains((from, to)) || OnAPrefixOfItsLink(from, to);
        var accepted = proposals
            .Where(proposal => Accepted(proposal.Source.Token.Id, proposal.Target.Token.Id))
            .Select(proposal => (From: proposal.Source.Token.Id, To: proposal.Target.Token.Id))
            .ToHashSet();
        var acceptedTargets = accepted.Select(pair => pair.To).ToHashSet();

        // A conjunction the key folds renders the one it is paired with, or any of those left unpaired in its verse.
        bool FoldedSource(EvidentiaAnalysis word) =>
            pairedSources.Contains(word.Token.Id)
            || unpairedSources.Contains(word.Token.Id) && unpairedTargets.Any(id => targetById[id].Token.Address == word.Token.Address);

        bool FoldedTarget(EvidentiaAnalysis word) =>
            pairedTargets.Contains(word.Token.Id)
            || unpairedTargets.Contains(word.Token.Id) && unpairedSources.Any(id => sourceById[id].Token.Address == word.Token.Address);

        bool? Supplied(EvidentiaAnalysis word)
        {
            var id = word.Token.Id;
            if (gold.SuppliedSourceWords.Contains(id))
            {
                return true;
            }

            if (!goldTargets.TryGetValue(id, out var targets))
            {
                return null;
            }

            var linked = targets.Select(other => targetById.GetValueOrDefault(other)).OfType<EvidentiaAnalysis>().ToList();
            var kind = EvidentiaAbsences.FunctionClass(word);
            if (kind is not null
                    ? linked.Any(other => EvidentiaAbsences.FunctionClass(other) == kind) || FoldedSource(word)
                    : linked.Any(other => EvidentiaAbsences.Renders(word, other)))
            {
                return false;
            }

            // The only word of the translation a key links with a word of meaning renders it, whatever it is.
            return !linked.Any(other =>
                EvidentiaMorphologyLabels.IsFunctionWord(other.PartOfSpeech ?? other.Token.PartOfSpeech, other.Token.Language) != true
                && goldSources.GetValueOrDefault(other.Token.Id)?.Any(from => from != id) != true);
        }

        bool? NotRendered(EvidentiaAnalysis word)
        {
            var id = word.Token.Id;
            if (gold.UnrenderedTargetWords.Contains(id))
            {
                return true;
            }

            if (!goldSources.TryGetValue(id, out var sources))
            {
                return null;
            }

            var linked = sources.Select(other => sourceById.GetValueOrDefault(other)).OfType<EvidentiaAnalysis>().ToList();
            var kind = EvidentiaAbsences.FunctionClass(word);
            if (kind is not null)
            {
                return !(linked.Any(other => EvidentiaAbsences.FunctionClass(other) == kind) || FoldedTarget(word));
            }

            // אֶת in the Berean's and on וְאֶת: and has its ו, and nothing of the link is left for את.
            return !linked.Any(other =>
            {
                var rest = goldTargets[other.Token.Id].Where(target => target != id)
                    .Select(target => targetById.GetValueOrDefault(target)).OfType<EvidentiaAnalysis>().ToList();
                return EvidentiaAbsences.FunctionClass(other) is { } ownKind
                    ? rest.All(target => EvidentiaAbsences.FunctionClass(target) != ownKind)
                    : rest.All(target => EvidentiaMorphologyLabels.IsFunctionWord(
                        target.PartOfSpeech ?? target.Token.PartOfSpeech, target.Token.Language) == true);
            });
        }

        var verdicts = new Dictionary<long, bool?>();
        foreach (var absence in absences)
        {
            verdicts[absence.Word.Token.Id] = absence.Kind == EvidentiaAbsenceKind.Supplied
                ? Supplied(absence.Word)
                : NotRendered(absence.Word);
        }

        absenceVerdicts = verdicts;
        var supplied = absences.Where(absence => absence.Kind == EvidentiaAbsenceKind.Supplied)
            .Select(absence => absence.Word.Token.Id).Where(sourceById.ContainsKey).ToHashSet();
        var unrendered = absences.Where(absence => absence.Kind == EvidentiaAbsenceKind.NotRendered)
            .Select(absence => absence.Word.Token.Id).Where(targetById.ContainsKey).ToHashSet();
        var linkedSources = proposals.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var linkedTargets = proposals.Select(proposal => proposal.Target.Token.Id).Where(targetById.ContainsKey).ToHashSet();
        var bySource = proposals.GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.Select(proposal => proposal.Target.Token.Id).ToList());

        var scopeSources = goldTargets.Keys.Concat(gold.SuppliedSourceWords).Where(sourceById.ContainsKey).ToHashSet();
        var scopeTargets = goldSources.Keys.Concat(gold.UnrenderedTargetWords).Where(targetById.ContainsKey).ToHashSet();
        var linkScope = gold.CoveredSourceWords.Concat(gold.SuppliedSourceWords).ToHashSet();
        var linksOnScope = proposals.Where(proposal => linkScope.Contains(proposal.Source.Token.Id)).ToList();

        return new EvidentiaWordMeasure(
            SourceWords: sourceById.Count,
            SourceLinked: linkedSources.Count(sourceById.ContainsKey),
            SourceSupplied: supplied.Count(id => !linkedSources.Contains(id)),
            TargetWords: targetById.Count,
            TargetLinked: linkedTargets.Count,
            TargetUnrendered: unrendered.Count(id => !linkedTargets.Contains(id)),
            LinksOnScope: linksOnScope.Count,
            LinksRightByPair: linksOnScope.Count(proposal => gold.Pairs.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))),
            LinksRight: linksOnScope.Count(proposal => accepted.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))),
            SuppliedProposed: supplied.Count,
            SuppliedJudged: supplied.Count(id => verdicts[id] is not null),
            SuppliedRight: supplied.Count(id => verdicts[id] == true),
            UnrenderedProposed: unrendered.Count,
            UnrenderedJudged: unrendered.Count(id => verdicts[id] is not null),
            UnrenderedRight: unrendered.Count(id => verdicts[id] == true),
            ScopeSourceWords: scopeSources.Count,
            SourceWordsRight: scopeSources.Count(id =>
                bySource.TryGetValue(id, out var targets)
                    ? targets.All(to => accepted.Contains((id, to)))
                    : supplied.Contains(id) && verdicts[id] == true),
            ScopeTargetWords: scopeTargets.Count,
            TargetWordsRight: scopeTargets.Count(id =>
                acceptedTargets.Contains(id) || unrendered.Contains(id) && verdicts[id] == true),
            Accepted: accepted);
    }

    private static bool IsPrefix(EvidentiaAnalysis word) =>
        word.Token.StrongNumber is { Length: 5 } strong && strong.StartsWith("H900", StringComparison.Ordinal);

    private static Dictionary<long, HashSet<long>> Union(IEnumerable<(long Word, IReadOnlyList<long> Others)> rows)
    {
        var union = new Dictionary<long, HashSet<long>>();
        foreach (var (word, others) in rows)
        {
            if (!union.TryGetValue(word, out var found))
            {
                union[word] = found = [];
            }

            found.UnionWith(others);
        }

        return union;
    }

    /// <summary>
    /// The grammatical words of each verse the key folds into a link that names no word of their kind on
    /// the other side, in the order they are written.
    /// </summary>
    private static Dictionary<(EvidentiaAddress, EvidentiaFunctionClass), List<long>> Orphans(
        IReadOnlyDictionary<long, HashSet<long>> counterparts,
        IReadOnlyDictionary<long, EvidentiaAnalysis> side,
        IReadOnlyDictionary<long, EvidentiaAnalysis> other)
    {
        var orphans = new Dictionary<(EvidentiaAddress, EvidentiaFunctionClass), List<long>>();
        foreach (var word in side.Values.OrderBy(word => word.Token.Address.Verse).ThenBy(word => word.Token.Position))
        {
            const EvidentiaFunctionClass kind = EvidentiaFunctionClass.Coordinator;
            if (EvidentiaAbsences.FunctionClass(word) != kind
                || !counterparts.TryGetValue(word.Token.Id, out var linked)
                || linked.Any(id => other.TryGetValue(id, out var found) && EvidentiaAbsences.FunctionClass(found) == kind))
            {
                continue;
            }

            var key = (word.Token.Address, kind);
            if (!orphans.TryGetValue(key, out var list))
            {
                orphans[key] = list = [];
            }

            list.Add(word.Token.Id);
        }

        return orphans;
    }
}

/// <summary>
/// One passage measured by word. <see cref="LinksRightByPair"/> is the old rule, a link right only
/// where the key names that pair; <see cref="LinksRight"/> also counts a grammatical word placed on its
/// own kind where the key folded both into other links.
/// </summary>
internal readonly record struct EvidentiaWordMeasure(
    int SourceWords,
    int SourceLinked,
    int SourceSupplied,
    int TargetWords,
    int TargetLinked,
    int TargetUnrendered,
    int LinksOnScope,
    int LinksRightByPair,
    int LinksRight,
    int SuppliedProposed,
    int SuppliedJudged,
    int SuppliedRight,
    int UnrenderedProposed,
    int UnrenderedJudged,
    int UnrenderedRight,
    int ScopeSourceWords,
    int SourceWordsRight,
    int ScopeTargetWords,
    int TargetWordsRight,
    IReadOnlySet<(long From, long To)>? Accepted = null)
{
    public static EvidentiaWordMeasure operator +(EvidentiaWordMeasure one, EvidentiaWordMeasure two) => new(
        one.SourceWords + two.SourceWords,
        one.SourceLinked + two.SourceLinked,
        one.SourceSupplied + two.SourceSupplied,
        one.TargetWords + two.TargetWords,
        one.TargetLinked + two.TargetLinked,
        one.TargetUnrendered + two.TargetUnrendered,
        one.LinksOnScope + two.LinksOnScope,
        one.LinksRightByPair + two.LinksRightByPair,
        one.LinksRight + two.LinksRight,
        one.SuppliedProposed + two.SuppliedProposed,
        one.SuppliedJudged + two.SuppliedJudged,
        one.SuppliedRight + two.SuppliedRight,
        one.UnrenderedProposed + two.UnrenderedProposed,
        one.UnrenderedJudged + two.UnrenderedJudged,
        one.UnrenderedRight + two.UnrenderedRight,
        one.ScopeSourceWords + two.ScopeSourceWords,
        one.SourceWordsRight + two.SourceWordsRight,
        one.ScopeTargetWords + two.ScopeTargetWords,
        one.TargetWordsRight + two.TargetWordsRight);

    private static string Share(int part, int whole) => $"{part:N0}/{whole:N0} ({(whole == 0 ? 0 : (double)part / whole):P2})";

    public string Report() =>
        $"by word, coverage: source linked {Share(SourceLinked, SourceWords)}, with supplied {Share(SourceLinked + SourceSupplied, SourceWords)}; " +
        $"original rendered {Share(TargetLinked, TargetWords)}, with unrendered {Share(TargetLinked + TargetUnrendered, TargetWords)}\n" +
        $"by word, links: {Share(LinksRightByPair, LinksOnScope)} by pair; {Share(LinksRight, LinksOnScope)} with folded grammatical words paired\n" +
        $"by word, absences: supplied {Share(SuppliedRight, SuppliedJudged)} of {SuppliedProposed:N0}; " +
        $"unrendered {Share(UnrenderedRight, UnrenderedJudged)} of {UnrenderedProposed:N0}\n" +
        $"by word, right: source {Share(SourceWordsRight, ScopeSourceWords)}; original {Share(TargetWordsRight, ScopeTargetWords)}; " +
        $"both {Share(SourceWordsRight + TargetWordsRight, ScopeSourceWords + ScopeTargetWords)}";
}
