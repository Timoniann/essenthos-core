namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>What a grammatical word of a translation is attached as.</summary>
internal enum EvidentiaAttachment
{
    None,

    /// <summary><em>the</em> of <em>the waters</em>.</summary>
    Article,

    /// <summary><em>and</em> before the word it joins on.</summary>
    Conjunction,

    /// <summary><em>in</em> of <em>in the beginning</em>.</summary>
    Preposition,

    /// <summary><em>of</em> of <em>the face of the deep</em>, which Hebrew writes as a construct chain.</summary>
    Genitive,

    /// <summary><em>will</em> of <em>will send</em>.</summary>
    AuxiliaryVerb,

    /// <summary><em>he</em> of <em>he said</em>, which a Hebrew or Greek verb carries in its ending.</summary>
    SubjectPronoun,

    /// <summary><em>his</em> of <em>his sons</em>, which Hebrew writes as a suffix of the noun.</summary>
    PossessivePronoun,

    /// <summary><em>him</em> of <em>struck him</em> or <em>to him</em>, a suffix of the verb or of the preposition.</summary>
    ObjectPronoun,

    /// <summary><em>to</em> of <em>to separate</em>, which Hebrew writes as ל before the infinitive.</summary>
    Infinitive,

    /// <summary>
    /// <em>was</em> of <em>there was light</em> or <em>was good</em>, a form of <em>be</em> that is no
    /// other verb's auxiliary.
    /// </summary>
    Copula,

    /// <summary><em>there</em> of <em>there was</em>, which says nothing of its own.</summary>
    Expletive,

    /// <summary><em>because</em>, <em>that</em> or <em>if</em>, which opens the clause of the verb it belongs to.</summary>
    Subordinator,
}

/// <summary>Where an attached word goes once the word it belongs to has been placed.</summary>
internal enum EvidentiaAttachmentPlacement
{
    /// <summary>Not placed: the annotations measured against do not agree on a place for it.</summary>
    None,

    /// <summary>On the rendering of the word it belongs to.</summary>
    Rendering,

    /// <summary>On the word of its own kind written directly before that rendering, and nowhere if there is none.</summary>
    OwnKindBefore,

    /// <summary>
    /// On the rendering of the governed noun, when the word before <em>of</em> was placed on the Hebrew
    /// noun in the construct state it depends on.
    /// </summary>
    Dependent,

    /// <summary>
    /// On the rendering when its ending or suffix names the same person, number and gender as the
    /// pronoun; a pronoun object of an English preposition goes on the preposition written after the
    /// verb, when its suffix names it.
    /// </summary>
    AgreeingRendering,

    /// <summary>On the preposition written directly before an infinitive, or on the infinitive where there is none.</summary>
    Infinitive,

    /// <summary>On the original's own verb <em>be</em>, written directly beside the rendering of the word it links.</summary>
    BeBeside,

    /// <summary>On a conjunction other than <em>and</em> written directly before the rendering of the clause's verb.</summary>
    ConjunctionBefore,
}

/// <summary>
/// Places grammatical words no lexical tier attempts by the word they belong to. Nothing here is
/// evidence that two words correspond: the placement follows the proposal of the head word, and a
/// head left unplaced leaves its grammatical words unplaced too.
///
/// <para>Where each goes is the convention of the witness, as the Berean tables and, for the King
/// James, the Open Hebrew Bible state it. A Hebrew article is a prefix of its noun and both put
/// <em>the</em> with the noun; <em>and</em> goes on the ו BHSA writes as a word of its own before the
/// word it joins; an auxiliary goes on the verb, which carries tense and mood in itself.</para>
///
/// <para>A personal pronoun goes where the original writes the person: a subject on the verb whose
/// ending names it, a possessive on the Hebrew noun whose suffix does, an object on the suffix of the
/// verb or preposition. The morphology decides, not the habit: a pronoun whose head was placed on a
/// word that names another person, or none, is left unplaced, and so is a subject the original writes
/// as a pronoun of its own beside the verb, since both annotations put it there.</para>
///
/// <para>An English preposition goes on the preposition written before the rendering of the word it
/// governs, past an article - BHSA writes <em>בְּ</em> of <em>בְּרֵאשִׁית</em> as a word of its own -
/// or on that rendering itself where it is a preposition carrying the pronoun (<em>to him</em>, לוֹ).
/// Not <em>of</em>, which Hebrew writes as the construct state of the noun before it, and not the
/// particles of phrasal verbs (<em>out of</em>, <em>up</em>); and never the object marker את, which
/// no English preposition renders.</para>
///
/// <para>Against Hebrew <em>of</em> goes on the rendering of the noun it governs, and only where the
/// word before it was placed on a noun in the construct state standing before that rendering, with
/// nothing but an article between. Against Greek it is not placed: the annotations put <em>of</em> of
/// a genitive on its article as often as on its noun.</para>
///
/// <para>Against Greek only <em>and</em>, <em>the</em>, the preposition and the subject pronoun are
/// placed: <em>and</em> on the <em>καί</em> written directly before the rendering, which both
/// annotations do; <em>the</em> on the article of its noun, past an adjective between them, which is
/// where the Berean tables put it; the preposition on the preposition before the rendering; and the
/// subject on the verb whose ending names it.</para>
///
/// <para>Not placed, because on these texts they measured below the precision the lexical tiers
/// already reach: verb particles, which the parser finds as often in a preposition's place; and a
/// Greek auxiliary, which is often a periphrastic <em>ἦσαν</em> of its own rather than part of the
/// verb after it. The roles come from the English UDPipe parse, so every other source language is
/// left alone.</para>
/// </summary>
internal static class EvidentiaAttachedWords
{
    private const double ConfidenceBelowHead = 0.10;

    private const int HeadReach = 3;

    private const string EnglishLanguage = "eng";

    private const string HebrewLanguage = "hbo";

    private const string GreekLanguage = "grc";

    /// <summary>
    /// Greek's <em>and</em>. Its other conjunctions standing before a word subordinate rather than join
    /// (<em>εἰ</em>, <em>ὅτι</em>), and <em>δέ</em> is never written first.
    /// </summary>
    private const string GreekAnd = "καί";

    private const string DefiniteArticle = "the";

    private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase) { DefiniteArticle, "a", "an" };

    private static readonly HashSet<string> Conjunctions = new(StringComparer.OrdinalIgnoreCase) { "and" };

    private const string Of = "of";

    private static readonly HashSet<string> UnplacedPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        Of, "out", "up", "off", "as", "down", "away", "forth",
    };

    /// <summary>The Hebrew object marker, which BHSA classes as a preposition.</summary>
    private const string ObjectMarker = "H853";

    /// <summary>BHSA's construct state, the noun that the next one depends on.</summary>
    private const string ConstructState = "c";

    private static readonly HashSet<string> Negations = new(StringComparer.OrdinalIgnoreCase) { "not", "n't", "never" };

    /// <summary>How far after its verb the preposition carrying an object pronoun may stand: <em>וַיֹּאמֶר לוֹ</em>.</summary>
    private const int ObjectPrepositionReach = 2;

    /// <summary>How far from the verb an original writes a subject pronoun of its own: <em>וְהוּא יִמְשָׁל</em>.</summary>
    private const int SubjectPronounReach = 3;

    private const string To = "to";

    private const string Be = "be";

    private const string There = "there";

    /// <summary>The verb <em>be</em> of each original: היה, εἰμί and γίνομαι.</summary>
    private static readonly HashSet<string> BeVerbs = new(StringComparer.Ordinal) { "H1961", "G1510", "G1096" };

    /// <summary>The relations by which a form of <em>be</em> is the tense of another verb rather than a verb of its own.</summary>
    private static readonly HashSet<string> AuxiliaryRelations = new(StringComparer.Ordinal) { "aux", "aux:pass" };

    private const string ExpletiveRelation = "expl";

    private const string SubjectRelation = "nsubj";

    private const string CopulaRelation = "cop";

    private const string MarkerRelation = "mark";

    private const string SubordinatingConjunction = "SCONJ";

    /// <summary>
    /// The original's words for <em>and</em>: ו, καί and δέ. They open a clause as often as they join a
    /// word, and English writes them as <em>and</em>, which is placed by its own rule.
    /// </summary>
    private static readonly HashSet<string> Coordinators = new(StringComparer.Ordinal) { "H9000", "G2532", "G1161" };

    public static IReadOnlyList<EvidentiaProposal> Resolve(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaProposal> placed)
    {
        var placedBySource = placed
            .GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var targetsByVerse = target
            .DistinctBy(analysis => analysis.Token.Id)
            .GroupBy(analysis => analysis.Token.Address)
            .ToDictionary(group => group.Key, group => group.OrderBy(analysis => analysis.Token.Position).ToList());
        var taken = placed.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var proposals = new List<EvidentiaProposal>();
        var verses = source
            .Where(analysis => analysis.Token.Language.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(analysis => analysis.Token.Id)
            .GroupBy(analysis => analysis.Token.Address)
            .Select(verse => verse.OrderBy(analysis => analysis.Token.Position).ToList())
            .ToList();
        // A word can belong to one that is itself attached - and of and he said, to of to him - so the
        // pass repeats until it places nothing new.
        for (var placing = true; placing;)
        {
            placing = false;
            foreach (var words in verses)
            {
                placing |= Attach(words, placedBySource, targetsByVerse, taken, proposals);
            }
        }

        return proposals;
    }

    private static bool Attach(
        IReadOnlyList<EvidentiaAnalysis> words,
        Dictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse,
        HashSet<long> taken,
        List<EvidentiaProposal> proposals)
    {
        var attached = false;
        for (var index = 0; index < words.Count; index++)
        {
            if (placedBySource.ContainsKey(words[index].Token.Id)
                || Classify(words, index) is not ({ } attachment, { } head)
                || HeadProposal(attachment, index, head, words, placedBySource) is not ({ } headProposal, var afterVerb)
                || !targetsByVerse.TryGetValue(headProposal.Target.Token.Address, out var targetVerse)
                || Place(attachment, words, index, headProposal.Target, targetVerse, afterVerb, placedBySource, taken) is not { } placement)
            {
                continue;
            }

            taken.Add(placement.Token.Id);
            var proposal = new EvidentiaProposal(
                words[index],
                placement,
                EvidentiaProposalKind.AttachedWord,
                Math.Max(0, headProposal.Confidence - ConfidenceBelowHead),
                new EvidentiaDecisionTrace(
                    "attached",
                    $"{attachment} of '{head.Token.Surface}'",
                    headProposal.Trace?.Evidence ?? []),
                headProposal);
            proposals.Add(proposal);
            placedBySource[words[index].Token.Id] = proposal;
            attached = true;
        }

        return attached;
    }

    /// <summary>
    /// The attachments that inherit the safe tier from their head. Each placed the word where the answer
    /// keys put it at least as often as the safe tier's own renderings do, on the passages the rules
    /// were chosen on and on those they were not: an article, the <em>of</em> of a construct chain and a
    /// possessive written as the suffix of the noun. The others - a conjunction, a preposition, a subject
    /// or object pronoun and an auxiliary - stay in review however safe their head is.
    /// </summary>
    private static readonly HashSet<EvidentiaAttachment> SafeWithTheirHead =
    [
        EvidentiaAttachment.Article,
        EvidentiaAttachment.Genitive,
        EvidentiaAttachment.PossessivePronoun,
    ];

    /// <summary>
    /// The safe tier once attached words are counted with it: the safe renderings, and every attached word
    /// of a kind that inherits the tier whose head is itself in it.
    /// </summary>
    public static IReadOnlySet<(long From, long To)> Safe(
        IReadOnlyList<EvidentiaProposal> safe,
        IReadOnlyList<EvidentiaProposal> attached)
    {
        var pairs = safe.Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id)).ToHashSet();
        // In the order they were attached, so a word attached to an attached word finds its head decided.
        foreach (var proposal in attached)
        {
            if (proposal.Head is { } head
                && pairs.Contains((head.Source.Token.Id, head.Target.Token.Id))
                && Attachment(proposal) is { } attachment
                && SafeWithTheirHead.Contains(attachment))
            {
                pairs.Add((proposal.Source.Token.Id, proposal.Target.Token.Id));
            }
        }

        return pairs;
    }

    private static EvidentiaAttachment? Attachment(EvidentiaProposal proposal) =>
        proposal.Trace?.Rationale.Split(' ', 2)[0] is { } name && Enum.TryParse<EvidentiaAttachment>(name, out var attachment)
            ? attachment
            : null;

    internal static EvidentiaAttachmentPlacement Placement(EvidentiaAttachment attachment, string witnessLanguage) =>
        witnessLanguage.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
            ? attachment switch
            {
                EvidentiaAttachment.Article or EvidentiaAttachment.Conjunction or EvidentiaAttachment.Preposition =>
                    EvidentiaAttachmentPlacement.OwnKindBefore,
                EvidentiaAttachment.SubjectPronoun => EvidentiaAttachmentPlacement.AgreeingRendering,
                EvidentiaAttachment.Infinitive => EvidentiaAttachmentPlacement.Infinitive,
                EvidentiaAttachment.Copula => EvidentiaAttachmentPlacement.BeBeside,
                EvidentiaAttachment.Expletive => EvidentiaAttachmentPlacement.Rendering,
                EvidentiaAttachment.Subordinator => EvidentiaAttachmentPlacement.ConjunctionBefore,
                _ => EvidentiaAttachmentPlacement.None,
            }
            : !witnessLanguage.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
            ? EvidentiaAttachmentPlacement.None
            : attachment switch
            {
                EvidentiaAttachment.Article or EvidentiaAttachment.AuxiliaryVerb => EvidentiaAttachmentPlacement.Rendering,
                EvidentiaAttachment.Conjunction or EvidentiaAttachment.Preposition => EvidentiaAttachmentPlacement.OwnKindBefore,
                EvidentiaAttachment.Genitive => EvidentiaAttachmentPlacement.Dependent,
                EvidentiaAttachment.SubjectPronoun or EvidentiaAttachment.PossessivePronoun or EvidentiaAttachment.ObjectPronoun =>
                    EvidentiaAttachmentPlacement.AgreeingRendering,
                EvidentiaAttachment.Infinitive => EvidentiaAttachmentPlacement.Infinitive,
                EvidentiaAttachment.Copula => EvidentiaAttachmentPlacement.BeBeside,
                EvidentiaAttachment.Expletive => EvidentiaAttachmentPlacement.Rendering,
                EvidentiaAttachment.Subordinator => EvidentiaAttachmentPlacement.ConjunctionBefore,
                _ => EvidentiaAttachmentPlacement.None,
            };

    /// <summary>What a word attaches as, and the word it belongs to; both null when it is not one of these.</summary>
    internal static (EvidentiaAttachment? Attachment, EvidentiaAnalysis? Head) Classify(
        IReadOnlyList<EvidentiaAnalysis> words,
        int index)
    {
        var word = words[index];
        var partOfSpeech = Class(word);
        if (partOfSpeech == "det" && Articles.Contains(word.Token.Surface))
        {
            return Found(EvidentiaAttachment.Article, Forward(words, index, "noun", "propn", "adj", "num"));
        }

        if (partOfSpeech == "conj" && Conjunctions.Contains(word.Token.Surface))
        {
            return Found(EvidentiaAttachment.Conjunction,
                Forward(words, index, "noun", "propn", "pron", "verb", "adj", "adv", "num"));
        }

        if (partOfSpeech == "conj" && word.Token.Relation == MarkerRelation
            && string.Equals(word.PartOfSpeech ?? word.Token.PartOfSpeech, SubordinatingConjunction, StringComparison.OrdinalIgnoreCase))
        {
            return Found(EvidentiaAttachment.Subordinator, Syntactic(words, word.Token.SyntacticHead));
        }

        if (partOfSpeech == "adp" && word.Token.Surface.Equals(Of, StringComparison.OrdinalIgnoreCase))
        {
            return Found(EvidentiaAttachment.Genitive, Forward(words, index, "noun", "propn", "adj", "num", "pron"));
        }

        if (partOfSpeech == "part" && word.Token.Surface.Equals(To, StringComparison.OrdinalIgnoreCase))
        {
            return Found(EvidentiaAttachment.Infinitive, Forward(words, index, "verb"));
        }

        if (partOfSpeech == "pron" && word.Token.Relation == ExpletiveRelation
            && word.Token.Surface.Equals(There, StringComparison.OrdinalIgnoreCase))
        {
            return Found(EvidentiaAttachment.Expletive, Syntactic(words, word.Token.SyntacticHead));
        }

        if (partOfSpeech is "aux" or "verb" && word.Lemma == Be && word.Token.Relation is { } relation
            && !AuxiliaryRelations.Contains(relation))
        {
            return Found(EvidentiaAttachment.Copula, CopulaAnchor(words, word));
        }

        if (partOfSpeech == "adp" && !UnplacedPrepositions.Contains(word.Token.Surface))
        {
            return Found(EvidentiaAttachment.Preposition,
                Forward(words, index, "noun", "propn", "pron", "adj", "num", "verb"));
        }

        if (partOfSpeech == "pron" && Feature(word, "PronType") == "Prs" && Feature(word, "Reflex") is null
            && EvidentiaPersonAgreement.Person(word) is { } person)
        {
            if (Feature(word, "Poss") == "Yes")
            {
                return Found(EvidentiaAttachment.PossessivePronoun, Forward(words, index, "noun", "propn", "adj"));
            }

            // You states no case and is read as the subject of a verb after it. It is never read as a
            // subject: as often as not it is the empty it of it happened, which the original does not
            // write at all.
            var subject = Feature(word, "Case") is { } stated
                ? stated.Equals("Nom", StringComparison.OrdinalIgnoreCase) && Feature(word, "Gender") != "Neut"
                : person == "2" && Forward(words, index, "verb") is not null;
            return subject
                ? Found(EvidentiaAttachment.SubjectPronoun, Forward(words, index, "verb"))
                : Found(EvidentiaAttachment.ObjectPronoun, Backward(words, index, "verb", "adp"));
        }

        return word.Role == EvidentiaAuxiliaryRole.AuxiliaryVerb
            ? Found(EvidentiaAttachment.AuxiliaryVerb, Forward(words, index, "verb"))
            : (null, null);
    }

    /// <summary>
    /// The proposal the attached word follows, and whether it goes after that proposal rather than on
    /// it: in <em>said to him</em> the <em>to</em> is never placed, and <em>him</em> goes on the
    /// preposition written after the word <em>said</em> was placed on.
    /// </summary>
    private static (EvidentiaProposal? Proposal, bool AfterVerb) HeadProposal(
        EvidentiaAttachment attachment,
        int index,
        EvidentiaAnalysis head,
        IReadOnlyList<EvidentiaAnalysis> words,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource)
    {
        // Greek writes the article before its noun and repeats it before an attributive adjective
        // (τὸ πνεῦμα τὸ ἀκάθαρτον), so the unclean spirit puts the on the noun's article.
        if (attachment == EvidentiaAttachment.Article && Class(head) is "adj" or "num"
            && Forward(words, index, "noun", "propn") is { } noun
            && placedBySource.TryGetValue(noun.Token.Id, out var nounProposal)
            && nounProposal.Target.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return (nounProposal, false);
        }

        if (placedBySource.TryGetValue(head.Token.Id, out var proposal))
        {
            return (proposal, false);
        }

        if (attachment != EvidentiaAttachment.ObjectPronoun || Class(head) != "adp")
        {
            return (null, false);
        }

        var at = IndexOf(words, head);
        return at > 0 && Class(words[at - 1]) == "verb" && !Punctuated(words[at - 1])
            && placedBySource.TryGetValue(words[at - 1].Token.Id, out var verb)
                ? (verb, true)
                : (null, false);
    }

    private static EvidentiaAnalysis? Place(
        EvidentiaAttachment attachment,
        IReadOnlyList<EvidentiaAnalysis> words,
        int index,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        bool afterVerb,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlySet<long> taken)
    {
        var word = words[index];
        return Placement(attachment, rendering.Token.Language) switch
        {
            EvidentiaAttachmentPlacement.Rendering => rendering,
            EvidentiaAttachmentPlacement.Dependent => index > 0
                && placedBySource.TryGetValue(words[index - 1].Token.Id, out var governing)
                && Class(words[index - 1]) is "noun" or "propn" or "adj" or "num"
                    ? Dependent(governing.Target, rendering, verse)
                    : null,
            EvidentiaAttachmentPlacement.OwnKindBefore => OwnKindBefore(attachment, word, rendering, verse, taken),
            EvidentiaAttachmentPlacement.AgreeingRendering => afterVerb
                ? PrepositionAfter(word, rendering, verse, taken)
                : Agreeing(attachment, word, rendering, verse),
            EvidentiaAttachmentPlacement.Infinitive => Infinitive(rendering, verse, taken),
            EvidentiaAttachmentPlacement.BeBeside => BeBeside(rendering, verse, taken),
            EvidentiaAttachmentPlacement.ConjunctionBefore => ConjunctionBefore(rendering, verse, taken),
            _ => null,
        };
    }

    /// <summary>
    /// The word a form of <em>be</em> links, whose rendering its verb stands beside: the subject of
    /// <em>there was light</em>, which the parse makes the verb's own, and the predicate of <em>it was
    /// good</em>, which the parse makes the verb's head.
    /// </summary>
    private static EvidentiaAnalysis? CopulaAnchor(IReadOnlyList<EvidentiaAnalysis> words, EvidentiaAnalysis be)
    {
        var dependents = words.Where(other => other.Token.SyntacticHead == be.Token.Id).ToList();
        if (dependents.Any(other => other.Token.Relation == ExpletiveRelation))
        {
            var subjects = dependents.Where(other => other.Token.Relation == SubjectRelation).ToList();
            return subjects.Count == 1 ? subjects[0] : null;
        }

        return be.Token.Relation == CopulaRelation ? Syntactic(words, be.Token.SyntacticHead) : null;
    }

    private static EvidentiaAnalysis? Syntactic(IReadOnlyList<EvidentiaAnalysis> words, long? id) =>
        id is { } head ? words.FirstOrDefault(other => other.Token.Id == head) : null;

    /// <summary>
    /// The original's own <em>be</em>, free, written directly before or after the rendering: וַיְהִי עֶרֶב.
    /// Further away it is as often another clause's verb.
    /// </summary>
    private static EvidentiaAnalysis? BeBeside(
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        var at = IndexOf(verse, rendering);
        var beside = new[] { at - 1, at + 1 }
            .Where(index => at >= 0 && index >= 0 && index < verse.Count)
            .Select(index => verse[index])
            .Where(word => word.Token.StrongNumber is { } strong && BeVerbs.Contains(strong) && !taken.Contains(word.Token.Id))
            .ToList();
        return beside.Count == 1 ? beside[0] : null;
    }

    /// <summary>
    /// Where <em>to</em> goes once its verb is placed on an infinitive: on the ל written before it, or on
    /// the infinitive itself where the original writes none, as Greek never does. A verb rendered by any
    /// other form leaves it unplaced, since <em>to</em> is then often a ἵνα the parse does not see.
    /// </summary>
    private static EvidentiaAnalysis? Infinitive(
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        if (!IsInfinitive(rendering))
        {
            return null;
        }

        var at = IndexOf(verse, rendering);
        return at > 0 && Class(verse[at - 1]) == "adp" && !taken.Contains(verse[at - 1].Token.Id)
            && verse[at - 1].Token.StrongNumber != ObjectMarker
            ? verse[at - 1]
            : rendering;
    }

    /// <summary>
    /// The conjunction written directly before the rendering of the clause's verb - כִּי, אֲשֶׁר, ὅτι,
    /// εἰ - when it is free and is not the original's <em>and</em>. One word further back it is as often
    /// the conjunction of another clause.
    /// </summary>
    private static EvidentiaAnalysis? ConjunctionBefore(
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        var at = IndexOf(verse, rendering);
        return at > 0 && Class(verse[at - 1]) == "conj" && !taken.Contains(verse[at - 1].Token.Id)
            && !(verse[at - 1].Token.StrongNumber is { } strong && Coordinators.Contains(strong))
            ? verse[at - 1]
            : null;
    }

    private static bool IsInfinitive(EvidentiaAnalysis word) =>
        EvidentiaPersonAgreement.IsInfinitive(word)
        || string.Equals(Feature(word, "mood"), "infinitive", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where <em>of</em> goes once the noun before it and the noun after it are placed: on the governed
    /// rendering, when the governing one is a construct standing before it with at most an article between.
    /// </summary>
    private static EvidentiaAnalysis? Dependent(
        EvidentiaAnalysis governing,
        EvidentiaAnalysis governed,
        IReadOnlyList<EvidentiaAnalysis> verse)
    {
        var from = IndexOf(verse, governing);
        var to = IndexOf(verse, governed);
        return from >= 0 && to > from
            && verse.Skip(from + 1).Take(to - from - 1).All(between => Class(between) == "det")
            && string.Equals(Feature(governing, "state"), ConstructState, StringComparison.OrdinalIgnoreCase)
                ? governed
                : null;
    }

    /// <summary>
    /// The rendering, if it names the pronoun's person: a subject in the verb's own ending - a
    /// participle names none - or in the suffix of a Hebrew infinitive; a possessive or an object in
    /// the suffix.
    /// </summary>
    private static EvidentiaAnalysis? Agreeing(
        EvidentiaAttachment attachment,
        EvidentiaAnalysis pronoun,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse) =>
        attachment switch
        {
            EvidentiaAttachment.SubjectPronoun when Class(rendering) == "verb"
                && (EvidentiaPersonAgreement.Agrees(pronoun, rendering, suffix: false)
                    || EvidentiaPersonAgreement.IsInfinitive(rendering) && EvidentiaPersonAgreement.Agrees(pronoun, rendering, suffix: true))
                && !WritesTheSubjectApart(pronoun, rendering, verse) => rendering,
            EvidentiaAttachment.PossessivePronoun or EvidentiaAttachment.ObjectPronoun
                when EvidentiaPersonAgreement.Agrees(pronoun, rendering, suffix: true) => rendering,
            _ => null,
        };

    /// <summary>A pronoun of the same person standing by the verb, which is then where the subject is written.</summary>
    private static bool WritesTheSubjectApart(
        EvidentiaAnalysis pronoun,
        EvidentiaAnalysis verb,
        IReadOnlyList<EvidentiaAnalysis> verse) =>
        verse.Any(word => word.Token.Id != verb.Token.Id
            && Math.Abs(word.Token.Position - verb.Token.Position) <= SubjectPronounReach
            && Class(word) == "pron"
            && EvidentiaPersonAgreement.CouldBeTheSubject(pronoun, word));

    private static EvidentiaAnalysis? PrepositionAfter(
        EvidentiaAnalysis pronoun,
        EvidentiaAnalysis verb,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        var at = IndexOf(verse, verb);
        return at < 0
            ? null
            : verse.Skip(at + 1).Take(ObjectPrepositionReach).FirstOrDefault(word =>
                Class(word) == "adp" && !taken.Contains(word.Token.Id)
                && EvidentiaPersonAgreement.Agrees(pronoun, word, suffix: true));
    }

    private static int IndexOf(IReadOnlyList<EvidentiaAnalysis> words, EvidentiaAnalysis word)
    {
        for (var index = 0; index < words.Count; index++)
        {
            if (words[index].Token.Id == word.Token.Id)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The word of the attachment's kind among the grammatical words written directly before the
    /// rendering. BHSA writes a conjunction, a preposition and an article as words of their own, in
    /// that order back from the word they belong to: וּ בַ הַ מַּיִם.
    /// </summary>
    private static EvidentiaAnalysis? OwnKindBefore(
        EvidentiaAttachment attachment,
        EvidentiaAnalysis word,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        if (attachment == EvidentiaAttachment.Preposition)
        {
            return Class(rendering) == "adp" ? rendering : PrepositionBefore(rendering, verse, taken);
        }

        if (attachment == EvidentiaAttachment.Article)
        {
            return ArticleBefore(word, rendering, verse, taken);
        }

        var wanted = attachment == EvidentiaAttachment.Conjunction ? "conj" : null;
        var before = IndexOf(verse, rendering);
        for (before--; wanted is not null && before >= 0 && Class(verse[before]) is "det" or "adp" or "conj"; before--)
        {
            if (Class(verse[before]) == wanted && IsTheWitnessAnd(verse[before]))
            {
                return verse[before];
            }
        }

        return null;
    }

    /// <summary>
    /// The Greek article written before the rendering, past an adjective it also governs: <em>the</em>
    /// of <em>the other side</em> on τὸ of τὸ πέραν. Greek has no indefinite article, so only
    /// <em>the</em> is placed.
    /// </summary>
    private static EvidentiaAnalysis? ArticleBefore(
        EvidentiaAnalysis article,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        if (!article.Token.Surface.Equals(DefiniteArticle, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        for (var before = IndexOf(verse, rendering) - 1; before >= 0; before--)
        {
            if (Class(verse[before]) == "det")
            {
                return taken.Contains(verse[before].Token.Id) ? null : verse[before];
            }

            if (Class(verse[before]) != "adj")
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>The nearest preposition written before the rendering, past an article: מִן הָ אֲדָמָה.</summary>
    private static EvidentiaAnalysis? PrepositionBefore(
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        for (var before = IndexOf(verse, rendering) - 1; before >= 0 && Class(verse[before]) is "det" or "adp" or "conj"; before--)
        {
            if (Class(verse[before]) == "adp")
            {
                return taken.Contains(verse[before].Token.Id) || verse[before].Token.StrongNumber == ObjectMarker
                    ? null
                    : verse[before];
            }
        }

        return null;
    }

    private static bool IsTheWitnessAnd(EvidentiaAnalysis word) =>
        !word.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
        || string.Equals(word.Token.Lemma, GreekAnd, StringComparison.Ordinal);

    private static (EvidentiaAttachment?, EvidentiaAnalysis?) Found(EvidentiaAttachment attachment, EvidentiaAnalysis? head) =>
        head is null ? (null, null) : (attachment, head);

    /// <summary>
    /// The nearest following word of one of the classes, within reach, not across punctuation or a
    /// negation: <em>must not eat</em> is annotated with its negation, which an original writes apart.
    /// </summary>
    private static EvidentiaAnalysis? Forward(IReadOnlyList<EvidentiaAnalysis> words, int index, params string[] classes)
    {
        for (var next = index + 1; next < words.Count && next - index <= HeadReach; next++)
        {
            if (Punctuated(words[next - 1]) || Negations.Contains(words[next].Token.Surface))
            {
                return null;
            }

            if (classes.Contains(Class(words[next])))
            {
                return words[next];
            }
        }

        return null;
    }

    /// <summary>The nearest earlier word of one of the classes, within reach, not across punctuation or a negation.</summary>
    private static EvidentiaAnalysis? Backward(IReadOnlyList<EvidentiaAnalysis> words, int index, params string[] classes)
    {
        for (var back = index - 1; back >= 0 && index - back <= HeadReach; back--)
        {
            if (Punctuated(words[back]) || Negations.Contains(words[back].Token.Surface))
            {
                return null;
            }

            if (classes.Contains(Class(words[back])))
            {
                return words[back];
            }
        }

        return null;
    }

    private static bool Punctuated(EvidentiaAnalysis word) => word.Token.Trailer.Any(char.IsPunctuation);

    private static string? Feature(EvidentiaAnalysis word, string name) =>
        word.Token.Morphology?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? Class(EvidentiaAnalysis word) =>
        EvidentiaMorphologyLabels.PartOfSpeech(word.PartOfSpeech ?? word.Token.PartOfSpeech, word.Token.Language);
}
