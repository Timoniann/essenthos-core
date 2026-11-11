using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Essenthos.Core.Utils;
using Essenthos.Core.Zefania;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The New Testament correspondences, which **no source states**. They are this loader matching
/// Strong numbers within a verse, so every one carries <c>strong-number</c> and a confidence, and
/// none of them may be mistaken for the Old Testament's, which a file states.
///
/// A tag naming several numbers is one English word standing over a Greek phrase, and the link
/// names every Greek word the tag does. That much the source states; which of them answers which
/// English syllable it does not, and neither does the link.
///
/// Two things reach past a bare number match, and both are labelled apart from it. The concordance
/// numbers Greek by the form and the editions tag it by the lemma, so the dictionary's own
/// derivations are read to join the two — and every redirect is measured against the corpus before
/// it is used. And the tagged edition numbers content words only, so the article, the case endings
/// and the verb's person are recovered from the morphology both editions state, as <c>lexical</c>.
///
/// The tagged edition and the loaded one are two printings of the King James and divide the same
/// letters differently, so they are aligned rather than required to agree word for word. And where
/// the King James prints a word in italics the translators are saying they supplied it, which is
/// written as an absence rather than left as a gap.
///
/// The old loader silenced fifty-five English words and four passages of Matthew to make its
/// coverage look better. That list is deliberately not carried: its passage checks named no book,
/// so they silenced fifteen unintended verses in fourteen other books, and the word list hid 1,144
/// of the 4,057 words nothing matched. An unmatched word is a fact about the corpus and is counted.
/// </summary>
internal static class NewTestamentLinks
{
    internal static string Source(string greekSlug) =>
        $"Zefania KJV+ Strong numbers, matched within the verse against {greekSlug}";

    internal static string RecoveredSource(string greekSlug) =>
        $"the untagged English function words, matched to the morphology {greekSlug} states";

    internal const string SuppliedSource =
        "Zefania KJV+ italics, the King James translators' own mark that they supplied the word";

    internal const string PhraseSource =
        "Zefania KJV+ Strong numbers, the whole list a tag naming a Greek phrase states";

    /// <summary>The last book of the Old Testament, after which this file numbers in Greek.</summary>
    private const int LastOldTestamentBook = 39;

    /// <summary>
    /// How much of a verse the two editions have to write the same way before they are taken to be
    /// the same verse.
    ///
    /// They are two printings of one translation and they spell names differently — Boaz against
    /// Booz, Judea against Judaea, worshiped against worshipped — so demanding every word match
    /// refused 883 verses that are plainly the same verse. Demanding none would accept a verse whose
    /// words merely happen to look alike here and there. Four fifths written identically is the
    /// same verse; the rest of it is aligned on the letters, and what will not align is left
    /// untagged rather than guessed at.
    /// </summary>
    internal const double SameVerse = 0.8;

    internal static IEnumerable<NumberOccurrence> Occurrences(List<VersePair> pairs)
    {
        foreach (var pair in pairs)
        {
            var numbers = pair.Greek
                .Where(word => word.Strong is not null)
                .Select(word => word.Strong!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var number in pair.Tags.SelectMany(tag => tag.Numbers))
            {
                yield return new NumberOccurrence(number, numbers);
            }
        }
    }

    /// <summary>
    /// The links one verse yields: the Strong-number matches, then the words the translators
    /// supplied, then the untagged function words the morphology reaches.
    ///
    /// The matching itself is <see cref="StrongNumberMatch"/>, which Luther 1912 reaches the
    /// originals by as well. What is added here is what only the King James has — italics the
    /// translators printed to mark a word they supplied, and an English function-word matcher.
    /// </summary>
    internal static List<GreekLinkDraft> Build(
        VersePair pair,
        IReadOnlyDictionary<string, NumberRedirect> resolution,
        ref int unmatched,
        ref int resolved,
        ref int phrases)
    {
        var tagged = new List<StrongNumberMatch.TaggedWord>(pair.Tags.Count);
        for (var i = 0; i < pair.Tags.Count; i++)
        {
            tagged.Add(new StrongNumberMatch.TaggedWord(pair.English[i].Id, pair.Tags[i].Numbers));
        }

        var matches = StrongNumberMatch.Verse(
            tagged,
            [.. pair.Greek.Select(word => new StrongNumberMatch.WitnessWord(word.Id, word.Strong))],
            resolution,
            out var tally);

        unmatched += tally.Unmatched;
        resolved += tally.Resolved;
        phrases += tally.Phrases;

        var drafts = new List<GreekLinkDraft>(matches.Count + 8);
        foreach (var match in matches)
        {
            drafts.Add(new GreekLinkDraft(
                match.From,
                match.To,
                match.Confidence,
                match.Kind switch
                {
                    StrongMatchKind.Unambiguous => GreekMatch.Unambiguous,
                    StrongMatchKind.Paired => GreekMatch.Paired,
                    _ => GreekMatch.Contended,
                }));
        }

        var supplied = Supplied(pair);
        foreach (var english in supplied)
        {
            drafts.Add(new GreekLinkDraft([pair.English[english].Id], [], null, GreekMatch.Supplied));
        }

        drafts.AddRange(Recover(pair, drafts, supplied));
        return drafts;
    }

    /// <summary>
    /// The words the translators printed in italics and no number reaches, each as a link with an
    /// English word and no Greek one. It is the source stating an absence, so it carries no
    /// confidence, and it is written before the function-word pass so that pass cannot hand a Greek
    /// word to something the King James says renders nothing.
    /// </summary>
    private static HashSet<int> Supplied(VersePair pair)
    {
        var supplied = new HashSet<int>();
        for (var i = 0; i < pair.Tags.Count; i++)
        {
            if (pair.Tags[i] is { Supplied: true, Numbers.Count: 0 })
            {
                supplied.Add(i);
            }
        }

        return supplied;
    }

    /// <summary>
    /// The untagged English words, given the Greek their phrase's own word states. They hang off the
    /// links already built: a function word is only attached through a content word whose Greek is
    /// a single settled word, so nothing here can widen a set that was already a guess.
    /// </summary>
    private static List<GreekLinkDraft> Recover(
        VersePair pair,
        List<GreekLinkDraft> drafts,
        IReadOnlySet<int> supplied)
    {
        var greekAt = new Dictionary<long, int>(pair.Greek.Count);
        for (var i = 0; i < pair.Greek.Count; i++)
        {
            greekAt[pair.Greek[i].Id] = i;
        }

        var englishAt = new Dictionary<long, int>(pair.English.Count);
        for (var i = 0; i < pair.English.Count; i++)
        {
            englishAt[pair.English[i].Id] = i;
        }

        var anchors = new int[pair.English.Count];
        Array.Fill(anchors, -1);
        var claimed = new HashSet<int>(pair.Greek.Count);

        foreach (var draft in drafts)
        {
            foreach (var word in draft.Greek)
            {
                claimed.Add(greekAt[word]);
            }

            // A phrase link names more than one Greek word and is still settled, because the tag
            // states the whole list rather than leaving a choice — so it anchors on the first of
            // them, as a single settled word does.
            if (draft.Greek.Count != 1 && draft.Kind != GreekMatch.Unambiguous)
            {
                continue;
            }

            foreach (var word in draft.English)
            {
                anchors[englishAt[word]] = greekAt[draft.Greek[0]];
            }
        }

        var matches = GreekFunctionWords.Match(
            [.. pair.English.Select(word => word.Text)],
            [.. pair.Tags.Select(tag => tag.Numbers.Count == 0 ? null : tag.Numbers[0])],
            anchors,
            [.. pair.Greek.Select(word => word.Morphology)],
            claimed,
            supplied);

        return
        [
            .. matches.Select(match => new GreekLinkDraft(
                [pair.English[match.EnglishWord].Id],
                [pair.Greek[match.GreekWord].Id],
                match.Confidence,
                GreekMatch.FunctionWord)),
        ];
    }

    /// <summary>
    /// The tagged edition's words re-divided onto the loaded one's, so that everything downstream
    /// can go on reading the nth tag as belonging to the nth word.
    ///
    /// A span may be several words on either side. Where the tagged edition writes two words for
    /// one — <em>child</em> and <em>'s</em> — the number is the one the pair states, and only one
    /// of them ever carries it. Where it writes one for two — <em>forever</em> against <em>for
    /// ever</em> — both loaded words take that number, which is the truth: they render the one
    /// Greek word between them, and the grouping below turns that into a single link naming both.
    /// A word in no span is untagged, and the function-word pass may still reach it.
    /// </summary>
    internal static List<TaggedWord> Project(List<TaggedWord> tags, int words, List<EditionSpan> spans)
    {
        var projected = new List<TaggedWord>(words);
        for (var i = 0; i < words; i++)
        {
            projected.Add(new TaggedWord(string.Empty, [], false));
        }

        foreach (var span in spans)
        {
            IReadOnlyList<string> numbers = [];
            var supplied = true;
            for (var i = span.TaggedFrom; i < span.TaggedTo; i++)
            {
                if (numbers.Count == 0)
                {
                    numbers = tags[i].Numbers;
                }

                supplied &= tags[i].Supplied;
            }

            for (var i = span.CorpusFrom; i < span.CorpusTo; i++)
            {
                projected[i] = new TaggedWord(tags[span.TaggedFrom].Text, numbers, supplied);
            }
        }

        return projected;
    }

    internal static Dictionary<(int, int, int), List<TaggedWord>> Tagged(string path)
    {
        var bible = new ZefaniaParser().Parse(File.ReadAllText(path));
        var verses = new Dictionary<(int, int, int), List<TaggedWord>>(8_000);

        foreach (var book in bible.Books)
        {
            var canonical = BibleBookAbbreviation.GetAbbreviation(book.ShortName)
                            ?? BibleBookAbbreviation.GetByOrdinal(book.Number);

            // The file numbers its Old Testament in Hebrew and its New Testament in Greek, and the
            // tag itself says which of the two only by where it stands.
            if (canonical is null || canonical.Ordinal <= LastOldTestamentBook)
            {
                continue;
            }

            foreach (var chapter in book.Chapters)
            {
                foreach (var verse in chapter.Verses)
                {
                    verses[(canonical.Ordinal, chapter.Number, verse.Number)] = verse.Words
                        .Select(word => new TaggedWord(
                            word.Text, StrongTags.Read(word.StrongNo, StrongNumbers.Greek), word.Italic))
                        .ToList();
                }
            }
        }

        return verses;
    }

    /// <param name="Supplied">
    /// Whether the King James prints this word in italics. The translators mark that way what they
    /// added to make English of the Greek, so it is the source saying no Greek word stands behind
    /// it — 4,175 words of the New Testament, 71 of which the same file also numbers, and where the
    /// two disagree the number is the more specific claim and wins.
    /// </param>
    /// <param name="Numbers">
    /// The Strong numbers the tag names, which is usually one, sometimes none, and 2,725 times in
    /// the New Testament several — one English word standing over a Greek phrase.
    /// </param>
    internal sealed record TaggedWord(string Text, IReadOnlyList<string> Numbers, bool Supplied);

    internal sealed record Word(long Id, string Text, string? Strong, GreekMorphology Morphology);

    /// <summary>A verse the tagged edition and the corpus agree on, with both texts' words beside it.</summary>
    internal sealed record VersePair(List<TaggedWord> Tags, List<Word> English, List<Word> Greek);

    /// <summary>What joined the two sides, so the load can report each kind apart from the others.</summary>
    internal enum GreekMatch
    {
        Unambiguous,
        Paired,
        Contended,
        FunctionWord,
        Supplied,
    }

    /// <param name="Confidence">
    /// How sure the pairing is, or null where nothing was inferred — a supplied word's absence is
    /// stated by the source, and a number on it would invite the reader to weigh a claim that was
    /// never a guess.
    /// </param>
    internal sealed record GreekLinkDraft(List<long> English, List<long> Greek, double? Confidence, GreekMatch Kind);
}
