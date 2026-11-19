using System.Text;
using Essenthos.Core.Door43;
using Essenthos.Core.Corpus;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// A word of a corpus verse: its id, the folded form a source's spelling is matched by, and the form
/// the corpus prints, which is what a source's occurrence count is a count of.
/// </summary>
/// <param name="Strong">The Strong number the corpus gives the word, where it gives one.</param>
/// <param name="Attached">Whether the next word is printed against it with nothing between, as BHSA prints וְ before its word.</param>
internal sealed record InterlinearWord(long Id, string Folded, string Language, string Written = "", string? Strong = null, bool Attached = false);

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
    public int SpansJoinedByNumber { get; set; }
    public int SpansJoinedByTheOnePlaceLeft { get; set; }
    public int SpansSharingAnOriginal { get; set; }
    public int SpansOverANest { get; set; }
    public int SpansOverANestJoined { get; set; }
    public int NestWordsTheWitnessLacks { get; set; }
    public int NestsJoinedAsOneWord { get; set; }
    public int NestWordsPlacedBeside { get; set; }
    public int CompoundsJoined { get; set; }
    public int TranslatedPiecesJoined { get; set; }
    public int TranslatedFragments { get; set; }
    public int SuppliedWordsLeftOut { get; set; }
    public int SpansOnlySupplied { get; set; }
    public int TranslatedWords { get; set; }
    public int TranslatedWordsJoined { get; set; }
    public int TranslatedWordsInMissingVerses { get; set; }
    public int TranslatedWordsOnAnotherOccurrence { get; set; }
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
        SpansJoinedByNumber += other.SpansJoinedByNumber;
        SpansJoinedByTheOnePlaceLeft += other.SpansJoinedByTheOnePlaceLeft;
        SpansSharingAnOriginal += other.SpansSharingAnOriginal;
        SpansOverANest += other.SpansOverANest;
        SpansOverANestJoined += other.SpansOverANestJoined;
        NestWordsTheWitnessLacks += other.NestWordsTheWitnessLacks;
        NestsJoinedAsOneWord += other.NestsJoinedAsOneWord;
        NestWordsPlacedBeside += other.NestWordsPlacedBeside;
        CompoundsJoined += other.CompoundsJoined;
        TranslatedPiecesJoined += other.TranslatedPiecesJoined;
        TranslatedFragments += other.TranslatedFragments;
        SuppliedWordsLeftOut += other.SuppliedWordsLeftOut;
        SpansOnlySupplied += other.SpansOnlySupplied;
        TranslatedWords += other.TranslatedWords;
        TranslatedWordsJoined += other.TranslatedWordsJoined;
        TranslatedWordsInMissingVerses += other.TranslatedWordsInMissingVerses;
        TranslatedWordsOnAnotherOccurrence += other.TranslatedWordsOnAnotherOccurrence;
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
            .Append($"{SpansJoinedByNumber:N0} by a Strong number the verse holds once on each side, ")
            .Append($"{SpansJoinedByTheOnePlaceLeft:N0} on the one word of their lemma the others' spellings leave, ")
            .Append($"{SpansSharingAnOriginal:N0} on an original word another span also stands on; ")
            .Append($"{SpansOverANestJoined:N0}/{SpansOverANest:N0} over a nest of several original words joined, {NestsJoinedAsOneWord:N0} of them as one word of the witness, {NestWordsPlacedBeside:N0} nest words placed beside the rest of their nest, and {NestWordsTheWitnessLacks:N0} words of a Greek nest the witness verse does not print left out; ")
            .Append($"{CompoundsJoined:N0} words the witness prints whole joined to the spans over their parts; ")
            .Append($"{SpansInMissingVerses:N0} in a verse missing on one side; ")
            .AppendLine(string.Join("; ", Enum.GetValues<InterlinearSpanFailure>()
                .Where(failure => failure != InterlinearSpanFailure.None)
                .Select(failure => $"{failure} {SpanFailures.GetValueOrDefault(failure):N0}")))
            .Append($"translated words in spans: {TranslatedWordsJoined:N0}/{TranslatedWords:N0} joined ")
            .Append($"({Share(TranslatedWordsJoined, TranslatedWords)}); ")
            .Append($"{TranslatedWordsInMissingVerses:N0} in a verse missing on one side; ")
            .Append($"{TranslatedWordsOnAnotherOccurrence:N0} joined to another occurrence than the source counts; ")
            .Append($"{TranslatedPiecesJoined:N0} pieces of a word the source split and the corpus prints whole, joined to that word; ")
            .AppendLine($"{TranslatedFragments:N0} pieces after an apostrophe in another span than the word's stem, not joined; "
                + $"{SuppliedWordsLeftOut:N0} words the translators supplied in braces left unlinked, {SpansOnlySupplied:N0} spans of nothing else")
            .Append($"stated but not kept: {UnalignedTranslatedWords:N0} translated words outside every span");

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
        var here = new TranslatedCursor(0, string.Empty);
        var taken = new HashSet<int>();
        var language = original[0].Language;
        var writings = original.Select(word => Written(word.Written, language)).ToArray();

        for (var index = 0; index < verse.Spans.Count; index++)
        {
            var span = verse.Spans[index];
            var nested = span.Nest is { Count: > 0 };
            if (nested)
            {
                account.SpansOverANest++;
            }

            var (matched, afterOurs, fragments, failure) = FindTranslated(translated, span, translated[0].Language, here);
            var theirs = new List<int>();
            var loose = false;
            var byNumber = false;
            var left = false;
            AlignedOriginal? missing = null;
            IReadOnlyList<AlignedOriginal> lacking = [];
            if (failure == InterlinearSpanFailure.None)
            {
                (theirs, loose, byNumber, failure, missing, lacking, var asOne, var beside, left) =
                    FindOriginals(original, writings, span, verse, language);
                if (asOne)
                {
                    account.NestsJoinedAsOneWord++;
                }

                account.NestWordsPlacedBeside += beside;
            }

            if (failure is InterlinearSpanFailure.OriginalWordAbsent or InterlinearSpanFailure.OriginalOccurrenceUnresolved
                || (failure == InterlinearSpanFailure.None && loose))
            {
                var joined = false;
                for (var count = nested ? 1 : 2; count <= CompoundParts && index + count <= verse.Spans.Count && !joined; count++)
                {
                    if (JoinCompound(where, verse, [.. verse.Spans.Skip(index).Take(count)], matched, afterOurs, fragments, translated,
                            original, writings, language, taken, pairs, account) is { } compound)
                    {
                        (here, made, index, joined) = (compound.After, made + compound.Made, index + count - 1, true);
                    }
                }

                if (joined)
                {
                    continue;
                }
            }

            if (failure != InterlinearSpanFailure.None)
            {
                account.SpanFailures[failure] = account.SpanFailures.GetValueOrDefault(failure) + 1;
                var failed = missing ?? span.Originals[^1];
                account.Example(failure.ToString(),
                    $"{where} {string.Join(' ', span.Words)} = {failed.Content} ({failed.Strong}, " +
                    $"occurrence {failed.Occurrence}/{failed.Occurrences})"
                    + (nested ? $" in the nest {string.Join(" + ", span.Originals.Select(one => one.Content))}" : string.Empty));
                // A span refused for its original word still found its translated words, and they are its own: the next
                // span is looked for after them, or it takes a word of theirs that it spells too (Matthew 14:29 in the ULT,
                // «to» of «to Jesus» on the «to» of «to go», whose ἐλθεῖν Nestle reads ἦλθεν). A span whose translated
                // words were not found leaves the cursor where it was. Either way the next span's first piece cannot be the
                // rest of a word of the refused span.
                here = failure is InterlinearSpanFailure.OriginalWordAbsent or InterlinearSpanFailure.OriginalOccurrenceUnresolved
                    ? afterOurs with { Rest = string.Empty }
                    : here with { Rest = string.Empty };
                continue;
            }

            // A word the translators set in braces renders nothing, so it is not linked; a word printed whole whose
            // braced piece is only a part of it (bird{s}) still renders through its other pieces.
            var ours = matched.Where(match => !Supplied(span, match.Token)).Select(match => match.Word).Distinct().ToList();
            var leftOut = matched.Select(match => match.Word).Distinct().Count() - ours.Count;
            account.SuppliedWordsLeftOut += leftOut;
            if (ours.Count == 0)
            {
                account.SpansOnlySupplied++;
                account.Example("span of supplied words only", $"{where} {string.Join(' ', span.Words)}");
                here = afterOurs;
                continue;
            }

            CheckTranslatedOccurrences(where, span, translated, matched, account);
            account.TranslatedFragments += fragments;
            account.TranslatedPiecesJoined += matched.Count - ours.Count;
            if (nested)
            {
                account.SpansOverANestJoined++;
            }

            if (lacking.Count > 0)
            {
                account.NestWordsTheWitnessLacks += lacking.Count;
                account.Example("nest word the witness lacks",
                    $"{where} {string.Join(' ', span.Words)} = {string.Join(" + ", span.Originals.Select(one => one.Content))}, "
                    + $"without {string.Join(" + ", lacking.Select(one => one.Content))}");
            }

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
            account.TranslatedWordsJoined += matched.Count;
            if (loose)
            {
                account.SpansJoinedOnTheLooseFold++;
            }

            if (left)
            {
                account.SpansJoinedByTheOnePlaceLeft++;
                account.Example("joined on the one place left", $"{where} {string.Join(' ', span.Words)} = {span.Content} ({span.Strong}) "
                    + $"on {string.Join(' ', theirs.Select(index => original[index].Written))}");
            }
            else if (byNumber)
            {
                account.SpansJoinedByNumber++;
                account.Example("joined by number", $"{where} {string.Join(' ', span.Words)} = {span.Content} ({span.Strong}) "
                    + $"on {string.Join(' ', theirs.Select(index => original[index].Written))}");
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
    /// Spans in a row whose original words include every part of one witness word that BHSA prints whole and the
    /// source as several words: a name with a maqaf or a space inside, בֶּן־הֲדַד, בֵּית עֲנָת. The source aligns each
    /// part over its own English (<c>Ben | Hadad</c>, 1 Kings 20:33), so no span finds its word alone, or finds a
    /// word of the same letters elsewhere in the verse by the bare letters (<c>El</c> on אֶל, Joshua 19:27). The
    /// English of all of them renders the one word together, so it is one link, not a link of each part to the
    /// whole word. A part may stand in a nest among other words: <c>Beth</c> over אֶת + בֵּית, then <c>Shan</c> over
    /// שְׁאָן (Judges 1:27). Null, with nothing joined, where the spans are not that.
    /// </summary>
    private static (TranslatedCursor After, int Made)? JoinCompound(
        string where,
        AlignedVerse verse,
        IReadOnlyList<AlignmentSpan> spans,
        List<(int Token, int Word)> firstMatched,
        TranslatedCursor afterFirst,
        int firstFragments,
        IReadOnlyList<InterlinearWord> translated,
        IReadOnlyList<InterlinearWord> original,
        string[] writings,
        string language,
        HashSet<int> taken,
        List<InterlinearPair> pairs,
        InterlinearJoinAccount account)
    {
        if (spans is [{ Nest: null or { Count: 0 } }]
            || spans.Skip(1).Any(span => FindOriginals(original, writings, span, verse, language) is { Failure: InterlinearSpanFailure.None, Loose: false })
            || Compound(original, [.. spans.Select(span => span.Originals)], language) is not { } theirs)
        {
            return null;
        }

        var matched = firstMatched.Select(match => (Span: spans[0], match.Token, match.Word)).ToList();
        var (after, fragments) = (afterFirst, firstFragments);
        foreach (var next in spans.Skip(1))
        {
            var (nextMatched, afterNext, nextFragments, nextFailure) = FindTranslated(translated, next, translated[0].Language, after);
            if (nextFailure != InterlinearSpanFailure.None)
            {
                return null;
            }

            matched.AddRange(nextMatched.Select(match => (Span: next, match.Token, match.Word)));
            (after, fragments) = (afterNext, fragments + nextFragments);
        }

        var ours = matched.Where(match => !Supplied(match.Span, match.Token)).Select(match => match.Word).Distinct().ToList();
        if (ours.Count == 0)
        {
            return null;
        }

        foreach (var span in spans)
        {
            CheckTranslatedOccurrences(where, span, translated,
                [.. matched.Where(match => match.Span == span).Select(match => (match.Token, match.Word))], account);
            if (span.Nest is { Count: > 0 })
            {
                // The loop counted the first span as it reached it, and skips the others.
                account.SpansOverANest += span == spans[0] ? 0 : 1;
                account.SpansOverANestJoined++;
            }
        }

        account.SuppliedWordsLeftOut += matched.Select(match => match.Word).Distinct().Count() - ours.Count;
        account.TranslatedPiecesJoined += matched.Count - ours.Count;
        account.TranslatedFragments += fragments;
        if (theirs.Any(taken.Contains))
        {
            account.SpansSharingAnOriginal++;
        }

        taken.UnionWith(theirs);
        pairs.Add(new InterlinearPair(
            [.. ours.Select(index => translated[index].Id)],
            [.. theirs.Select(index => original[index].Id)]));
        account.SpansJoined += spans.Count;
        account.TranslatedWordsJoined += matched.Count;
        account.CompoundsJoined++;
        account.Example("compound joined", $"{where} {string.Join(" | ", spans.Select(span => string.Join(' ', span.Words)))} = "
            + $"{string.Join(" + ", spans.SelectMany(span => span.Originals).Select(one => one.Content))} on "
            + string.Join(' ', theirs.Select(index => original[index].Written)));
        return (after, ours.Count);
    }

    /// <summary>The most parts a witness word printed whole has in BHSA: אָבֵל בֵּית־מַעֲכָה.</summary>
    private const int CompoundParts = 3;

    private const char Maqaf = '־';

    private static readonly char[] PartBreaks = [Maqaf, ' '];

    /// <summary>
    /// The witness words the original words of several spans stand on, where some of them are the parts of one
    /// witness word printed with a maqaf or a space inside it. Each original word is placed as the source counts
    /// it, as a printed word of its own, so the witness's words are cut into their parts for this, and each is
    /// found by its own stated occurrence. Null unless every one is placed, the ones that land on parts land on all
    /// the parts of one witness word, each starting where the one before it ended, and each span has one of them.
    /// </summary>
    private static List<int>? Compound(IReadOnlyList<InterlinearWord> words, IReadOnlyList<IReadOnlyList<AlignedOriginal>> spans, string language)
    {
        if (language is not ("hbo" or "arc") || !words.Any(word => word.Written.IndexOfAny(PartBreaks) >= 0))
        {
            return null;
        }

        var cut = new List<InterlinearWord>(words.Count + 4);
        var of = new List<(int Word, int Part, int Parts)>(words.Count + 4);
        for (var at = 0; at < words.Count; at++)
        {
            var parts = words[at].Written.Split(PartBreaks);
            if (parts.Length == 1 || parts.Any(part => part.Length == 0))
            {
                cut.Add(words[at]);
                of.Add((at, 0, 1));
                continue;
            }

            for (var part = 0; part < parts.Length; part++)
            {
                cut.Add(words[at] with
                {
                    Written = parts[part], Folded = WordFolding.Fold(parts[part], language),
                    Attached = part == parts.Length - 1 && words[at].Attached,
                });
                of.Add((at, part, parts.Length));
            }
        }

        var writings = cut.Select(word => Written(word.Written, language)).ToArray();
        var placed = new List<(int Span, List<int> Place)>();
        for (var span = 0; span < spans.Count; span++)
        {
            foreach (var one in spans[span])
            {
                var (place, _, failure) = FindOriginal(cut, writings, one, language);
                if (failure != InterlinearSpanFailure.None)
                {
                    return null;
                }

                placed.Add((span, place));
            }
        }

        var parted = placed.Where(one => one.Place.Any(at => of[at].Parts > 1)).OrderBy(one => one.Place[0]).ToList();
        if (parted.Count < 2)
        {
            return null;
        }

        var first = parted[0].Place;
        var word = of[first[^1]].Word;
        if (of[first[^1]].Part != 0 || first.Take(first.Count - 1).Any(at => of[at].Parts > 1))
        {
            return null;
        }

        var end = first[^1];
        foreach (var (_, place) in parted.Skip(1))
        {
            if (place[0] != end + 1 || place.Any(at => of[at].Word != word))
            {
                return null;
            }

            end = place[^1];
        }

        if (of[end].Part != of[end].Parts - 1 || parted.Select(one => one.Span).Distinct().Count() != spans.Count)
        {
            return null;
        }

        return [.. placed.SelectMany(one => one.Place).Select(at => of[at].Word).Distinct().Order()];
    }

    /// <summary>
    /// The witness words all of a span's original words stand on. A nest is one statement: its phrase renders
    /// every original word of it, so it joins only where each of them is found, and never as a part of itself
    /// (<c>Then he waited</c> on עוֹד alone would say עוֹד means it).
    ///
    /// Two editions divide and read some words differently, so a word of a nest that is not found is looked at
    /// again, in this order:
    /// <list type="bullet">
    /// <item>A word the witness prints more often than the source counts it (a variant elsewhere in the verse:
    /// Nestle has two τὸν in Mark 9:2 where the source has three) is the one place of it standing right beside the
    /// rest of the nest or between its words, if there is exactly one: the nest's words stand together.</item>
    /// <item>The nest's words spelled together may be one word of the witness: בֵּית + אֵל is BHSA's one word
    /// בֵּית־אֵל.</item>
    /// <item>In the Greek an outer word of the nest is another edition's reading and is left out (Nestle prints no
    /// τὸν before Ἰωάνην in Matthew 14:10, so «John» is Ἰωάνην). The innermost word, and any word of a Hebrew nest,
    /// is not: there the missing word is mostly one the witness divides or spells otherwise, and the phrase would
    /// land on the word beside it (Bethel on אֵל, «John» on the article).</item>
    /// </list>
    /// </summary>
    private static (List<int> Words, bool Loose, bool ByNumber, InterlinearSpanFailure Failure, AlignedOriginal? Missing,
        IReadOnlyList<AlignedOriginal> Lacking, bool AsOneWord, int Beside, bool Left) FindOriginals(
        IReadOnlyList<InterlinearWord> original,
        string[] writings,
        AlignmentSpan span,
        AlignedVerse verse,
        string language)
    {
        var originals = verse.Originals;
        var all = span.Originals;
        var theirs = new List<int>();
        var loose = false;
        var byNumber = false;
        var left = false;
        var failed = new List<(AlignedOriginal Original, InterlinearSpanFailure Failure)>();
        foreach (var one in all)
        {
            var (words, wordLoose, failure) = FindOriginal(original, writings, one, language);
            if (failure != InterlinearSpanFailure.None
                && ByNumber(original, one, originals, language) is { Count: > 0 } numbered)
            {
                (words, failure, byNumber) = (numbered, InterlinearSpanFailure.None, true);
            }

            if (failure != InterlinearSpanFailure.None)
            {
                failed.Add((one, failure));
                continue;
            }

            theirs.AddRange(words);
            loose |= wordLoose;
        }

        if (failed.Count == 0)
        {
            return ([.. theirs.Distinct().Order()], loose, byNumber, InterlinearSpanFailure.None, null, [], false, 0, false);
        }

        var beside = 0;
        foreach (var (one, failure) in failed.ToList())
        {
            if (failure == InterlinearSpanFailure.OriginalOccurrenceUnresolved
                && Beside(original, one, theirs, language) is { } place)
            {
                theirs.AddRange(place);
                failed.Remove((one, failure));
                beside++;
            }
        }

        foreach (var (one, failure) in failed.ToList())
        {
            if (OnePlaceLeft(original, writings, one, verse, language) is { Count: > 0 } remaining)
            {
                theirs.AddRange(remaining);
                failed.Remove((one, failure));
                (byNumber, left) = (true, true);
            }
        }

        if (failed.Count == 0)
        {
            return ([.. theirs.Distinct().Order()], loose, byNumber, InterlinearSpanFailure.None, null, [], false, beside, left);
        }

        if (all.Count == 1)
        {
            return ([], false, false, failed[0].Failure, failed[0].Original, [], false, 0, false);
        }

        if (failed.Count > 0 && failed.Any(one => one.Failure == InterlinearSpanFailure.OriginalWordAbsent))
        {
            // BHSA prints some names as one word with a space inside: נֹּא אָמֹון, Thebes.
            var unspaced = original.Select(word => word with
            {
                Folded = word.Folded.Replace(" ", string.Empty), Written = word.Written.Replace(" ", string.Empty),
            }).ToList();
            var together = new AlignedOriginal(all[^1].Strong, string.Join('\u2060', all.Select(one => one.Content)), 1, 1);
            var (whole, wholeLoose, wholeFailure) = FindOriginal(
                unspaced, [.. unspaced.Select(word => Written(word.Written, language))], together, language);
            if (wholeFailure == InterlinearSpanFailure.None && whole.Count > 0)
            {
                return (whole, wholeLoose, false, InterlinearSpanFailure.None, null, [], true, 0, false);
            }
        }

        if (failed.Count > 0 && (language != "grc" || failed.Any(one => one.Original == all[^1]) || theirs.Count == 0))
        {
            return ([], false, false, failed[0].Failure, failed[0].Original, [], false, 0, false);
        }

        return ([.. theirs.Distinct().Order()], loose, byNumber, InterlinearSpanFailure.None, null,
            [.. failed.Select(one => one.Original)], false, beside, left);
    }

    /// <summary>
    /// The one place of an original word that stands right before or right after the words the rest of its nest
    /// stands on, or in a gap between them (בֶּן of יְהוֹאָשׁ בֶּן יְהוֹאָחָז once both names are placed); null where
    /// none or several do.
    /// </summary>
    private static List<int>? Beside(IReadOnlyList<InterlinearWord> words, AlignedOriginal one, List<int> nest, string language)
    {
        if (nest.Count == 0)
        {
            return null;
        }

        var (first, last) = (nest.Min(), nest.Max());
        var folded = one.Morphemes.Select(morpheme => WordFolding.Fold(morpheme, language)).ToArray();
        var touching = Places(words.Count, folded, at => words[at].Folded)
            .Where(place => place[^1] == first - 1 || place[0] == last + 1
                            || (place[0] > first && place[^1] < last && !place.Any(nest.Contains)))
            .ToList();
        return touching.Count == 1 ? touching[0] : null;
    }

    /// <summary>
    /// The witness words a span's original word stands on. Its morphemes have to stand side by side,
    /// and of the places they do, the source's occurrence picks one - but only where the witness
    /// prints the word as many times as the source counts, because a count taken over another edition
    /// means nothing otherwise.
    ///
    /// The count is tried on the printed form first, then, in Hebrew, on the letters and their accents,
    /// then on those and the meteg, and on the bare letters last. The source counts spellings: <c>καὶ</c>
    /// and <c>καί</c> are two words to it, and so are two Hebrew words that differ in an accent. The bare
    /// letters join those, which is right only where they give the same total the source gives. The
    /// accents tell them apart again where the printed form cannot because the two editions put a vowel
    /// on another letter (BHSA's יֹואָב, the source's יוֹאָב).
    /// </summary>
    private static (List<int> Words, bool Loose, InterlinearSpanFailure Failure) FindOriginal(
        IReadOnlyList<InterlinearWord> words,
        string[] writings,
        AlignedOriginal span,
        string language)
    {
        var morphemes = span.Morphemes;
        var written = morphemes.Select(morpheme => Written(morpheme, language)).ToArray();
        var folded = morphemes.Select(morpheme => WordFolding.Fold(morpheme, language)).ToArray();
        var byLetters = Places(words.Count, folded, at => words[at].Folded);
        if (byLetters.Count == 0)
        {
            return ([], false, InterlinearSpanFailure.OriginalWordAbsent);
        }

        var stated = span.Occurrences > 0 ? span.Occurrences : 1;
        var occurrence = span.Occurrence > 0 ? span.Occurrence : 1;
        var byWriting = Places(words.Count, written, at => writings[at]);
        var byAccents = byWriting.Count == stated || language is not ("hbo" or "arc")
            ? []
            : ByAccents(words, morphemes, meteg: false);
        var byMeteg = byWriting.Count == stated || byAccents.Count == stated || language is not ("hbo" or "arc")
            ? []
            : ByAccents(words, morphemes, meteg: true);
        var (places, loose) = byWriting.Count == stated
            ? (byWriting, false)
            : byAccents.Count == stated
                ? (byAccents, false)
                : byMeteg.Count == stated
                    ? (byMeteg, false)
                    : byLetters.Count == stated
                        ? (byLetters, true)
                        : (null, false);
        if (places is null)
        {
            // The source counts whole printed words: אֶת standing alone is another spelling to it than the
            // אֶת of וְאֶת, which the witness holds as a word of its own after the conjunction.
            bool Whole(List<int> place) => (place[0] == 0 || !words[place[0] - 1].Attached) && !words[place[^1]].Attached;
            var wholeByWriting = byWriting.Where(Whole).ToList();
            var wholeByAccents = byAccents.Where(Whole).ToList();
            var wholeByMeteg = byMeteg.Where(Whole).ToList();
            var wholeByLetters = byLetters.Where(Whole).ToList();
            (places, loose) = wholeByWriting.Count == stated
                ? (wholeByWriting, false)
                : wholeByAccents.Count == stated
                    ? (wholeByAccents, false)
                    : wholeByMeteg.Count == stated
                        ? (wholeByMeteg, false)
                        : wholeByLetters.Count == stated
                            ? (wholeByLetters, true)
                            : (null, false);
        }

        return places is null || occurrence > places.Count
            ? ([], false, InterlinearSpanFailure.OriginalOccurrenceUnresolved)
            : (places[occurrence - 1], loose, InterlinearSpanFailure.None);
    }

    /// <summary>
    /// The witness words a span stands on, found by its Strong number where its spelling did not find
    /// them: unfoldingWord's Hebrew and Greek are not BHSA and Nestle, and the same word can be
    /// accented, divided or spelled differently in the two. The number is taken only where it cannot be
    /// mistaken — the source verse names that lemma once, the witness verse holds it once — and a
    /// prefix the source cuts off (<c>b:</c>, <c>c:</c>, <c>d:</c>) has to be spelled by the witness words
    /// standing right before it, or nothing is joined.
    /// </summary>
    private static List<int> ByNumber(
        IReadOnlyList<InterlinearWord> words,
        AlignedOriginal span,
        IReadOnlyList<(string Strong, string Content, int Occurrence)>? originals,
        string language)
    {
        var number = Lemma(span.Strong);
        if (number is null || originals is null
            || originals.Count(original => Lemma(original.Strong) == number) != 1)
        {
            return [];
        }

        var at = -1;
        for (var i = 0; i < words.Count; i++)
        {
            if (words[i].Strong != number)
            {
                continue;
            }

            if (at >= 0)
            {
                return [];
            }

            at = i;
        }

        if (at < 0)
        {
            return [];
        }

        var prefixes = span.Strong.Count(character => character == ':');
        var morphemes = span.Morphemes;
        if (prefixes == 0)
        {
            return [at];
        }

        if (morphemes.Length <= prefixes)
        {
            return [];
        }

        var spelled = string.Concat(morphemes.Take(prefixes).Select(morpheme => WordFolding.Fold(morpheme, language)));
        var used = new List<int>(prefixes + 1) { at };
        var before = string.Empty;
        for (var i = at - 1; i >= 0 && before.Length < spelled.Length; i--)
        {
            before = words[i].Folded + before;
            used.Insert(0, i);
        }

        return before == spelled ? used : [];
    }

    /// <summary>
    /// The witness word a Greek original word stands on where the two editions spell it differently and its lemma stands
    /// more than once: the source verse holds the lemma, every spelling of it counted as often as the source counts it, as
    /// many times as the witness verse holds it, and every other one of those is found by its own spelling and count on a
    /// word of the lemma, each on another one, so one word of the lemma is left in the witness and it is this one.
    /// 1 Corinthians 7:9 has γαμησάτωσαν and γαμῆσαι in the source and γαμησάτωσαν and γαμεῖν in Nestle 1904: «to marry»
    /// is γαμεῖν. A spelling counts the words the source does not align too (Matthew 13:4's first καὶ in the UST), so a
    /// lemma the two editions hold a different number of times places nothing. αὐτός is never placed so: its case and
    /// number say who is meant, and where the editions differ they differ in that (Mark 14:46, «their» hands against
    /// Nestle's αὐτῷ, on him).
    /// </summary>
    private static List<int> OnePlaceLeft(
        IReadOnlyList<InterlinearWord> words,
        string[] writings,
        AlignedOriginal span,
        AlignedVerse verse,
        string language)
    {
        if (language != "grc" || span.Strong.Contains(':') || Lemma(span.Strong) is not { } number || number == Autos
            || span.Occurrence < 1 || span.Occurrences < span.Occurrence)
        {
            return [];
        }

        var spellings = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var named in verse.Spans.SelectMany(one => one.Originals))
        {
            if (!named.Strong.Contains(':') && Lemma(named.Strong) == number)
            {
                if (named.Occurrences < 1 || spellings.GetValueOrDefault(named.Content, named.Occurrences) != named.Occurrences)
                {
                    return [];
                }

                spellings[named.Content] = named.Occurrences;
            }
        }

        var held = Enumerable.Range(0, words.Count).Where(at => words[at].Strong == number).ToList();
        if (held.Count < 2 || held.Count != spellings.Values.Sum())
        {
            return [];
        }

        var placed = new HashSet<int>();
        foreach (var (content, occurrences) in spellings)
        {
            for (var occurrence = 1; occurrence <= occurrences; occurrence++)
            {
                if (content == span.Content && occurrence == span.Occurrence)
                {
                    continue;
                }

                var (place, loose, failure) = FindOriginal(words, writings, new AlignedOriginal(span.Strong, content, occurrence, occurrences), language);
                if (failure != InterlinearSpanFailure.None || loose || place is not [var at] || !held.Contains(at) || !placed.Add(at))
                {
                    return [];
                }
            }
        }

        return held.Where(at => !placed.Contains(at)).ToList() is [var left] ? [left] : [];
    }

    private const string Autos = "G846";

    /// <summary>
    /// The lemma a Strong code names, as the corpus writes it: unfoldingWord's <c>c:d:H0776</c> is H776
    /// with two prefixes, its <c>H1254a</c> is H1254 told apart from a homonym, and its Greek
    /// <c>G17220</c> is G1722 with a fifth digit of its own.
    /// </summary>
    internal static string? Lemma(string strong)
    {
        var main = strong[(strong.LastIndexOf(':') + 1)..].TrimEnd(Letters);
        if (main.Length == 6 && main[0] == 'G')
        {
            main = main[..^1];
        }

        return Essenthos.Core.Strong.StrongNumbers.Normalize(main);
    }

    private static readonly char[] Letters = [.. "abcdefghijklmnopqrstuvwxyz"];

    /// <summary>
    /// Every run of consecutive witness words that spells the morphemes in order, each word one or
    /// more morphemes long. The source cuts a pronominal suffix off as a morpheme of its own and BHSA
    /// keeps it on its word - <c>מַלְכוּת⁠וֹ</c> against <c>מַלְכוּתֹו</c> - and that is one word
    /// divided twice, not a word the witness lacks.
    /// </summary>
    private static List<List<int>> Places(
        int count,
        string[] morphemes,
        Func<int, string> form)
    {
        var places = new List<List<int>>();
        for (var start = 0; start < count; start++)
        {
            var used = new List<int>(morphemes.Length);
            if (Spells(count, start, morphemes, 0, form, used))
            {
                places.Add(used);
            }
        }

        return places;
    }

    private static bool Spells(
        int count,
        int at,
        string[] morphemes,
        int from,
        Func<int, string> form,
        List<int> used)
    {
        if (from == morphemes.Length)
        {
            return true;
        }

        if (at >= count)
        {
            return false;
        }

        var word = form(at);
        if (word.Length == 0)
        {
            // BHSA keeps the article a preposition swallowed as a word with nothing printed; it
            // belongs to the word it stands inside, and never starts or ends one.
            if (from == 0)
            {
                return false;
            }

            used.Add(at);
            if (Spells(count, at + 1, morphemes, from, form, used))
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
                if (Spells(count, at + 1, morphemes, through + 1, form, used))
                {
                    return true;
                }

                used.RemoveAt(used.Count - 1);
            }
        }

        return false;
    }

    /// <summary>
    /// The places of a Hebrew word by its letters and cantillation accents. The accents are the
    /// Masoretes' marks of how a word stands in its verse, so two editions of the same text agree on
    /// them far more than on where they print a vowel; the vowels, dagesh and other points are dropped,
    /// and the meteg too unless <paramref name="meteg"/> keeps it. The source counts אוֹ and אֽוֹ as two
    /// spellings, and once the holam is dropped only the meteg tells them apart (Leviticus 25:49).
    /// </summary>
    private static List<List<int>> ByAccents(IReadOnlyList<InterlinearWord> words, string[] morphemes, bool meteg)
    {
        var accented = words.Select(word => Accented(word.Written, meteg)).ToArray();
        return Places(words.Count, [.. morphemes.Select(morpheme => Accented(morpheme, meteg))], at => accented[at]);
    }

    /// <summary>
    /// The letters and accents of a Hebrew word, and its meteg where asked: <see cref="WordFolding.Fold"/>,
    /// keeping U+0591-U+05AF.
    /// </summary>
    private static string Accented(string word, bool meteg)
    {
        var kept = new StringBuilder(word.Length);
        foreach (var character in UnicodeNormalization.Composed(word))
        {
            if (character is <= '\u05AF' or > '\u05C7' || (meteg && character == '\u05BD'))
            {
                kept.Append(character);
            }
        }

        return kept.ToString();
    }

    /// <summary>
    /// A word as the source's count sees it: its printed letters and marks in one normal form, in
    /// lower case, with the several apostrophes an elided Greek word is printed with made one.
    /// </summary>
    private static string Written(string word, string language)
    {
        var normal = UnicodeNormalization.Composed(word);
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
        List<(int Token, int Word)> matched,
        InterlinearJoinAccount account)
    {
        foreach (var (token, word) in matched)
        {
            if (span.WordOccurrences is not { } occurrences || token >= occurrences.Count
                || occurrences[token] is not { Occurrence: > 0, Occurrences: > 0 } stated)
            {
                continue;
            }

            var spelling = span.Words[token];
            var printed = translated.Count(one => one.Written == spelling);
            if (printed != stated.Occurrences)
            {
                continue;
            }

            if (translated[word].Written != spelling)
            {
                continue;
            }

            var at = translated.Take(word + 1).Count(one => one.Written == spelling);
            if (at != stated.Occurrence)
            {
                account.TranslatedWordsOnAnotherOccurrence++;
                account.Example("translated word on another occurrence",
                    $"{where} '{spelling}' stated occurrence {stated.Occurrence}, joined {at}");
            }
        }
    }

    /// <summary>
    /// Where the search for a span's translated words starts: the first word no span has taken, and
    /// what is left unread of the word before it, where the source cut that word into pieces.
    /// </summary>
    private readonly record struct TranslatedCursor(int At, string Rest);

    /// <summary>
    /// The translated words of a span, found in order from the cursor, each with the word of the span
    /// it stands for. Answers an empty list when any one of them is missing, so the caller can leave the
    /// cursor where it was, and says whether the one that was missing is nowhere in the verse or only
    /// behind the cursor.
    ///
    /// The source cuts one printed word into several where its parts render different things or a part
    /// is supplied, and the corpus prints the word whole: <c>bird{s}</c> is <c>bird</c> and <c>s</c>
    /// there and <c>birds</c> here, <c>he-goats</c> is <c>he</c> and <c>goats</c>. A word printed against
    /// the ones after it finds the corpus word they spell together, and each piece after it stands on that
    /// word too. A piece in another span joins the word to that span's original word as well: the corpus
    /// word renders both, as <c>Rehoboth-Ir</c> renders רְחֹבֹת and עִיר.
    ///
    /// Except after an apostrophe: <c>Шім’ї</c>, <c>brother’s</c>. The piece before the apostrophe finds
    /// the corpus word; a piece after it in another span is counted and not joined, because the source
    /// aligns it to a different original word from its stem (the possessive <c>s</c> to the noun in the
    /// construct state, <c>brother’s blood</c>), and the corpus word renders its stem.
    /// </summary>
    private static bool Supplied(AlignmentSpan span, int token) => span.Supplied is { } supplied && token < supplied.Count && supplied[token];

    private static (List<(int Token, int Word)> Words, TranslatedCursor After, int Fragments, InterlinearSpanFailure Failure) FindTranslated(
        IReadOnlyList<InterlinearWord> words,
        AlignmentSpan span,
        string? language,
        TranslatedCursor from)
    {
        var wanted = span.Words;
        var found = new List<(int Token, int Word)>(wanted.Count);
        var at = from.At;
        var rest = from.Rest;
        var fragments = 0;

        for (var token = 0; token < wanted.Count; token++)
        {
            var folded = Apostrophe(WordFolding.Fold(wanted[token], language));
            var glue = span.Glue is { } glues && token < glues.Count ? glues[token] : default;
            var piece = Unpunctuated(folded);
            if (at > 0 && Pieces(words[at - 1]).Skip(1).Contains(folded))
            {
                if (found.Count > 0 && found[^1].Word == at - 1)
                {
                    found.Add((token, at - 1));
                }
                else
                {
                    fragments++;
                }

                rest = rest.StartsWith(piece, StringComparison.Ordinal) ? rest[piece.Length..] : string.Empty;
                continue;
            }

            if (glue.Joined && piece.Length > 0 && rest.StartsWith(piece, StringComparison.Ordinal))
            {
                found.Add((token, at - 1));
                rest = rest[piece.Length..];
                continue;
            }

            var next = -1;
            rest = string.Empty;
            for (var i = at; i < words.Count && next < 0; i++)
            {
                if (Apostrophe(words[i].Folded) == folded || Pieces(words[i]) is [var stem, _, ..] && stem == folded)
                {
                    next = i;
                }
                else if (Spelled(Unpunctuated(Apostrophe(words[i].Folded)), piece, glue.After, language) is { } left)
                {
                    next = i;
                    rest = left;
                }
            }

            if (next < 0)
            {
                return ([], from, 0, words.Any(word => Apostrophe(word.Folded) == folded)
                    ? InterlinearSpanFailure.TranslatedWordBehind
                    : InterlinearSpanFailure.TranslatedWordAbsent);
            }

            found.Add((token, next));
            at = next + 1;
        }

        return found.Count == 0
            ? ([], from, 0, InterlinearSpanFailure.TranslatedWordAbsent)
            : (found, new TranslatedCursor(at, rest), fragments, InterlinearSpanFailure.None);
    }

    /// <summary>
    /// What is left of a corpus word after its first piece, where the piece and some of the words printed
    /// against it after it spell the whole word; null where they do not.
    /// </summary>
    private static string? Spelled(string word, string piece, IReadOnlyList<string>? after, string? language)
    {
        if (after is not { Count: > 0 } || piece.Length == 0 || word.Length <= piece.Length
            || !word.StartsWith(piece, StringComparison.Ordinal))
        {
            return null;
        }

        var spelled = piece;
        foreach (var next in after)
        {
            spelled += Unpunctuated(Apostrophe(WordFolding.Fold(next, language)));
            if (spelled.Length == word.Length && spelled == word)
            {
                return word[piece.Length..];
            }

            if (!word.StartsWith(spelled, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>A word without its punctuation: what two ways of cutting the same printed word agree on.</summary>
    private static string Unpunctuated(string word) =>
        string.Concat(word.Where(character => !char.IsPunctuation(character) && !char.IsSymbol(character) && !char.IsWhiteSpace(character)));

    private static string[] Pieces(InterlinearWord word) =>
        word.Written.ToLowerInvariant().Split(Apostrophes, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// A word with its apostrophes made one, and without the quotation mark, bracket or dash a corpus word
    /// can open with: the corpus keeps <c>“Let</c> and <c>—the</c> as one word where the source names
    /// <c>Let</c> and <c>the</c>.
    /// </summary>
    private static string Apostrophe(string word) =>
        word.TrimStart(Opening).Replace('’', '\'').Replace('ʼ', '\'');

    private static readonly char[] Opening = ['“', '‘', '"', '«', '„', '‚', '(', '[', '{', '—', '–'];

    private static readonly char[] Apostrophes = ['\'', '’', 'ʼ'];
}
