namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>What a grammatical word of a translation is attached as.</summary>
internal enum EvidentiaAttachment
{
    None,

    /// <summary><em>the</em> of <em>the waters</em>.</summary>
    Article,

    /// <summary><em>and</em> before the word it joins on.</summary>
    Conjunction,

    /// <summary><em>will</em> of <em>will send</em>.</summary>
    AuxiliaryVerb,

    /// <summary><em>he</em> of <em>he said</em>, which a Hebrew or Greek verb carries in its ending.</summary>
    SubjectPronoun,

    /// <summary><em>his</em> of <em>his sons</em>, which Hebrew writes as a suffix of the noun.</summary>
    PossessivePronoun,

    /// <summary><em>him</em> of <em>struck him</em> or <em>to him</em>, a suffix of the verb or of the preposition.</summary>
    ObjectPronoun,
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
    /// On the rendering when its ending or suffix names the same person, number and gender as the
    /// pronoun; a pronoun object of an English preposition goes on the preposition written after the
    /// verb, when its suffix names it.
    /// </summary>
    AgreeingRendering,
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
/// <para>Against Greek only <em>and</em> and the subject pronoun are placed: <em>and</em> on the
/// <em>καί</em> written directly before the rendering, which both annotations do, and the subject on
/// the verb whose ending names it.</para>
///
/// <para>Not placed, because on these texts they measured below the precision the lexical tiers
/// already reach: verb particles, which the parser finds as often in a preposition's place; the
/// Greek article, which the Berean tables put on the English article and the Clear Bible
/// alignments with the noun; and a Greek auxiliary, which is often a periphrastic <em>ἦσαν</em> of
/// its own rather than part of the verb after it. The roles come from the English UDPipe parse, so
/// every other source language is left alone.</para>
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

    private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase) { "the", "a", "an" };

    private static readonly HashSet<string> Conjunctions = new(StringComparer.OrdinalIgnoreCase) { "and" };

    private static readonly HashSet<string> Negations = new(StringComparer.OrdinalIgnoreCase) { "not", "n't", "never" };

    /// <summary>How far after its verb the preposition carrying an object pronoun may stand: <em>וַיֹּאמֶר לוֹ</em>.</summary>
    private const int ObjectPrepositionReach = 2;

    /// <summary>How far from the verb an original writes a subject pronoun of its own: <em>וְהוּא יִמְשָׁל</em>.</summary>
    private const int SubjectPronounReach = 3;

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
        foreach (var verse in source
                     .Where(analysis => analysis.Token.Language.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(analysis => analysis.Token.Id)
                     .GroupBy(analysis => analysis.Token.Address))
        {
            var words = verse.OrderBy(analysis => analysis.Token.Position).ToList();
            for (var index = 0; index < words.Count; index++)
            {
                if (placedBySource.ContainsKey(words[index].Token.Id)
                    || Classify(words, index) is not ({ } attachment, { } head)
                    || HeadProposal(attachment, head, words, placedBySource) is not ({ } headProposal, var afterVerb)
                    || !targetsByVerse.TryGetValue(headProposal.Target.Token.Address, out var targetVerse)
                    || Place(attachment, words[index], headProposal.Target, targetVerse, afterVerb, taken) is not { } placement)
                {
                    continue;
                }

                taken.Add(placement.Token.Id);

                proposals.Add(new EvidentiaProposal(
                    words[index],
                    placement,
                    EvidentiaProposalKind.AttachedWord,
                    Math.Max(0, headProposal.Confidence - ConfidenceBelowHead),
                    new EvidentiaDecisionTrace(
                        "attached",
                        $"{attachment} of '{head.Token.Surface}'",
                        headProposal.Trace?.Evidence ?? [])));
            }
        }

        return proposals;
    }

    internal static EvidentiaAttachmentPlacement Placement(EvidentiaAttachment attachment, string witnessLanguage) =>
        witnessLanguage.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
            ? attachment switch
            {
                EvidentiaAttachment.Conjunction => EvidentiaAttachmentPlacement.OwnKindBefore,
                EvidentiaAttachment.SubjectPronoun => EvidentiaAttachmentPlacement.AgreeingRendering,
                _ => EvidentiaAttachmentPlacement.None,
            }
            : !witnessLanguage.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
            ? EvidentiaAttachmentPlacement.None
            : attachment switch
            {
                EvidentiaAttachment.Article or EvidentiaAttachment.AuxiliaryVerb => EvidentiaAttachmentPlacement.Rendering,
                EvidentiaAttachment.Conjunction => EvidentiaAttachmentPlacement.OwnKindBefore,
                EvidentiaAttachment.SubjectPronoun or EvidentiaAttachment.PossessivePronoun or EvidentiaAttachment.ObjectPronoun =>
                    EvidentiaAttachmentPlacement.AgreeingRendering,
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
        EvidentiaAnalysis head,
        IReadOnlyList<EvidentiaAnalysis> words,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource)
    {
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
        EvidentiaAnalysis word,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        bool afterVerb,
        IReadOnlySet<long> taken) =>
        Placement(attachment, rendering.Token.Language) switch
        {
            EvidentiaAttachmentPlacement.Rendering => rendering,
            EvidentiaAttachmentPlacement.OwnKindBefore => OwnKindBefore(attachment, rendering, verse),
            EvidentiaAttachmentPlacement.AgreeingRendering => afterVerb
                ? PrepositionAfter(word, rendering, verse, taken)
                : Agreeing(attachment, word, rendering, verse),
            _ => null,
        };

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
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse)
    {
        var wanted = attachment == EvidentiaAttachment.Conjunction ? "conj" : null;
        var before = verse.Count - 1;
        while (before >= 0 && verse[before].Token.Id != rendering.Token.Id)
        {
            before--;
        }

        for (before--; wanted is not null && before >= 0 && Class(verse[before]) is "det" or "adp" or "conj"; before--)
        {
            if (Class(verse[before]) == wanted && IsTheWitnessAnd(verse[before]))
            {
                return verse[before];
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
