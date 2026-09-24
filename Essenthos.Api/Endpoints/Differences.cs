namespace Essenthos.Core.Endpoints;

/// <summary>
/// What a word of one text is, measured against another text at the same place.
///
/// The three a reader asks about are <see cref="Absent"/>, <see cref="Lemma"/> and
/// <see cref="Form"/>: the other text does not have this at all, it has it and says something else,
/// it has it and writes it differently. The rest say how far the corpus could get: a word the
/// two texts share, one standing elsewhere in the verse, one whose counterpart is known and whose
/// dictionary form is not, and one nothing links at all.
/// </summary>
internal enum Degree
{
    Same,
    Form,
    Lemma,
    Absent,
    Moved,

    /// <summary>
    /// Across two languages, a counterpart is linked and nothing independent of that link says
    /// which word it is, so whether it says the same thing cannot be told.
    /// </summary>
    Linked,

    /// <summary>
    /// In one language, a counterpart written with other letters, and neither side carries a
    /// dictionary form to tell a respelling from another word.
    /// </summary>
    Unsure,

    /// <summary>
    /// Through a third text: this word is linked to it, and no word of the other text is linked to
    /// the same word. Weaker than <see cref="Absent"/>, which a link records; this is only what the
    /// two texts' links fail to meet on, and a link either of them missed looks the same.
    /// </summary>
    Unmatched,

    /// <summary>Nothing links the word to the other text, which is silence rather than absence.</summary>
    Unlinked,
}

/// <summary>What a word's degree was decided by.</summary>
internal enum Decision
{
    /// <summary>The two words are written with the same letters.</summary>
    Letters,

    /// <summary>The dictionary forms of two Hebrew words, compared by their consonants.</summary>
    Lexeme,

    /// <summary>The dictionary forms of two Greek words.</summary>
    Lemma,

    /// <summary>The Strong numbers each text's own source gives its word.</summary>
    Strong,

    /// <summary>
    /// The Strong numbers of the Hebrew or Greek words each is linked to in some third text — which
    /// is what a translation's word has instead of a dictionary form.
    /// </summary>
    Original,

    /// <summary>Both texts are linked to one word of a third, or only one of them is.</summary>
    Through,

    /// <summary>A link records that one text has words here the other has not.</summary>
    Recorded,

    /// <summary>Recorded as missing from both places, and the same letters stand in the other one.</summary>
    Position,

    /// <summary>A link joins the two and nothing else could be compared.</summary>
    Link,

    /// <summary>Nothing reached the word at all.</summary>
    Nothing,
}

/// <summary>How the two texts are joined word to word.</summary>
internal enum DifferenceBasis
{
    /// <summary>Links between the two texts themselves.</summary>
    Links,

    /// <summary>
    /// Both are linked to a third text, and either nothing links them to each other or both are
    /// translations, whose links to each other are an aligner's guess where their links to the
    /// original are mostly a source's statement.
    /// </summary>
    Through,

    /// <summary>Neither.</summary>
    None,
}

/// <param name="Letters">
/// The word as its letters, folded the way its language needs: consonants for Hebrew, bare
/// alphabet for Greek, lower case with the marks and punctuation gone for the rest.
/// </param>
/// <param name="Lexeme">
/// A Hebrew word's dictionary form as consonants: BHSA's <c>voc_lex</c>, never its <c>lex</c>, which
/// is the occurrence spelling for most words.
/// </param>
/// <param name="Lemma">A Greek word's dictionary form, bare.</param>
/// <param name="Strong">The Strong number the text's own source gives it.</param>
/// <param name="Originals">
/// The Strong numbers of the words of other texts it is linked to, where those state one. Empty
/// for a text with dictionary forms of its own, where it is not needed.
/// </param>
internal sealed record DifferenceWord(
    long Id,
    string Letters,
    string? Lexeme,
    string? Lemma,
    string? Strong,
    IReadOnlySet<string> Originals);

/// <summary>
/// One verse of each text and what the corpus says joins their words.
/// </summary>
/// <param name="Joined">
/// Words of the two texts that correspond, as the links group them: each entry is one link's
/// two sides, or, through a third text, one of its words and the words of each text linked to it.
/// </param>
/// <param name="Recorded">Words a link records the other text as not having.</param>
/// <param name="Reaching">
/// Through a third text, the words of either text that are linked to it at all; one of these that
/// joins nothing in the other text is a word the other has nothing for.
/// </param>
internal sealed record VersePair(
    IReadOnlyList<DifferenceWord> A,
    IReadOnlyList<DifferenceWord> B,
    IReadOnlyList<(IReadOnlyList<long> A, IReadOnlyList<long> B)> Joined,
    IReadOnlySet<long> Recorded,
    IReadOnlySet<long> Reaching);

internal readonly record struct WordDegree(Degree Degree, Decision By, IReadOnlyList<long> With);

/// <summary>
/// Two texts compared word by word, from what the corpus already records: its links say which words
/// correspond and which one text has alone, and the words' dictionary forms say whether two
/// corresponding words are one word written two ways or two different words.
///
/// <para>
/// Nothing here guesses a correspondence. Where the links do not reach a word the answer is that
/// nothing reached it, and where two corresponding words carry nothing to compare the answer is that
/// it cannot be told — across two languages that is every word the links alone join, because a link
/// between the Hebrew and the Greek already says they correspond and cannot also be the evidence
/// that they mean the same.
/// </para>
/// </summary>
internal static class Differences
{
    /// <summary>
    /// The shortest word that stands on its own. A one-letter word is a conjunction or a preposition
    /// written onto the next and a verse has several, so finding one elsewhere is a coincidence: it
    /// moves only beside a longer word that moved, and it is never exchanged for a longer word.
    /// </summary>
    private const int ShortestWord = 2;

    public static (Dictionary<long, WordDegree> A, Dictionary<long, WordDegree> B) Compare(
        VersePair pair,
        DifferenceBasis basis,
        bool sameLanguage)
    {
        var withA = new Dictionary<long, HashSet<long>>();
        var withB = new Dictionary<long, HashSet<long>>();
        foreach (var (a, b) in pair.Joined)
        {
            foreach (var id in a)
            {
                Add(withA, id, b);
            }

            foreach (var id in b)
            {
                Add(withB, id, a);
            }
        }

        var byIdA = pair.A.ToDictionary(w => w.Id);
        var byIdB = pair.B.ToDictionary(w => w.Id);
        var left = pair.A.ToDictionary(
            w => w.Id, w => Degree(w, withA.GetValueOrDefault(w.Id), byIdB, pair, basis, sameLanguage));
        var right = pair.B.ToDictionary(
            w => w.Id, w => Degree(w, withB.GetValueOrDefault(w.Id), byIdA, pair, basis, sameLanguage));

        if (basis == DifferenceBasis.Links && sameLanguage)
        {
            Moved(pair.A, pair.B, left, right);
            Exchanged(pair.A, pair.B, left, right);
        }

        return (left, right);
    }

    private static void Add(Dictionary<long, HashSet<long>> with, long id, IReadOnlyList<long> others)
    {
        if (others.Count == 0)
        {
            return;
        }

        if (!with.TryGetValue(id, out var set))
        {
            with[id] = set = [];
        }

        set.UnionWith(others);
    }

    private static WordDegree Degree(
        DifferenceWord word,
        HashSet<long>? with,
        IReadOnlyDictionary<long, DifferenceWord> other,
        VersePair pair,
        DifferenceBasis basis,
        bool sameLanguage)
    {
        if (with is null || with.Count == 0)
        {
            if (pair.Recorded.Contains(word.Id))
            {
                return new WordDegree(Endpoints.Degree.Absent, Decision.Recorded, []);
            }

            return basis == DifferenceBasis.Through && pair.Reaching.Contains(word.Id)
                ? new WordDegree(Endpoints.Degree.Unmatched, Decision.Through, [])
                : new WordDegree(Endpoints.Degree.Unlinked, Decision.Nothing, []);
        }

        var ids = with.Order().ToList();
        var counterparts = ids.Select(id => other.GetValueOrDefault(id)).OfType<DifferenceWord>().ToList();
        if (counterparts.Count == 0)
        {
            // Linked only to words outside the verse pair: it corresponds, somewhere else.
            return new WordDegree(Endpoints.Degree.Linked, Decision.Link, ids);
        }

        if (sameLanguage && word.Letters.Length > 0 && counterparts.Any(c => c.Letters == word.Letters))
        {
            return new WordDegree(Endpoints.Degree.Same, Decision.Letters, ids);
        }

        if (basis == DifferenceBasis.Through)
        {
            return new WordDegree(sameLanguage ? Endpoints.Degree.Form : Endpoints.Degree.Same, Decision.Through, ids);
        }

        return ByDictionary(word, counterparts, sameLanguage, ids)
               ?? new WordDegree(sameLanguage ? Endpoints.Degree.Unsure : Endpoints.Degree.Linked, Decision.Link, ids);
    }

    /// <summary>
    /// Two corresponding words told apart by the first thing both carry: one word written or formed
    /// differently where it is the same, another word where it is not. Null where they carry
    /// nothing in common to compare.
    /// </summary>
    private static WordDegree? ByDictionary(
        DifferenceWord word,
        IReadOnlyList<DifferenceWord> counterparts,
        bool sameLanguage,
        IReadOnlyList<long> ids)
    {
        foreach (var (decision, key) in Keys)
        {
            var own = key(word);
            if (own.Count == 0)
            {
                continue;
            }

            var theirs = counterparts.SelectMany(key).ToHashSet(StringComparer.Ordinal);
            if (theirs.Count == 0)
            {
                continue;
            }

            return own.Overlaps(theirs)
                ? new WordDegree(sameLanguage ? Endpoints.Degree.Form : Endpoints.Degree.Same, decision, ids)
                : new WordDegree(Endpoints.Degree.Lemma, decision, ids);
        }

        return null;
    }

    /// <summary>
    /// What a word can be compared by, most specific first: a dictionary form beats a Strong number,
    /// which lumps some words together, and a word's own number beats one borrowed from the word it
    /// is linked to.
    /// </summary>
    private static readonly (Decision Decision, Func<DifferenceWord, IReadOnlySet<string>> Key)[] Keys =
    [
        (Decision.Lexeme, word => One(word.Lexeme)),
        (Decision.Lemma, word => One(word.Lemma)),
        (Decision.Strong, word => One(word.Strong)),
        (Decision.Original, word => word.Originals),
    ];

    private static IReadOnlySet<string> One(string? key) =>
        string.IsNullOrEmpty(key) ? EmptySet : new HashSet<string>(StringComparer.Ordinal) { key };

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    /// <summary>
    /// Words the links record as missing from both texts where the other text has the same letters
    /// elsewhere in the verse. An alignment keeps both verses in order, so two words in another order
    /// come out of it as one missing here and one missing there; that is a word moved, not two gone.
    ///
    /// Nearest first, so a repeated word moves from the nearer of its places. A one-letter word
    /// moves only where it stands beside a moved word in both texts: the ו of <em>and the earth</em>
    /// goes where the earth goes.
    /// </summary>
    private static void Moved(
        IReadOnlyList<DifferenceWord> a,
        IReadOnlyList<DifferenceWord> b,
        Dictionary<long, WordDegree> left,
        Dictionary<long, WordDegree> right)
    {
        bool Open(IReadOnlyList<DifferenceWord> words, Dictionary<long, WordDegree> degrees, int at) =>
            degrees[words[at].Id] is { Degree: Endpoints.Degree.Absent, By: Decision.Recorded };

        var candidates = new List<(int I, int J)>();
        for (var i = 0; i < a.Count; i++)
        {
            for (var j = 0; j < b.Count; j++)
            {
                if (Open(a, left, i) && Open(b, right, j)
                                     && a[i].Letters.Length >= ShortestWord
                                     && a[i].Letters == b[j].Letters)
                {
                    candidates.Add((i, j));
                }
            }
        }

        var movedA = new HashSet<int>();
        var movedB = new HashSet<int>();
        foreach (var (i, j) in candidates.OrderBy(c => Math.Abs(c.I - c.J)).ThenBy(c => c.I))
        {
            if (movedA.Contains(i) || movedB.Contains(j))
            {
                continue;
            }

            Move(i, j);
        }

        foreach (var i in Enumerable.Range(0, a.Count).Where(i => !movedA.Contains(i) && Open(a, left, i)
                     && a[i].Letters.Length is > 0 and < ShortestWord
                     && Beside(movedA, i)))
        {
            var j = Enumerable.Range(0, b.Count)
                .Where(j => !movedB.Contains(j) && Open(b, right, j) && b[j].Letters == a[i].Letters
                            && Beside(movedB, j))
                .DefaultIfEmpty(-1)
                .First();
            if (j >= 0)
            {
                Move(i, j);
            }
        }

        return;

        void Move(int i, int j)
        {
            movedA.Add(i);
            movedB.Add(j);
            left[a[i].Id] = new WordDegree(Endpoints.Degree.Moved, Decision.Position, [b[j].Id]);
            right[b[j].Id] = new WordDegree(Endpoints.Degree.Moved, Decision.Position, [a[i].Id]);
        }

        static bool Beside(HashSet<int> moved, int at) => moved.Contains(at - 1) || moved.Contains(at + 1);
    }
    /// <summary>
    /// Words the links record as missing from both texts at the same place: between the same two
    /// corresponding words, one text has these and the other those, as many of each. That is one
    /// text saying something else where the other says this — the Samaritan's <em>keep</em> the
    /// sabbath day where the Masoretic has <em>remember</em> — and the alignment behind the links
    /// could not pair them only because their letters are unlike. Paired in order, and told apart
    /// by their dictionary forms like any other pair.
    ///
    /// Where the two places hold different numbers of words, one text added or left out something
    /// there and no word of it stands for a particular word of the other, so they stay missing. So
    /// does a one-letter word against a longer one: a conjunction where the other text has a noun is
    /// one text's word and the other's, not one word said two ways.
    /// </summary>
    private static void Exchanged(
        IReadOnlyList<DifferenceWord> a,
        IReadOnlyList<DifferenceWord> b,
        Dictionary<long, WordDegree> left,
        Dictionary<long, WordDegree> right)
    {
        var atB = b.Select((word, index) => (word.Id, index)).ToDictionary(x => x.Id, x => x.index);
        var anchors = new List<(int I, int From, int To)> { (-1, -1, -1) };
        for (var i = 0; i < a.Count; i++)
        {
            var places = left[a[i].Id].With.Where(atB.ContainsKey).Select(id => atB[id]).ToList();
            if (places.Count > 0 && left[a[i].Id].Degree != Endpoints.Degree.Moved && places.Min() > anchors[^1].To)
            {
                anchors.Add((i, places.Min(), places.Max()));
            }
        }

        anchors.Add((a.Count, b.Count, b.Count));

        for (var k = 1; k < anchors.Count; k++)
        {
            var (previous, _, afterB) = anchors[k - 1];
            var (next, beforeB, _) = anchors[k];
            var here = Missing(a, left, previous + 1, next);
            var there = Missing(b, right, afterB + 1, beforeB);
            if (here.Count == 0 || here.Count != there.Count)
            {
                continue;
            }

            for (var n = 0; n < here.Count; n++)
            {
                var (one, other) = (a[here[n]], b[there[n]]);
                if (one.Letters.Length < ShortestWord != other.Letters.Length < ShortestWord)
                {
                    continue;
                }

                left[one.Id] = Exchange(one, other);
                right[other.Id] = Exchange(other, one);
            }
        }

        return;

        static WordDegree Exchange(DifferenceWord word, DifferenceWord other) =>
            ByDictionary(word, [other], sameLanguage: true, [other.Id])
            ?? new WordDegree(Endpoints.Degree.Unsure, Decision.Position, [other.Id]);

        static List<int> Missing(
            IReadOnlyList<DifferenceWord> words,
            Dictionary<long, WordDegree> degrees,
            int from,
            int to) =>
            [.. Enumerable.Range(from, Math.Max(0, to - from))
                .Where(i => degrees[words[i].Id] is { Degree: Endpoints.Degree.Absent, By: Decision.Recorded })];
    }
}
