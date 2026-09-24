namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>Which side of a correspondence has nothing on the other.</summary>
internal enum EvidentiaAbsenceKind
{
    /// <summary>A word of the translation the original does not have: <em>a</em>, or <em>the</em> of <em>the face</em>. Written as <c>expands</c>.</summary>
    Supplied,

    /// <summary>A word of the original the translation does not render: את, or a ו no <em>and</em> stands for. Written as <c>omits</c>.</summary>
    NotRendered,
}

/// <param name="Anchor">The placed word the claim rests on: the head of a supplied article, or the word a prefix is written onto.</param>
internal sealed record EvidentiaAbsence(
    EvidentiaAnalysis Word,
    EvidentiaAbsenceKind Kind,
    string Rationale,
    EvidentiaProposal? Anchor);

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

    /// <summary>The English words that render ו, καί and δέ.</summary>
    private static readonly HashSet<string> EnglishCoordinators = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "then", "so", "now", "but", "or", "yet", "thus",
    };

    /// <summary>ו, καί and δέ.</summary>
    private static readonly HashSet<string> Coordinators = new(StringComparer.Ordinal) { "H9000", "G2532", "G1161" };

    private const string HebrewArticle = "H9009";

    private const string GreekArticle = "G3588";

    private const string ObjectMarker = "H853";

    /// <summary><em>One</em> and <em>a certain</em>, which an English <em>a</em> does render: אֶחָד, εἷς, τις.</summary>
    private static readonly HashSet<string> NumeralOne = new(StringComparer.Ordinal) { "H259", "G1520", "G5100" };

    /// <summary><em>of</em> renders a construct state as often as a preposition, so it is no preposition's counterpart.</summary>
    private const string Of = "of";

    private const int HeadReach = 3;

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
                     .Where(analysis => analysis.Token.Language.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(analysis => analysis.Token.Id)
                     .GroupBy(analysis => analysis.Token.Address)
                     .Select(verse => verse.OrderBy(analysis => analysis.Token.Position).ToList()))
        {
            var verse = targetsByVerse.GetValueOrDefault(words[0].Token.Address) ?? [];
            absences.AddRange(Supplied(words, verse, placedBySource));
            absences.AddRange(NotRendered(words, verse, placedBySource, placedByTarget));
        }

        return absences;
    }

    private static IEnumerable<EvidentiaAbsence> Supplied(
        IReadOnlyList<EvidentiaAnalysis> words,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource)
    {
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            if (placedBySource.ContainsKey(word.Token.Id) || EvidentiaAttachedWords.Class(word) != "det"
                || EvidentiaAttachedWords.Forward(words, index, "noun", "propn", "adj", "num") is not { } head
                || !placedBySource.TryGetValue(head.Token.Id, out var headProposal))
            {
                continue;
            }

            var rendering = headProposal.Target;
            if (IndefiniteArticles.Contains(word.Token.Surface)
                && !(rendering.Token.StrongNumber is { } strong && NumeralOne.Contains(strong)))
            {
                yield return new EvidentiaAbsence(word, EvidentiaAbsenceKind.Supplied, "indefinite article", headProposal);
            }
            else if (word.Token.Surface.Equals(DefiniteArticle, StringComparison.OrdinalIgnoreCase)
                     && !HasArticle(rendering, verse))
            {
                yield return new EvidentiaAbsence(word, EvidentiaAbsenceKind.Supplied, "article the original does not write", headProposal);
            }
        }
    }

    /// <summary>
    /// Whether the original writes an article for the word: directly before it in Hebrew, where it is a
    /// prefix; before it past any adjective in Greek, where it may stand apart.
    /// </summary>
    private static bool HasArticle(EvidentiaAnalysis rendering, IReadOnlyList<EvidentiaAnalysis> verse)
    {
        if (IsArticle(rendering))
        {
            return true;
        }

        var at = EvidentiaAttachedWords.IndexOf(verse, rendering);
        if (at <= 0)
        {
            return at < 0;
        }

        if (rendering.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return IsArticle(verse[at - 1]);
        }

        for (var before = at - 1; before >= 0; before--)
        {
            if (IsArticle(verse[before]))
            {
                return true;
            }

            if (EvidentiaAttachedWords.Class(verse[before]) is not ("adj" or "adv" or "adp"))
            {
                return false;
            }
        }

        return false;
    }

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
                yield return new EvidentiaAbsence(word, EvidentiaAbsenceKind.NotRendered, "conjunction no word of the verse renders", joined);
            }
            else if (hebrew && word.Token.StrongNumber == ObjectMarker
                     && !HasSuffix(word)
                     && Anchor(verse, index, placedByTarget) is { } marked)
            {
                yield return new EvidentiaAbsence(word, EvidentiaAbsenceKind.NotRendered, "object marker", marked);
            }
            else if (functionClass == EvidentiaFunctionClass.Article && !freeArticle
                     && hebrew
                     && Anchor(verse, index, placedByTarget) is { } noun)
            {
                yield return new EvidentiaAbsence(word, EvidentiaAbsenceKind.NotRendered, "article no the of the verse renders", noun);
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
