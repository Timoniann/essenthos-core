namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>Why a placement the key counts wrong is quoted apart from the other wrong ones.</summary>
internal enum EvidentiaBoundaryClass
{
    None,

    /// <summary>
    /// A conjunction or preposition on a free prefix of its own kind, written on the word whose rendering
    /// it stands directly before: <em>In</em> of <em>In those days</em> on the בַּ of בַּיָּמִים.
    /// </summary>
    Prefix,

    /// <summary>
    /// An auxiliary or a personal pronoun on the word of the word it belongs to, where the key chunks it
    /// with the word beside it: <em>May</em> of <em>May the LORD answer</em> on the verb.
    /// </summary>
    Attached,
}

/// <summary>One placement of a class, and which of the class's kinds it is.</summary>
internal readonly record struct EvidentiaBoundaryCase(
    EvidentiaBoundaryClass Class,
    bool Conjunction = false,
    bool WordLeftOut = false,
    bool Auxiliary = false,
    bool BesideItsInfinitive = false);

/// <summary>How many placements the key counts wrong stand in each class, and how many of them in the safe tier.</summary>
internal readonly record struct EvidentiaBoundaryCount(
    int Prefix,
    int PrefixConjunctions,
    int PrefixOnWordsLeftOut,
    int Attached,
    int AttachedAuxiliaries,
    int SafePrefix,
    int SafeAttached,
    int AttachedBesideInfinitives = 0)
{
    public static EvidentiaBoundaryCount operator +(EvidentiaBoundaryCount one, EvidentiaBoundaryCount two) => new(
        one.Prefix + two.Prefix,
        one.PrefixConjunctions + two.PrefixConjunctions,
        one.PrefixOnWordsLeftOut + two.PrefixOnWordsLeftOut,
        one.Attached + two.Attached,
        one.AttachedAuxiliaries + two.AttachedAuxiliaries,
        one.SafePrefix + two.SafePrefix,
        one.SafeAttached + two.SafeAttached,
        one.AttachedBesideInfinitives + two.AttachedBesideInfinitives);
}

/// <summary>
/// The Berean tables link an English chunk with a written Hebrew word, and the chunk follows the English
/// phrase: <em>In those</em> with הָהֵם and <em>days</em> with בַּיָּמִים, <em>or take</em> with תִּשְׂאוּ and
/// <em>their daughters</em> with וּבְנֹתֵיהֶם. The split of the key cuts inside a link and never between
/// written words, so a word placed where the convention wants it - <em>and</em>, <em>from</em>,
/// <em>to</em>, <em>in</em>, <em>as</em> on the prefix of its own kind - is counted wrong where the chunk
/// put it with the word beside it. Those placements are named here, from the key and the two texts
/// alone, so that every figure can be quoted both ways; nothing here reads how a word was placed.
///
/// <para><see cref="EvidentiaBoundaryClass.Prefix"/>: a coordinator or a preposition on a prefix of its
/// own kind that no word of the key takes, where the key gives the word nothing of its kind and the
/// prefix is written on the word whose rendering the English word stands directly before. Directly:
/// with nothing between but words of its own link and an article, and no punctuation. Where the key
/// leaves the whole written word out of every link (וְאֶת, וְכָל), the prefix counts when that word is
/// written directly before the first word the English word's own link names and the English word opens
/// its chunk: <em>and his sons</em> with בָּנָיו, <em>and</em> on the ו of וְאֶת before it. An article is
/// never of the class: <em>the</em> is as often supplied beside an article another word renders.</para>
///
/// <para><see cref="EvidentiaBoundaryClass.Attached"/>: an auxiliary or a personal pronoun placed on the
/// very word the key names for the word the parse hangs it on - the verb of an auxiliary, a subject or
/// an object, the noun of a possessive - where that word writes it (a verb for an auxiliary; an ending
/// or a suffix of the pronoun's person) and the key's own link for it is a chunk with the word standing
/// beside it that names nothing else that could: no verb for an auxiliary; no verb, pronoun, preposition
/// or suffixed word for a pronoun, and for a subject no noun, name, adjective or numeral either, since
/// <em>He</em> of <em>and He separated</em> is the אֱלֹהִים the original writes. Where the original writes
/// the verb twice, the infinitive absolute beside the finite form (מוֹת תָּמוּת), and the key puts the whole
/// phrase <em>you will surely die</em> on the infinitive and leaves the finite verb out of every link, the
/// finite verb is the word the auxiliary and the subject belong on, and the infinitive is no other verb.</para>
///
/// <para>Whatever fails one of these stays wrong: a prefix another word of the key takes, a word whose
/// link names a word of its kind elsewhere (<em>from</em> with מִמֶּנּוּ), a conjunction reordered away
/// from its clause, an auxiliary whose key names another verb.</para>
/// </summary>
internal static class EvidentiaChunkBoundary
{
    public const string PrefixName = "chunk-boundary";

    public const string AttachedName = "chunk-boundary-attached";

    private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase) { "the", "a", "an" };

    private static readonly HashSet<string> AuxiliaryRelations = new(StringComparer.Ordinal) { "aux", "aux:pass" };

    /// <summary>The relations by which a personal pronoun is written in a verb's ending.</summary>
    private static readonly HashSet<string> SubjectRelations = new(StringComparer.Ordinal) { "nsubj", "nsubj:pass" };

    /// <summary>The relations by which a personal pronoun is written in a suffix of the verb or the noun.</summary>
    private static readonly HashSet<string> SuffixRelations = new(StringComparer.Ordinal) { "obj", "iobj", "nmod:poss" };

    /// <summary>The kinds of word of an original that write a person themselves.</summary>
    private static readonly HashSet<string> WritesAPerson = ["verb", "pron", "adp"];

    /// <summary>The kinds of word an original writes its subject with, where it writes one.</summary>
    private static readonly HashSet<string> Nominals = ["noun", "propn", "adj", "num"];

    private const string PersonalPronoun = "Prs";

    private const string SuffixPerson = "suffixPerson";

    private const string InfinitiveAbsolute = "infa";

    public static string? Name(EvidentiaBoundaryClass boundary) => boundary switch
    {
        EvidentiaBoundaryClass.Prefix => PrefixName,
        EvidentiaBoundaryClass.Attached => AttachedName,
        _ => null,
    };

    /// <summary>The placements of a class among those the key judges and does not accept.</summary>
    public static IReadOnlyDictionary<(long From, long To), EvidentiaBoundaryCase> Of(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaProposal> proposals,
        EvidentiaGold gold,
        EvidentiaSplitKey key,
        IReadOnlySet<(long From, long To)> accepted)
    {
        var reading = new Reading(source, target, gold, key);
        var cases = new Dictionary<(long From, long To), EvidentiaBoundaryCase>();
        foreach (var proposal in proposals)
        {
            var pair = (proposal.Source.Token.Id, proposal.Target.Token.Id);
            if (!gold.CoveredSourceWords.Contains(pair.Item1) || accepted.Contains(pair) || gold.Pairs.Contains(pair))
            {
                continue;
            }

            if ((reading.OnAFreePrefix(proposal.Source, proposal.Target) ?? reading.OnTheWordItBelongsTo(proposal.Source, proposal.Target))
                is { } found)
            {
                cases[pair] = found;
            }
        }

        return cases;
    }

    public static EvidentiaBoundaryCount Count(
        IReadOnlyDictionary<(long From, long To), EvidentiaBoundaryCase> cases,
        IReadOnlySet<(long From, long To)> safe)
    {
        int Of(EvidentiaBoundaryClass boundary, Func<EvidentiaBoundaryCase, bool>? kind = null, bool safeOnly = false) =>
            cases.Count(found => found.Value.Class == boundary && (kind is null || kind(found.Value))
                && (!safeOnly || safe.Contains(found.Key)));

        return new EvidentiaBoundaryCount(
            Of(EvidentiaBoundaryClass.Prefix),
            Of(EvidentiaBoundaryClass.Prefix, found => found.Conjunction),
            Of(EvidentiaBoundaryClass.Prefix, found => found.WordLeftOut),
            Of(EvidentiaBoundaryClass.Attached),
            Of(EvidentiaBoundaryClass.Attached, found => found.Auxiliary),
            Of(EvidentiaBoundaryClass.Prefix, safeOnly: true),
            Of(EvidentiaBoundaryClass.Attached, safeOnly: true),
            Of(EvidentiaBoundaryClass.Attached, found => found.BesideItsInfinitive));
    }

    private sealed class Reading
    {
        private readonly EvidentiaGold gold;
        private readonly EvidentiaSplitKey key;
        private readonly Dictionary<long, EvidentiaAnalysis> sourceById;
        private readonly Dictionary<long, EvidentiaAnalysis> targetById;
        private readonly Dictionary<EvidentiaAddress, List<EvidentiaAnalysis>> verses;
        private readonly Dictionary<long, (EvidentiaAddress Verse, int Word)> written;
        private readonly ILookup<(EvidentiaAddress Verse, int Word), EvidentiaAnalysis> parts;
        private readonly ILookup<long, long> keyTargets;
        private readonly ILookup<long, long> keySources;
        private readonly ILookup<long, long> loadedTargets;
        private readonly Dictionary<long, HashSet<long>> chunks = [];
        private readonly HashSet<long> named;

        public Reading(
            IReadOnlyList<EvidentiaAnalysis> source,
            IReadOnlyList<EvidentiaAnalysis> target,
            EvidentiaGold gold,
            EvidentiaSplitKey key)
        {
            this.gold = gold;
            this.key = key;
            sourceById = source.DistinctBy(word => word.Token.Id).ToDictionary(word => word.Token.Id);
            targetById = target.DistinctBy(word => word.Token.Id).ToDictionary(word => word.Token.Id);
            verses = sourceById.Values.GroupBy(word => word.Token.Address)
                .ToDictionary(verse => verse.Key, verse => verse.OrderBy(word => word.Token.Position).ToList());
            written = EvidentiaKeySplit.WrittenWords(targetById.Values);
            parts = targetById.Values.OrderBy(word => word.Token.Position).ToLookup(word => written[word.Token.Id]);
            keyTargets = key.Pairs.ToLookup(pair => pair.From, pair => pair.To);
            keySources = key.Pairs.ToLookup(pair => pair.To, pair => pair.From);
            loadedTargets = gold.Pairs.ToLookup(pair => pair.From, pair => pair.To);
            named = gold.Links.SelectMany(link => link.TargetWords).ToHashSet();
            foreach (var link in gold.Links)
            {
                foreach (var word in link.SourceWords)
                {
                    if (!chunks.TryGetValue(word, out var chunk))
                    {
                        chunks[word] = chunk = [];
                    }

                    chunk.UnionWith(link.SourceWords);
                }
            }
        }

        public EvidentiaBoundaryCase? OnAFreePrefix(EvidentiaAnalysis word, EvidentiaAnalysis prefix)
        {
            if (!EvidentiaKeySplit.IsPrefix(prefix)
                || EvidentiaAbsences.FunctionClass(prefix) is not { } kind || kind == EvidentiaFunctionClass.Article
                || !EvidentiaKeySplit.Renders(word, kind) || EvidentiaKeySplit.IsInDoubtOn(word, prefix))
            {
                return null;
            }

            var ownKindElsewhere = keyTargets[word.Token.Id]
                .Any(to => targetById.TryGetValue(to, out var own) && EvidentiaAbsences.FunctionClass(own) == kind);
            if (keySources[prefix.Token.Id].Any() || ownKindElsewhere || !written.TryGetValue(prefix.Token.Id, out var place))
            {
                return null;
            }

            var conjunction = kind == EvidentiaFunctionClass.Coordinator;
            var rendering = parts[place].Where(part => !EvidentiaKeySplit.IsPrefix(part))
                .SelectMany(stem => keySources[stem.Token.Id]).Distinct()
                .Select(id => sourceById.GetValueOrDefault(id)).OfType<EvidentiaAnalysis>().ToList();
            if (rendering.Count > 0)
            {
                return !gold.UnrenderedTargetWords.Contains(prefix.Token.Id) && StandsDirectlyBefore(word, rendering)
                    ? new EvidentiaBoundaryCase(EvidentiaBoundaryClass.Prefix, conjunction)
                    : null;
            }

            return !parts[place].Any(part => named.Contains(part.Token.Id)) && OpensItsChunk(word)
                && IsWrittenDirectlyBeforeItsLink(place, word)
                    ? new EvidentiaBoundaryCase(EvidentiaBoundaryClass.Prefix, conjunction, WordLeftOut: true)
                    : null;
        }

        public EvidentiaBoundaryCase? OnTheWordItBelongsTo(EvidentiaAnalysis word, EvidentiaAnalysis placement)
        {
            var wordClass = EvidentiaAttachedWords.Class(word);
            var relation = word.Token.Relation ?? string.Empty;
            var auxiliary = wordClass == "aux" && AuxiliaryRelations.Contains(relation);
            var pronoun = wordClass == "pron" && Feature(word, "PronType") == PersonalPronoun;
            var subject = pronoun && SubjectRelations.Contains(relation);
            var suffixed = pronoun && SuffixRelations.Contains(relation);
            if (!auxiliary && !subject && !suffixed || word.Token.SyntacticHead is not { } head)
            {
                return null;
            }

            var infinitive = key.Pairs.Contains((head, placement.Token.Id)) ? null : InfinitiveBeside(head, placement);
            if (infinitive is null && !key.Pairs.Contains((head, placement.Token.Id)))
            {
                return null;
            }

            var placedOn = EvidentiaAttachedWords.Class(placement);
            var writesIt = auxiliary ? placedOn == "verb"
                : subject ? placedOn == "verb" && EvidentiaPersonAgreement.Agrees(word, placement, suffix: false)
                : EvidentiaPersonAgreement.Agrees(word, placement, suffix: true);
            var own = keyTargets[word.Token.Id].Select(id => targetById.GetValueOrDefault(id)).OfType<EvidentiaAnalysis>().ToList();
            var writtenElsewhere = own.Any(other => other.Token.Id != infinitive?.Token.Id
                && EvidentiaAttachedWords.Class(other) is var otherClass && (auxiliary
                    ? otherClass == "verb"
                    : otherClass is not null && (WritesAPerson.Contains(otherClass) || subject && Nominals.Contains(otherClass))
                      || Feature(other, SuffixPerson) is not null));
            return writesIt && own.Count > 0 && !writtenElsewhere && IsChunkedWithItsNeighbour(word, head)
                ? new EvidentiaBoundaryCase(EvidentiaBoundaryClass.Attached, Auxiliary: auxiliary, BesideItsInfinitive: infinitive is not null)
                : null;
        }

        /// <summary>
        /// The infinitive absolute of the placement's own lexeme, written directly beside it, where the key
        /// names the infinitive for the head and leaves the placement out of every link.
        /// </summary>
        private EvidentiaAnalysis? InfinitiveBeside(long head, EvidentiaAnalysis placement) =>
            named.Contains(placement.Token.Id) || EvidentiaAttachedWords.Class(placement) != "verb"
            || placement.Token.StrongNumber is not { } lexeme || !written.TryGetValue(placement.Token.Id, out var place)
                ? null
                : keyTargets[head].Select(id => targetById.GetValueOrDefault(id)).OfType<EvidentiaAnalysis>()
                    .FirstOrDefault(other => other.Token.StrongNumber == lexeme
                        && string.Equals(Feature(other, "tense"), InfinitiveAbsolute, StringComparison.OrdinalIgnoreCase)
                        && written.TryGetValue(other.Token.Id, out var beside) && beside.Verse.Equals(place.Verse)
                        && Math.Abs(beside.Word - place.Word) == 1);

        private bool StandsDirectlyBefore(EvidentiaAnalysis word, IReadOnlyList<EvidentiaAnalysis> rendering)
        {
            if (rendering.Any(other => !other.Token.Address.Equals(word.Token.Address) || other.Token.Position <= word.Token.Position))
            {
                return false;
            }

            var first = rendering.Min(other => other.Token.Position);
            var chunk = chunks.GetValueOrDefault(word.Token.Id);
            return verses[word.Token.Address]
                .Where(other => other.Token.Position >= word.Token.Position && other.Token.Position < first)
                .All(other => !Punctuated(other)
                    && (other.Token.Id == word.Token.Id || chunk?.Contains(other.Token.Id) == true || Articles.Contains(other.Token.Surface)));
        }

        private bool OpensItsChunk(EvidentiaAnalysis word) =>
            chunks.TryGetValue(word.Token.Id, out var chunk)
            && !chunk.Any(id => sourceById.TryGetValue(id, out var other)
                && other.Token.Address.Equals(word.Token.Address) && other.Token.Position < word.Token.Position);

        private bool IsWrittenDirectlyBeforeItsLink((EvidentiaAddress Verse, int Word) place, EvidentiaAnalysis word)
        {
            var own = loadedTargets[word.Token.Id].Where(written.ContainsKey).Select(id => written[id]).ToList();
            return own.Count > 0 && own.All(other => other.Verse.Equals(place.Verse)) && own.Min(other => other.Word) == place.Word + 1;
        }

        private bool IsChunkedWithItsNeighbour(EvidentiaAnalysis word, long head) =>
            chunks.TryGetValue(word.Token.Id, out var chunk)
            && chunk.Any(id => id != head && sourceById.TryGetValue(id, out var other)
                && other.Token.Address.Equals(word.Token.Address)
                && Math.Abs(other.Token.Position - word.Token.Position) == 1);

        private static bool Punctuated(EvidentiaAnalysis word) => word.Token.Trailer.Any(char.IsPunctuation);

        private static string? Feature(EvidentiaAnalysis word, string name) =>
            word.Token.Morphology?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    }
}
