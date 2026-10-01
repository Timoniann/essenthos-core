using Essenthos.Core.Corpus;

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

/// <summary>
/// One word's state, the rule that gave it, the word it was read with, the split key's verdict, the
/// class a placement the key counts wrong is quoted apart in, the verdict of the key as its readers left
/// it, and what they made of this placement where they read it.
/// </summary>
internal sealed record EvidentiaWordStateRecord(
    EvidentiaWordState State,
    string? Rule,
    bool Grammatical,
    long? HeadWordId,
    EvidentiaHeadState? HeadState,
    bool? Right,
    IReadOnlyList<long> KeyTargets,
    EvidentiaBoundaryClass Boundary = EvidentiaBoundaryClass.None,
    bool? RightByTheJudgedKey = null,
    EvidentiaKeyVerdict? Reading = null);

/// <summary>
/// How many words stand in a state or came from a rule, and how many of those the key can judge and calls
/// right. <see cref="Boundary"/> and <see cref="BoundaryAttached"/> are the words among those it calls
/// wrong that stand in the two chunk-boundary classes, and <see cref="SafeBoundary"/> those of either in
/// the safe tier. <see cref="RightByTheJudgedKey"/> and <see cref="SafeRightByTheJudgedKey"/> are the words
/// right by the key as its readers left it (<see cref="EvidentiaJudgedKey"/>).
/// </summary>
internal readonly record struct EvidentiaStateCount(
    int Words,
    int Judged,
    int Right,
    int SafeJudged,
    int SafeRight,
    int Boundary = 0,
    int BoundaryAttached = 0,
    int SafeBoundary = 0,
    int RightByTheJudgedKey = 0,
    int SafeRightByTheJudgedKey = 0)
{
    public static EvidentiaStateCount operator +(EvidentiaStateCount one, EvidentiaStateCount two) => new(
        one.Words + two.Words, one.Judged + two.Judged, one.Right + two.Right,
        one.SafeJudged + two.SafeJudged, one.SafeRight + two.SafeRight,
        one.Boundary + two.Boundary, one.BoundaryAttached + two.BoundaryAttached, one.SafeBoundary + two.SafeBoundary,
        one.RightByTheJudgedKey + two.RightByTheJudgedKey, one.SafeRightByTheJudgedKey + two.SafeRightByTheJudgedKey);
}

/// <summary>
/// A passage measured on the split key and by state. A pair count rewards one state of five, so the
/// share of words and the precision are given for each: a word linked, attached or a phrase member is
/// right where the split key names its pair, a supplied word where the key's links leave it nothing to
/// render, and an unresolved word is neither.
///
/// <para>Every precision is quoted three ways: by the key, with the placements of a chunk-boundary
/// class (<see cref="EvidentiaChunkBoundary"/>) counted right, each class apart, and against the judged
/// key, which is the key with the corrections of its readers (<see cref="EvidentiaJudgedKey"/>) and no
/// class counted by rule. <see cref="Loaded"/> is the same passage on the key as it was loaded;
/// <see cref="JudgedFinal"/>, <see cref="JudgedSafe"/> and <see cref="JudgedLoaded"/> are the placements
/// right by the judged key: of all, of the safe tier, and on the key as loaded.</para>
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
    IReadOnlyDictionary<EvidentiaHeadState, int> Cascade,
    EvidentiaTierScore Loaded = default,
    EvidentiaBoundaryCount Boundary = default,
    int JudgedFinal = 0,
    int JudgedSafe = 0,
    int JudgedLoaded = 0,
    EvidentiaJudgedCount Readings = default)
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
        Merge(one.Cascade, two.Cascade, (first, second) => first + second),
        one.Loaded + two.Loaded,
        one.Boundary + two.Boundary,
        one.JudgedFinal + two.JudgedFinal,
        one.JudgedSafe + two.JudgedSafe,
        one.JudgedLoaded + two.JudgedLoaded,
        one.Readings + two.Readings);

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

    public const string JudgedName = "against the judged key";

    /// <summary>A figure by the key, then with each class counted right, the classes apart.</summary>
    private static string BothWays(int right, int judged, int boundary, int attached) =>
        $"{Share(right, judged)} by the key; with {EvidentiaChunkBoundary.PrefixName} counted right {Share(right + boundary, judged)}; " +
        $"with {EvidentiaChunkBoundary.AttachedName} {Share(right + attached, judged)}";

    private static string Classes(EvidentiaStateCount count) =>
        $"with {EvidentiaChunkBoundary.PrefixName} counted right {Share(count.Right + count.Boundary, count.Judged)}; " +
        $"with {EvidentiaChunkBoundary.AttachedName} {Share(count.Right + count.BoundaryAttached, count.Judged)}";

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
            $"chunk boundary: {EvidentiaChunkBoundary.PrefixName} {Share(Boundary.Prefix, Final.OnCoveredWords)} of the placed words the key judges, " +
            $"conjunctions {Share(Boundary.PrefixConjunctions, Boundary.Prefix)}, on a written word the key leaves out " +
            $"{Share(Boundary.PrefixOnWordsLeftOut, Boundary.Prefix)}; {EvidentiaChunkBoundary.AttachedName} " +
            $"{Share(Boundary.Attached, Final.OnCoveredWords)}, auxiliaries {Share(Boundary.AttachedAuxiliaries, Boundary.Attached)}, " +
            $"on a verb the key leaves out beside its infinitive absolute {Share(Boundary.AttachedBesideInfinitives, Boundary.Attached)}",
            $"key as loaded, pairs, three ways: {BothWays(Loaded.Correct, Loaded.OnCoveredWords, Boundary.Prefix, Boundary.Attached)}; " +
            $"with both {Share(Loaded.Correct + Boundary.Prefix + Boundary.Attached, Loaded.OnCoveredWords)}; " +
            $"{JudgedName} {Share(JudgedLoaded, Loaded.OnCoveredWords)}",
            $"split key, pairs, three ways: {BothWays(Final.Correct, Final.OnCoveredWords, Boundary.Prefix, Boundary.Attached)}; " +
            $"with both {Share(Final.Correct + Boundary.Prefix + Boundary.Attached, Final.OnCoveredWords)}; " +
            $"safe tier with both {Share(Safe.Correct + Boundary.SafePrefix + Boundary.SafeAttached, Safe.OnCoveredWords)}; " +
            $"{JudgedName} {Share(JudgedFinal, Final.OnCoveredWords)}; safe tier {JudgedName} {Share(JudgedSafe, Safe.OnCoveredWords)}",
            $"judged key: corrected {Share(Readings.Corrected, Readings.Readings)} of the readings of the key on these words, " +
            $"defensible {Share(Readings.Defensible, Readings.Readings)}, confirmed {Share(Readings.Confirmed, Readings.Readings)}, " +
            $"neither {Share(Readings.Neither, Readings.Readings)}, unsettled {Share(Readings.Unsettled, Readings.Readings)}; " +
            $"no longer reading as the text does and not applied {Share(Readings.Stale, Readings.Readings + Readings.Stale)}; " +
            $"wrong by the key and read by nobody {Share(Readings.Unread, Readings.WrongByTheKey)}",
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
                    $"by state, {Name(state)}: {Share(count.Words, SourceWords)} of words; right {Share(count.Right, count.Judged)}; " +
                    $"{JudgedName} {Share(count.RightByTheJudgedKey, count.Judged)}",
                _ =>
                    $"by state, {Name(state)}: {Share(count.Words, SourceWords)} of words; right {Share(count.Right, count.Judged)}; " +
                    $"safe tier {Share(count.SafeRight, count.SafeJudged)}; {Classes(count)}; " +
                    $"safe tier with both {Share(count.SafeRight + count.SafeBoundary, count.SafeJudged)}; " +
                    $"{JudgedName} {Share(count.RightByTheJudgedKey, count.Judged)}; " +
                    $"safe tier {JudgedName} {Share(count.SafeRightByTheJudgedKey, count.SafeJudged)}",
            });
        }

        var explained = States.Where(pair => pair.Key != EvidentiaWordState.Unresolved).ToList();
        lines.Add(
            $"by state, explained: {Share(explained.Sum(pair => pair.Value.Words), SourceWords)} of words; " +
            $"right {Share(explained.Sum(pair => pair.Value.Right), SourceWords)} of all words; with both chunk-boundary classes " +
            $"{Share(explained.Sum(pair => pair.Value.Right + pair.Value.Boundary + pair.Value.BoundaryAttached), SourceWords)}; " +
            $"{JudgedName} {Share(explained.Sum(pair => pair.Value.RightByTheJudgedKey), SourceWords)}");
        lines.AddRange(Rules
            .OrderBy(pair => pair.Key.State)
            .ThenByDescending(pair => pair.Value.Words)
            .ThenBy(pair => pair.Key.Rule, StringComparer.Ordinal)
            .Select(pair => $"by rule, {Name(pair.Key.State)}, {pair.Key.Rule}: {pair.Value.Words:N0} words; " +
                            $"right {Share(pair.Value.Right, pair.Value.Judged)}; {Classes(pair.Value)}; " +
                            $"{JudgedName} {Share(pair.Value.RightByTheJudgedKey, pair.Value.Judged)}"));
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
        [.. AttachedWords.PhraseAttachments.Select(Enum.Parse<EvidentiaAttachment>)];

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
        IReadOnlyDictionary<(long From, long To), EvidentiaBoundaryCase> boundary,
        out IReadOnlyDictionary<long, EvidentiaWordStateRecord> records,
        EvidentiaJudgedKey? judged = null)
    {
        judged ??= EvidentiaJudgedKey.None;
        var words = source.DistinctBy(word => word.Token.Id).ToList();
        var proposalBySource = proposals.GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var supplied = absences.Where(absence => absence.Kind == EvidentiaAbsenceKind.Supplied)
            .Select(absence => absence.Word.Token.Id).ToHashSet();
        var keyTargets = key.Pairs.GroupBy(pair => pair.From)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<long>)[.. group.Select(pair => pair.To)]);
        var states = new Dictionary<long, (EvidentiaWordState State, string? Rule, bool? Right)>();
        var judgedRight = new Dictionary<long, bool?>();
        foreach (var word in words)
        {
            var id = word.Token.Id;
            if (proposalBySource.TryGetValue(id, out var proposal))
            {
                var pair = (id, proposal.Target.Token.Id);
                states[id] = (StateOf(proposal), RuleOf(proposal),
                    gold.CoveredSourceWords.Contains(id) ? accepted.Contains(pair) : null);
                judgedRight[id] = judged.Verdict(id, proposal.Target.Token.Id, states[id].Right);
            }
            else if (supplied.Contains(id))
            {
                states[id] = (EvidentiaWordState.Supplied, null, absenceVerdicts.GetValueOrDefault(id));
                judgedRight[id] = judged.Supplied(id, states[id].Right);
            }
            else
            {
                states[id] = (EvidentiaWordState.Unresolved, null, null);
            }
        }

        EvidentiaBoundaryClass Boundary(long id) =>
            proposalBySource.TryGetValue(id, out var placement) && states[id].Right == false
                ? boundary.GetValueOrDefault((id, placement.Target.Token.Id)).Class
                : EvidentiaBoundaryClass.None;

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
                    keyTargets.GetValueOrDefault(word.Token.Id) ?? [],
                    Boundary(word.Token.Id),
                    judgedRight.GetValueOrDefault(word.Token.Id),
                    proposalBySource.TryGetValue(word.Token.Id, out var placement)
                        ? judged.Reading(word.Token.Id, placement.Target.Token.Id)
                        : state == EvidentiaWordState.Supplied ? judged.ReadingOfSupplied(word.Token.Id) : null);
            });

        var byState = new Dictionary<EvidentiaWordState, EvidentiaStateCount>();
        var byRule = new Dictionary<(EvidentiaWordState, string), EvidentiaStateCount>();
        foreach (var word in words)
        {
            var id = word.Token.Id;
            var (state, rule, right) = states[id];
            var isSafe = proposalBySource.TryGetValue(id, out var proposal) && safe.Contains((id, proposal.Target.Token.Id));
            var found = Boundary(id);
            var count = new EvidentiaStateCount(
                1, right is null ? 0 : 1, right == true ? 1 : 0,
                isSafe && right is not null ? 1 : 0, isSafe && right == true ? 1 : 0,
                found == EvidentiaBoundaryClass.Prefix ? 1 : 0, found == EvidentiaBoundaryClass.Attached ? 1 : 0,
                isSafe && found != EvidentiaBoundaryClass.None ? 1 : 0,
                judgedRight.GetValueOrDefault(id) == true ? 1 : 0,
                isSafe && judgedRight.GetValueOrDefault(id) == true ? 1 : 0);
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
                .ToDictionary(group => group.Key, group => group.Count()),
            EvidentiaTierScore.Of(proposals, gold.Pairs, gold.CoveredSourceWords),
            EvidentiaChunkBoundary.Count(boundary, safe),
            JudgedPairs(proposals, key.Pairs),
            JudgedPairs(safeProposals, key.Pairs),
            JudgedPairs(proposals, gold.Pairs),
            Readings());

        // By pair a placement is right where the readers found it right, wrong where they found it wrong,
        // and otherwise where the key names the pair: the folded conjunctions the by-word score pairs in
        // order count in the figures by state and not here, as they do by the key.
        int JudgedPairs(IEnumerable<EvidentiaProposal> placed, IReadOnlySet<(long From, long To)> pairs) =>
            placed.Count(proposal => gold.CoveredSourceWords.Contains(proposal.Source.Token.Id)
                && judged.Verdict(
                    proposal.Source.Token.Id, proposal.Target.Token.Id,
                    pairs.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))) == true);

        EvidentiaJudgedCount Readings()
        {
            var wrong = words.Where(word => states[word.Token.Id].Right == false).ToList();
            return new EvidentiaJudgedCount(
                judged.Readings,
                judged.Count(EvidentiaKeyVerdict.Corrected),
                judged.Count(EvidentiaKeyVerdict.Defensible),
                judged.Count(EvidentiaKeyVerdict.Confirmed),
                judged.Count(EvidentiaKeyVerdict.Neither),
                judged.Count(EvidentiaKeyVerdict.Unsettled),
                judged.Stale,
                wrong.Count,
                wrong.Count(word => proposalBySource.TryGetValue(word.Token.Id, out var placement)
                    ? !judged.HasRead(word.Token.Id, placement.Target.Token.Id)
                    : !judged.HasReadSupplied(word.Token.Id)));
        }
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
