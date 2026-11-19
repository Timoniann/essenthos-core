namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>A word an attached-word rule placed: on which original word, by which rule, following which head.</summary>
internal sealed record EvidentiaSlavicPlacement(long Word, long Original, string Rule, long Head);

/// <summary>
/// The small words a Ukrainian or Russian translation writes around a word that is already placed, put on the original word
/// they render or whose form they spell out. Each rule needs two signals: the translation's own parse ties the word to a head
/// that is placed, and the original shows, at that head's rendering, the word or the form the small word stands for. The
/// forms, what each says of person, number, gender and case, and the original words each may render are the language's word
/// list (<see cref="EvidentiaWordList"/>); what follows is only how they are applied.
/// </summary>
internal static class EvidentiaSlavicAttachedWords
{
    /// <summary>How far, in the original's order, a word a small word renders may stand before its head's rendering.</summary>
    private const int Reach = 3;

    /// <summary>How far from a verb the original may write the pronoun that is its subject.</summary>
    private const int PronounReach = 5;

    /// <summary>
    /// How far apart, in the translation's order, a subject pronoun and its verb may stand: the parse's head is taken only where
    /// the words' order says the same, a second signal against a pronoun the parse hangs on another clause's verb.
    /// </summary>
    private const int SubjectReach = 3;

    private const string GreekArticle = "G3588";

    private const string GreekDe = "G1161";

    private const string GreekMen = "G3303";

    private const string CopulaRelation = "cop";

    /// <summary>אֵת, which BHSA classes as a preposition and no translation's preposition renders.</summary>
    private const string HebrewObjectMarker = "H853";

    private static readonly HashSet<string> GreekFiniteMoods = new(StringComparer.Ordinal) { "indicative", "subjunctive", "imperative", "optative" };

    private static readonly HashSet<string> HebrewFiniteTenses = new(StringComparer.Ordinal) { "perf", "impf", "wayq", "impv" };

    /// <summary>The UD relations by which a pronoun is the subject of its head.</summary>
    private static readonly HashSet<string> SubjectRelations = new(StringComparer.Ordinal) { "nsubj", "nsubj:pass" };

    public static IEnumerable<EvidentiaSlavicPlacement> Place(
        IEvidentiaSlavicWords language,
        IReadOnlyList<EvidentiaToken> verse,
        IReadOnlyList<EvidentiaToken> unplaced,
        IReadOnlyList<EvidentiaToken> original,
        IReadOnlyDictionary<long, IReadOnlyList<long>> placed)
    {
        var words = language.Words;
        var byId = verse.ToDictionary(token => token.Id);
        var originals = original.DistinctBy(token => token.Id).ToDictionary(token => token.Id);
        var originalVerses = originals.Values
            .GroupBy(token => token.Address)
            .ToDictionary(group => group.Key, group => group.OrderBy(token => token.Position).ToList());
        var reachedBy = new Dictionary<long, HashSet<long>>();
        foreach (var (word, targets) in placed)
        {
            foreach (var target in targets)
            {
                if (!reachedBy.TryGetValue(target, out var by))
                {
                    reachedBy[target] = by = [];
                }

                by.Add(word);
            }
        }

        var takenHere = new HashSet<long>();
        bool Free(long target, long word) =>
            !takenHere.Contains(target) && (!reachedBy.TryGetValue(target, out var by) || by.All(other => other == word));

        foreach (var word in unplaced)
        {
            var form = word.Surface.ToLowerInvariant();
            var head = word.SyntacticHead is { } headId && headId != word.Id && byId.TryGetValue(headId, out var parsed) ? parsed : null;
            var renderings = head is not null && placed.TryGetValue(head.Id, out var targets)
                ? targets.Where(originals.ContainsKey).Select(id => originals[id]).OrderBy(token => token.Position).ToList()
                : [];
            if (renderings.Count == 0 || head is null)
            {
                continue;
            }

            List<EvidentiaToken> Around(EvidentiaToken rendering) => originalVerses[rendering.Address];

            EvidentiaSlavicPlacement? placement = null;
            foreach (var row in words.Of(EvidentiaWordKinds.SubjectPronoun, form))
            {
                if (word.Relation is null || !SubjectRelations.Contains(word.Relation)
                    || Math.Abs(word.Position - head.Position) > SubjectReach)
                {
                    break;
                }

                var verbs = renderings.Where(rendering => IsFinite(rendering) && Agrees(row, rendering)).ToList();

                // A finite verb of the same person and number that no word renders may be the one the pronoun's own clause
                // leaves unwritten (що ми з правди, ὅτι ἐκ τῆς ἀληθείας ἐσμέν), so it bars the placement.
                if (verbs.Count == 1 && !Writes(row, Around(verbs[0]), verbs[0])
                    && !Around(verbs[0]).Any(other => other.Id != verbs[0].Id && IsFinite(other) && Agrees(row, other) && !reachedBy.ContainsKey(other.Id)))
                {
                    placement = new(word.Id, verbs[0].Id, EvidentiaWordKinds.SubjectPronoun, head.Id);
                    break;
                }
            }

            if (placement is null && words.Of(EvidentiaWordKinds.CasePreposition, form).ToList() is { Count: > 0 } prepositions
                && word.Relation == "case")
            {
                var noun = renderings[0];
                var around = Around(noun);
                var at = around.FindIndex(token => token.Id == noun.Id);
                var before = Before(around, at).ToList();
                var preposition = before.FirstOrDefault(token => token.PartOfSpeech == "prep" && token.StrongNumber != HebrewObjectMarker);
                if (preposition is not null)
                {
                    if (Free(preposition.Id, word.Id))
                    {
                        placement = new(word.Id, preposition.Id, "preposition-before-noun", head.Id);
                    }
                }
                else if (Case(noun) is { } nounCase && prepositions.Any(row => row.Case!.Split('|').Contains(nounCase)))
                {
                    placement = new(word.Id, noun.Id, "preposition-for-case", head.Id);
                }
            }

            if (placement is null)
            {
                foreach (var kind in new[] { EvidentiaWordKinds.Negation, EvidentiaWordKinds.Conjunction, EvidentiaWordKinds.Copula })
                {
                    var rows = words.Of(kind, form).ToList();
                    if (rows.Count == 0 || kind == EvidentiaWordKinds.Copula && word.Relation != CopulaRelation)
                    {
                        continue;
                    }

                    var rendering = renderings[0];
                    var around = Around(rendering);
                    var at = around.FindIndex(token => token.Id == rendering.Id);
                    var found = (kind == EvidentiaWordKinds.Copula ? Near(around, at) : Before(around, at))
                        .Where(token => rows.Any(row => Renders(row, token)) && Free(token.Id, word.Id))
                        .ToList();
                    if (found.Count > 0)
                    {
                        placement = new(word.Id, found[0].Id, kind, head.Id);
                        break;
                    }
                }
            }

            if (placement is null && words.Of(EvidentiaWordKinds.Relative, form).ToList() is { Count: > 0 } relatives)
            {
                var participles = renderings.Where(rendering => rendering.Morphology?.GetValueOrDefault("mood") == "participle").ToList();
                if (participles.Count == 1)
                {
                    var participle = participles[0];
                    var around = Around(participle);
                    var at = around.FindIndex(token => token.Id == participle.Id);
                    var articleAt = Before(around, at).Select(token => around.IndexOf(token))
                        .FirstOrDefault(index => around[index].StrongNumber == GreekArticle && Case(around[index]) == Case(participle), -1);
                    if (articleAt >= 0 && !(articleAt > 0 && relatives.Any(row => Renders(row, around[articleAt - 1]))))
                    {
                        placement = new(word.Id, participle.Id, EvidentiaWordKinds.Relative, head.Id);
                    }
                }
            }

            if (placement is not null)
            {
                if (placement.Rule is not (EvidentiaWordKinds.SubjectPronoun or EvidentiaWordKinds.Relative or "preposition-for-case"))
                {
                    takenHere.Add(placement.Original);
                }

                yield return placement;
            }
        }
    }

    /// <summary>The words before a word of the original, nearest first, as far as <see cref="Reach"/>.</summary>
    private static IEnumerable<EvidentiaToken> Before(List<EvidentiaToken> around, int at)
    {
        for (var index = at - 1; index >= 0 && index >= at - Reach; index--)
        {
            yield return around[index];
        }
    }

    /// <summary>The words on either side of a word of the original, nearest first, as far as <see cref="Reach"/>.</summary>
    private static IEnumerable<EvidentiaToken> Near(List<EvidentiaToken> around, int at, int reach = Reach)
    {
        for (var distance = 1; distance <= reach; distance++)
        {
            if (at - distance >= 0)
            {
                yield return around[at - distance];
            }

            if (at + distance < around.Count)
            {
                yield return around[at + distance];
            }
        }
    }

    /// <summary>
    /// Whether the original writes a pronoun that takes the subject pronoun instead: in any case within
    /// <see cref="PronounReach"/> of the verb (<em>οἵτινες περὶ τὴν ἀλήθειαν ἠστόχησαν</em>), in the nominative anywhere in the
    /// verse (<em>Σὺ δέ, ὦ ἄνθρωπε θεοῦ, ταῦτα φεῦγε</em>), or before the verb the article that serves as one with δέ or μέν
    /// (<em>ὁ δὲ εἶπεν</em>, <em>он же сказал</em>).
    /// </summary>
    private static bool Writes(EvidentiaWordRow row, List<EvidentiaToken> around, EvidentiaToken verb)
    {
        var at = around.FindIndex(token => token.Id == verb.Id);
        return Near(around, at, PronounReach).Any(token => Renders(row, token))
            || around.Any(token => Renders(row, token) && Case(token) == "nominative")
            || Enumerable.Range(Math.Max(0, at - PronounReach), Math.Min(PronounReach, at)).Any(index =>
                around[index].StrongNumber == GreekArticle && Case(around[index]) == "nominative"
                && index + 1 < around.Count && around[index + 1].StrongNumber is GreekDe or GreekMen);
    }

    private static bool Renders(EvidentiaWordRow row, EvidentiaToken token) =>
        row.Original is { } originals && token.StrongNumber is { } strong && originals.Split('|').Contains(strong, StringComparer.Ordinal);

    private static string? Case(EvidentiaToken token) => token.Morphology?.GetValueOrDefault("case");

    private static bool IsFinite(EvidentiaToken token) => token.Language == "hbo"
        ? token.PartOfSpeech == "verb" && HebrewFiniteTenses.Contains(token.Morphology?.GetValueOrDefault("tense") ?? "")
        : token.PartOfSpeech == "verb" && GreekFiniteMoods.Contains(token.Morphology?.GetValueOrDefault("mood") ?? "");

    /// <summary>Whether a verb's person and number, and in Hebrew its gender where both say one, are the pronoun's.</summary>
    private static bool Agrees(EvidentiaWordRow row, EvidentiaToken verb)
    {
        var morphology = verb.Morphology ?? new Dictionary<string, string>();
        var person = morphology.GetValueOrDefault("person") switch
        {
            "first" or "p1" => "1",
            "second" or "p2" => "2",
            "third" or "p3" => "3",
            _ => null,
        };
        var number = morphology.GetValueOrDefault("number") switch
        {
            "singular" or "sg" => "sg",
            "plural" or "pl" => "pl",
            _ => null,
        };
        var gender = morphology.GetValueOrDefault("gender") switch
        {
            "m" => "m",
            "f" => "f",
            _ => null,
        };
        return person == row.Person && number == row.Number
            && (row.Gender is null || gender is null || verb.Language != "hbo" || gender == row.Gender);
    }
}
