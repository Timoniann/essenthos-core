namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// The words a translation needs only to say what one word of the original carries in its form: the tense, mood or
/// voice of a verb, the case of a noun, the article a participle stands with. Each goes with that one word of the
/// original, and only where the feature is in that word's own parse and the original writes no word of its own for it.
///
/// <para>Every rule here reads the English parse and places English words only: none has been measured to the bar on
/// another translation.</para>
/// </summary>
internal static partial class EvidentiaAttachedWords
{
    private static readonly HashSet<string> FutureAuxiliaries = new(StringComparer.OrdinalIgnoreCase) { "will", "shall", "wilt", "shalt", "’ll", "'ll" };

    private static readonly HashSet<string> PerfectAuxiliaries = new(StringComparer.OrdinalIgnoreCase) { "have", "has", "hath", "hast", "having", "’ve", "'ve" };

    private static readonly HashSet<string> PluperfectAuxiliaries = new(StringComparer.OrdinalIgnoreCase) { "had", "hadst" };

    private static readonly HashSet<string> SubjunctiveAuxiliaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "may", "might", "should", "mayest", "mightest", "shouldest",
    };

    private static readonly HashSet<string> DoAuxiliaries = new(StringComparer.OrdinalIgnoreCase) { "do", "does", "did", "doth", "dost", "didst" };

    /// <summary>
    /// The auxiliaries that write a tense or a mood a Hebrew verb carries in its form. <em>can</em> and <em>could</em>
    /// are not among them: the original writes יכל as a word of its own.
    /// </summary>
    private static readonly HashSet<string> TenseAuxiliaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "will", "shall", "wilt", "shalt", "would", "should", "may", "might", "must",
        "do", "does", "did", "doth", "dost", "didst",
        "have", "has", "had", "hath", "hast",
        "am", "is", "are", "was", "were", "be", "been", "art",
    };

    private static readonly HashSet<string> GreekPassiveVoices = new(StringComparer.OrdinalIgnoreCase) { "passive", "middlepassive" };

    private static readonly HashSet<string> GreekPastTenses = new(StringComparer.OrdinalIgnoreCase) { "pluperfect", "aorist", "perfect" };

    private static readonly HashSet<string> GreekPerfectTenses = new(StringComparer.OrdinalIgnoreCase) { "perfect", "aorist" };

    private static readonly HashSet<string> GreekProgressiveTenses = new(StringComparer.OrdinalIgnoreCase) { "imperfect", "present" };

    private static readonly HashSet<string> GreekSubjunctiveMoods = new(StringComparer.OrdinalIgnoreCase) { "subjunctive", "optative" };

    private static readonly HashSet<string> GreekFiniteMoods = new(StringComparer.OrdinalIgnoreCase)
    {
        "indicative", "subjunctive", "imperative", "optative",
    };

    private static readonly HashSet<string> GreekDoMoods = new(StringComparer.OrdinalIgnoreCase) { "indicative", "subjunctive", "imperative" };

    private const string GreekFuture = "future";

    private const string PassiveAuxiliaryRelation = "aux:pass";

    /// <summary>How far before its infinitive μέλλω may stand: <em>τὴν μέλλουσαν δόξαν ἀποκαλυφθῆναι</em>.</summary>
    private const int AboutToReach = 3;

    /// <summary>γάρ, δέ, οὖν, μέν and τε, which Greek writes second, between an article and its noun.</summary>
    private static readonly HashSet<string> GreekPostpositives = new(StringComparer.Ordinal) { "G1063", "G1161", "G3767", "G3303", "G5037" };

    /// <summary>How far before its noun the article of a dative may stand: <em>τῷ γὰρ νόμῳ</em>, <em>τῷ ζῶντι ἀνδρί</em>.</summary>
    private const int DativeArticleReach = 3;

    private const string GreekDative = "dative";

    /// <summary>How far before its participle the article may stand: <em>ὁ ἐκ τοῦ οὐρανοῦ καταβάς</em>.</summary>
    private const int ParticipleArticleReach = 6;

    /// <summary>The English words a relative clause on a participle hangs from, which the article of the participle writes.</summary>
    private static readonly HashSet<string> Antecedents = new(StringComparer.OrdinalIgnoreCase)
    {
        "he", "him", "she", "her", "they", "them", "those", "one", "ones", "anyone",
    };

    private static readonly HashSet<string> Ones = new(StringComparer.OrdinalIgnoreCase) { "one", "ones" };

    /// <summary>אֲשֶׁר, the relative Hebrew writes as a word of its own.</summary>
    private const string HebrewRelative = "H834";

    /// <summary>How far before its participle a Hebrew אֲשֶׁר opens the clause: <em>הַקְּרִיאָה אֲשֶׁר אָנֹכִי דֹּבֵר</em>.</summary>
    private const int RelativeReach = 3;

    private const string RelativePronounType = "Rel";

    private const string No = "no";

    private static void AttachInflection(
        IReadOnlyList<EvidentiaAnalysis> words,
        Dictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse,
        HashSet<long> taken,
        ILookup<long, EvidentiaProposal> renderedBy,
        List<EvidentiaProposal> proposals)
    {
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            if (!IsEnglish(word) || placedBySource.ContainsKey(word.Token.Id))
            {
                continue;
            }

            var found = GreekAuxiliary(words, word, placedBySource, targetsByVerse, taken, renderedBy)
                ?? AcrossNegation(words, index, placedBySource, targetsByVerse, taken)
                ?? DativeOfTo(words, index, placedBySource, targetsByVerse)
                ?? RelativeOfTheParticiple(words, index, placedBySource, targetsByVerse);
            if (found is not { } placement)
            {
                continue;
            }

            Propose(word, placement.Target, placement.Head, placement.Attachment, placement.Note, placedBySource, proposals);
        }

        foreach (var let in words.Where(word => IsEnglish(word) && word.Token.Surface.Equals(Let, StringComparison.OrdinalIgnoreCase)))
        {
            AttachLet(words, let, placedBySource, proposals);
        }
    }

    private static void Propose(
        EvidentiaAnalysis word,
        EvidentiaAnalysis placement,
        EvidentiaProposal head,
        EvidentiaAttachment attachment,
        string note,
        Dictionary<long, EvidentiaProposal> placedBySource,
        List<EvidentiaProposal> proposals)
    {
        var proposal = new EvidentiaProposal(
            word,
            placement,
            EvidentiaProposalKind.AttachedWord,
            Math.Max(0, head.Confidence - ConfidenceBelowHead),
            new EvidentiaDecisionTrace("attached", $"{attachment} of '{head.Source.Token.Surface}', {note}", head.Trace?.Evidence ?? []),
            head);
        proposals.Add(proposal);
        placedBySource[word.Token.Id] = proposal;
    }

    /// <summary>What an English word was placed on by a rule of its own rather than as another word's.</summary>
    private static EvidentiaProposal? OwnPlacement(EvidentiaAnalysis? word, IReadOnlyDictionary<long, EvidentiaProposal> placedBySource) =>
        word is not null && placedBySource.TryGetValue(word.Token.Id, out var placed) && placed.Kind != EvidentiaProposalKind.AttachedWord
            ? placed
            : null;

    /// <summary>
    /// An English auxiliary of a verb placed on a Greek verb whose own form says what the auxiliary says: <em>will</em>
    /// or <em>shall</em> a future (or the subjunctive of οὐ μή), <em>have</em> a perfect or an aorist, <em>had</em> a
    /// pluperfect, an aorist or a perfect, <em>may</em>, <em>might</em> or <em>should</em> a subjunctive or an
    /// optative, <em>do</em> a finite verb that is no optative, the <em>be</em> of a passive a passive form, and the
    /// <em>be</em> of <em>was baptizing</em> an imperfect or a present. It goes on that verb, across a negation the
    /// original writes apart (<em>did not know</em>, οὐκ ᾔδειν), and never on a verb whose form says otherwise.
    ///
    /// <para>Where the original writes the tense in a word of its own, the auxiliary goes there: on the εἰμί of a
    /// participle (<em>were sitting</em>, ἦσαν … καθήμενοι), found before it with no finite verb between, and nowhere if
    /// another word already holds that εἰμί; and <em>will</em> on the μέλλω before an infinitive, which is otherwise no
    /// future.</para>
    /// </summary>
    private static (EvidentiaAnalysis Target, EvidentiaProposal Head, EvidentiaAttachment Attachment, string Note)? GreekAuxiliary(
        IReadOnlyList<EvidentiaAnalysis> words,
        EvidentiaAnalysis word,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse,
        IReadOnlySet<long> taken,
        ILookup<long, EvidentiaProposal> renderedBy)
    {
        if (word.Token.Relation is not { } relation || !AuxiliaryRelations.Contains(relation)
            || OwnPlacement(Syntactic(words, word.Token.SyntacticHead), placedBySource) is not { } head
            || !head.Target.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
            || Class(head.Target) != "verb"
            || !targetsByVerse.TryGetValue(head.Target.Token.Address, out var verse))
        {
            return null;
        }

        var verb = head.Target;
        var surface = word.Token.Surface;
        var infinitive = IsInfinitive(verb);
        if (FutureAuxiliaries.Contains(surface) && infinitive)
        {
            return AboutTo(verb, verse, taken) is { } aboutTo
                ? (aboutTo, head, EvidentiaAttachment.GreekAuxiliary, "on the μέλλω of its infinitive")
                : null;
        }

        if (!WritesTheAuxiliary(word, relation, verb))
        {
            return null;
        }

        if (IsParticiple(verb) && !IsNonFinite(word) && GreekBeOfTheParticiple(verb, verse) is { } be)
        {
            return taken.Contains(be.Token.Id) || renderedBy[be.Token.Id].Any()
                ? null
                : (be, head, EvidentiaAttachment.GreekAuxiliary, "on the εἰμί written with its participle");
        }

        return EvidentiaTenseAgreement.Disagrees(word, verb)
            ? null
            : (verb, head, EvidentiaAttachment.GreekAuxiliary, "on the verb whose form carries it");
    }

    private static bool WritesTheAuxiliary(EvidentiaAnalysis auxiliary, string relation, EvidentiaAnalysis verb)
    {
        var surface = auxiliary.Token.Surface;
        var tense = Feature(verb, "tense");
        var mood = Feature(verb, "mood");
        if (auxiliary.Lemma == Be)
        {
            return relation == PassiveAuxiliaryRelation
                ? Feature(verb, "voice") is { } voice && GreekPassiveVoices.Contains(voice)
                : tense is not null && GreekProgressiveTenses.Contains(tense);
        }

        return FutureAuxiliaries.Contains(surface)
                ? string.Equals(tense, GreekFuture, StringComparison.OrdinalIgnoreCase)
                  || string.Equals(mood, "subjunctive", StringComparison.OrdinalIgnoreCase)
            : PerfectAuxiliaries.Contains(surface) ? tense is not null && GreekPerfectTenses.Contains(tense)
            : PluperfectAuxiliaries.Contains(surface) ? tense is not null && GreekPastTenses.Contains(tense)
            : SubjunctiveAuxiliaries.Contains(surface) ? mood is not null && GreekSubjunctiveMoods.Contains(mood)
            : DoAuxiliaries.Contains(surface) && mood is not null && GreekDoMoods.Contains(mood);
    }

    /// <summary>
    /// The εἰμί a participle makes one tense with: written directly after it (<em>γεγραμμένον ἐστίν</em>), or before it
    /// in its clause, with no finite verb between (<em>ἦσαν δέ τινες τῶν γραμματέων ἐκεῖ καθήμενοι</em>). A
    /// participle with its own article before it is a noun of its own (<em>τὸ γεγεννημένον</em>) and has none.
    /// </summary>
    private static EvidentiaAnalysis? GreekBeOfTheParticiple(EvidentiaAnalysis participle, IReadOnlyList<EvidentiaAnalysis> verse)
    {
        var at = IndexOf(verse, participle);
        if (at > 0 && Class(verse[at - 1]) == "det")
        {
            return null;
        }

        if (at + 1 < verse.Count && verse[at + 1].Token.StrongNumber == GreekBe)
        {
            return verse[at + 1];
        }

        for (var before = at - 1; before >= 0; before--)
        {
            var word = verse[before];
            if (word.Token.StrongNumber == GreekBe)
            {
                return word;
            }

            if (Class(word) == "verb" && Feature(word, "mood") is { } mood && GreekFiniteMoods.Contains(mood))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary><em>being</em> and <em>having</em>, which English writes as a participle of its own and never for a finite εἰμί.</summary>
    private static bool IsNonFinite(EvidentiaAnalysis auxiliary) =>
        Feature(auxiliary, "VerbForm") is "Ger" or "Part" || auxiliary.Token.Surface.EndsWith("ing", StringComparison.OrdinalIgnoreCase);

    /// <summary>The free μέλλω written just before an infinitive: <em>μέλλετε ἀποθνήσκειν</em>, <em>you will die</em>.</summary>
    private static EvidentiaAnalysis? AboutTo(EvidentiaAnalysis infinitive, IReadOnlyList<EvidentiaAnalysis> verse, IReadOnlySet<long> taken)
    {
        var at = IndexOf(verse, infinitive);
        return verse.Skip(Math.Max(0, at - AboutToReach)).Take(Math.Min(at, AboutToReach))
            .LastOrDefault(word => word.Token.StrongNumber == GreekAboutTo && !taken.Contains(word.Token.Id));
    }

    /// <summary>
    /// An auxiliary written before a negation (<em>did not see</em>, <em>will not go</em>, <em>must not eat</em>), on
    /// the Hebrew verb its verb was placed on, as an auxiliary is without one: the negation is a word the original
    /// writes, the tense is the verb's.
    /// </summary>
    private static (EvidentiaAnalysis Target, EvidentiaProposal Head, EvidentiaAttachment Attachment, string Note)? AcrossNegation(
        IReadOnlyList<EvidentiaAnalysis> words,
        int index,
        Dictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse,
        HashSet<long> taken)
    {
        var word = words[index];
        if (word.Token.Relation != "aux" || !TenseAuxiliaries.Contains(word.Token.Surface) || Punctuated(word)
            || !words.Skip(index + 1).Take(2).Any(next => Negations.Contains(next.Token.Surface))
            || Syntactic(words, word.Token.SyntacticHead) is not { } verb || Class(verb) != "verb"
            || OwnPlacement(verb, placedBySource) is not { } head
            || !head.Target.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
            || !IsHebrewFinite(head.Target))
        {
            return null;
        }

        return Placed(EvidentiaAttachment.AuxiliaryVerb, index, verb, words, placedBySource, targetsByVerse, taken) is { } found
            ? (found.Placement, found.Head, EvidentiaAttachment.AuxiliaryAcrossNegation, "on the verb across its negation")
            : null;
    }

    /// <summary>
    /// <em>to</em> of a noun placed on a Greek dative that has no preposition written before it: <em>to God</em> on
    /// Θεῷ of τῷ Θεῷ, and on the article where English writes <em>the</em> or a possessive after <em>to</em> (<em>to
    /// the servants</em> on τοῖς, <em>to God's law</em> on the τῷ of τῷ γὰρ νόμῳ), as the keys put <em>of</em> of a
    /// genitive.
    /// </summary>
    private static (EvidentiaAnalysis Target, EvidentiaProposal Head, EvidentiaAttachment Attachment, string Note)? DativeOfTo(
        IReadOnlyList<EvidentiaAnalysis> words,
        int index,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse)
    {
        var word = words[index];
        if (!word.Token.Surface.Equals(To, StringComparison.OrdinalIgnoreCase)
            || word.Token.Relation?.StartsWith("case", StringComparison.Ordinal) != true
            || OwnPlacement(Syntactic(words, word.Token.SyntacticHead), placedBySource) is not { } head
            || !head.Target.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Feature(head.Target, "case"), GreekDative, StringComparison.OrdinalIgnoreCase)
            || Class(head.Target) is not ("noun" or "pron" or "adj" or "propn")
            || !targetsByVerse.TryGetValue(head.Target.Token.Address, out var verse))
        {
            return null;
        }

        var noun = head.Target;
        var at = IndexOf(verse, noun);
        EvidentiaAnalysis? article = null;
        var before = at - 1;
        for (; before >= 0 && at - before <= DativeArticleReach; before--)
        {
            var other = verse[before];
            if (Class(other) == "det")
            {
                article = string.Equals(Feature(other, "case"), GreekDative, StringComparison.OrdinalIgnoreCase) ? other : null;
                break;
            }

            if (!(other.Token.StrongNumber is { } strong && GreekPostpositives.Contains(strong)
                  || Class(other) == "adj"
                  || Class(other) == "verb" && IsParticiple(other)))
            {
                break;
            }
        }

        var opening = article is null ? at - 1 : before - 1;
        while (opening >= 0 && verse[opening].Token.StrongNumber is { } postpositive && GreekPostpositives.Contains(postpositive))
        {
            opening--;
        }

        if (opening >= 0 && Class(verse[opening]) == "adp")
        {
            return null;
        }

        var determined = index + 1 < words.Count
            && (words[index + 1].Token.Surface.Equals(DefiniteArticle, StringComparison.OrdinalIgnoreCase) || IsPossessive(words[index + 1]));
        return article is not null && determined
            ? (article, head, EvidentiaAttachment.DativeTo, "on the article of its dative")
            : (noun, head, EvidentiaAttachment.DativeTo, "on its dative");
    }

    private static bool IsPossessive(EvidentiaAnalysis word) =>
        Feature(word, "Poss") == "Yes"
        || word.Token.Surface.EndsWith("’s", StringComparison.OrdinalIgnoreCase)
        || word.Token.Surface.EndsWith("'s", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A relative pronoun whose clause's verb was placed on a participle, and the word the clause hangs from where it
    /// is a pronoun or <em>the one</em>: in Greek both go on the article of the participle (<em>He who comes</em> on
    /// ὁ of ὁ ἐρχόμενος), which must agree with it and stand before it with no verb or other article between, and
    /// nowhere where there is none; in Hebrew on the participle, which writes the person itself (<em>those who walk
    /// in it</em> on הֹלְכִים), and nowhere where the original writes אֲשֶׁר, nor for the <em>one</em> of <em>no
    /// one</em>, which is אֵין.
    /// </summary>
    private static (EvidentiaAnalysis Target, EvidentiaProposal Head, EvidentiaAttachment Attachment, string Note)? RelativeOfTheParticiple(
        IReadOnlyList<EvidentiaAnalysis> words,
        int index,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse)
    {
        var word = words[index];
        EvidentiaAnalysis? clauseVerb = null;
        if (Feature(word, "PronType") == RelativePronounType)
        {
            clauseVerb = Syntactic(words, word.Token.SyntacticHead);
        }
        else if ((Antecedents.Contains(word.Token.Surface)
                     || word.Token.Surface.Equals(DefiniteArticle, StringComparison.OrdinalIgnoreCase)
                        && index + 1 < words.Count && Ones.Contains(words[index + 1].Token.Surface)))
        {
            var nominal = Antecedents.Contains(word.Token.Surface) ? word : words[index + 1];
            if (words.Any(other => other.Token.SyntacticHead == nominal.Token.Id && other.Token.Surface.Equals(No, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            clauseVerb = words.FirstOrDefault(other => other.Token.SyntacticHead == nominal.Token.Id
                && other.Token.Relation?.StartsWith("acl", StringComparison.Ordinal) == true);
        }

        if (clauseVerb is null || Class(clauseVerb) != "verb"
            || OwnPlacement(clauseVerb, placedBySource) is not { } head
            || !targetsByVerse.TryGetValue(head.Target.Token.Address, out var verse))
        {
            return null;
        }

        var participle = head.Target;
        if (participle.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return IsParticiple(participle) && ArticleOfTheParticiple(participle, verse) is { } article
                ? (article, head, EvidentiaAttachment.RelativeOfTheParticiple, "on the article of its participle")
                : null;
        }

        if (!participle.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase) || !IsHebrewParticiple(participle))
        {
            return null;
        }

        var at = IndexOf(verse, participle);
        if (verse.Skip(Math.Max(0, at - RelativeReach)).Take(Math.Min(at, RelativeReach)).Any(other => other.Token.StrongNumber == HebrewRelative))
        {
            return null;
        }

        if (word.Token.Surface.Equals(DefiniteArticle, StringComparison.OrdinalIgnoreCase))
        {
            return at > 0 && verse[at - 1].Token.StrongNumber == HebrewArticle
                ? (verse[at - 1], head, EvidentiaAttachment.RelativeOfTheParticiple, "on the article of its participle")
                : null;
        }

        return (participle, head, EvidentiaAttachment.RelativeOfTheParticiple, "on its participle");
    }

    /// <summary>The article of a Greek participle: before it, of its case, number and gender, with no verb or other article between.</summary>
    private static EvidentiaAnalysis? ArticleOfTheParticiple(EvidentiaAnalysis participle, IReadOnlyList<EvidentiaAnalysis> verse)
    {
        var at = IndexOf(verse, participle);
        for (var before = at - 1; before >= 0 && at - before <= ParticipleArticleReach; before--)
        {
            var other = verse[before];
            if (Class(other) == "det")
            {
                return new[] { "case", "number", "gender" }.All(name => string.Equals(Feature(other, name), Feature(participle, name), StringComparison.OrdinalIgnoreCase))
                    ? other
                    : null;
            }

            if (Class(other) == "verb")
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// <em>Let</em> and the pronoun after it (<em>Let us make</em>, <em>let him hear</em>) on the verb that says the
    /// wish in its own form: a Hebrew imperfect, whose cohortative and jussive BHSA writes as one, or a Greek
    /// hortatory subjunctive of the first person or imperative of the third, whose ending names the pronoun's person
    /// and number. Where English's subject is a noun (<em>Let the earth bring forth</em>), <em>Let</em> goes on a verb of
    /// the third person the same way and the noun keeps its own word: the wish is in the verb's form, though the
    /// Berean key puts <em>Let</em> with the noun (the owner's ruling of 2026-10-16 on TSK-1095).
    /// </summary>
    private static void AttachLet(
        IReadOnlyList<EvidentiaAnalysis> words,
        EvidentiaAnalysis let,
        Dictionary<long, EvidentiaProposal> placedBySource,
        List<EvidentiaProposal> proposals)
    {
        var subjects = words.Where(other => other.Token.SyntacticHead == let.Token.Id
            && other.Token.Relation is "obj" or "nsubj").ToList();
        var pronoun = subjects.FirstOrDefault(other => Class(other) == "pron" && Feature(other, "PronType") == PersonalPronounType);
        var noun = pronoun is null ? subjects.FirstOrDefault(other => Class(other) is "noun" or "propn") : null;
        var verb = words.FirstOrDefault(other => other.Token.SyntacticHead == let.Token.Id && other.Token.Relation == "xcomp");
        if (placedBySource.ContainsKey(let.Token.Id) || pronoun is null && noun is null
            || OwnPlacement(verb, placedBySource) is not { } head)
        {
            return;
        }

        var mood = head.Target;
        var wish = mood.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
            ? Class(mood) == "verb" && string.Equals(Feature(mood, "tense"), "impf", StringComparison.OrdinalIgnoreCase)
            : mood.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
              && (string.Equals(Feature(mood, "mood"), "subjunctive", StringComparison.OrdinalIgnoreCase)
                  && string.Equals(Feature(mood, "person"), "first", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(Feature(mood, "mood"), "imperative", StringComparison.OrdinalIgnoreCase)
                  && string.Equals(Feature(mood, "person"), "third", StringComparison.OrdinalIgnoreCase));
        var agrees = pronoun is not null
            ? EvidentiaPersonAgreement.Agrees(pronoun, mood, suffix: false)
            : EvidentiaMorphologyLabels.Feature(Feature(mood, "person"), mood.Token.Language) == "3";
        if (!wish || !agrees)
        {
            return;
        }

        Propose(let, mood, head, EvidentiaAttachment.LetOfTheMood, "on the verb whose form says the wish", placedBySource, proposals);
        if (pronoun is not null && !placedBySource.ContainsKey(pronoun.Token.Id))
        {
            Propose(pronoun, mood, head, EvidentiaAttachment.LetOfTheMood, "on the verb whose ending names it", placedBySource, proposals);
        }
    }
}
