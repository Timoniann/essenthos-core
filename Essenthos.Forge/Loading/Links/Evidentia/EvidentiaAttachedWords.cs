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
/// <para>Against Greek only <em>and</em> is placed, on the <em>καί</em> written directly before the
/// rendering, which both annotations do.</para>
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
                    || !placedBySource.TryGetValue(head.Token.Id, out var headProposal)
                    || !targetsByVerse.TryGetValue(headProposal.Target.Token.Address, out var targetVerse)
                    || Place(attachment, headProposal.Target, targetVerse) is not { } placement)
                {
                    continue;
                }

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
            ? attachment == EvidentiaAttachment.Conjunction ? EvidentiaAttachmentPlacement.OwnKindBefore : EvidentiaAttachmentPlacement.None
            : !witnessLanguage.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
            ? EvidentiaAttachmentPlacement.None
            : attachment switch
            {
                EvidentiaAttachment.Article or EvidentiaAttachment.AuxiliaryVerb => EvidentiaAttachmentPlacement.Rendering,
                EvidentiaAttachment.Conjunction => EvidentiaAttachmentPlacement.OwnKindBefore,
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

        return word.Role == EvidentiaAuxiliaryRole.AuxiliaryVerb
            ? Found(EvidentiaAttachment.AuxiliaryVerb, Forward(words, index, "verb"))
            : (null, null);
    }

    private static EvidentiaAnalysis? Place(
        EvidentiaAttachment attachment,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse) =>
        Placement(attachment, rendering.Token.Language) switch
        {
            EvidentiaAttachmentPlacement.Rendering => rendering,
            EvidentiaAttachmentPlacement.OwnKindBefore => OwnKindBefore(attachment, rendering, verse),
            _ => null,
        };

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

    private static bool Punctuated(EvidentiaAnalysis word) => word.Token.Trailer.Any(char.IsPunctuation);

    private static string? Class(EvidentiaAnalysis word) =>
        EvidentiaMorphologyLabels.PartOfSpeech(word.PartOfSpeech ?? word.Token.PartOfSpeech, word.Token.Language);
}
