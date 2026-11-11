using System.Globalization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Old Testament correspondences, from the file that states them.
///
/// Two joins have to hold before a single link is written, and both are checked rather than
/// assumed: the file's Hebrew words against BHSA's, verified by the glosses both carry, and the
/// file's English words against the King James as loaded. A verse where either fails is refused and
/// counted, because a link is a scholarly claim and one built on a misalignment is worse than none.
/// </summary>
internal static class OldTestamentLinks
{
    /// <summary>
    /// Who says so, not where the bytes are.
    ///
    /// This was the filename. `link.source` is the column a reader is answered with when they ask
    /// where a claim came from, and answering with a path names nobody — while the Ukrainian
    /// interlinear beside it in the same column has always named its project and its licence. The
    /// file is CC BY-NC 4.0 and requires attribution, and for the corpus's most cited source there
    /// was none anywhere: not here, not in the resource folder, not in the store.
    ///
    /// NonCommercial is not a new constraint — BHSA, which this maps onto, is non-commercial
    /// itself. Attribution was the term being broken.
    /// </summary>
    internal const string Source =
        "Open Hebrew Bible Project by Eliran Wong, github.com/eliranwong/OpenHebrewBible, CC BY-NC 4.0";

    /// <summary>
    /// The second source, where it reaches: STEPBible's TAHOT, which says which morphemes are
    /// prefixes and what each one means where it stands. It states nothing about the King James, so
    /// it never makes a link <c>stated-by-source</c> — it is a second, independent answer to what
    /// the Hebrew says, and the claim it writes records that rather than upgrading the link.
    /// </summary>
    internal const string StatedSource =
        Sources.TahotPrefix + ", www.STEPBible.org, based on work at Tyndale House Cambridge, CC BY-NC 3.0";

    /// <summary>What the second claim asserts, as against what the link's own claim asserts.</summary>
    internal const string StatedNote =
        "the gloss TAHOT prints for this morpheme where it stands";

    /// <summary>
    /// How much of a verse's Hebrew the file and BHSA have to gloss the same way before the two are
    /// taken to be the same words in the same order.
    ///
    /// The file's glosses come from an older revision of the same ETCBC vocabulary, so a correct
    /// verse is not always a perfect match: <em>Kittim</em> became <em>Cypriot</em>, <em>cloth</em>
    /// became <em>clothe</em>, <em>lefthand side</em> gained a hyphen.
    ///
    /// <para>
    /// The line is where it is because the data puts a gap there. Measured over all 23,021 verses
    /// whose counts agree:
    /// </para>
    /// <code>
    ///   0.077  1 Samuel 20:42     3/39   different division
    ///   0.097  1 Kings 22:43      3/31   different division
    ///   0.227  1 Chronicles 12:4  5/22   different division
    ///   0.579  Numbers 26:1      11/19   different division
    ///   ————————————————————————————————— the gap
    ///   0.667  Genesis 10:4       6/9    revision drift
    ///   0.667  1 Chronicles 1:7   6/9    revision drift
    ///   0.750  …and 605 more, none of them wrong
    /// </code>
    /// <para>
    /// So it separates cleanly, and only here: at 0.8 it also refuses five correct verses, at 0.9
    /// eighty, at 1.0 six hundred and eleven. Raising it does not buy strictness, it buys the loss
    /// of verses whose only fault is that a dictionary was revised — and the four it must catch are
    /// already three times below the nearest of them.
    /// </para>
    /// </summary>
    internal const double SameVerse = 0.6;

    /// <summary>
    /// How much of a verse's Hebrew the running numbers must reach before the verse is read through
    /// them. Below it the file's verse is not the stretch of BHSA its numbers name, and nothing is
    /// written; above it the entries left over are words a revision split or merged, and only the
    /// English rendering them stays unlinked.
    /// </summary>
    private const double MostlyReached = 0.8;

    /// <summary>
    /// Where the frame puts a psalm's superscription, which BHSA numbers as its first verse and the
    /// King James does not number at all. The file follows the King James and counts the
    /// superscription's Hebrew into verse 1.
    /// </summary>
    private const int Superscription = 0;

    private const int FirstVerse = 1;

    /// <summary>
    /// The BHSA word at each running number: the words a joined verse placed, and the stretches
    /// between them numbered by the same reading as the Strong numbers are, with each entry's own
    /// running number standing where its Strong number would.
    /// </summary>
    internal static Dictionary<int, Word> WordsByNumber(
        IReadOnlyList<MappingRecord> records,
        Dictionary<(int, int, int), List<Word>> hebrewVerses,
        Dictionary<long, int> placed) =>
        WordsByNumber(records, hebrewVerses, placed, out _);

    /// <param name="split">
    /// The numbers no word took that stand for a word BHSA writes as two or three (<see cref="Split"/>):
    /// the first of them, carrying the others in <see cref="Word.Also"/>. Kept apart because a number
    /// is a Strong number's place, and neither half is the whole word the number names.
    /// </param>
    internal static Dictionary<int, Word> WordsByNumber(
        IReadOnlyList<MappingRecord> records,
        Dictionary<(int, int, int), List<Word>> hebrewVerses,
        Dictionary<long, int> placed,
        out Dictionary<int, Word> split)
    {
        var numbers = new Dictionary<int, (string Strong, string Gloss)>(430_000);
        foreach (var entry in records.SelectMany(record => record.Hebrew))
        {
            numbers.TryAdd(entry.Position, (entry.Position.ToString(CultureInfo.InvariantCulture), entry.Gloss));
        }

        var inTextOrder = hebrewVerses.Values
            .SelectMany(words => words)
            .OrderBy(word => word.Id)
            .ToList();
        var byId = inTextOrder.ToDictionary(word => word.Id);
        var byNumber = new Dictionary<int, Word>(inTextOrder.Count);
        foreach (var (wordId, number) in placed)
        {
            byNumber.TryAdd(number, byId[wordId]);
        }

        foreach (var (wordId, number) in RunningWordNumbers.Between(
                     [.. inTextOrder.Select(word => (word.Id, word.Gloss))], placed, numbers, SameVerse))
        {
            byNumber.TryAdd(int.Parse(number, CultureInfo.InvariantCulture), byId[wordId]);
        }

        split = Split(numbers.Keys, byNumber, inTextOrder);
        return byNumber;
    }

    /// <summary>The most words of BHSA one entry of the file is read as, where a revision split it.</summary>
    private const int SplitInto = 3;

    /// <summary>
    /// A number no word took whose neighbours did, with only words no number took between them: one
    /// word of the file that BHSA writes as two or three, Aram-naharaim as אֲרַם נַהֲרַיִם. The glosses
    /// cannot pair it with either half, so the running numbers leave it without a word, but it is
    /// those words together and nothing else, and the link of its English names them all. A single
    /// word between, or more than three, is not a split and stays without one.
    /// </summary>
    private static Dictionary<int, Word> Split(
        IEnumerable<int> numbers,
        Dictionary<int, Word> byNumber,
        List<Word> inTextOrder)
    {
        var ordinal = new Dictionary<long, int>(inTextOrder.Count);
        for (var i = 0; i < inTextOrder.Count; i++)
        {
            ordinal[inTextOrder[i].Id] = i;
        }

        var taken = byNumber.Values.Select(word => word.Id).ToHashSet();
        var split = new Dictionary<int, Word>();
        foreach (var number in numbers)
        {
            if (byNumber.ContainsKey(number)
                || !byNumber.TryGetValue(number - 1, out var before)
                || !byNumber.TryGetValue(number + 1, out var after))
            {
                continue;
            }

            var from = ordinal[before.Id] + 1;
            var count = ordinal[after.Id] - from;
            if (count is < 2 or > SplitInto
                || inTextOrder.Skip(from).Take(count).Any(word => taken.Contains(word.Id)))
            {
                continue;
            }

            split[number] = inTextOrder[from] with
            {
                Also = [.. inTextOrder.Skip(from + 1).Take(count - 1).Select(word => word.Id)],
            };
        }

        return split;
    }

    /// <summary>
    /// A verse's Hebrew read through the running numbers, one word or none per entry: a verse the file
    /// and BHSA divide differently, a word the file lists twice under one number because it gives it
    /// two Strong numbers, a verse of the file that runs into the next one of BHSA, and a word BHSA
    /// splits, read as its first half carrying the rest (<see cref="Split"/>). Null where too little
    /// of it is reached to be the verse the file means.
    /// </summary>
    internal static List<Word?>? ByRunningNumber(
        MappingRecord record,
        Dictionary<int, Word> byNumber,
        Dictionary<int, Word> split)
    {
        var words = record.Hebrew
            .Select(entry => byNumber.GetValueOrDefault(entry.Position) ?? split.GetValueOrDefault(entry.Position))
            .ToList();
        var reached = words.Count(word => word is not null);
        return reached > 0 && reached >= record.Hebrew.Count * MostlyReached ? words : null;
    }

    /// <summary>
    /// The BHSA words the file's Hebrew for this verse lines up with, or null where it does not.
    ///
    /// The join is positional within the verse and checked against the glosses BHSA carries, so a
    /// verse the two sides divide differently is refused even when the counts agree. It runs before
    /// the English join, so the count of refusals for glosses means what it says. A verse whose file
    /// numbers two entries as one word is never joined by position
    /// (<see cref="NumbersTwoEntriesAsOneWord"/>): its count can agree with BHSA's only by accident,
    /// and is read through the running numbers, its links, its Strong numbers and the running
    /// numbers the other mappings borrow alike.
    /// </summary>
    internal static List<Word>? JoinHebrew(
        MappingRecord record,
        Dictionary<(int, int, int), List<Word>> hebrewVerses,
        out GlossAgreement glosses)
    {
        glosses = default;
        return NumbersTwoEntriesAsOneWord(record) ? null : ByPosition(record, hebrewVerses, out glosses);
    }

    /// <summary>
    /// The positional join alone, whatever the file numbers: what <see cref="JoinHebrew"/> reads, and
    /// what loads before it read for a verse whose file numbers two entries as one word.
    /// </summary>
    private static List<Word>? ByPosition(
        MappingRecord record,
        Dictionary<(int, int, int), List<Word>> hebrewVerses,
        out GlossAgreement glosses)
    {
        glosses = default;
        hebrewVerses.TryGetValue((record.Book, record.Chapter, record.Verse), out var bhsa);
        if (bhsa?.Count != record.Hebrew.Count &&
            record.Verse == FirstVerse &&
            hebrewVerses.TryGetValue((record.Book, record.Chapter, Superscription), out var superscription))
        {
            bhsa = [.. superscription, .. bhsa ?? []];
        }

        if (bhsa is null || bhsa.Count != record.Hebrew.Count)
        {
            return null;
        }

        glosses = Glosses.Agreement(
            [.. record.Hebrew.Select(entry => (string?)entry.Gloss)],
            [.. bhsa.Select(word => word.Gloss)]);
        return glosses.Share < SameVerse ? null : bhsa;
    }

    internal static void Place(MappingRecord record, List<Word> bhsa, Dictionary<long, int> placed)
    {
        for (var i = 0; i < record.Hebrew.Count; i++)
        {
            placed[bhsa[i].Id] = record.Hebrew[i].Position;
        }
    }

    /// <summary>
    /// Whether the file gives one running word number to two of its entries — a word BHSA writes as
    /// one, such as <em>the Valley of Salt</em>, under each Strong number it is made of. Its entries
    /// are then not one per word, and a count that agrees with BHSA's agrees by accident: Psalm 60's
    /// title writes the valley twice and Aram-naharaim once where BHSA writes the reverse, so every
    /// word between them took the number of the word before it, and every English phrase between
    /// them the link of the word before its own. Such a verse is numbered and linked by the running
    /// numbers and the glosses instead (<see cref="RunningWordNumbers"/>).
    /// </summary>
    private static bool NumbersTwoEntriesAsOneWord(MappingRecord record) =>
        record.Hebrew.Select(entry => entry.Position).Distinct().Count() < record.Hebrew.Count;

    internal static LinkRelation RelationOf(LinkDraft draft) => draft.Omitted ? LinkRelation.Omits : draft.Relation;

    /// <summary>
    /// Lines the file's English up with the King James as loaded, over a verse whose Hebrew has
    /// already been read, and refuses rather than guessing when it cannot (<see cref="EnglishWordJoin"/>).
    /// A Hebrew entry with no word is one BHSA divides otherwise, and the English rendering it is
    /// left unlinked rather than given a neighbour's word.
    /// </summary>
    internal static List<LinkDraft>? Build(
        MappingRecord record,
        List<Word> kjv,
        IReadOnlyList<Word?> bhsa,
        IReadOnlyDictionary<int, TahotMorpheme>? stated)
    {
        var fileWords = record.English.SelectMany(segment => segment.Words).ToList();
        if (EnglishWordJoin.Of([.. fileWords.Select(word => word.Text)], [.. kjv.Select(word => word.Text)])
            is not { } joined)
        {
            return null;
        }

        // A loaded word two file words are written as belongs to the first of them.
        var claimed = new HashSet<int>();
        List<long> Loaded(IEnumerable<int> file) =>
            [.. file.SelectMany(i => joined[i]).Where(claimed.Add).Select(k => kjv[k].Id)];

        var byPosition = new Dictionary<int, int>(record.Hebrew.Count);
        for (var i = 0; i < record.Hebrew.Count; i++)
        {
            byPosition.TryAdd(record.Hebrew[i].Position, i);
        }

        // The file marks content words only, so the function words in a phrase would otherwise be
        // claimed by the one word it marks. What they actually render is recovered first, and they
        // leave the phrase for links of their own.
        var prefixes = HebrewPrefixes.Match(record.Hebrew, record.English, stated)
            .ToDictionary(match => match.EnglishWord, match => match);

        // And a word the King James prints in italics renders nothing at all: the translators are
        // saying they supplied it. It leaves its phrase too, for a link with an empty Hebrew side —
        // which is what `expands` is for, and is a statement rather than a gap.
        var supplied = new HashSet<int>();
        for (var i = 0; i < fileWords.Count; i++)
        {
            if (fileWords[i].Supplied && !prefixes.ContainsKey(i))
            {
                supplied.Add(i);
            }
        }

        var drafts = new List<LinkDraft>(record.English.Count + prefixes.Count + supplied.Count);
        foreach (var (english, match) in prefixes)
        {
            if (bhsa[byPosition[match.HebrewPosition]] is { } prefix && Loaded([english]) is { Count: > 0 } words)
            {
                drafts.Add(new LinkDraft(words, [prefix.Id], match.Confidence) { Stated = match.Stated });
            }
        }

        foreach (var english in supplied)
        {
            if (Loaded([english]) is { Count: > 0 } words)
            {
                drafts.Add(new LinkDraft(words, []) { Relation = LinkRelation.Expands });
            }
        }

        var segments = new List<(EnglishSegment Segment, List<int> Own)>(record.English.Count);
        var read = 0;
        foreach (var segment in record.English)
        {
            segments.Add((segment, [
                .. Enumerable.Range(read, segment.Words.Count)
                    .Where(i => !prefixes.ContainsKey(i) && !supplied.Contains(i)),
            ]));
            read += segment.Words.Count;
        }

        // A word the file lists twice under one number has two markers, and only one of them carries
        // English. The bare one says nothing the other does not: it is neither the rest of the phrase
        // before it nor a word the English leaves unrendered.
        var rendered = segments
            .Where(s => s.Own.Count > 0 && s.Segment.RendersHebrew is not null
                        && byPosition.ContainsKey(s.Segment.RendersHebrew.Position))
            .Select(s => bhsa[byPosition[s.Segment.RendersHebrew!.Position]]?.Id)
            .ToHashSet();

        foreach (var (segment, own) in segments)
        {
            if (segment.RendersHebrew is null || !byPosition.TryGetValue(segment.RendersHebrew.Position, out var index)
                || bhsa[index] is not { } hebrewWord
                || own.Count > 0 && own.All(i => joined[i].Length == 0))
            {
                continue;
            }

            var words = Loaded(own);
            var position = record.Hebrew[index].Position;

            if (words.Count == 0 && own.Count > 0)
            {
                // Its English is one loaded word with the phrase before it -- "days'journey" for the
                // file's "days journey" -- and that word renders this Hebrew word too.
                var shared = kjv[own.SelectMany(i => joined[i]).First()].Id;
                if (drafts.FirstOrDefault(draft => draft.English.Contains(shared)) is { } holder
                    && !holder.Hebrew.Contains(hebrewWord.Id))
                {
                    holder.Hebrew.Add(hebrewWord.Id);
                }

                continue;
            }

            if (words.Count == 0 && rendered.Contains(hebrewWord.Id))
            {
                continue;
            }

            if (words.Count == 0)
            {
                // A marker with no English text of its own says one of two things, and which one
                // depends on where its Hebrew word stands. Next to the word the phrase before it
                // already names, it is the rest of that phrase's Hebrew: מן plus פשע are "for our
                // transgressions", one link naming both, and never two links each claiming the whole
                // phrase. Anywhere else it is a Hebrew word the English does not render at all — the
                // object marker את has no English word, and saying that "created" renders it would
                // be a claim about the wrong word.
                if (drafts.Count > 0 && drafts[^1].LastHebrewPosition == position - 1)
                {
                    drafts[^1].Hebrew.Add(hebrewWord.Id);
                    drafts[^1].LastHebrewPosition = position;
                    continue;
                }

                drafts.Add(new LinkDraft([], [hebrewWord.Id]) { LastHebrewPosition = position, Omitted = true });
                continue;
            }

            drafts.Add(new LinkDraft(words, [hebrewWord.Id]) { LastHebrewPosition = position });
        }

        // A word BHSA splits is rendered by whatever renders its first half.
        foreach (var split in bhsa.OfType<Word>().Where(word => word.Also.Count > 0))
        {
            foreach (var draft in drafts.Where(draft => draft.Hebrew.Contains(split.Id)))
            {
                draft.Hebrew.AddRange(split.Also.Where(id => !draft.Hebrew.Contains(id)));
            }
        }

        return drafts;
    }

    /// <param name="Also">
    /// The rest of a word BHSA writes as several where the file writes it as one (<see cref="Split"/>),
    /// which every link of this word names too. Empty for every other word.
    /// </param>
    internal sealed record Word(long Id, string Text, string? Gloss)
    {
        public IReadOnlyList<long> Also { get; init; } = [];
    }

    /// <param name="Omitted">
    /// The King James renders nothing here, so the link says so with an empty side rather than
    /// attaching the Hebrew word to whatever phrase happened to precede it.
    /// </param>
    /// <param name="Confidence">
    /// Null for everything the file states, and set for the function words matched to their
    /// prefixes, so that the two can never be read as the same kind of claim.
    /// </param>
    internal sealed record LinkDraft(List<long> English, List<long> Hebrew, double? Confidence = null)
    {
        public int LastHebrewPosition { get; set; }

        /// <summary>
        /// Whether a second source states what this morpheme means where it stands. It does not
        /// change what the link is — the pairing is still inferred — it earns the link a second
        /// claim, so that the agreement measure can see two sources standing behind it.
        /// </summary>
        public bool Stated { get; init; }

        public bool Omitted { get; init; }

        /// <summary>
        /// What the link says. Most say the English renders the Hebrew; a marker with no English
        /// says the Hebrew is unrendered, and an italic word says the English was supplied.
        /// </summary>
        public LinkRelation Relation { get; init; } = LinkRelation.Renders;
    }
}
