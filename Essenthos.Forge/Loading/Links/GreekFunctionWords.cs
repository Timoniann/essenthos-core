namespace Essenthos.Core.Loading.Links;

/// <param name="EnglishWord">Index of the English word within its verse, counting from zero.</param>
/// <param name="GreekWord">Index of the Greek word it renders, within the same verse.</param>
internal readonly record struct FunctionWordMatch(int EnglishWord, int GreekWord, double Confidence);

/// <summary>
/// Gives the English function words their Greek.
///
/// The tagged King James numbers its content words and leaves 56,878 New Testament words — 31.5% of
/// it — with no tag at all: <em>the</em>, <em>of</em>, <em>unto</em>, <em>he</em>, <em>shall</em>.
/// Matching Strong numbers can never reach them, so without this they are linked to nothing and a
/// reader hovering a Greek word sees half an English clause light up.
///
/// The Old Testament has the same hole and fills it from the mapping file's morphemes. Greek gives
/// more to work with, not less: both editions state a Robinson tag for every word, so the article
/// is a word on the page rather than a guess, and the case, the person and the mood English writes
/// as a separate word are stated rather than inferred. Each rule below has the tag as its evidence
/// and adjacency only to say which word is meant. Where the tag does not say it, nothing is
/// written — <em>a</em> and <em>an</em> render the absence of the Greek article and appear nowhere
/// here, and <em>is</em> is left alone because Greek states a copula it does not write only by
/// writing nothing, which no tag on any other word can be read as. The King James italics settle
/// the supplied ones, and they are settled before this runs; what is left is the copula the
/// tagged edition simply did not number, and nothing here can tell that from a supplied one.
/// </summary>
internal static class GreekFunctionWords
{
    /// <summary>
    /// The article is a word in the verse, it stands before what it governs, and it agrees with it
    /// in case, number and gender. Three signals, and the Hebrew side has two.
    /// </summary>
    private const double Article = 0.9;

    /// <summary>
    /// The same, for a noun that states no case to agree with — a foreign name, which Greek
    /// declines by putting the article in front of it and nothing else. Adjacency is then the whole
    /// evidence, so it is required to be exact.
    /// </summary>
    private const double ArticleOnIndeclinable = 0.8;

    /// <summary>
    /// English writes with a preposition what Greek writes as a case ending, so <em>of</em> before
    /// a genitive and <em>unto</em> before a dative render that word and no other. The case is
    /// stated by the edition; adjacency only says which word.
    /// </summary>
    private const double CaseEnding = 0.9;

    /// <summary>The <em>to</em> that marks an English infinitive, before a Greek one.</summary>
    private const double InfinitiveMarker = 0.9;

    /// <summary>
    /// Greek puts the subject on the verb and English cannot, so an untagged English pronoun before
    /// a finite verb of the same person and number is that ending. Below the article because the
    /// Greek could have carried an explicit pronoun elsewhere in the verse that the tagged edition
    /// failed to mark.
    /// </summary>
    private const double SubjectEnding = 0.85;

    /// <summary>
    /// An English auxiliary against the tense or mood the Greek verb states. Lowest of the five:
    /// the agreement is real, but English chooses its modals for reasons Greek morphology does not
    /// settle.
    /// </summary>
    private const double Auxiliary = 0.8;

    /// <summary>
    /// How far ahead a function word may look for the word it belongs to. Long enough for
    /// <em>of the kingdom</em> and for one adjective in between; short enough that a word at the
    /// end of a clause cannot claim the next clause's noun.
    /// </summary>
    private const int EnglishReach = 4;

    /// <summary>
    /// How far before its noun a Greek article may stand. Two words covers ὁ ἀγαθὸς ἄνθρωπος, and
    /// past that the article is another phrase's.
    /// </summary>
    private const int GreekReach = 3;

    private const char Singular = 'S';
    private const char Plural = 'P';

    /// <summary>
    /// The English pronouns that stand for a Greek verb ending, with the person and number the
    /// ending has to state. <em>You</em> is absent because the King James does not use it as a
    /// subject; <em>ye</em> and <em>thou</em> are how it separates the two second persons, which is
    /// exactly the distinction the Greek carries.
    /// </summary>
    private static readonly Dictionary<string, (int Person, char Number)> Subjects =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["i"] = (1, Singular),
            ["we"] = (1, Plural),
            ["thou"] = (2, Singular),
            ["ye"] = (2, Plural),
            ["he"] = (3, Singular),
            ["she"] = (3, Singular),
            ["it"] = (3, Singular),
            ["they"] = (3, Plural),
        };

    private static readonly HashSet<string> Prospective =
        new(["shall", "shalt", "will", "wilt"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Contingent =
        new(["should", "shouldest", "would", "wouldest", "might", "may"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Completed =
        new(["have", "hast", "hath", "has", "had", "hadst"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Dative =
        new(["to", "unto", "for"], StringComparer.OrdinalIgnoreCase);

    /// <param name="anchors">
    /// For each English word, the one Greek word its link names, or a negative number where it has
    /// no link or where its link names several. A function word is only ever attached through a
    /// content word whose own Greek is settled — an anchor naming a set would spread the guess.
    /// </param>
    /// <param name="claimed">Greek words some link already names, so an article is not taken twice.</param>
    /// <param name="supplied">
    /// English words the King James prints in italics. The translators are saying no Greek word
    /// stands behind them, which settles the question this class exists to guess at — so they are
    /// passed over here rather than filtered out afterwards, and the article one of them would have
    /// taken stays free for the word that does render it.
    /// </param>
    public static IReadOnlyList<FunctionWordMatch> Match(
        IReadOnlyList<string> english,
        IReadOnlyList<string?> tags,
        IReadOnlyList<int> anchors,
        IReadOnlyList<GreekMorphology> greek,
        IReadOnlySet<int> claimed,
        IReadOnlySet<int> supplied)
    {
        var matches = new List<FunctionWordMatch>();
        var taken = new HashSet<int>();

        for (var i = 0; i < english.Count; i++)
        {
            if (tags[i] is not null || supplied.Contains(i))
            {
                continue;
            }

            var anchor = NextAnchor(anchors, i);
            if (anchor < 0)
            {
                continue;
            }

            var at = anchors[anchor];
            var word = english[i];
            var morphology = greek[at];

            if (word.Equals("the", StringComparison.OrdinalIgnoreCase))
            {
                if (PrecedingArticle(greek, claimed, taken, at, morphology) is { } found)
                {
                    matches.Add(new FunctionWordMatch(i, found.Article, found.Confidence));
                    taken.Add(found.Article);
                }

                continue;
            }

            if (Renders(word, morphology, greek, at) is { } inferred)
            {
                matches.Add(new FunctionWordMatch(i, at, inferred));
            }
        }

        return matches;
    }

    /// <summary>
    /// What an untagged English word renders of the Greek word its phrase is anchored to — its
    /// case, its mood, its person, its tense. Null where the tag does not say.
    /// </summary>
    private static double? Renders(
        string word,
        GreekMorphology morphology,
        IReadOnlyList<GreekMorphology> greek,
        int at)
    {
        if (word.Equals("of", StringComparison.OrdinalIgnoreCase))
        {
            return morphology.Case == GreekCase.Genitive && !Governed(greek, at) ? CaseEnding : null;
        }

        if (word.Equals("to", StringComparison.OrdinalIgnoreCase) && morphology.Mood == GreekMood.Infinitive)
        {
            return InfinitiveMarker;
        }

        if (Dative.Contains(word))
        {
            return morphology.Case == GreekCase.Dative && !Governed(greek, at) ? CaseEnding : null;
        }

        if (Subjects.TryGetValue(word, out var subject))
        {
            return morphology is { Part: GreekPart.Verb, IsFinite: true }
                   && morphology.Person == subject.Person && morphology.Number == subject.Number
                ? SubjectEnding
                : null;
        }

        if (morphology.Part != GreekPart.Verb)
        {
            return null;
        }

        if (Prospective.Contains(word))
        {
            return morphology.Tense == GreekTense.Future || morphology.Mood == GreekMood.Subjunctive
                ? Auxiliary
                : null;
        }

        if (Contingent.Contains(word))
        {
            return morphology.Mood is GreekMood.Subjunctive or GreekMood.Optative ? Auxiliary : null;
        }

        if (Completed.Contains(word))
        {
            return morphology.Tense is GreekTense.Perfect or GreekTense.Pluperfect ? Auxiliary : null;
        }

        return null;
    }

    /// <summary>
    /// The article standing before a word and agreeing with it. The walk stops at the first word
    /// that is neither an article nor an adjective, because past that the article belongs to
    /// another phrase however well it agrees.
    /// </summary>
    private static (int Article, double Confidence)? PrecedingArticle(
        IReadOnlyList<GreekMorphology> greek,
        IReadOnlySet<int> claimed,
        IReadOnlySet<int> taken,
        int at,
        GreekMorphology noun)
    {
        for (var k = at - 1; k >= 0 && k >= at - GreekReach; k--)
        {
            if (greek[k].Part == GreekPart.Adjective)
            {
                continue;
            }

            if (greek[k].Part != GreekPart.Article)
            {
                return null;
            }

            if (claimed.Contains(k) || taken.Contains(k))
            {
                continue;
            }

            if (noun.Case != GreekCase.None)
            {
                return greek[k].Agrees(noun) ? (k, Article) : null;
            }

            return k == at - 1 ? (k, ArticleOnIndeclinable) : null;
        }

        return null;
    }

    /// <summary>
    /// Whether a preposition governs this word. <em>Out of the city</em> is ἐκ τῆς πόλεως: the
    /// genitive is the preposition's doing, the English <em>of</em> belongs to <em>out</em>, and
    /// handing it to πόλεως would be a claim about the wrong word.
    /// </summary>
    private static bool Governed(IReadOnlyList<GreekMorphology> greek, int at)
    {
        for (var k = at - 1; k >= 0 && k >= at - GreekReach; k--)
        {
            if (greek[k].Part is GreekPart.Article or GreekPart.Adjective)
            {
                continue;
            }

            return greek[k].Part == GreekPart.Preposition;
        }

        return false;
    }

    private static int NextAnchor(IReadOnlyList<int> anchors, int from)
    {
        var last = Math.Min(from + EnglishReach, anchors.Count - 1);
        for (var j = from + 1; j <= last; j++)
        {
            if (anchors[j] >= 0)
            {
                return j;
            }
        }

        return -1;
    }
}
