namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>What a pair of the split key joins.</summary>
internal enum EvidentiaKeyPairKind
{
    /// <summary>A word on the stem of the written word its link names, or on a word written apart.</summary>
    Stem,

    /// <summary><em>and</em> on ו, <em>from</em> on מִ, <em>the</em> on הַ: a grammatical word on the prefix of its own kind.</summary>
    PrefixOfItsKind,

    /// <summary>
    /// <em>the</em> of <em>the king of Persia</em> on מֶלֶךְ, which is written without an article: the key
    /// links it with its noun, and a word said to be supplied there is right too.
    /// </summary>
    UnwrittenArticle,
}

/// <summary>
/// An answer key cut to the parts of a written Hebrew word. <see cref="PairsAsLoaded"/> is what the key
/// held before; <see cref="UnclaimedPrefixes"/> are the prefixes a link names and no word of its kind takes.
/// </summary>
internal sealed record EvidentiaSplitKey(
    IReadOnlySet<(long From, long To)> Pairs,
    IReadOnlyDictionary<(long From, long To), EvidentiaKeyPairKind> Kinds,
    IReadOnlySet<long> UnclaimedPrefixes,
    int PairsAsLoaded,
    IReadOnlyList<EvidentiaKeyDoubt> Doubts)
{
    public int Count(EvidentiaKeyPairKind kind) => Kinds.Values.Count(found => found == kind);
}

/// <summary>A link the split could not cut one way only, for a person to judge.</summary>
internal sealed record EvidentiaKeyDoubt(string Kind, long SourceWordId, IReadOnlyList<long> TargetWordIds);

/// <summary>
/// The Berean tables link an English word with a whole written Hebrew word, and BHSA writes that word's
/// prefixes as words of their own, so every such link reads as a pair per part: <em>said</em> with both
/// וַ and יֹּאמֶר. Nobody wants <em>said</em> on the conjunction, and a key holding that pair counts it
/// missing and counts a word placed on any part of the written word right.
///
/// <para>The split is by kind and chooses nothing between written words. Within one link a coordinator
/// (<em>and</em>, <em>but</em>, <em>or</em>, <em>then</em>) takes the ו, a preposition (<em>from</em>,
/// <em>in</em>, <em>to</em>, <em>as</em>, <em>like</em>, <em>of</em>, <em>when</em>) the prefixed
/// preposition, <em>the</em> the article; every other word of the link takes the stems. Words and prefixes
/// of one kind are paired in the order they are written; a second word of the kind standing directly
/// after the first shares its prefix (<em>because of</em> on בַּ), and a second prefix written on the same
/// word goes to the word that took the first (<em>from</em> on מִלִּ of מִלִּפְנֵי).</para>
///
/// <para>Three things keep a word on the stem as well as, or instead of, a prefix. <em>now</em>,
/// <em>then</em>, <em>so</em>, <em>yet</em> and <em>thus</em> render ו only where the written word has no
/// adverb of its own: in וְעַתָּה they are עַתָּה. A preposition written on a preposition is one word to
/// English (<em>above</em> on מֵעַל). And a stem no word of meaning is left for stays with the word that
/// took the prefix: <em>before You</em> on לְפָנֶיךָ leaves only the suffix's pronoun.</para>
///
/// <para>Greek links, and links with no prefix in them, are kept as they are, except that an English
/// article beside another word of a link whose Hebrew writes no article is marked: the key puts it on
/// the noun, and the measurement by word has always counted it right as supplied.</para>
/// </summary>
internal static class EvidentiaKeySplit
{
    /// <summary>The English words that render ו.</summary>
    private static readonly HashSet<string> Coordinators = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "but", "or", "nor", "then", "so", "now", "yet", "thus", "while",
    };

    /// <summary>Those of them that are as often the adverb written after the ו: עַתָּה, אָז, כֵּן, עוֹד, כֹּה.</summary>
    private static readonly HashSet<string> Adverbs = new(StringComparer.OrdinalIgnoreCase) { "then", "so", "now", "yet", "thus" };

    /// <summary>What renders a prefixed preposition without being one to the parser: <em>to</em> of an infinitive, <em>when</em> of בְּ with one.</summary>
    private static readonly HashSet<string> PrepositionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "to", "as", "like", "than", "when", "while",
    };

    private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase) { DefiniteArticle, "a", "an" };

    private const string DefiniteArticle = "the";

    private const string Genitive = "of";

    private const string Interrogative = "H9008";

    /// <summary>The prepositions <em>of</em> is a preposition's counterpart for: לְ and מִן.</summary>
    private static readonly HashSet<string> GenitivePrefixes = new(StringComparer.Ordinal) { "H9005", "H4480" };

    private static readonly HashSet<string> TemporalWords = new(StringComparer.OrdinalIgnoreCase) { "when", "while" };

    private static readonly HashSet<string> WordsOfMeaning = ["noun", "propn", "verb", "adj", "num", "adv"];

    private static readonly EvidentiaFunctionClass[] Kinds =
        [EvidentiaFunctionClass.Coordinator, EvidentiaFunctionClass.Preposition, EvidentiaFunctionClass.Article];

    public const string DoubtSharedPrefixes = "one word, two prefixes of its kind in one written word";

    public const string DoubtLaterWord = "more words of a kind than prefixes of it: the later word keeps the stem";

    public const string DoubtTemporal = "when or while on a preposition prefix";

    public const string DoubtGenitive = "of on a prefix other than ל or מ";

    public static EvidentiaSplitKey Of(
        EvidentiaGold gold,
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target)
    {
        var sourceById = source.DistinctBy(word => word.Token.Id).ToDictionary(word => word.Token.Id);
        var targetById = target.DistinctBy(word => word.Token.Id).ToDictionary(word => word.Token.Id);
        var written = WrittenWords(targetById.Values);
        var kinds = new Dictionary<(long From, long To), EvidentiaKeyPairKind>();
        var claimed = new HashSet<long>();
        var prefixes = new HashSet<long>();
        var doubts = new List<EvidentiaKeyDoubt>();

        void Add(long from, long to, EvidentiaKeyPairKind kind)
        {
            if (!kinds.TryGetValue((from, to), out var held) || held == EvidentiaKeyPairKind.UnwrittenArticle)
            {
                kinds[(from, to)] = kind;
            }
        }

        foreach (var link in gold.Links)
        {
            var known = link.SourceWords.All(sourceById.ContainsKey) && link.TargetWords.All(targetById.ContainsKey);
            if (!known || !link.TargetWords.Any(id => IsHebrew(targetById[id])))
            {
                foreach (var from in link.SourceWords)
                {
                    foreach (var to in link.TargetWords)
                    {
                        Add(from, to, EvidentiaKeyPairKind.Stem);
                    }
                }

                continue;
            }

            var english = link.SourceWords.Select(id => sourceById[id]).OrderBy(Order).ToList();
            var original = link.TargetWords.Select(id => targetById[id]).OrderBy(Order).ToList();
            var parts = original.Where(IsPrefix).ToList();
            var stems = original.Where(word => !IsPrefix(word)).ToList();
            prefixes.UnionWith(parts.Select(word => word.Token.Id));
            var takes = new Dictionary<long, List<EvidentiaAnalysis>>();
            var adverbStem = stems.Any(word => EvidentiaAttachedWords.Class(word) == "adv");
            foreach (var kind in Kinds)
            {
                var ofKind = parts.Where(word => EvidentiaAbsences.FunctionClass(word) == kind).ToList();
                if (ofKind.Count == 0)
                {
                    continue;
                }

                var words = english
                    .Where(word => !takes.ContainsKey(word.Token.Id) && Renders(word, kind)
                        && !(kind == EvidentiaFunctionClass.Coordinator && adverbStem && Adverbs.Contains(word.Token.Surface)))
                    .ToList();
                for (var index = 0; index < Math.Min(words.Count, ofKind.Count); index++)
                {
                    takes[words[index].Token.Id] = [ofKind[index]];
                }

                if (words.Count > 0 && ofKind.Count > words.Count)
                {
                    var last = words[^1];
                    var shared = ofKind.Skip(words.Count)
                        .Where(prefix => written[prefix.Token.Id] == written[takes[last.Token.Id][0].Token.Id])
                        .ToList();
                    if (shared.Count > 0)
                    {
                        takes[last.Token.Id].AddRange(shared);
                        doubts.Add(new EvidentiaKeyDoubt(DoubtSharedPrefixes, last.Token.Id, [.. takes[last.Token.Id].Select(word => word.Token.Id)]));
                    }
                }

                foreach (var later in words.Skip(ofKind.Count))
                {
                    var before = words.FirstOrDefault(word => takes.ContainsKey(word.Token.Id)
                        && word.Token.Address.Equals(later.Token.Address) && word.Token.Position == later.Token.Position - 1);
                    if (before is not null)
                    {
                        takes[later.Token.Id] = takes[before.Token.Id];
                    }
                    else
                    {
                        doubts.Add(new EvidentiaKeyDoubt(DoubtLaterWord, later.Token.Id, [.. ofKind.Select(word => word.Token.Id)]));
                    }
                }
            }

            foreach (var (from, taken) in takes)
            {
                claimed.UnionWith(taken.Select(word => word.Token.Id));
                foreach (var prefix in taken)
                {
                    Add(from, prefix.Token.Id, EvidentiaKeyPairKind.PrefixOfItsKind);
                }

                var word = sourceById[from];
                if (TemporalWords.Contains(word.Token.Surface) && EvidentiaAbsences.FunctionClass(taken[0]) == EvidentiaFunctionClass.Preposition)
                {
                    doubts.Add(new EvidentiaKeyDoubt(DoubtTemporal, from, [.. taken.Select(prefix => prefix.Token.Id)]));
                }

                if (word.Token.Surface.Equals(Genitive, StringComparison.OrdinalIgnoreCase)
                    && !(taken[0].Token.StrongNumber is { } strong && GenitivePrefixes.Contains(strong)))
                {
                    doubts.Add(new EvidentiaKeyDoubt(DoubtGenitive, from, [.. taken.Select(prefix => prefix.Token.Id)]));
                }

                // A preposition written on a preposition is one word to English: above on מֵעַל.
                foreach (var stem in stems.Where(stem =>
                             EvidentiaAbsences.FunctionClass(stem) == EvidentiaFunctionClass.Preposition
                             && taken.Any(prefix => EvidentiaAbsences.FunctionClass(prefix) == EvidentiaFunctionClass.Preposition
                                 && written[prefix.Token.Id] == written[stem.Token.Id])))
                {
                    Add(from, stem.Token.Id, EvidentiaKeyPairKind.Stem);
                }
            }

            var rest = english.Where(word => !takes.ContainsKey(word.Token.Id)).ToList();
            var keepers = rest.Any(word => EvidentiaAttachedWords.Class(word) is { } wordClass && WordsOfMeaning.Contains(wordClass))
                ? rest
                : [.. rest, .. english.Where(word => takes.ContainsKey(word.Token.Id))];
            // Nothing but prefixes in the link: whatever no word of its kind took is all the others have.
            var theirs = stems.Count > 0 ? stems : [.. parts.Where(word => !claimed.Contains(word.Token.Id))];
            foreach (var word in keepers)
            {
                var article = english.Count > 1 && !takes.ContainsKey(word.Token.Id) && Articles.Contains(word.Token.Surface);
                foreach (var stem in theirs)
                {
                    Add(word.Token.Id, stem.Token.Id, article ? EvidentiaKeyPairKind.UnwrittenArticle : EvidentiaKeyPairKind.Stem);
                }
            }
        }

        var inScope = kinds.Keys.Where(gold.Pairs.Contains).ToHashSet();
        return new EvidentiaSplitKey(
            inScope,
            kinds.Where(pair => inScope.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value),
            prefixes.Where(id => !claimed.Contains(id)).ToHashSet(),
            gold.Pairs.Count,
            doubts);
    }

    /// <summary>
    /// A part of a written Hebrew word that BHSA writes as a word of its own: the conjunction, a
    /// preposition, the article or the interrogative, with nothing between it and the next word.
    /// </summary>
    public static bool IsPrefix(EvidentiaAnalysis word) =>
        IsHebrew(word) && word.Token.Trailer.Length == 0
        && (EvidentiaAbsences.FunctionClass(word) is not null || word.Token.StrongNumber == Interrogative);

    /// <summary>The written word each word of the original is part of, numbered within its verse.</summary>
    public static Dictionary<long, (EvidentiaAddress Verse, int Word)> WrittenWords(IEnumerable<EvidentiaAnalysis> target)
    {
        var written = new Dictionary<long, (EvidentiaAddress, int)>();
        foreach (var verse in target.GroupBy(word => word.Token.Address))
        {
            var number = 0;
            foreach (var word in verse.OrderBy(word => word.Token.Position))
            {
                written[word.Token.Id] = (verse.Key, number);
                if (word.Token.Trailer.Length > 0)
                {
                    number++;
                }
            }
        }

        return written;
    }

    /// <summary>Whether an English word is of the kind that renders a prefix of that kind.</summary>
    public static bool Renders(EvidentiaAnalysis word, EvidentiaFunctionClass kind) => kind switch
    {
        EvidentiaFunctionClass.Coordinator => Coordinators.Contains(word.Token.Surface),
        EvidentiaFunctionClass.Article => word.Token.Surface.Equals(DefiniteArticle, StringComparison.OrdinalIgnoreCase),
        _ => EvidentiaAttachedWords.Class(word) == "adp" || PrepositionWords.Contains(word.Token.Surface),
    };

    /// <summary>
    /// Whether a word on a prefix is a pair the split makes by order and leaves for a person to judge:
    /// <em>when</em> or <em>while</em> on a preposition, <em>of</em> on a preposition other than ל or מ.
    /// </summary>
    public static bool IsInDoubtOn(EvidentiaAnalysis word, EvidentiaAnalysis prefix) =>
        EvidentiaAbsences.FunctionClass(prefix) == EvidentiaFunctionClass.Preposition
        && (TemporalWords.Contains(word.Token.Surface)
            || word.Token.Surface.Equals(Genitive, StringComparison.OrdinalIgnoreCase)
            && !(prefix.Token.StrongNumber is { } strong && GenitivePrefixes.Contains(strong)));

    private static bool IsHebrew(EvidentiaAnalysis word) => word.Token.Language is "hbo" or "arc";

    private static (int, int, int) Order(EvidentiaAnalysis word) =>
        (word.Token.Address.Chapter, word.Token.Address.Verse, word.Token.Position);
}

/// <summary>One case the split key, the key as loaded and EVIDENTIA's placement do not settle between them.</summary>
internal sealed record EvidentiaKeyDoubtRecord(
    string Kind,
    int CanonicalBook,
    int CanonicalChapter,
    int CanonicalVerse,
    long SourceWordId,
    int SourcePosition,
    string SourceSurface,
    string? SourcePartOfSpeech,
    string Context,
    IReadOnlyList<string> SplitKey,
    IReadOnlyList<string> KeyAsLoaded,
    string? PlacedOn,
    string? Rule,
    string? Note);

/// <summary>
/// The cases left for a person: nothing here is decided by the split. A word placed on another part of
/// its written word than the split key names; a word placed on a prefix of its kind that its link does
/// not reach (the Berean chunks by the English phrase, so <em>In</em> of <em>In those days</em> stands in
/// the link of הָהֵם and its בַּ in the link of <em>days</em>); a word its link leaves on a stem while a
/// prefix of its kind stands unclaimed on the written word beside; and the links the split cut by order
/// because kind alone did not settle them.
/// </summary>
internal static class EvidentiaKeyDoubts
{
    private const int ContextBefore = 4;

    private const int ContextAfter = 5;

    private static readonly EvidentiaFunctionClass[] Kinds =
        [EvidentiaFunctionClass.Coordinator, EvidentiaFunctionClass.Preposition, EvidentiaFunctionClass.Article];

    private static readonly HashSet<string> IndefiniteArticles = new(StringComparer.OrdinalIgnoreCase) { "a", "an" };

    public static IReadOnlyList<EvidentiaKeyDoubtRecord> Of(
        EvidentiaGold gold,
        EvidentiaSplitKey key,
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaProposal> proposals,
        IReadOnlySet<(long From, long To)> accepted)
    {
        var targetById = target.DistinctBy(word => word.Token.Id).ToDictionary(word => word.Token.Id);
        var written = EvidentiaKeySplit.WrittenWords(targetById.Values);
        var verses = source.DistinctBy(word => word.Token.Id)
            .GroupBy(word => word.Token.Address)
            .ToDictionary(verse => verse.Key, verse => verse.OrderBy(word => word.Token.Position).ToList());
        var proposalBySource = proposals.GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var splitByWord = key.Pairs.ToLookup(pair => pair.From, pair => pair.To);
        var loadedByWord = gold.Pairs.ToLookup(pair => pair.From, pair => pair.To);
        var unclaimedByVerse = key.UnclaimedPrefixes.Where(targetById.ContainsKey)
            .Select(id => targetById[id])
            .ToLookup(word => word.Token.Address);
        var doubtsByWord = key.Doubts.ToLookup(doubt => doubt.SourceWordId);
        var rows = new List<EvidentiaKeyDoubtRecord>();

        IReadOnlyList<string> Surfaces(IEnumerable<long> ids) =>
        [
            .. ids.Where(targetById.ContainsKey).Select(id => targetById[id])
                .OrderBy(word => word.Token.Address.Verse).ThenBy(word => word.Token.Position)
                .Select(word => $"{(word.Token.Surface.Length == 0 ? "∅" : word.Token.Surface)} ({word.Token.StrongNumber})"),
        ];

        foreach (var (address, verse) in verses.OrderBy(pair => pair.Key.Chapter).ThenBy(pair => pair.Key.Verse))
        {
            for (var index = 0; index < verse.Count; index++)
            {
                var word = verse[index];
                var id = word.Token.Id;
                var proposal = proposalBySource.GetValueOrDefault(id);
                var context = string.Join(" ", verse
                    .Skip(Math.Max(0, index - ContextBefore)).Take(Math.Min(index, ContextBefore) + 1 + ContextAfter)
                    .Select(other => other.Token.Id == id ? $"[{other.Token.Surface}]" : other.Token.Surface));

                void Add(string kind, string? note = null) => rows.Add(new EvidentiaKeyDoubtRecord(
                    kind, address.Book, address.Chapter, address.Verse, id, word.Token.Position, word.Token.Surface,
                    word.PartOfSpeech ?? word.Token.PartOfSpeech, context, Surfaces(splitByWord[id]), Surfaces(loadedByWord[id]),
                    proposal is null ? null : $"{proposal.Target.Token.Surface} ({proposal.Target.Token.StrongNumber})",
                    proposal is null ? null : EvidentiaAttachedWords.Attachment(proposal)?.ToString() ?? proposal.Kind.ToString(),
                    note));

                var kinds = Kinds.Where(kind => EvidentiaKeySplit.Renders(word, kind)).ToList();
                var takesAPrefix = splitByWord[id].Any(to => key.Kinds[(id, to)] == EvidentiaKeyPairKind.PrefixOfItsKind);
                if (proposal is not null && gold.CoveredSourceWords.Contains(id))
                {
                    var pair = (id, proposal.Target.Token.Id);
                    var onAPrefix = EvidentiaKeySplit.IsPrefix(proposal.Target);
                    if (gold.Pairs.Contains(pair) && !key.Pairs.Contains(pair))
                    {
                        Add($"A placed on {(onAPrefix ? "a prefix" : "the stem")} of its written word; " +
                            $"the split key names {(takesAPrefix ? "the prefix of its kind" : "the stem")}",
                            accepted.Contains(pair) ? "counted right: a prefix of its kind in its own link" : "counted wrong");
                    }
                    else if (!gold.Pairs.Contains(pair) && onAPrefix
                             && EvidentiaAbsences.FunctionClass(proposal.Target) is { } kind && kinds.Contains(kind))
                    {
                        Add("B placed on a prefix of its kind outside its link; " +
                            (key.UnclaimedPrefixes.Contains(pair.Item2) ? "no word takes that prefix" : "another word takes that prefix"),
                            accepted.Contains(pair) ? "counted right: the folded conjunctions of the verse paired in order" : "counted wrong");
                    }
                }

                if (kinds.Count > 0 && !takesAPrefix && splitByWord[id].Any() && !IndefiniteArticles.Contains(word.Token.Surface))
                {
                    var own = loadedByWord[id].Where(written.ContainsKey).Select(to => written[to]).ToHashSet();
                    var beside = unclaimedByVerse[address]
                        .Where(prefix => EvidentiaAbsences.FunctionClass(prefix) is { } kind && kinds.Contains(kind)
                            && own.Any(mine => mine.Verse.Equals(written[prefix.Token.Id].Verse)
                                && Math.Abs(mine.Word - written[prefix.Token.Id].Word) == 1))
                        .ToList();
                    if (beside.Count > 0)
                    {
                        Add("C its link leaves it on a stem, and a prefix of its kind stands unclaimed on the written word beside",
                            string.Join(" ", Surfaces(beside.Select(prefix => prefix.Token.Id))));
                    }
                }

                foreach (var doubt in doubtsByWord[id])
                {
                    Add("D " + doubt.Kind);
                }
            }
        }

        return rows;
    }
}
