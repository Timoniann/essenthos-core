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

    /// <summary>How many steps up the translation's parse a word may stand below a noun and still be of the noun's phrase.</summary>
    private const int PhraseDepth = 3;

    /// <summary>The marks after a word that end a sentence or a colon, in the translation and in the original alike.</summary>
    private static readonly char[] SentenceMarks = ['.', ';', ':', '!', '?', '·', '·'];

    /// <summary>The translation's classes of word that run on after a noun as its modifiers (UD UPOS).</summary>
    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal) { "ADJ", "DET", "NUM", "PROPN" };

    /// <summary>The UD relations by which a word modifies the noun it hangs on, and so stands in its phrase.</summary>
    private static readonly HashSet<string> ModifierRelations = new(StringComparer.Ordinal)
    {
        "amod", "det", "nmod", "nummod", "flat", "fixed", "compound", "case",
    };

    /// <summary>The original's classes of word that join or mark a phrase rather than make one of their own.</summary>
    private static readonly HashSet<string> Linking = new(StringComparer.Ordinal) { "conj", "ptcl", "det", "intj" };

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

    /// <summary>How far below its head's confidence an attached word's placement is stored.</summary>
    private const double ConfidenceBelowHead = 0.05;

    /// <summary>
    /// The placements of a stored run: in every verse, the rules the language's rule list names, applied to the words no proposal
    /// placed, with the run's own proposals as their heads. Each is an attached word under its rule's tier, never the safe one.
    /// </summary>
    public static IReadOnlyList<EvidentiaProposal> Proposals(
        EvidentiaSlavicRun run,
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaProposal> proposals)
    {
        var heads = proposals.GroupBy(proposal => proposal.Source.Token.Id).ToDictionary(group => group.Key, group => group.First());
        var placed = proposals.GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<long>)[.. group.Select(proposal => proposal.Target.Token.Id).Distinct()]);
        var analyses = source.DistinctBy(word => word.Token.Id).ToDictionary(word => word.Token.Id);
        var originals = target.DistinctBy(word => word.Token.Id).ToDictionary(word => word.Token.Id);
        List<EvidentiaToken> originalTokens = [.. originals.Values.Select(word => word.Token)];
        var result = new List<EvidentiaProposal>();
        foreach (var verse in analyses.Values.GroupBy(word => word.Token.Address))
        {
            List<EvidentiaToken> words = [.. verse.Select(word => word.Token).OrderBy(token => token.Position)];
            List<EvidentiaToken> unplaced = [.. words.Where(token => !placed.ContainsKey(token.Id))];
            if (unplaced.Count == 0)
            {
                continue;
            }

            foreach (var placement in Place(run.Words, words, unplaced, originalTokens, placed))
            {
                if (!originals.TryGetValue(placement.Original, out var original) || !heads.TryGetValue(placement.Head, out var head)
                    || !run.Tiers.TryGetValue((original.Token.Language, placement.Rule), out var tier))
                {
                    continue;
                }

                var attachment = placement.Rule == EvidentiaWordKinds.SubjectPronoun ? EvidentiaAttachment.SubjectPronoun
                    : placement.Rule.StartsWith("preposition", StringComparison.Ordinal) ? EvidentiaAttachment.Preposition
                    : placement.Rule == EvidentiaWordKinds.Conjunction ? EvidentiaAttachment.Conjunction
                    : placement.Rule == EvidentiaWordKinds.Copula ? EvidentiaAttachment.Copula
                    : (EvidentiaAttachment?)null;
                var word = analyses[placement.Word];
                result.Add(new EvidentiaProposal(
                    word,
                    original,
                    EvidentiaProposalKind.AttachedWord,
                    Math.Max(0, head.Confidence - ConfidenceBelowHead),
                    new EvidentiaDecisionTrace(
                        tier,
                        $"{attachment?.ToString() ?? placement.Rule} of '{head.Source.Token.Surface}' by the {run.Words.Language} rule " +
                        $"{placement.Rule} (evidentia/words/rules), on '{original.Token.Surface}'",
                        head.Trace?.Evidence ?? []),
                    head));
            }
        }

        return result;
    }

    public static IEnumerable<EvidentiaSlavicPlacement> Place(
        IEvidentiaSlavicWords language,
        IReadOnlyList<EvidentiaToken> verse,
        IReadOnlyList<EvidentiaToken> unplaced,
        IReadOnlyList<EvidentiaToken> original,
        IReadOnlyDictionary<long, IReadOnlyList<long>> placed,
        Action<EvidentiaSlavicPlacement>? refused = null)
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
                        var found = new EvidentiaSlavicPlacement(word.Id, preposition.Id, "preposition-before-noun", head.Id);
                        if (Governs(word, head, preposition, noun, around, verse, byId, placed, reachedBy))
                        {
                            placement = found;
                        }
                        else
                        {
                            refused?.Invoke(found);
                        }
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

    /// <summary>
    /// Whether the original's preposition before a noun's rendering is the one of the small word's own phrase. No original word
    /// between the two may be rendered by a translation word outside that phrase (<em>на путь до поган</em> over
    /// <em>Εἰς ὁδὸν ἐθνῶν</em>: Εἰς governs ὁδὸν, which «путь» renders).
    /// A conjunction, particle or article between them (<em>Περὶ δὲ τῆς</em>) is no other phrase's. And where the original's
    /// sentence holding the preposition is rendered by other words of the translation, one of them must stand in the small word's
    /// own sentence (<em>ті, хто з ним був</em> is not served by <em>ἀπ' αὐτῶν</em> of the next sentence, «відлучившись від них»).
    /// </summary>
    private static bool Governs(
        EvidentiaToken word,
        EvidentiaToken head,
        EvidentiaToken preposition,
        EvidentiaToken noun,
        List<EvidentiaToken> around,
        IReadOnlyList<EvidentiaToken> verse,
        Dictionary<long, EvidentiaToken> byId,
        IReadOnlyDictionary<long, IReadOnlyList<long>> placed,
        Dictionary<long, HashSet<long>> reachedBy)
    {
        var ordered = verse.OrderBy(token => token.Position).ToList();
        var last = Math.Max(word.Position, head.Position);
        foreach (var next in ordered.Where(token => token.Position > last))
        {
            if (next.Position != last + 1 || !Modifiers.Contains(next.PartOfSpeech ?? ""))
            {
                break;
            }

            last = next.Position;
        }

        // The phrase is the small word, its noun and what stands between them, the modifiers running on after the noun
        // (від Духа Святого), and the words the parse hangs on the noun as its modifiers; not the whole clause a copular noun
        // heads (в Дамаску був учень).
        bool InPhrase(EvidentiaToken other)
        {
            if (other.Position >= Math.Min(word.Position, head.Position) && other.Position <= last)
            {
                return true;
            }

            var current = other;
            for (var step = 0; step < PhraseDepth && current.SyntacticHead is { } up && up != current.Id && byId.TryGetValue(up, out var parent)
                 && current.Relation is { } relation && ModifierRelations.Contains(relation.Split(':')[0]); step++)
            {
                if (parent.Id == head.Id)
                {
                    return true;
                }

                current = parent;
            }

            return false;
        }

        var from = around.FindIndex(token => token.Id == preposition.Id);
        var to = around.FindIndex(token => token.Id == noun.Id);

        // The preposition governs the first word after it, past an article, that has a case; a noun in another case than the
        // noun's, which no word of the phrase renders, begins another phrase (Εἰς ὁδὸν ἐθνῶν: Εἰς takes ὁδὸν, not ἐθνῶν). An
        // adjective or pronoun there (ἐν μέσῳ αὐτῶν, «серед них») is the preposition's own compound.
        if (Case(noun) is { } nounCase
            && around.Skip(from + 1).Take(to - from - 1)
                .FirstOrDefault(token => Case(token) is not null && token.StrongNumber != GreekArticle && token.PartOfSpeech != "det") is { } governed
            && governed.PartOfSpeech == "noun" && Case(governed) != nounCase
            && !(reachedBy.TryGetValue(governed.Id, out var governing) && governing.Any(id => byId.TryGetValue(id, out var other) && InPhrase(other))))
        {
            return false;
        }

        for (var index = from + 1; index < to; index++)
        {
            if (!Linking.Contains(around[index].PartOfSpeech ?? "") && around[index].StrongNumber != GreekArticle
                && reachedBy.TryGetValue(around[index].Id, out var by)
                && by.Any(id => byId.TryGetValue(id, out var other) && !InPhrase(other)))
            {
                return false;
            }
        }

        // A sentence is told by its marks, which the translation sets where the original has none (a colon before speech), so
        // it refuses only where the small word's own sentence is rendered elsewhere in the original and the preposition's
        // sentence by another sentence of the translation.
        var inVerse = around.Select(token => token.Id).ToHashSet();
        var own = Sentence(ordered, word).Where(other => !InPhrase(other)).ToList();
        var ownRendered = own.Where(other => placed.ContainsKey(other.Id)).SelectMany(other => placed[other.Id]).Where(inVerse.Contains).ToList();
        var sentence = Sentence(around, preposition).Select(token => token.Id).ToHashSet();
        var ownIds = own.Select(other => other.Id).ToHashSet();
        var elsewhere = sentence.SelectMany(id => reachedBy.GetValueOrDefault(id) ?? [])
            .Any(id => byId.TryGetValue(id, out var other) && !ownIds.Contains(id) && !InPhrase(other));
        return ownRendered.Count == 0 || ownRendered.Any(sentence.Contains) || !elsewhere;
    }

    /// <summary>The words of a verse in the same sentence or colon as a word, as far as the verse goes.</summary>
    private static List<EvidentiaToken> Sentence(IReadOnlyList<EvidentiaToken> verse, EvidentiaToken word)
    {
        var ordered = verse.OrderBy(token => token.Position).ToList();
        var at = ordered.FindIndex(token => token.Id == word.Id);
        var start = at;
        while (start > 0 && ordered[start - 1].Trailer.IndexOfAny(SentenceMarks) < 0)
        {
            start--;
        }

        var end = at;
        while (end < ordered.Count - 1 && ordered[end].Trailer.IndexOfAny(SentenceMarks) < 0)
        {
            end++;
        }

        return ordered.GetRange(start, end - start + 1);
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
