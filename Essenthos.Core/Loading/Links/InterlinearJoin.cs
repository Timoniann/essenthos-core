using System.Text;
using Essenthos.Core.Door43;
using Essenthos.Core.Endpoints;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// A word of a corpus verse: its id, the folded form a source's spelling is matched by, and the form
/// the corpus prints, which is what a source's occurrence count is a count of.
/// </summary>
internal sealed record InterlinearWord(long Id, string Folded, string Language, string Written = "");

/// <summary>One stated span that found its words on both sides.</summary>
internal sealed record InterlinearPair(List<long> From, List<long> To);

/// <summary>
/// Why a span of the interlinear was not joined, in the order it is decided: the translated side is
/// asked first, because a translated word the corpus does not print makes the original side moot.
/// </summary>
internal enum InterlinearSpanFailure
{
    None,

    /// <summary>A translated word of the span is not in the corpus verse at all: the two editions print differently.</summary>
    TranslatedWordAbsent,

    /// <summary>It is in the verse, but only before a word an earlier span already joined.</summary>
    TranslatedWordBehind,

    /// <summary>
    /// The original word, morpheme by morpheme and side by side, is not in the witness verse: another
    /// edition's reading, or a word the witness divides differently.
    /// </summary>
    OriginalWordAbsent,

    /// <summary>
    /// The original word is in the witness verse more than once, and the witness does not print it
    /// as many times as the source counts, so which one the source means cannot be told.
    /// </summary>
    OriginalOccurrenceUnresolved,
}

/// <summary>
/// Everything that became of one interlinear on its way into the corpus: every verse, every span,
/// every translated word, and why the ones that did not arrive did not. A stated gold that joined
/// 70% of itself is a different answer key from one that joined all of it, and which 30% it lost
/// decides whose words a benchmark can score.
/// </summary>
internal sealed class InterlinearJoinAccount
{
    private const int ExamplesPerClass = 8;

    private readonly Dictionary<string, List<string>> examples = new(StringComparer.Ordinal);

    public int VersesRead { get; set; }
    public int VersesMissingInTranslation { get; set; }
    public int VersesMissingInWitness { get; set; }
    public int VersesJoined { get; set; }
    public int VersesWithNoSpanJoined { get; set; }
    public int Spans { get; set; }
    public int SpansJoined { get; set; }
    public int SpansInMissingVerses { get; set; }
    public Dictionary<InterlinearSpanFailure, int> SpanFailures { get; } = [];
    public int SpansJoinedOnTheLooseFold { get; set; }
    public int SpansSharingAnOriginal { get; set; }
    public int TranslatedFragments { get; set; }
    public int TranslatedWords { get; set; }
    public int TranslatedWordsJoined { get; set; }
    public int TranslatedWordsInMissingVerses { get; set; }
    public int TranslatedWordsOnAnotherOccurrence { get; set; }
    public int SharedOriginals { get; set; }
    public int UnalignedTranslatedWords { get; set; }

    public IReadOnlyDictionary<string, List<string>> Examples => examples;

    public void Example(string kind, string example)
    {
        if (!examples.TryGetValue(kind, out var list))
        {
            examples[kind] = list = [];
        }

        if (list.Count < ExamplesPerClass)
        {
            list.Add(example);
        }
    }

    public void Add(InterlinearJoinAccount other)
    {
        VersesRead += other.VersesRead;
        VersesMissingInTranslation += other.VersesMissingInTranslation;
        VersesMissingInWitness += other.VersesMissingInWitness;
        VersesJoined += other.VersesJoined;
        VersesWithNoSpanJoined += other.VersesWithNoSpanJoined;
        Spans += other.Spans;
        SpansJoined += other.SpansJoined;
        SpansInMissingVerses += other.SpansInMissingVerses;
        foreach (var (failure, count) in other.SpanFailures)
        {
            SpanFailures[failure] = SpanFailures.GetValueOrDefault(failure) + count;
        }

        SpansJoinedOnTheLooseFold += other.SpansJoinedOnTheLooseFold;
        SpansSharingAnOriginal += other.SpansSharingAnOriginal;
        TranslatedFragments += other.TranslatedFragments;
        TranslatedWords += other.TranslatedWords;
        TranslatedWordsJoined += other.TranslatedWordsJoined;
        TranslatedWordsInMissingVerses += other.TranslatedWordsInMissingVerses;
        TranslatedWordsOnAnotherOccurrence += other.TranslatedWordsOnAnotherOccurrence;
        SharedOriginals += other.SharedOriginals;
        UnalignedTranslatedWords += other.UnalignedTranslatedWords;
        foreach (var (kind, list) in other.examples)
        {
            foreach (var example in list)
            {
                Example(kind, example);
            }
        }
    }

    public string Report(bool withExamples)
    {
        static string Share(int part, int whole) => whole == 0 ? "-" : $"{(double)part / whole:P2}";

        var report = new StringBuilder()
            .Append($"verses: {VersesRead:N0} read; {VersesJoined:N0} joined at least one span; ")
            .Append($"{VersesMissingInTranslation:N0} not at that address in the translation; ")
            .Append($"{VersesMissingInWitness:N0} not at that address in the witness; ")
            .AppendLine($"{VersesWithNoSpanJoined:N0} with no span joined")
            .Append($"spans: {SpansJoined:N0}/{Spans:N0} joined ({Share(SpansJoined, Spans)}), ")
            .Append($"{SpansJoinedOnTheLooseFold:N0} of them told apart only by the bare letters, ")
            .Append($"{SpansSharingAnOriginal:N0} on an original word another span also stands on; ")
            .Append($"{SpansInMissingVerses:N0} in a verse missing on one side; ")
            .AppendLine(string.Join("; ", Enum.GetValues<InterlinearSpanFailure>()
                .Where(failure => failure != InterlinearSpanFailure.None)
                .Select(failure => $"{failure} {SpanFailures.GetValueOrDefault(failure):N0}")))
            .Append($"translated words in spans: {TranslatedWordsJoined:N0}/{TranslatedWords:N0} joined ")
            .Append($"({Share(TranslatedWordsJoined, TranslatedWords)}); ")
            .Append($"{TranslatedWordsInMissingVerses:N0} in a verse missing on one side; ")
            .Append($"{TranslatedWordsOnAnotherOccurrence:N0} joined to another occurrence than the source counts; ")
            .AppendLine($"{TranslatedFragments:N0} pieces of an apostrophe word the source split and the corpus prints whole, not joined")
            .Append($"stated but not kept by the reader: {SharedOriginals:N0} original words in a nest over an inner span's words; ")
            .Append($"{UnalignedTranslatedWords:N0} translated words outside every span");

        if (withExamples)
        {
            foreach (var (kind, list) in examples.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                report.AppendLine().Append($"  {kind}:");
                foreach (var example in list)
                {
                    report.AppendLine().Append($"    {example}");
                }
            }
        }

        return report.ToString();
    }
}

/// <summary>
/// The join of a stated interlinear onto the corpus, as a pure function of what was read, so what it
/// loses can be counted and tested without a database.
/// </summary>
internal static class InterlinearJoin
{
    /// <summary>
    /// One verse, span by span.
    ///
    /// The two sides are joined differently because the file is ordered by one of them. Spans stand
    /// in the translation's word order, so the translated words are found with a cursor that only
    /// moves past confirmed words: a span that fails cannot push the following ones onto the wrong
    /// words. The original words are in a different order in every verse that reorders anything, so
    /// a cursor there dropped every span whose original word stood before an earlier span's - a
    /// quarter of the Ukrainian interlinear, and exactly the reordered quarter. They are found by the
    /// occurrence the source states instead.
    ///
    /// The sources are not the same editions. unfoldingWord aligns against its own Hebrew and its own
    /// Greek, and ours are BHSA and Nestle 1904 - the Hebrew divides some words differently and the
    /// Greek is a different text altogether - so a span whose word or whose count cannot be found in
    /// ours is refused, and the rest of the verse still joins.
    /// </summary>
    public static int Verse(
        string where,
        AlignedVerse verse,
        IReadOnlyList<InterlinearWord> translated,
        IReadOnlyList<InterlinearWord> original,
        List<InterlinearPair> pairs,
        InterlinearJoinAccount account)
    {
        account.VersesRead++;
        account.SharedOriginals += verse.SharedOriginals;
        account.UnalignedTranslatedWords += verse.UnalignedWords;
        var spanWords = verse.Spans.Sum(span => span.Words.Count);
        account.Spans += verse.Spans.Count;
        account.TranslatedWords += spanWords;

        if (translated.Count == 0 || original.Count == 0)
        {
            if (translated.Count == 0)
            {
                account.VersesMissingInTranslation++;
                account.Example("verse missing in translation", where);
            }
            else
            {
                account.VersesMissingInWitness++;
                account.Example("verse missing in witness", where);
            }

            account.SpansInMissingVerses += verse.Spans.Count;
            account.TranslatedWordsInMissingVerses += spanWords;
            return 0;
        }

        var made = 0;
        var here = 0;
        var taken = new HashSet<int>();
        var language = original[0].Language;

        foreach (var span in verse.Spans)
        {
            var (ours, afterOurs, fragments, failure) = FindTranslated(translated, span.Words, translated[0].Language, here);
            var theirs = new List<int>();
            var loose = false;
            if (failure == InterlinearSpanFailure.None)
            {
                (theirs, loose, failure) = FindOriginal(original, span, language);
            }

            if (failure != InterlinearSpanFailure.None)
            {
                account.SpanFailures[failure] = account.SpanFailures.GetValueOrDefault(failure) + 1;
                account.Example(failure.ToString(),
                    $"{where} {string.Join(' ', span.Words)} = {span.Content} ({span.Strong}, " +
                    $"occurrence {span.Occurrence}/{span.Occurrences})");
                continue;
            }

            CheckTranslatedOccurrences(where, span, translated, ours, account);
            account.TranslatedFragments += fragments;
            // One original word over two groups of translated words that do not stand together is
            // written as two spans with the same occurrence, so the second one landing on the same
            // witness word is the source's statement, not a collision.
            if (theirs.Any(taken.Contains))
            {
                account.SpansSharingAnOriginal++;
            }

            taken.UnionWith(theirs);
            pairs.Add(new InterlinearPair(
                [.. ours.Select(index => translated[index].Id)],
                [.. theirs.Select(index => original[index].Id)]));
            here = afterOurs;
            made += ours.Count;
            account.SpansJoined++;
            account.TranslatedWordsJoined += ours.Count;
            if (loose)
            {
                account.SpansJoinedOnTheLooseFold++;
            }
        }

        if (made == 0)
        {
            account.VersesWithNoSpanJoined++;
        }
        else
        {
            account.VersesJoined++;
        }

        return made;
    }

    /// <summary>
    /// The witness words a span's original word stands on. Its morphemes have to stand side by side,
    /// and of the places they do, the source's occurrence picks one - but only where the witness
    /// prints the word as many times as the source counts, because a count taken over another edition
    /// means nothing otherwise.
    ///
    /// The count is tried on the printed form first and on the bare letters second. The source counts
    /// spellings: <c>καὶ</c> and <c>καί</c> are two words to it, and so are two Hebrew words that
    /// differ in an accent. The bare letters join those, which is right only where they give the same
    /// total the source gives.
    /// </summary>
    private static (List<int> Words, bool Loose, InterlinearSpanFailure Failure) FindOriginal(
        IReadOnlyList<InterlinearWord> words,
        AlignmentSpan span,
        string language)
    {
        var morphemes = span.Morphemes;
        var written = morphemes.Select(morpheme => Written(morpheme, language)).ToArray();
        var folded = morphemes.Select(morpheme => WordFolding.Fold(morpheme, language)).ToArray();
        var byLetters = Places(words, folded, word => word.Folded);
        if (byLetters.Count == 0)
        {
            return ([], false, InterlinearSpanFailure.OriginalWordAbsent);
        }

        var stated = span.Occurrences > 0 ? span.Occurrences : 1;
        var occurrence = span.Occurrence > 0 ? span.Occurrence : 1;
        var byWriting = Places(words, written, word => Written(word.Written, language));
        var (places, loose) = byWriting.Count == stated
            ? (byWriting, false)
            : byLetters.Count == stated
                ? (byLetters, true)
                : (null, false);
        return places is null || occurrence > places.Count
            ? ([], false, InterlinearSpanFailure.OriginalOccurrenceUnresolved)
            : (places[occurrence - 1], loose, InterlinearSpanFailure.None);
    }

    /// <summary>
    /// Every run of consecutive witness words that spells the morphemes in order, each word one or
    /// more morphemes long. The source cuts a pronominal suffix off as a morpheme of its own and BHSA
    /// keeps it on its word - <c>מַלְכוּת⁠וֹ</c> against <c>מַלְכוּתֹו</c> - and that is one word
    /// divided twice, not a word the witness lacks.
    /// </summary>
    private static List<List<int>> Places(
        IReadOnlyList<InterlinearWord> words,
        string[] morphemes,
        Func<InterlinearWord, string> form)
    {
        var places = new List<List<int>>();
        for (var start = 0; start < words.Count; start++)
        {
            var used = new List<int>(morphemes.Length);
            if (Spells(words, start, morphemes, 0, form, used))
            {
                places.Add(used);
            }
        }

        return places;
    }

    private static bool Spells(
        IReadOnlyList<InterlinearWord> words,
        int at,
        string[] morphemes,
        int from,
        Func<InterlinearWord, string> form,
        List<int> used)
    {
        if (from == morphemes.Length)
        {
            return true;
        }

        if (at >= words.Count)
        {
            return false;
        }

        var word = form(words[at]);
        if (word.Length == 0)
        {
            // BHSA keeps the article a preposition swallowed as a word with nothing printed; it
            // belongs to the word it stands inside, and never starts or ends one.
            if (from == 0)
            {
                return false;
            }

            used.Add(at);
            if (Spells(words, at + 1, morphemes, from, form, used))
            {
                return true;
            }

            used.RemoveAt(used.Count - 1);
            return false;
        }

        var spelled = string.Empty;
        for (var through = from; through < morphemes.Length; through++)
        {
            spelled += morphemes[through];
            if (!word.StartsWith(spelled, StringComparison.Ordinal))
            {
                return false;
            }

            if (spelled.Length == word.Length)
            {
                used.Add(at);
                if (Spells(words, at + 1, morphemes, through + 1, form, used))
                {
                    return true;
                }

                used.RemoveAt(used.Count - 1);
            }
        }

        return false;
    }

    /// <summary>
    /// A word as the source's count sees it: its printed letters and marks in one normal form, in
    /// lower case, with the several apostrophes an elided Greek word is printed with made one.
    /// </summary>
    private static string Written(string word, string language)
    {
        var normal = word.Normalize(NormalizationForm.FormC);
        return language == "grc"
            ? normal.ToLowerInvariant().Replace('’', '\'').Replace('ʼ', '\'').Replace('᾽', '\'')
            : normal;
    }

    /// <summary>
    /// Whether a translated word landed on the occurrence the source names. It can only be asked
    /// where the corpus prints the spelling as many times as the source counts it.
    /// </summary>
    private static void CheckTranslatedOccurrences(
        string where,
        AlignmentSpan span,
        IReadOnlyList<InterlinearWord> translated,
        List<int> ours,
        InterlinearJoinAccount account)
    {
        for (var i = 0; i < ours.Count; i++)
        {
            if (span.WordOccurrences is not { } occurrences || i >= occurrences.Count
                || occurrences[i] is not { Occurrence: > 0, Occurrences: > 0 } stated)
            {
                continue;
            }

            var spelling = span.Words[i];
            var printed = translated.Count(word => word.Written == spelling);
            if (printed != stated.Occurrences)
            {
                continue;
            }

            if (translated[ours[i]].Written != spelling)
            {
                continue;
            }

            var at = translated.Take(ours[i] + 1).Count(word => word.Written == spelling);
            if (at != stated.Occurrence)
            {
                account.TranslatedWordsOnAnotherOccurrence++;
                account.Example("translated word on another occurrence",
                    $"{where} '{spelling}' stated occurrence {stated.Occurrence}, joined {at}");
            }
        }
    }

    /// <summary>
    /// The translated words of a span, found in order from the cursor. Answers an empty list when
    /// any one of them is missing, so the caller can leave the cursor where it was, and says whether
    /// the one that was missing is nowhere in the verse or only behind the cursor.
    ///
    /// The source tokenises at an apostrophe and the corpus does not: <c>Шім’ї</c> is two words
    /// there and one here. The piece before the apostrophe finds the corpus word; a piece after it,
    /// standing on the word just found, is counted and not joined, because the source has been seen
    /// to align that piece to a different original word from its stem and the corpus word cannot be
    /// both.
    /// </summary>
    private static (List<int> Words, int After, int Fragments, InterlinearSpanFailure Failure) FindTranslated(
        IReadOnlyList<InterlinearWord> words,
        IReadOnlyList<string> wanted,
        string? language,
        int from)
    {
        var found = new List<int>(wanted.Count);
        var at = from;
        var fragments = 0;

        foreach (var one in wanted)
        {
            var folded = Apostrophe(WordFolding.Fold(one, language));
            if (at > 0 && Pieces(words[at - 1]).Skip(1).Contains(folded))
            {
                fragments++;
                continue;
            }

            var next = -1;
            for (var i = at; i < words.Count && next < 0; i++)
            {
                if (Apostrophe(words[i].Folded) == folded || Pieces(words[i]) is [var stem, _, ..] && stem == folded)
                {
                    next = i;
                }
            }

            if (next < 0)
            {
                return ([], from, 0, words.Any(word => Apostrophe(word.Folded) == folded)
                    ? InterlinearSpanFailure.TranslatedWordBehind
                    : InterlinearSpanFailure.TranslatedWordAbsent);
            }

            found.Add(next);
            at = next + 1;
        }

        return found.Count == 0
            ? ([], from, 0, InterlinearSpanFailure.TranslatedWordAbsent)
            : (found, at, fragments, InterlinearSpanFailure.None);
    }

    private static string[] Pieces(InterlinearWord word) =>
        word.Written.ToLowerInvariant().Split(Apostrophes, StringSplitOptions.RemoveEmptyEntries);

    private static string Apostrophe(string word) => word.Replace('’', '\'').Replace('ʼ', '\'');

    private static readonly char[] Apostrophes = ['\'', '’', 'ʼ'];
}
