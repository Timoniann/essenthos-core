namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>Which side of a correspondence has nothing on the other.</summary>
internal enum EvidentiaAbsenceKind
{
    /// <summary>A word of the translation the original does not have: <em>a</em>, or <em>the</em> of <em>the face</em>. Written as <c>expands</c>.</summary>
    Supplied,

    /// <summary>A word of the original the translation does not render: את, or a ו no <em>and</em> stands for. Written as <c>omits</c>.</summary>
    NotRendered,
}

/// <summary>
/// One rule that says a word has no counterpart, with how often it was right where the Berean's own
/// tables could judge it: the Berean Standard Bible read against them over the benchmark passages and
/// the unseen validation passages. A stored absence claims that record — its confidence is the rule's
/// measured precision, smoothed so that a rule never wrong on a few hundred words does not claim
/// certainty.
/// </summary>
/// <param name="Spelling">The rule's stored name, which a link's source names it by.</param>
internal sealed record EvidentiaAbsenceRule(string Spelling, EvidentiaAbsenceKind Kind, string Rationale, int Right, int Judged)
{
    public static readonly EvidentiaAbsenceRule IndefiniteArticle =
        new("supplied-indefinite-article", EvidentiaAbsenceKind.Supplied, "indefinite article", 386, 386);

    public static readonly EvidentiaAbsenceRule UnwrittenArticle =
        new("supplied-article", EvidentiaAbsenceKind.Supplied, "article the original does not write", 1_072, 1_104);

    public static readonly EvidentiaAbsenceRule Conjunction =
        new("unrendered-conjunction", EvidentiaAbsenceKind.NotRendered, "conjunction no word of the verse renders", 192, 199);

    public static readonly EvidentiaAbsenceRule ObjectMarker =
        new("unrendered-object-marker", EvidentiaAbsenceKind.NotRendered, "object marker", 267, 274);

    public static readonly EvidentiaAbsenceRule Article =
        new("unrendered-article", EvidentiaAbsenceKind.NotRendered, "article no the of the verse renders", 117, 118);

    /// <summary>
    /// The safe tier's precision on the independent sample. An absence in that tier whose rule is
    /// measured at least this well is held more surely than the aligner's guess on the same word.
    /// </summary>
    public const double SafeTierPrecision = 0.9554;

    /// <summary>
    /// The article rules of German and Spanish. No key states their absences, so each was read by hand on
    /// twenty-four chapters drawn at random, six strata of the canon twice over, in Luther 1912, the
    /// Elberfelder 1905 and the Reina-Valera 1909: the first twelve as the rules stood before the
    /// errors read there were fixed, the second twelve as they stand. A supplied article counts right when
    /// the original writes no article of its own for the word, as the Berean's key judges the English rule.
    /// The articles are found by the UDPipe parse, which the rationale credits.
    /// </summary>
    public static readonly EvidentiaAbsenceRule GermanIndefiniteArticle =
        new("supplied-indefinite-article-deu", EvidentiaAbsenceKind.Supplied,
            "indefinite article" + GermanParse, 151, 152);

    public static readonly EvidentiaAbsenceRule GermanUnwrittenArticle =
        new("supplied-article-deu", EvidentiaAbsenceKind.Supplied,
            "article the original does not write" + GermanParse, 598, 616);

    public static readonly EvidentiaAbsenceRule SpanishIndefiniteArticle =
        new("supplied-indefinite-article-spa", EvidentiaAbsenceKind.Supplied,
            "indefinite article" + SpanishParse, 22, 22);

    public static readonly EvidentiaAbsenceRule SpanishUnwrittenArticle =
        new("supplied-article-spa", EvidentiaAbsenceKind.Supplied,
            "article the original does not write" + SpanishParse, 414, 427);

    private const string GermanParse =
        ", read as one by UDPipe's German-HDT model (Straka and Straková, ÚFAL, CC BY-NC-SA 4.0)";

    private const string SpanishParse =
        ", read as one by UDPipe's Spanish-AnCora model (Straka and Straková, ÚFAL, CC BY-NC-SA 4.0)";

    public static readonly IReadOnlyList<EvidentiaAbsenceRule> All =
    [
        IndefiniteArticle, UnwrittenArticle, Conjunction, ObjectMarker, Article,
        GermanIndefiniteArticle, GermanUnwrittenArticle, SpanishIndefiniteArticle, SpanishUnwrittenArticle,
    ];

    /// <summary>The measured precision with one right and one wrong added, so it stays below 1.</summary>
    public double Confidence => (Right + 1.0) / (Judged + 2.0);

    /// <summary>Whether a safe-tier absence by this rule takes its word out of a link only the aligner states.</summary>
    public bool OutranksTheAligner => Confidence >= SafeTierPrecision;

    public static EvidentiaAbsenceRule? Named(string? spelling) =>
        All.FirstOrDefault(rule => rule.Spelling == spelling);
}

/// <param name="Anchor">The placed word the claim rests on: the head of a supplied article, or the word a prefix is written onto.</param>
internal sealed record EvidentiaAbsence(
    EvidentiaAnalysis Word,
    EvidentiaAbsenceRule Rule,
    EvidentiaProposal? Anchor)
{
    public EvidentiaAbsenceKind Kind => Rule.Kind;

    public string Rationale => Rule.Rationale;
}

/// <summary>
/// The grammatical words that render one another across the languages: <em>and</em> and its kin for ו,
/// καί and δέ; <em>the</em> for the article; an English preposition for a preposition of the original.
/// </summary>
internal enum EvidentiaFunctionClass
{
    Coordinator,
    Article,
    Preposition,
}

/// <summary>
/// Says where a word has no counterpart, once every word that has one is placed. An absence is a claim
/// like a link and is made only where the verse leaves no other reading: a ו is unrendered only when
/// no <em>and</em>, <em>then</em>, <em>so</em> or <em>now</em> of the verse is left without a place,
/// and an article is supplied only when the word it belongs to is placed on a word the original writes
/// without one. A word whose head is unplaced is left alone, since nothing then says where it went.
/// </summary>
internal static class EvidentiaAbsences
{
    private const string EnglishLanguage = "eng";

    private const string HebrewLanguage = "hbo";

    private const string GreekLanguage = "grc";

    private const string DefiniteArticle = "the";

    private static readonly HashSet<string> IndefiniteArticles = new(StringComparer.OrdinalIgnoreCase) { "a", "an" };

    /// <summary>
    /// The articles of each translation language a rule is measured for, and the rules that mark them.
    /// A contraction of preposition and article (<em>im</em>, <em>del</em>) is a preposition to the parse
    /// and is never among them: half of it renders a word of the original.
    /// </summary>
    private static readonly Dictionary<string, TranslationArticles> ArticlesOf = new(StringComparer.OrdinalIgnoreCase)
    {
        [EnglishLanguage] = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DefiniteArticle }, IndefiniteArticles,
            EvidentiaAbsenceRule.UnwrittenArticle, EvidentiaAbsenceRule.IndefiniteArticle, Parsed: false),
        ["deu"] = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "der", "die", "das", "des", "dem", "den" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ein", "eine", "einen", "einem", "einer", "eines" },
            EvidentiaAbsenceRule.GermanUnwrittenArticle, EvidentiaAbsenceRule.GermanIndefiniteArticle, Parsed: true),
        ["spa"] = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "el", "la", "los", "las" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "un", "una", "unos", "unas" },
            EvidentiaAbsenceRule.SpanishUnwrittenArticle, EvidentiaAbsenceRule.SpanishIndefiniteArticle, Parsed: true),
    };

    /// <param name="Parsed">
    /// Whether the language's articles are read from the UDPipe parse, and so by the stricter reading
    /// the German and Spanish rules were measured with. The parse must call the word an article and the
    /// word must agree with its noun, since German <em>der</em> and <em>das</em> and Spanish <em>los</em>
    /// are as often the demonstrative, the relative or the clitic. The head is the noun of the article's
    /// own phrase (<see cref="PhraseHead"/>). The indefinite article is also the numeral <em>one</em>
    /// (<em>ein Fleisch</em> is בָשָׂר אֶחָד), so it is supplied only where no אֶחָד or εἷς of the verse
    /// is left to render and the original writes no article either (<em>unter einen Scheffel</em> is ὑπὸ
    /// τὸν μόδιον). And the original's article is looked for more widely (<see cref="HasArticle"/>).
    /// </param>
    private sealed record TranslationArticles(
        IReadOnlySet<string> Definite,
        IReadOnlySet<string> Indefinite,
        EvidentiaAbsenceRule Unwritten,
        EvidentiaAbsenceRule IndefiniteRule,
        bool Parsed);

    /// <summary>The English words that render ו, καί and δέ.</summary>
    private static readonly HashSet<string> EnglishCoordinators = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "then", "so", "now", "but", "or", "yet", "thus",
    };

    /// <summary>ו, καί and δέ.</summary>
    private static readonly HashSet<string> Coordinators = new(StringComparer.Ordinal) { "H9000", "G2532", "G1161" };

    private const string HebrewArticle = "H9009";

    private const string GreekArticle = "G3588";

    /// <summary>The numbers the article of the original is written under.</summary>
    public static readonly IReadOnlyList<string> ArticleNumbers = [HebrewArticle, GreekArticle];

    private const string ObjectMarker = "H853";

    private const string GreekAnd = "G2532";

    /// <summary>δέ, τε, γάρ, μέν and οὖν.</summary>
    private static readonly HashSet<string> Postpositives = new(StringComparer.Ordinal) { "G1161", "G5037", "G1063", "G3303", "G3767" };

    /// <summary><em>One</em> and <em>a certain</em>, which an English <em>a</em> does render: אֶחָד, εἷς, τις.</summary>
    private static readonly HashSet<string> NumeralOne = new(StringComparer.Ordinal) { "H259", "G1520", "G5100" };

    /// <summary><em>of</em> renders a construct state as often as a preposition, so it is no preposition's counterpart.</summary>
    private const string Of = "of";

    private const int HeadReach = 3;

    /// <summary>How far before its noun a Greek article may stand with the noun's attributes between.</summary>
    private const int ArticleReach = 6;

    /// <summary>How far from its noun a numeral <em>one</em> stands: לְבָשָׂר אֶחָד, εἷς ἄρτος.</summary>
    private const int OneReach = 2;

    public static EvidentiaFunctionClass? FunctionClass(EvidentiaAnalysis word)
    {
        var token = word.Token;
        if (token.Language.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return EnglishCoordinators.Contains(token.Surface) ? EvidentiaFunctionClass.Coordinator
                : token.Surface.Equals(DefiniteArticle, StringComparison.OrdinalIgnoreCase) ? EvidentiaFunctionClass.Article
                : EvidentiaAttachedWords.Class(word) == "adp" && !token.Surface.Equals(Of, StringComparison.OrdinalIgnoreCase)
                    ? EvidentiaFunctionClass.Preposition
                    : null;
        }

        return token.StrongNumber is { } strong && Coordinators.Contains(strong) ? EvidentiaFunctionClass.Coordinator
            : IsArticle(word) ? EvidentiaFunctionClass.Article
            : EvidentiaAttachedWords.Class(word) == "adp" && token.StrongNumber != ObjectMarker ? EvidentiaFunctionClass.Preposition
            : null;
    }

    /// <summary>
    /// Whether an English word without a grammatical kind renders a word of the original a key linked it
    /// with: <em>a</em> renders <em>one</em> or <em>a certain</em>; any other word renders whatever it is
    /// linked with.
    /// </summary>
    public static bool Renders(EvidentiaAnalysis english, EvidentiaAnalysis original) =>
        !IndefiniteArticles.Contains(english.Token.Surface)
        || original.Token.StrongNumber is { } strong && NumeralOne.Contains(strong);

    private static bool IsArticle(EvidentiaAnalysis word) =>
        word.Token.StrongNumber is HebrewArticle or GreekArticle
        || word.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
            && EvidentiaAttachedWords.Class(word) == "det" && word.Token.Lemma == "ὁ";

    public static IReadOnlyList<EvidentiaAbsence> Resolve(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaProposal> placed)
    {
        var placedBySource = placed
            .GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var placedByTarget = placed
            .GroupBy(proposal => proposal.Target.Token.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var targetsByVerse = target
            .DistinctBy(analysis => analysis.Token.Id)
            .GroupBy(analysis => analysis.Token.Address)
            .ToDictionary(group => group.Key, group => group.OrderBy(analysis => analysis.Token.Position).ToList());
        var absences = new List<EvidentiaAbsence>();
        foreach (var words in source
                     .Where(analysis => ArticlesOf.ContainsKey(analysis.Token.Language))
                     .DistinctBy(analysis => analysis.Token.Id)
                     .GroupBy(analysis => analysis.Token.Address)
                     .Select(verse => verse.OrderBy(analysis => analysis.Token.Position).ToList()))
        {
            var verse = targetsByVerse.GetValueOrDefault(words[0].Token.Address) ?? [];
            absences.AddRange(Supplied(words, verse, placedBySource, placedByTarget));
            if (words[0].Token.Language.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase))
            {
                absences.AddRange(NotRendered(words, verse, placedBySource, placedByTarget));
            }
        }

        return absences;
    }

    private static IEnumerable<EvidentiaAbsence> Supplied(
        IReadOnlyList<EvidentiaAnalysis> words,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<long, EvidentiaProposal> placedByTarget)
    {
        var freeOne = verse.Any(word => word.Token.StrongNumber is { } strong && NumeralOne.Contains(strong)
            && !placedByTarget.ContainsKey(word.Token.Id));
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var articles = ArticlesOf[word.Token.Language];
            if (placedBySource.ContainsKey(word.Token.Id) || EvidentiaAttachedWords.Class(word) != "det"
                || articles.Parsed && (!IsParsedAsArticle(word) || Negated(words, index))
                || (articles.Parsed ? PhraseHead(words, index) : Head(words, index)) is not { } head
                || !placedBySource.TryGetValue(head.Token.Id, out var headProposal)
                || articles.Parsed && (Disagrees(word, head) || !Nominal(headProposal.Target)))
            {
                continue;
            }

            var rendering = headProposal.Target;
            if (articles.Indefinite.Contains(word.Token.Surface))
            {
                if (!(rendering.Token.StrongNumber is { } strong && NumeralOne.Contains(strong))
                    && !(articles.Parsed && (freeOne || NextToOne(rendering, verse) || HasArticle(rendering, verse, articles.Parsed))))
                {
                    yield return new EvidentiaAbsence(word, articles.IndefiniteRule, headProposal);
                }
            }
            else if (articles.Definite.Contains(word.Token.Surface) && !HasArticle(rendering, verse, articles.Parsed))
            {
                yield return new EvidentiaAbsence(word, articles.Unwritten, headProposal);
            }
        }
    }

    /// <summary>Whether אֶחָד or εἷς stands within reach of the rendering, whatever else it was placed on.</summary>
    private static bool NextToOne(EvidentiaAnalysis rendering, IReadOnlyList<EvidentiaAnalysis> verse)
    {
        var at = EvidentiaAttachedWords.IndexOf(verse, rendering);
        return at >= 0 && verse
            .Skip(Math.Max(0, at - OneReach)).Take(2 * OneReach + 1)
            .Any(word => word.Token.StrongNumber is { } strong && NumeralOne.Contains(strong));
    }

    private static EvidentiaAnalysis? Head(IReadOnlyList<EvidentiaAnalysis> words, int index) =>
        EvidentiaAttachedWords.Forward(words, index, "noun", "propn", "adj", "num");

    /// <summary>
    /// The noun an article belongs to, read only across the words of its own phrase: <em>der ganze Leib</em>
    /// is Leib's. A verb, a preposition or a pronoun between them says the word is no article of that noun
    /// but the demonstrative or the relative the parse took for one (<em>der wohnte zu Aroer</em>, <em>la
    /// cual mandó Jehová</em>). Without a noun, an adjective or numeral standing for one is the head.
    /// </summary>
    private static EvidentiaAnalysis? PhraseHead(IReadOnlyList<EvidentiaAnalysis> words, int index)
    {
        EvidentiaAnalysis? modifier = null;
        for (var next = index + 1; next < words.Count && next - index <= HeadReach; next++)
        {
            if (words[next - 1].Token.Trailer.Any(char.IsPunctuation))
            {
                break;
            }

            switch (EvidentiaAttachedWords.Class(words[next]))
            {
                case "noun" or "propn":
                    return words[next];
                case "adj" or "num":
                    modifier = words[next];
                    break;
                case "adv" or "det":
                    break;
                default:
                    return modifier;
            }
        }

        return modifier;
    }

    /// <summary><em>nicht ein Weiser</em> is οὐδεὶς σοφός: after a negation the article is the numeral.</summary>
    private static bool Negated(IReadOnlyList<EvidentiaAnalysis> words, int index) =>
        index > 0 && Feature(words[index - 1], "Polarity") == "Neg";

    /// <summary>
    /// Whether the article and its noun state a different number, or a different gender in the singular:
    /// <em>das Eitelkeit</em> and <em>das Brüder</em> are the demonstrative before a noun of another phrase.
    /// </summary>
    private static bool Disagrees(EvidentiaAnalysis article, EvidentiaAnalysis head)
    {
        if (EvidentiaAttachedWords.Class(head) is not ("noun" or "propn"))
        {
            return false;
        }

        var number = Feature(article, "Number");
        var headNumber = Feature(head, "Number");
        if (number is not null && headNumber is not null && number != headNumber)
        {
            return true;
        }

        var gender = Feature(article, "Gender");
        var headGender = Feature(head, "Gender");
        return number == "Sing" && gender is not null && headGender is not null && !headGender.Split(',').Contains(gender);
    }

    /// <summary>
    /// Whether the word the head was placed on can take an article at all: a pronoun or a verb inflected for
    /// person cannot, and an article said to stand before one is a clitic the parse misread (<em>los
    /// santifico</em>, <em>los cogen</em>) or a head placed on the wrong word.
    /// </summary>
    private static bool Nominal(EvidentiaAnalysis rendering) =>
        EvidentiaAttachedWords.Class(rendering) switch
        {
            "pron" => false,
            "verb" => Feature(rendering, "person") is null,
            _ => true,
        };

    private static string? Feature(EvidentiaAnalysis word, string name) =>
        word.Token.Morphology?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool IsParsedAsArticle(EvidentiaAnalysis word) =>
        word.Token.Morphology?.Any(pair => pair.Key.Equals("PronType", StringComparison.OrdinalIgnoreCase)
            && pair.Value.Split(',').Contains("Art", StringComparer.Ordinal)) == true;

    /// <summary>
    /// Whether the original writes an article for the word: directly before it in Hebrew, where it is a
    /// prefix; before it past any adjective in Greek, where it may stand apart, or before the word it is
    /// joined to by καί where an article is read as shared. Read strictly, a Hebrew noun is also definite
    /// by the article of the adjective after it, which BHSA writes even where the noun has none: מִיּוֹם
    /// הָרִאשֹׁן.
    /// </summary>
    private static bool HasArticle(EvidentiaAnalysis rendering, IReadOnlyList<EvidentiaAnalysis> verse, bool strict = false)
    {
        if (IsArticle(rendering))
        {
            return true;
        }

        var at = EvidentiaAttachedWords.IndexOf(verse, rendering);
        if (at <= 0)
        {
            return at < 0 || strict && ArticleOfTheAdjectiveAfter(verse, at);
        }

        if (rendering.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return IsArticle(verse[at - 1]) || strict && ArticleOfTheAdjectiveAfter(verse, at);
        }

        for (var before = at - 1; before >= 0; before--)
        {
            if (IsArticle(verse[before]))
            {
                return true;
            }

            if (strict && verse[before].Token.StrongNumber == GreekAnd && before > 0
                && EvidentiaAttachedWords.Class(verse[before - 1]) is "noun" or "propn" or "adj")
            {
                return HasArticle(verse[before - 1], verse);
            }

            // τὸ δὲ σῶμα, τήν τε Φοινίκην, τοῦ ἐν ὑμῖν Ἁγίου Πνεύματος: a particle that stands second in
            // its clause, and a preposition with its pronoun, come between an article and its noun.
            if (strict && (verse[before].Token.StrongNumber is { } particle && Postpositives.Contains(particle)
                    || EvidentiaAttachedWords.Class(verse[before]) == "pron" && before > 0
                    && EvidentiaAttachedWords.Class(verse[before - 1]) == "adp"))
            {
                continue;
            }

            if (EvidentiaAttachedWords.Class(verse[before]) is not ("adj" or "adv" or "adp"))
            {
                return strict && AgreeingArticleBefore(verse, at);
            }
        }

        return false;
    }

    /// <summary>
    /// A Greek article of the same case, number and gender a few words before the noun, with no verb or
    /// clause break between and no other noun of that case: ἡ γὰρ κατὰ Θεὸν λύπη, τὴν πάντων ὑμῶν ὑπακοήν.
    /// What stands between is the noun's own attribute.
    /// </summary>
    private static bool AgreeingArticleBefore(IReadOnlyList<EvidentiaAnalysis> verse, int at)
    {
        var noun = verse[at];
        if (Feature(noun, "case") is not { } nounCase)
        {
            return false;
        }

        for (var before = at - 1; before >= 0 && at - before <= ArticleReach; before--)
        {
            var word = verse[before];
            // A verb or a clause break ends the phrase; a noun of the same case before it has the article.
            if (EvidentiaAttachedWords.Class(word) == "verb" || word.Token.Trailer.Any(char.IsPunctuation)
                || EvidentiaAttachedWords.Class(word) is "noun" or "propn" && Feature(word, "case") == nounCase)
            {
                return false;
            }

            if (IsArticle(word) && Feature(word, "case") == nounCase
                && Feature(word, "number") == Feature(noun, "number")
                && Feature(word, "gender") == Feature(noun, "gender"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ArticleOfTheAdjectiveAfter(IReadOnlyList<EvidentiaAnalysis> verse, int at) =>
        at + 2 < verse.Count
        && verse[at].Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
        && IsArticle(verse[at + 1])
        && EvidentiaAttachedWords.Class(verse[at + 2]) is "adj" or "num";

    private static IEnumerable<EvidentiaAbsence> NotRendered(
        IReadOnlyList<EvidentiaAnalysis> words,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<long, EvidentiaProposal> placedByTarget)
    {
        var free = words.Where(word => !placedBySource.ContainsKey(word.Token.Id)).ToList();
        var freeCoordinator = free.Any(word => FunctionClass(word) == EvidentiaFunctionClass.Coordinator);
        var freeArticle = free.Any(word => FunctionClass(word) == EvidentiaFunctionClass.Article);
        for (var index = 0; index < verse.Count; index++)
        {
            var word = verse[index];
            if (placedByTarget.ContainsKey(word.Token.Id))
            {
                continue;
            }

            var hebrew = word.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase);
            var functionClass = FunctionClass(word);
            if (functionClass == EvidentiaFunctionClass.Coordinator && !freeCoordinator
                && hebrew
                && Anchor(verse, index, placedByTarget) is { } joined)
            {
                yield return new EvidentiaAbsence(word, EvidentiaAbsenceRule.Conjunction, joined);
            }
            else if (hebrew && word.Token.StrongNumber == ObjectMarker
                     && !HasSuffix(word)
                     && Anchor(verse, index, placedByTarget) is { } marked)
            {
                yield return new EvidentiaAbsence(word, EvidentiaAbsenceRule.ObjectMarker, marked);
            }
            else if (functionClass == EvidentiaFunctionClass.Article && !freeArticle
                     && hebrew
                     && Anchor(verse, index, placedByTarget) is { } noun)
            {
                yield return new EvidentiaAbsence(word, EvidentiaAbsenceRule.Article, noun);
            }
        }
    }

    private static bool HasSuffix(EvidentiaAnalysis word) =>
        word.Token.Morphology?.Any(pair => pair.Key.Equals("suffixPerson", StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// The placed word the grammatical word belongs to: for a Hebrew prefix the first placed word of the rest
    /// of the written word (וּ בַ הַ מַּיִם), for a word of its own such as אֶת the first open-class word
    /// within reach.
    /// </summary>
    private static EvidentiaProposal? Anchor(
        IReadOnlyList<EvidentiaAnalysis> verse,
        int index,
        IReadOnlyDictionary<long, EvidentiaProposal> placedByTarget)
    {
        if (verse[index].Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase))
        {
            for (var next = index + 1; next < verse.Count && verse[next - 1].Token.Trailer.Length == 0; next++)
            {
                if (placedByTarget.GetValueOrDefault(verse[next].Token.Id) is { } placed)
                {
                    return placed;
                }
            }

            if (verse[index].Token.Trailer.Length == 0)
            {
                return null;
            }
        }

        for (var next = index + 1; next < verse.Count && next - index <= HeadReach; next++)
        {
            if (EvidentiaMorphologyLabels.IsOpenClass(verse[next].PartOfSpeech ?? verse[next].Token.PartOfSpeech, verse[next].Token.Language)
                && EvidentiaAttachedWords.Class(verse[next]) is not null)
            {
                return placedByTarget.GetValueOrDefault(verse[next].Token.Id);
            }
        }

        return null;
    }
}
