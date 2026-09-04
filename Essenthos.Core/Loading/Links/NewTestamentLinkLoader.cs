using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Essenthos.Core.Utils;
using Essenthos.Core.Zefania;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Unmatched">
/// English words carrying a Strong number that no Greek word in their verse carries, and that the
/// dictionary could not send anywhere either. Counted and **not** written as anything: the King
/// James may be rendering a longer Greek text than this corpus holds, or the match may simply have
/// failed, and nothing here can tell those apart. Saying <c>expands</c> would assert the first.
/// Loading the Textus Receptus is what settles it.
/// </param>
/// <param name="Resolved">
/// English words whose own number matched nothing and whose lemma the dictionary named — the tagged
/// edition's G2076 ἐστί against the edition's G1510 εἰμί. They are still <c>strong-number</c> links
/// and they carry a lower confidence, because the number that joined them was inferred rather than
/// written on both sides.
/// </param>
/// <param name="Recovered">
/// Untagged English words given a Greek word from the morphology — the article, a case ending, a
/// verb's person. These are the 31.5% of the New Testament that carries no Strong number at all,
/// and they are <c>lexical</c>, never <c>strong-number</c>.
/// </param>
/// <param name="StrongNumbers">
/// English words given the Strong number the tagged edition puts on them. It is the evidence these
/// links were built from, and a corpus that keeps the conclusion and throws the evidence away
/// cannot afterwards be asked whether the conclusion follows.
/// </param>
/// <param name="Phrases">
/// English words whose tag names more than one Greek word — <c>1223 5124</c> for διὰ τοῦτο,
/// written <em>therefore</em>. The tag states the whole list, so the link names every one of them.
/// </param>
/// <param name="Supplied">
/// English words the King James prints in italics, which is the translators saying they put the
/// word there and the Greek does not have it. They are written as <c>expands</c> with no Greek
/// word at all — an absence the source states, which is a different fact from a word the matcher
/// failed on, and the only kind of claim about the New Testament here that rests on a source
/// rather than on an inference.
/// </param>
/// <param name="Redivided">
/// Verses the two editions do not write word for word — a different spelling, or the same letters
/// divided differently. They are aligned rather than refused, and this counts how often that was
/// needed.
/// </param>
internal sealed record GreekLinkOutcome(
    bool AlreadyLoaded,
    int Verses,
    int Refused,
    int Links,
    int Unambiguous,
    int Contended,
    int Unmatched,
    int Resolved,
    int Redirects,
    int Recovered,
    int Redivided,
    int StrongNumbers,
    int Phrases,
    int Supplied,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the New Testament links are already loaded"
            : $"{Links} links over {Verses} verses in {Elapsed}: {Unambiguous} where each number the tag names " +
              $"was written once on each side, {Contended} where more than one word carried it, {Resolved} English " +
              $"words matched through the lemma the dictionary names for their form over {Redirects} numbers it " +
              $"resolved, {Recovered} untagged English words given a Greek word by its morphology, {Supplied} " +
              $"English words the translators printed in italics and no Greek word stands behind, {Unmatched} " +
              $"English words whose number no Greek word in the verse carries, {Redivided} verses the two " +
              $"editions do not write word for word, {StrongNumbers} English words given the number the " +
              $"tagged edition states, {Phrases} of them a phrase of several Greek words, {Refused} verses refused";
}

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
internal sealed class NewTestamentLinkLoader(AppDbContext db, ILogger<NewTestamentLinkLoader> logger)
{
    private static string Source(string greekSlug) =>
        $"Zefania KJV+ Strong numbers, matched within the verse against {greekSlug}";

    private static string RecoveredSource(string greekSlug) =>
        $"the untagged English function words, matched to the morphology {greekSlug} states";

    private const string SuppliedSource =
        "Zefania KJV+ italics, the King James translators' own mark that they supplied the word";

    private const string PhraseSource =
        "Zefania KJV+ Strong numbers, the whole list a tag naming a Greek phrase states";

    /// <summary>The last book of the Old Testament, after which this file numbers in Greek.</summary>
    private const int LastOldTestamentBook = 39;

    /// <summary>
    /// One English word and one Greek word in the verse carry the number. The correspondence is
    /// still inferred — the number is right and the occurrence could still be another — so it is
    /// high rather than certain.
    /// </summary>
    private const double Unambiguous = 0.9;

    /// <summary>One side has more than one candidate, so which pairs with which is a guess.</summary>
    private const double OneSideContended = 0.5;

    private const double BothSidesContended = 0.3;

    /// <summary>
    /// The same number as many times on one side as the other, paired in the order both texts write
    /// them. It is an assumption on top of an inference, so it sits below an unambiguous match —
    /// but well above a set naming every candidate, because for a word repeated identically any
    /// bijection reads the same to a reader and order is the one both texts agree on.
    /// </summary>
    private const double PairedInOrder = 0.7;

    /// <summary>
    /// Deducted wherever the two numbers were joined by the dictionary rather than written on both
    /// sides. Every tier loses the same amount, because what the redirect adds is the same
    /// everywhere: one more inference between the link and the two texts that state it.
    /// </summary>
    private const double ResolvedNumber = 0.1;

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
    private const double SameVerse = 0.8;

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    private const string StrongNumberTable =
        """
        CREATE TEMP TABLE tagged_strong (word_id bigint, strong_number text, position int) ON COMMIT DROP;
        """;

    private const string StrongNumberUpdate =
        """
        UPDATE word SET strong_number = s.strong_number
        FROM tagged_strong s
        WHERE word.id = s.word_id AND s.position = 0
          AND word.strong_number IS DISTINCT FROM s.strong_number
        """;

    /// <summary>
    /// Every number of a word whose tag names more than one. <c>word.strong_number</c> holds the
    /// first because a column holds one, and the rest would otherwise be the half of the source's
    /// statement the corpus threw away. They are <c>stated-by-source</c> and carry no confidence:
    /// nothing was inferred, the file writes the list.
    /// </summary>
    private const string PhraseNumbers =
        """
        INSERT INTO word_strong (word_id, number, method, confidence, source)
        SELECT DISTINCT s.word_id, s.strong_number, @method, NULL::double precision, @source
        FROM tagged_strong s
        WHERE s.word_id IN (SELECT word_id FROM tagged_strong GROUP BY word_id HAVING count(*) > 1)
        ON CONFLICT DO NOTHING
        """;

    /// <param name="greekSlug">
    /// Which Greek witness to match against. The King James renders the Textus Receptus and is
    /// matched to Nestle 1904 as well, because the difference between what it reaches in each is
    /// the evidence of which text it followed — and that evidence is ours, derived from our own
    /// data, needing no licence and no outside claim.
    /// </param>
    public async Task<GreekLinkOutcome> Load(
        string zefaniaPath,
        string greekSlug,
        CancellationToken cancellationToken = default)
    {
        var english = await db.Texts.SingleOrDefaultAsync(t => t.Slug == "kjv", cancellationToken);
        var greek = await db.Texts.SingleOrDefaultAsync(t => t.Slug == greekSlug, cancellationToken);
        if (english is null || greek is null)
        {
            throw new InvalidOperationException(
                $"The King James and \"{greekSlug}\" must both be loaded before the correspondences between them " +
                "can be. Load the texts first; this reads them, it does not create them.");
        }

        if (await db.Links.AnyAsync(l => l.FromTextId == english.Id && l.ToTextId == greek.Id, cancellationToken))
        {
            logger.LogInformation("The New Testament links are already loaded; nothing to do");
            return new GreekLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var tagged = Tagged(zefaniaPath);
        var englishVerses = await VerseWords(english.Id, cancellationToken);
        var greekVerses = await VerseWords(greek.Id, cancellationToken);

        var refused = 0;
        var redivided = 0;
        var pairs = new List<VersePair>(8_000);

        foreach (var (address, tags) in tagged)
        {
            if (!englishVerses.TryGetValue(address, out var kjv) ||
                !greekVerses.TryGetValue(address, out var witness))
            {
                continue;
            }

            var spans = TaggedEdition.Align(
                [.. tags.Select(tag => tag.Text)],
                [.. kjv.Select(word => word.Text)]);
            if (TaggedEdition.Agreement(spans, kjv.Count) < SameVerse)
            {
                refused++;
                continue;
            }

            if (spans.Count != kjv.Count || spans.Any(span => span.Match != EditionMatch.Identical))
            {
                redivided++;
            }

            pairs.Add(new VersePair(Project(tags, kjv.Count, spans), kjv, witness));
        }

        var resolution = await Resolution(pairs, cancellationToken);

        var drafts = new List<GreekLinkDraft>(300_000);
        var stated = new List<(long WordId, string Strong)>(140_000);
        var unmatched = 0;
        var resolved = 0;
        var phrases = 0;

        foreach (var pair in pairs)
        {
            drafts.AddRange(Build(pair, resolution, ref unmatched, ref resolved, ref phrases));

            for (var i = 0; i < pair.Tags.Count; i++)
            {
                foreach (var number in pair.Tags[i].Numbers)
                {
                    stated.Add((pair.English[i].Id, number));
                }
            }
        }

        await Write(english.Id, greek.Id, greekSlug, drafts, stated, cancellationToken);

        var outcome = new GreekLinkOutcome(
            false,
            pairs.Count,
            refused,
            drafts.Count,
            drafts.Count(d => d.Kind == GreekMatch.Unambiguous),
            drafts.Count(d => d.Kind == GreekMatch.Contended),
            unmatched,
            resolved,
            resolution.Count,
            drafts.Count(d => d.Kind == GreekMatch.FunctionWord),
            redivided,
            stated.DistinctBy(pair => pair.WordId).Count(),
            phrases,
            drafts.Count(d => d.Kind == GreekMatch.Supplied),
            started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The numbers the dictionary can join to the ones this Greek witness writes, kept only where
    /// the verses bear the join out. It has to run over the whole New Testament before a single
    /// link is written, because a redirect is admitted on how often it explains a failure and one
    /// verse cannot say.
    /// </summary>
    private async Task<Dictionary<string, NumberRedirect>> Resolution(
        List<VersePair> pairs,
        CancellationToken cancellationToken)
    {
        var dictionary = await db.StrongEntries
            .Where(entry => entry.StrongNumber.StartsWith("G"))
            .Select(entry => new GreekEntry(entry.StrongNumber, entry.Lemma, entry.Derivation))
            .ToListAsync(cancellationToken);

        var attested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in pairs.SelectMany(pair => pair.Greek).Where(word => word.Strong is not null))
        {
            attested.Add(word.Strong!);
        }

        return GreekNumberResolution.Admit(dictionary, attested, Occurrences(pairs));
    }

    private static IEnumerable<NumberOccurrence> Occurrences(List<VersePair> pairs)
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
    /// One link per effective set of Strong numbers per verse, naming every English word that
    /// carries it and every Greek word that does. Where several English words render one Greek word
    /// — 995 times in the New Testament against 30 in the whole Old — that is one claim about a set,
    /// not several claims each pretending to be about a pair.
    ///
    /// The effective number is the tagged edition's own where the Greek writes it, and the one the
    /// dictionary resolves it to otherwise. Grouping on it rather than on the tag is what lets a
    /// verse's ἐστί and its εἰμί arrive at the same link instead of at two claiming the same word.
    /// A tag naming several numbers is grouped under all of them together, which keeps a phrase
    /// apart from the words that carry one of its numbers alone.
    /// </summary>
    private static List<GreekLinkDraft> Build(
        VersePair pair,
        IReadOnlyDictionary<string, NumberRedirect> resolution,
        ref int unmatched,
        ref int resolved,
        ref int phrases)
    {
        var byNumber = pair.Greek
            .Where(word => word.Strong is not null)
            .GroupBy(word => word.Strong!)
            .ToDictionary(group => group.Key, group => group.Select(word => word.Id).ToList(), StringComparer.Ordinal);

        var order = new List<string>(16);
        var groups = new Dictionary<string, Group>(16, StringComparer.Ordinal);

        for (var i = 0; i < pair.Tags.Count; i++)
        {
            var numbers = pair.Tags[i].Numbers;
            if (numbers.Count == 0)
            {
                continue;
            }

            if (Reach(byNumber, resolution, numbers) is not { } reached)
            {
                unmatched++;
                continue;
            }

            Collect(order, groups, reached, pair.English[i].Id);
            if (reached.Resolved)
            {
                resolved++;
            }

            if (reached.Numbers > 1)
            {
                phrases++;
            }
        }

        var drafts = new List<GreekLinkDraft>(order.Count + 8);
        foreach (var key in order)
        {
            var group = groups[key];

            // A set naming every candidate on both sides is a true claim and a useless one. Matthew
            // 1:4 has three "and" against three δέ, and one link naming all six makes the reader
            // light the whole verse when a single word is touched — which says the corpus cannot
            // tell them apart, when in fact both texts write them in the same order.
            //
            // Where the counts agree the words are paired in that order, one link each. Where they
            // do not, nothing here can choose, and the set stands. A phrase is never paired this
            // way: two English words tagged with the same two Greek words each render both of them,
            // and pairing them off would split one stated claim into two invented ones.
            if (group.Numbers == 1 && group.English.Count == group.Greek.Count && group.English.Count > 1)
            {
                for (var at = 0; at < group.English.Count; at++)
                {
                    drafts.Add(new GreekLinkDraft(
                        [group.English[at]],
                        [group.Greek[at]],
                        Lower(PairedInOrder, group.Resolved),
                        GreekMatch.Paired));
                }

                continue;
            }

            var settled = group.English.Count == 1 && group.Greek.Count == group.Numbers;
            drafts.Add(new GreekLinkDraft(
                group.English,
                group.Greek,
                Confidence(settled, group.English.Count, group.Greek.Count, group.Resolved),
                settled ? GreekMatch.Unambiguous : GreekMatch.Contended));
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
    /// The Greek words carrying every one of these numbers, or null where the verse is missing any
    /// of them. A phrase entry — G3364 for οὐ μή — names two words and is a claim about both.
    /// </summary>
    private static List<long>? Together(
        Dictionary<string, List<long>> byNumber,
        IReadOnlyList<string> numbers)
    {
        var words = new List<long>(numbers.Count);
        foreach (var number in numbers)
        {
            if (!byNumber.TryGetValue(number, out var carrying))
            {
                return null;
            }

            words.AddRange(carrying);
        }

        return words;
    }

    /// <summary>
    /// The Greek words one tag's numbers reach, and the key they group under.
    ///
    /// A tag naming several numbers is the source stating a phrase — <c>1223 5124</c> for διὰ
    /// τοῦτο, written <em>therefore</em> — so the words of all of them together are one claim, the
    /// shape <see cref="Together"/> already builds for a redirect. A number this witness does not
    /// write is left out rather than sinking the rest: the editions differ, and what the English
    /// still reaches is what the link should say.
    /// </summary>
    private static Reached? Reach(
        Dictionary<string, List<long>> byNumber,
        IReadOnlyDictionary<string, NumberRedirect> resolution,
        IReadOnlyList<string> numbers)
    {
        var keys = new List<string>(numbers.Count);
        var greek = new List<long>(numbers.Count);
        var taken = new HashSet<long>(numbers.Count);
        var resolved = false;

        foreach (var number in numbers)
        {
            string key;
            List<long> carrying;
            if (byNumber.TryGetValue(number, out var direct))
            {
                key = number;
                carrying = direct;
            }
            else if (resolution.TryGetValue(number, out var redirect)
                     && Together(byNumber, redirect.Numbers) is { } through)
            {
                key = string.Join('+', redirect.Numbers);
                carrying = through;
                resolved = true;
            }
            else
            {
                continue;
            }

            if (keys.Contains(key))
            {
                continue;
            }

            keys.Add(key);
            greek.AddRange(carrying.Where(taken.Add));
        }

        return keys.Count == 0 ? null : new Reached(string.Join('+', keys), greek, keys.Count, resolved);
    }

    private static void Collect(
        List<string> order,
        Dictionary<string, Group> groups,
        Reached reached,
        long english)
    {
        if (!groups.TryGetValue(reached.Key, out var group))
        {
            group = new Group(reached.Greek, reached.Numbers);
            groups[reached.Key] = group;
            order.Add(reached.Key);
        }

        group.English.Add(english);
        group.Resolved |= reached.Resolved;
    }

    /// <param name="settled">
    /// One English word, and the verse writes each number its tag names exactly once. Which Greek
    /// word answers which is then not a choice — the ordinary single number matched alone is the
    /// commonest case of it, and a two-number phrase whose words the verse writes once each is as
    /// settled as that.
    /// </param>
    private static double Confidence(bool settled, int englishWords, int greekWords, bool resolved) => Lower(
        (settled, englishWords, greekWords) switch
        {
            (true, _, _) => Unambiguous,
            (_, 1, _) or (_, _, 1) => OneSideContended,
            _ => BothSidesContended,
        },
        resolved);

    // Rounded because the column is read by people and 0.3 less 0.1 is 0.19999999999999998.
    private static double Lower(double confidence, bool resolved) =>
        resolved ? Math.Round(confidence - ResolvedNumber, 2) : confidence;

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
    private static List<TaggedWord> Project(List<TaggedWord> tags, int words, List<EditionSpan> spans)
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

    private static Dictionary<(int, int, int), List<TaggedWord>> Tagged(string path)
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

    private async Task<Dictionary<(int, int, int), List<Word>>> VerseWords(
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.TextId == textId)
            .SelectMany(r => r.Verse!.Words.Select(w => new
            {
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                w.Position,
                w.Id,
                w.Surface,
                w.StrongNumber,
                w.Morphology,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position)
                    .Select(r => new Word(r.Id, r.Surface, r.StrongNumber, GreekMorphology.Of(r.Morphology)))
                    .ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        string greekSlug,
        List<GreekLinkDraft> drafts,
        List<(long WordId, string Strong)> stated,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveLinkIds(connection, drafts.Count, cancellationToken);
        var renders = EnumSpelling.Of(LinkRelation.Renders);
        var expands = EnumSpelling.Of(LinkRelation.Expands);
        var byNumber = EnumSpelling.Of(LinkMethod.StrongNumber);
        var lexical = EnumSpelling.Of(LinkMethod.Lexical);
        var bySource = EnumSpelling.Of(LinkMethod.StatedBySource);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);
        var source = Source(greekSlug);
        var recovered = RecoveredSource(greekSlug);

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                var kind = drafts[i].Kind;
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(
                    kind == GreekMatch.Supplied ? expands : renders, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(
                    kind switch
                    {
                        GreekMatch.Supplied => bySource,
                        GreekMatch.FunctionWord => lexical,
                        _ => byNumber,
                    },
                    NpgsqlDbType.Text,
                    cancellationToken);

                if (drafts[i].Confidence is { } confidence)
                {
                    await writer.WriteAsync(confidence, NpgsqlDbType.Double, cancellationToken);
                }
                else
                {
                    await writer.WriteNullAsync(cancellationToken);
                }

                await writer.WriteAsync(
                    kind switch
                    {
                        GreekMatch.Supplied => SuppliedSource,
                        GreekMatch.FunctionWord => recovered,
                        _ => source,
                    },
                    NpgsqlDbType.Text,
                    cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                foreach (var wordId in drafts[i].English)
                {
                    await Row(writer, firstId + i, wordId, fromSide, cancellationToken);
                }

                foreach (var wordId in drafts[i].Greek)
                {
                    await Row(writer, firstId + i, wordId, toSide, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await WriteStrongNumbers(connection, stated, cancellationToken);
        // The claim that says this loader is the one asserting these links. Written here rather
        // than left to a backfill: a link with no claim is invisible to the agreement measure, and
        // the measure spent a day reporting the migration instead of the corpus. PRB-0198.
        await LinkClaims.Record(connection, transaction, firstId, drafts.Count, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Puts the tagged edition's Strong numbers on the English words themselves, so that the links
    /// built from them can afterwards be checked against something. It runs once per Greek witness
    /// over the same tags, which is why it writes only where the number is not already there.
    ///
    /// A word whose tag names several numbers keeps the first in the column and all of them in
    /// <c>word_strong</c>, which is where a number the column cannot hold belongs.
    /// </summary>
    private static async Task WriteStrongNumbers(
        NpgsqlConnection connection,
        List<(long WordId, string Strong)> stated,
        CancellationToken cancellationToken)
    {
        if (stated.Count == 0)
        {
            return;
        }

        await using (var create = new NpgsqlCommand(StrongNumberTable, connection))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY tagged_strong (word_id, strong_number, position) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            var written = new HashSet<(long, string)>(stated.Count);
            var positions = new Dictionary<long, int>(stated.Count);

            foreach (var (wordId, strong) in stated)
            {
                if (!written.Add((wordId, strong)))
                {
                    continue;
                }

                var position = positions.GetValueOrDefault(wordId);
                positions[wordId] = position + 1;

                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(strong, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(position, NpgsqlDbType.Integer, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var update = new NpgsqlCommand(StrongNumberUpdate, connection))
        {
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var phrases = new NpgsqlCommand(PhraseNumbers, connection);
        phrases.Parameters.AddWithValue("method", EnumSpelling.Of(LinkMethod.StatedBySource));
        phrases.Parameters.AddWithValue("source", PhraseSource);
        await phrases.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task Row(
        NpgsqlBinaryImporter writer,
        long linkId,
        long wordId,
        string side,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(linkId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
    }

    private static async Task<long> ReserveLinkIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('link', 'id'), " +
            "coalesce((SELECT max(id) FROM link), 0) + @count) - @count + 1", connection);
        command.Parameters.AddWithValue("count", count);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
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
    private sealed record TaggedWord(string Text, IReadOnlyList<string> Numbers, bool Supplied);

    private sealed record Word(long Id, string Text, string? Strong, GreekMorphology Morphology);

    /// <summary>A verse the tagged edition and the corpus agree on, with both texts' words beside it.</summary>
    private sealed record VersePair(List<TaggedWord> Tags, List<Word> English, List<Word> Greek);

    /// <summary>What joined the two sides, so the load can report each kind apart from the others.</summary>
    private enum GreekMatch
    {
        Unambiguous,
        Paired,
        Contended,
        FunctionWord,
        Supplied,
    }

    /// <param name="Numbers">
    /// How many distinct Strong numbers this group stands on. One is the ordinary case; more is a
    /// phrase tag, and it is what says whether the Greek words are occurrences of one number to
    /// choose between or the several words the source names together.
    /// </param>
    private sealed record Group(List<long> Greek, int Numbers)
    {
        public List<long> English { get; } = [];

        public bool Resolved { get; set; }
    }

    /// <param name="Greek">Every Greek word the tag's numbers name, in the order the numbers stand.</param>
    /// <param name="Numbers">
    /// How many of the tag's numbers this witness writes at all. Compared against the Greek words
    /// found, it is what says whether each of them was written once or some of them several times.
    /// </param>
    private sealed record Reached(string Key, List<long> Greek, int Numbers, bool Resolved);

    /// <param name="Confidence">
    /// How sure the pairing is, or null where nothing was inferred — a supplied word's absence is
    /// stated by the source, and a number on it would invite the reader to weigh a claim that was
    /// never a guess.
    /// </param>
    private sealed record GreekLinkDraft(List<long> English, List<long> Greek, double? Confidence, GreekMatch Kind);
}
