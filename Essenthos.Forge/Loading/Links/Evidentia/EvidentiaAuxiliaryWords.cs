namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// A word whose correspondence is fixed by the word it belongs to rather than by its own meaning.
/// Only the roles a witness or a parser states are marked; anything else is <see cref="None"/> and
/// is placed on its own lexical evidence as before.
/// </summary>
internal enum EvidentiaAuxiliaryRole
{
    None,

    /// <summary>A personal pronoun outside the nominative, such as <em>them</em> or <em>him</em>.</summary>
    PersonalPronoun,

    /// <summary>A personal pronoun in the nominative, whose referent an inflected original carries in its verb.</summary>
    SubjectPronoun,

    /// <summary><em>went out</em>, <em>came down</em>: the particle completes its verb's meaning.</summary>
    VerbParticle,

    /// <summary><em>had</em> of <em>had gone</em>, <em>will</em> of <em>will send</em>: tense and mood an original writes in its verb.</summary>
    AuxiliaryVerb,

    /// <summary>A witness article that belongs to the noun or participle after it.</summary>
    NominalArticle,

    /// <summary>A Greek article standing for a person, as in <em>ὁ δὲ εἶπεν</em>.</summary>
    PronominalArticle,
}

/// <summary>
/// Auxiliary words are placed only on the same kind of word on the other side, or on the word they
/// belong to. A learned habit cannot override that: an index learned from phrase links pairs every
/// word of a phrase with every word of the other, so it holds <em>they</em> on the Greek article and
/// <em>out</em> on מִן as often as the phrases did, and the one-to-one assignment would otherwise hand
/// such a form whichever occurrence of the habit is free in the verse.
///
/// <para>Deliberately not covered: possessive and reflexive pronouns, which a Hebrew suffix on a noun
/// or a preposition carries; object pronouns onto a Hebrew preposition, which is where the suffix
/// stands; position relative to the host word; and every source language but English,
/// since the roles come from the English UDPipe parse.</para>
/// </summary>
internal static class EvidentiaAuxiliaryWords
{
    private static readonly HashSet<string> VerbParticles = new(StringComparer.OrdinalIgnoreCase)
    {
        "out", "down", "up", "away", "forth", "off",
    };

    /// <summary>
    /// The auxiliaries themselves, modern and King James. The parser's tag alone is not enough: it
    /// reads the <em>'s</em> of <em>brother's</em> as <em>is</em>, and a name before a verb as a modal.
    /// </summary>
    private static readonly HashSet<string> AuxiliaryVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "am", "are", "art", "be", "been", "being", "is", "was", "were", "wast", "wert",
        "have", "has", "had", "hast", "hath", "having", "hadst",
        "do", "does", "did", "doth", "dost", "didst",
        "will", "would", "wilt", "shall", "should", "shalt", "shouldest",
        "may", "might", "mayest", "must", "can", "could", "canst", "couldest",
    };

    /// <summary>
    /// How far back a particle looks for its verb: <em>drove them out</em>, <em>brought the man out</em>.
    /// Further than that, across punctuation, or past a preposition or conjunction (<em>from off the
    /// face</em>), the particle is more often a preposition's.
    /// </summary>
    private const int ParticleReach = 3;

    public static IReadOnlyList<EvidentiaAnalysis> Mark(IReadOnlyList<EvidentiaAnalysis> analyses)
    {
        var marked = new EvidentiaAnalysis[analyses.Count];
        for (var index = 0; index < analyses.Count; index++)
        {
            var role = Role(analyses, index);
            marked[index] = role == EvidentiaAuxiliaryRole.None ? analyses[index] : analyses[index] with { Role = role };
        }

        return marked;
    }

    /// <summary>
    /// Whether placing the source word on the target would put an auxiliary word on a kind of word it
    /// cannot correspond to, or put anything but an article on the article of a noun, which belongs to
    /// that noun whatever the index learned from phrases, or a personal pronoun on a Greek one whose
    /// number or case says it is another. A target that states no class is not refused:
    /// nothing is known against it.
    /// </summary>
    public static bool PlacesOffItsKind(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        if (target.Role == EvidentiaAuxiliaryRole.NominalArticle && IsEnglish(source.Token)
            && Feature(source.Token, "PronType") != "Art")
        {
            return true;
        }

        if (DisagreesAsAPersonalPronoun(source, target))
        {
            return true;
        }

        var targetClass = EvidentiaMorphologyLabels.PartOfSpeech(target.PartOfSpeech, target.Token.Language);
        return source.Role switch
        {
            EvidentiaAuxiliaryRole.VerbParticle => targetClass is not (null or "verb" or "adv"),
            EvidentiaAuxiliaryRole.AuxiliaryVerb => targetClass is not (null or "verb"),
            EvidentiaAuxiliaryRole.PersonalPronoun => target.Role == EvidentiaAuxiliaryRole.NominalArticle,
            EvidentiaAuxiliaryRole.SubjectPronoun => target.Role == EvidentiaAuxiliaryRole.NominalArticle
                || targetClass is "adp" or "conj"
                || Case(target) is { } targetCase && targetCase != "nom",
            _ => false,
        };
    }

    private static EvidentiaAuxiliaryRole Role(IReadOnlyList<EvidentiaAnalysis> analyses, int index)
    {
        var analysis = analyses[index];
        var token = analysis.Token;
        var partOfSpeech = EvidentiaMorphologyLabels.PartOfSpeech(analysis.PartOfSpeech, token.Language);
        if (partOfSpeech == "det")
        {
            return ArticleRole(analyses, index);
        }

        if (!IsEnglish(token))
        {
            return EvidentiaAuxiliaryRole.None;
        }

        if (partOfSpeech == "pron" && Feature(token, "PronType") == "Prs"
            && Feature(token, "Poss") is null && Feature(token, "Reflex") is null)
        {
            return Case(analysis) == "nom" ? EvidentiaAuxiliaryRole.SubjectPronoun : EvidentiaAuxiliaryRole.PersonalPronoun;
        }

        if (partOfSpeech == "aux" && AuxiliaryVerbs.Contains(token.Surface))
        {
            return EvidentiaAuxiliaryRole.AuxiliaryVerb;
        }

        return VerbParticles.Contains(token.Surface) && FollowsItsVerb(analyses, index)
            ? EvidentiaAuxiliaryRole.VerbParticle
            : EvidentiaAuxiliaryRole.None;
    }

    /// <summary>
    /// Hebrew's article is a prefix of its noun and never stands alone. Greek's stands for a person
    /// when a particle or conjunction follows it (<em>οἱ δὲ εἶπαν</em>) and what comes after that is
    /// not a word it could be the article of (<em>ἡ δὲ πενθερὰ</em>); otherwise it is the article of
    /// what follows.
    /// </summary>
    private static EvidentiaAuxiliaryRole ArticleRole(IReadOnlyList<EvidentiaAnalysis> analyses, int index)
    {
        var article = analyses[index];
        if (!article.Token.Language.Equals("grc", StringComparison.OrdinalIgnoreCase))
        {
            return EvidentiaAuxiliaryRole.NominalArticle;
        }

        var next = index + 1;
        while (next < analyses.Count && analyses[next].Token.Address == article.Token.Address
               && EvidentiaMorphologyLabels.PartOfSpeech(analyses[next].PartOfSpeech, analyses[next].Token.Language) is "conj" or "part")
        {
            next++;
        }

        if (next == index + 1)
        {
            return EvidentiaAuxiliaryRole.NominalArticle;
        }

        if (next == analyses.Count || analyses[next].Token.Address != article.Token.Address)
        {
            return EvidentiaAuxiliaryRole.PronominalArticle;
        }

        var head = analyses[next];
        var headClass = EvidentiaMorphologyLabels.PartOfSpeech(head.PartOfSpeech, head.Token.Language);
        return headClass is "noun" or "propn" or "adj" or "num" || Case(head) is { } headCase && headCase == Case(article)
            ? EvidentiaAuxiliaryRole.NominalArticle
            : EvidentiaAuxiliaryRole.PronominalArticle;
    }

    private static bool FollowsItsVerb(IReadOnlyList<EvidentiaAnalysis> analyses, int index)
    {
        var particle = analyses[index].Token;
        for (var back = index - 1; back >= 0 && index - back <= ParticleReach; back--)
        {
            var previous = analyses[back];
            if (previous.Token.Address != particle.Address || previous.Token.Trailer.Any(char.IsPunctuation))
            {
                return false;
            }

            var previousClass = EvidentiaMorphologyLabels.PartOfSpeech(previous.PartOfSpeech, previous.Token.Language);
            if (previousClass == "verb")
            {
                return true;
            }

            if (previousClass is "adp" or "conj")
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// An English personal pronoun against a Greek one, where both say what they are: <em>his</em> is
    /// a genitive and <em>him</em> is one man, so neither is αὐτῷ nor αὐτούς. The second person is left
    /// alone, because <em>you</em> does not say how many it addresses and the parser guesses.
    /// </summary>
    private static bool DisagreesAsAPersonalPronoun(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        if (!IsEnglish(source.Token) || Feature(source.Token, "PronType") != "Prs"
            || !target.Token.Language.Equals("grc", StringComparison.OrdinalIgnoreCase)
            || EvidentiaMorphologyLabels.PartOfSpeech(target.PartOfSpeech, target.Token.Language) != "pron")
        {
            return false;
        }

        if (Feature(source.Token, "Poss") == "Yes" && Case(target) is { } targetCase && targetCase != "gen")
        {
            return true;
        }

        return Feature(source.Token, "Person") is "1" or "3"
               && Number(source) is { } sourceNumber
               && Number(target) is { } targetNumber
               && sourceNumber != targetNumber;
    }

    private static string? Number(EvidentiaAnalysis analysis) =>
        EvidentiaMorphologyLabels.Feature(Feature(analysis.Token, "number"), analysis.Token.Language);

    private static bool IsEnglish(EvidentiaToken token) =>
        token.Language.Equals("eng", StringComparison.OrdinalIgnoreCase);

    private static string? Case(EvidentiaAnalysis analysis) =>
        EvidentiaMorphologyLabels.Feature(Feature(analysis.Token, "case"), analysis.Token.Language);

    private static string? Feature(EvidentiaToken token, string name) =>
        token.Morphology?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
