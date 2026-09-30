namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>What became of a word of the translation, as a reader will see it.</summary>
internal enum EvidentiaWordState
{
    /// <summary>On its own counterpart: a word of the original no other word of the translation was placed on before it.</summary>
    Linked,

    /// <summary>
    /// On the word of the word it belongs to, whose inflection or function it writes: <em>did</em> of
    /// <em>did see</em>, <em>he</em> on a verb's ending, <em>of</em> on a genitive.
    /// </summary>
    Attached,

    /// <summary>One of several words that render one word together: <em>out</em> of <em>went out</em>.</summary>
    PhraseMember,

    /// <summary>Said to have no counterpart: the original does not write it.</summary>
    Supplied,

    /// <summary>Nothing decided.</summary>
    Unresolved,
}

/// <summary>Where the word an unresolved grammatical word belongs to stands.</summary>
internal enum EvidentiaHeadState
{
    /// <summary>No word it could belong to was found.</summary>
    None,

    Placed,

    Supplied,

    /// <summary>Unplaced, and the key has a counterpart for it: the grammatical word waits on it.</summary>
    UnplacedWithCounterpart,

    /// <summary>Unplaced, and the key says nothing of it.</summary>
    UnplacedKeySilent,
}

/// <summary>One word's state, the rule that gave it, the word it was read with, and the split key's verdict.</summary>
internal sealed record EvidentiaWordStateRecord(
    EvidentiaWordState State,
    string? Rule,
    bool Grammatical,
    long? HeadWordId,
    EvidentiaHeadState? HeadState,
    bool? Right,
    IReadOnlyList<long> KeyTargets);

/// <summary>How many words stand in a state or came from a rule, and how many of those the key can judge and calls right.</summary>
internal readonly record struct EvidentiaStateCount(int Words, int Judged, int Right, int SafeJudged, int SafeRight)
{
    public static EvidentiaStateCount operator +(EvidentiaStateCount one, EvidentiaStateCount two) => new(
        one.Words + two.Words, one.Judged + two.Judged, one.Right + two.Right,
        one.SafeJudged + two.SafeJudged, one.SafeRight + two.SafeRight);
}

/// <summary>
/// A passage measured on the split key and by state. A pair count rewards one state of five, so the
/// share of words and the precision are given for each: a word linked, attached or a phrase member is
/// right where the split key names its pair, a supplied word where the key's links leave it nothing to
/// render, and an unresolved word is neither.
/// </summary>
internal sealed record EvidentiaStateMeasure(
    int SourceWords,
    int KeyPairsAsLoaded,
    IReadOnlyDictionary<EvidentiaKeyPairKind, (int Pairs, int Found)> KeyPairs,
    int UnclaimedPrefixes,
    int UnwrittenArticlesSupplied,
    EvidentiaTierScore Final,
    EvidentiaTierScore Safe,
    IReadOnlyDictionary<EvidentiaWordState, EvidentiaStateCount> States,
    IReadOnlyDictionary<(EvidentiaWordState State, string Rule), EvidentiaStateCount> Rules,
    int UnresolvedWithCounterpart,
    int UnresolvedGrammatical,
    IReadOnlyDictionary<EvidentiaHeadState, int> Cascade)
{
    public static readonly EvidentiaStateMeasure Empty = new(
        0, 0, new Dictionary<EvidentiaKeyPairKind, (int, int)>(), 0, 0, default, default,
        new Dictionary<EvidentiaWordState, EvidentiaStateCount>(),
        new Dictionary<(EvidentiaWordState, string), EvidentiaStateCount>(), 0, 0,
        new Dictionary<EvidentiaHeadState, int>());

    public int SplitPairs => KeyPairs.Values.Sum(kind => kind.Pairs);

    public EvidentiaStateCount Of(EvidentiaWordState state) => States.GetValueOrDefault(state);

    public static EvidentiaStateMeasure operator +(EvidentiaStateMeasure one, EvidentiaStateMeasure two) => new(
        one.SourceWords + two.SourceWords,
        one.KeyPairsAsLoaded + two.KeyPairsAsLoaded,
        Merge(one.KeyPairs, two.KeyPairs, (first, second) => (first.Pairs + second.Pairs, first.Found + second.Found)),
        one.UnclaimedPrefixes + two.UnclaimedPrefixes,
        one.UnwrittenArticlesSupplied + two.UnwrittenArticlesSupplied,
        one.Final + two.Final,
        one.Safe + two.Safe,
        Merge(one.States, two.States, (first, second) => first + second),
        Merge(one.Rules, two.Rules, (first, second) => first + second),
        one.UnresolvedWithCounterpart + two.UnresolvedWithCounterpart,
        one.UnresolvedGrammatical + two.UnresolvedGrammatical,
        Merge(one.Cascade, two.Cascade, (first, second) => first + second));

    private static Dictionary<TKey, TValue> Merge<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> one,
        IReadOnlyDictionary<TKey, TValue> two,
        Func<TValue, TValue, TValue> add)
        where TKey : notnull
    {
        var merged = one.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (key, value) in two)
        {
            merged[key] = merged.TryGetValue(key, out var held) ? add(held, value) : value;
        }

        return merged;
    }

    private static string Share(int part, int whole) => $"{part:N0}/{whole:N0} ({(whole == 0 ? 0 : (double)part / whole):P2})";

    public static string Name(EvidentiaWordState state) => state switch
    {
        EvidentiaWordState.PhraseMember => "phrase member",
        _ => state.ToString().ToLowerInvariant(),
    };

    public string Report()
    {
        (int Pairs, int Found) Kind(EvidentiaKeyPairKind kind) => KeyPairs.GetValueOrDefault(kind);
        var prefix = Kind(EvidentiaKeyPairKind.PrefixOfItsKind);
        var stem = Kind(EvidentiaKeyPairKind.Stem);
        var article = Kind(EvidentiaKeyPairKind.UnwrittenArticle);
        var lines = new List<string>
        {
            $"split key: {SplitPairs:N0} pairs of {KeyPairsAsLoaded:N0} as loaded; a grammatical word on the prefix of its kind " +
            $"{prefix.Pairs:N0}, a word on a stem {stem.Pairs:N0}, an article on a noun written without one {article.Pairs:N0}; " +
            $"{UnclaimedPrefixes:N0} prefixes no word of their kind takes",
            $"split key, pairs: {Share(Final.Correct, Final.OnCoveredWords)} over gold-covered words; " +
            $"recall {Share(Final.Correct, SplitPairs)}; safe tier {Share(Safe.Correct, Safe.OnCoveredWords)}",
            $"split key, pairs found: prefix of its kind {Share(prefix.Found, prefix.Pairs)}; stem {Share(stem.Found, stem.Pairs)}; " +
            $"unwritten article {Share(article.Found, article.Pairs)}, {UnwrittenArticlesSupplied:N0} more said supplied",
        };
        foreach (var state in Enum.GetValues<EvidentiaWordState>())
        {
            var count = Of(state);
            lines.Add(state switch
            {
                EvidentiaWordState.Unresolved =>
                    $"by state, {Name(state)}: {Share(count.Words, SourceWords)} of words; the key has a counterpart for " +
                    $"{UnresolvedWithCounterpart:N0}; grammatical {UnresolvedGrammatical:N0}",
                EvidentiaWordState.Supplied =>
                    $"by state, {Name(state)}: {Share(count.Words, SourceWords)} of words; right {Share(count.Right, count.Judged)}",
                _ =>
                    $"by state, {Name(state)}: {Share(count.Words, SourceWords)} of words; right {Share(count.Right, count.Judged)}; " +
                    $"safe tier {Share(count.SafeRight, count.SafeJudged)}",
            });
        }

        var explained = States.Where(pair => pair.Key != EvidentiaWordState.Unresolved).ToList();
        lines.Add(
            $"by state, explained: {Share(explained.Sum(pair => pair.Value.Words), SourceWords)} of words; " +
            $"right {Share(explained.Sum(pair => pair.Value.Right), SourceWords)} of all words");
        lines.AddRange(Rules
            .OrderBy(pair => pair.Key.State)
            .ThenByDescending(pair => pair.Value.Words)
            .ThenBy(pair => pair.Key.Rule, StringComparer.Ordinal)
            .Select(pair => $"by rule, {Name(pair.Key.State)}, {pair.Key.Rule}: {pair.Value.Words:N0} words; " +
                            $"right {Share(pair.Value.Right, pair.Value.Judged)}"));
        var waiting = Cascade.Values.Sum();
        int Head(EvidentiaHeadState state) => Cascade.GetValueOrDefault(state);
        lines.Add(
            $"cascade: {waiting:N0} unresolved grammatical words with a key counterpart; head placed {Share(Head(EvidentiaHeadState.Placed), waiting)}, " +
            $"head unplaced with a key counterpart {Share(Head(EvidentiaHeadState.UnplacedWithCounterpart), waiting)}, " +
            $"head supplied {Head(EvidentiaHeadState.Supplied):N0}, head unplaced and the key silent {Head(EvidentiaHeadState.UnplacedKeySilent):N0}, " +
            $"no head {Head(EvidentiaHeadState.None):N0}");
        return string.Join("\n", lines);
    }
}

/// <summary>
/// Gives every word of the translation its state and scores each state on the split key. A word placed
/// by the word it belongs to is attached where it lands on that word's own counterpart and linked where
/// it lands on a part of its own (<em>and</em> on the ו before its head's rendering); a particle joined to
/// its verb, or a verb to its particle, is a member of the phrase.
///
/// <para>The cascade asks, for each unresolved grammatical word the key has a counterpart for, where the
/// word it belongs to stands: the head its attachment rule reads it with, and failing that its head in
/// the parse. A grammatical word is one the pack calls a function word or the parse puts outside the
/// open classes and adverbs.</para>
/// </summary>
internal static class EvidentiaStateScore
{
    public const string LexicalRule = "lexical";

    private static readonly HashSet<EvidentiaAttachment> PhraseAttachments =
        [EvidentiaAttachment.PhrasalParticle, EvidentiaAttachment.VerbOfItsParticle];

    private static readonly HashSet<string> OpenClasses = ["noun", "propn", "verb", "adj", "num", "adv"];

    public static EvidentiaStateMeasure Of(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaProposal> proposals,
        IReadOnlyList<EvidentiaAbsence> absences,
        IReadOnlyDictionary<long, bool?> absenceVerdicts,
        EvidentiaGold gold,
        EvidentiaSplitKey key,
        IReadOnlySet<(long From, long To)> accepted,
        IReadOnlySet<(long From, long To)> safe,
        out IReadOnlyDictionary<long, EvidentiaWordStateRecord> records)
    {
        var words = source.DistinctBy(word => word.Token.Id).ToList();
        var proposalBySource = proposals.GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var supplied = absences.Where(absence => absence.Kind == EvidentiaAbsenceKind.Supplied)
            .Select(absence => absence.Word.Token.Id).ToHashSet();
        var keyTargets = key.Pairs.GroupBy(pair => pair.From)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<long>)[.. group.Select(pair => pair.To)]);
        var states = new Dictionary<long, (EvidentiaWordState State, string? Rule, bool? Right)>();
        foreach (var word in words)
        {
            var id = word.Token.Id;
            if (proposalBySource.TryGetValue(id, out var proposal))
            {
                var pair = (id, proposal.Target.Token.Id);
                states[id] = (StateOf(proposal), RuleOf(proposal),
                    gold.CoveredSourceWords.Contains(id) ? accepted.Contains(pair) : null);
            }
            else if (supplied.Contains(id))
            {
                states[id] = (EvidentiaWordState.Supplied, null, absenceVerdicts.GetValueOrDefault(id));
            }
            else
            {
                states[id] = (EvidentiaWordState.Unresolved, null, null);
            }
        }

        var heads = Heads(words, states, keyTargets);
        records = words.ToDictionary(
            word => word.Token.Id,
            word =>
            {
                var (state, rule, right) = states[word.Token.Id];
                var head = heads.GetValueOrDefault(word.Token.Id);
                return new EvidentiaWordStateRecord(
                    state, rule, IsGrammatical(word),
                    proposalBySource.GetValueOrDefault(word.Token.Id)?.Head?.Source.Token.Id ?? head.Head,
                    state == EvidentiaWordState.Unresolved && IsGrammatical(word) ? head.State : null,
                    right,
                    keyTargets.GetValueOrDefault(word.Token.Id) ?? []);
            });

        var byState = new Dictionary<EvidentiaWordState, EvidentiaStateCount>();
        var byRule = new Dictionary<(EvidentiaWordState, string), EvidentiaStateCount>();
        foreach (var word in words)
        {
            var id = word.Token.Id;
            var (state, rule, right) = states[id];
            var isSafe = proposalBySource.TryGetValue(id, out var proposal) && safe.Contains((id, proposal.Target.Token.Id));
            var count = new EvidentiaStateCount(
                1, right is null ? 0 : 1, right == true ? 1 : 0,
                isSafe && right is not null ? 1 : 0, isSafe && right == true ? 1 : 0);
            byState[state] = byState.GetValueOrDefault(state) + count;
            if (rule is not null)
            {
                byRule[(state, rule)] = byRule.GetValueOrDefault((state, rule)) + count;
            }
        }

        var placed = proposals.Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id)).ToHashSet();
        var unresolved = words.Where(word => states[word.Token.Id].State == EvidentiaWordState.Unresolved).ToList();
        var waiting = unresolved.Where(word => IsGrammatical(word) && keyTargets.ContainsKey(word.Token.Id)).ToList();
        List<EvidentiaProposal> safeProposals = [.. proposals.Where(proposal => safe.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id)))];
        return new EvidentiaStateMeasure(
            words.Count,
            key.PairsAsLoaded,
            Enum.GetValues<EvidentiaKeyPairKind>().ToDictionary(
                kind => kind,
                kind => (key.Kinds.Count(pair => pair.Value == kind), key.Kinds.Count(pair => pair.Value == kind && placed.Contains(pair.Key)))),
            key.UnclaimedPrefixes.Count,
            key.Kinds.Where(pair => pair.Value == EvidentiaKeyPairKind.UnwrittenArticle)
                .Select(pair => pair.Key.From).Distinct().Count(supplied.Contains),
            EvidentiaTierScore.Of(proposals, key.Pairs, gold.CoveredSourceWords),
            EvidentiaTierScore.Of(safeProposals, key.Pairs, gold.CoveredSourceWords),
            byState,
            byRule,
            unresolved.Count(word => keyTargets.ContainsKey(word.Token.Id)),
            unresolved.Count(IsGrammatical),
            waiting.GroupBy(word => heads.GetValueOrDefault(word.Token.Id).State)
                .ToDictionary(group => group.Key, group => group.Count()));
    }

    public static EvidentiaWordState StateOf(EvidentiaProposal proposal) =>
        proposal.Kind != EvidentiaProposalKind.AttachedWord ? EvidentiaWordState.Linked
        : EvidentiaAttachedWords.Attachment(proposal) is { } attachment && PhraseAttachments.Contains(attachment) ? EvidentiaWordState.PhraseMember
        : proposal.Head is { } head && head.Target.Token.Id == proposal.Target.Token.Id ? EvidentiaWordState.Attached
        : EvidentiaWordState.Linked;

    private static string RuleOf(EvidentiaProposal proposal) =>
        proposal.Kind == EvidentiaProposalKind.AttachedWord && EvidentiaAttachedWords.Attachment(proposal) is { } attachment
            ? attachment.ToString()
            : LexicalRule;

    public static bool IsGrammatical(EvidentiaAnalysis word) =>
        word.IsFunctionWord || EvidentiaAttachedWords.Class(word) is { } wordClass && !OpenClasses.Contains(wordClass);

    private static Dictionary<long, (long? Head, EvidentiaHeadState State)> Heads(
        IReadOnlyList<EvidentiaAnalysis> words,
        IReadOnlyDictionary<long, (EvidentiaWordState State, string? Rule, bool? Right)> states,
        IReadOnlyDictionary<long, IReadOnlyList<long>> keyTargets)
    {
        var heads = new Dictionary<long, (long?, EvidentiaHeadState)>();
        foreach (var verse in words.GroupBy(word => word.Token.Address)
                     .Select(verse => verse.OrderBy(word => word.Token.Position).ToList()))
        {
            for (var index = 0; index < verse.Count; index++)
            {
                var word = verse[index];
                var head = EvidentiaAttachedWords.Classify(verse, index).Head
                    ?? (word.Token.SyntacticHead is { } parsed ? verse.FirstOrDefault(other => other.Token.Id == parsed) : null);
                if (head is null || head.Token.Id == word.Token.Id)
                {
                    heads[word.Token.Id] = (null, EvidentiaHeadState.None);
                    continue;
                }

                heads[word.Token.Id] = (head.Token.Id, states[head.Token.Id].State switch
                {
                    EvidentiaWordState.Supplied => EvidentiaHeadState.Supplied,
                    EvidentiaWordState.Unresolved => keyTargets.ContainsKey(head.Token.Id)
                        ? EvidentiaHeadState.UnplacedWithCounterpart
                        : EvidentiaHeadState.UnplacedKeySilent,
                    _ => EvidentiaHeadState.Placed,
                });
            }
        }

        return heads;
    }
}
