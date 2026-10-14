using System.Globalization;
using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Refused">
/// Verses where the file and the corpus do not line up, so no link was written. A refusal is the
/// point: a link written from a verse whose words do not correspond is a claim about the wrong
/// words, and it would look exactly like the 402,232 correct ones.
/// </param>
/// <param name="NumberedVerses">
/// Of the verses linked, the ones whose Hebrew the file and BHSA divide differently, read word by
/// word through the file's running numbers instead of verse by verse.
/// </param>
/// <param name="GlossRefused">
/// Of those, the ones the counts accepted and the glosses caught. They are the misalignments
/// nothing else can see, so they are counted apart from the rest.
/// </param>
/// <param name="GlossesCompared">
/// Hebrew words where the file and BHSA both state a gloss, which is how far the check reached. A
/// zero here says the check ran over nothing, and a check that passes over nothing is not a check.
/// </param>
/// <param name="StatedPrefixes">
/// Prefix links whose English word is the one STEPBible prints for that morpheme where it stands,
/// rather than one of the senses this project listed for its Strong number. They are still
/// inferences — the pairing is ours — but they rest on a source saying what the morpheme means, and
/// each carries a second claim naming it.
/// </param>
/// <param name="StatedVerses">
/// Verses whose morphemes TAHOT and the mapping file could be lined up in. Zero means the
/// segmentation was absent or reached nothing, and the prefixes fell back to the project's list.
/// </param>
/// <param name="GlossesDrifted">
/// Verses that pass the check and are still not a perfect match — the two sides gloss the same
/// words from two revisions of one dictionary. Kept and counted rather than refused: they are
/// correct verses, and the number is the early warning that a revision has begun to matter.
/// </param>
/// <param name="Redrawn">
/// On a loaded corpus, verses whose links an earlier load drew by position although the file numbers
/// two entries as one word there, or left a word BHSA splits unlinked in, drawn again through the
/// running numbers.
/// </param>
internal sealed record LinkOutcome(
    bool AlreadyLoaded,
    int Verses,
    int NumberedVerses,
    int Refused,
    int GlossRefused,
    int GlossesCompared,
    int GlossesDrifted,
    int StatedPrefixes,
    int StatedVerses,
    int Links,
    int EnglishWordsLinked,
    int HebrewWordsLinked,
    int HebrewWordsUnreached,
    int MultiWordLinks,
    int Omissions,
    int Supplied,
    int StrongNumbers,
    TimeSpan Elapsed,
    int Redrawn = 0)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"the Old Testament links are already loaded; {Redrawn} verses linked again through the running numbers"
            : $"{Links} links over {Verses} verses in {Elapsed}, {NumberedVerses} of them read through the " +
              $"running word numbers: {EnglishWordsLinked} English and " +
              $"{HebrewWordsLinked} Hebrew words, {MultiWordLinks} naming more than one Hebrew word, " +
              $"{Omissions} naming a Hebrew word the English does not render, " +
              $"{Supplied} naming an English word the King James supplies and the Hebrew does not have, " +
              $"{HebrewWordsUnreached} Hebrew words reached by nothing, {StatedPrefixes} function words matched to " +
              $"the morpheme STEPBible glosses for them over {StatedVerses} verses it segments, " +
              $"{StrongNumbers} Hebrew words given the " +
              $"Strong number the file states, {Refused} verses refused of which {GlossRefused} for glosses that " +
              $"disagree, over {GlossesCompared} words the file and BHSA both gloss, {GlossesDrifted} verses " +
              $"passing with a gloss the dictionary has since revised";
}

/// <summary>
/// The Old Testament correspondences, from the file that states them.
///
/// Two joins have to hold before a single link is written, and both are checked rather than
/// assumed: the file's Hebrew words against BHSA's, verified by the glosses both carry, and the
/// file's English words against the King James as loaded. A verse where either fails is refused and
/// counted, because a link is a scholarly claim and one built on a misalignment is worse than none.
/// </summary>
internal sealed class OldTestamentLinkLoader(AppDbContext db, ILogger<OldTestamentLinkLoader> logger)
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
    private const string Source =
        "Open Hebrew Bible Project by Eliran Wong, github.com/eliranwong/OpenHebrewBible, CC BY-NC 4.0";

    /// <summary>
    /// The second source, where it reaches: STEPBible's TAHOT, which says which morphemes are
    /// prefixes and what each one means where it stands. It states nothing about the King James, so
    /// it never makes a link <c>stated-by-source</c> — it is a second, independent answer to what
    /// the Hebrew says, and the claim it writes records that rather than upgrading the link.
    /// </summary>
    private const string StatedSource =
        "TAHOT by STEP Bible, www.STEPBible.org, based on work at Tyndale House Cambridge, CC BY-NC 3.0";

    /// <summary>What the second claim asserts, as against what the link's own claim asserts.</summary>
    private const string StatedNote =
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
    /// <para>
    /// The verses between this line and a perfect match are not discarded and not ignored either:
    /// <see cref="LinkOutcome.GlossesDrifted"/> counts them, so a revision that starts to matter
    /// shows up as a number going the wrong way rather than as silence.
    /// </para>
    /// </summary>
    private const double SameVerse = 0.6;

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
    /// The mapping file gives every Hebrew word a Strong number, which BHSA itself does not carry.
    /// It is a claim the same source makes about the same words, so it arrives with the links and
    /// not before them: it is only trustworthy for a verse whose join held.
    /// </summary>
    private const string StrongNumberUpdate =
        """
        CREATE TEMP TABLE stated_strong (word_id bigint PRIMARY KEY, strong_number text) ON COMMIT DROP;
        CREATE TEMP TABLE doubtful_word (word_id bigint PRIMARY KEY) ON COMMIT DROP;
        """;

    /// <param name="segmentation">
    /// TAHOT, or null where it is not on disk. Without it the prefixes fall back to the project's
    /// own list of what each one can render, which is what the corpus had before and is weaker.
    /// </param>
    public async Task<LinkOutcome> Load(
        IReadOnlyList<MappingRecord> records,
        TahotSegmentation? segmentation = null,
        CancellationToken cancellationToken = default)
    {
        var english = await db.Texts.SingleOrDefaultAsync(t => t.Slug == Bible4uTextSource.KingJames, cancellationToken);
        var hebrew = await db.Texts.SingleOrDefaultAsync(t => t.Slug == BhsaTextSource.Slug, cancellationToken);
        if (english is null || hebrew is null)
        {
            throw new InvalidOperationException(
                "The King James and BHSA must both be loaded before the correspondences between them can be. " +
                "Load the texts first; this reads them, it does not create them.");
        }

        if (await db.Links.AnyAsync(l => l.FromTextId == english.Id && l.ToTextId == hebrew.Id, cancellationToken))
        {
            var numbered = await NumberTheUnnumbered(records, hebrew.Id, cancellationToken);
            var redrawn = await RedrawWhatTheRunningNumbersRead(records, segmentation, english.Id, hebrew.Id, cancellationToken);
            logger.LogInformation(
                "The Old Testament links are already loaded; {Numbered} Hebrew words that had no Strong number, or " +
                "one an earlier load read off the wrong word, were given the one the file states, and {Redrawn} " +
                "verses an earlier load linked by position where the file numbers two entries as one word, or " +
                "left a word BHSA splits unlinked in, were linked again through the running numbers", numbered, redrawn);
            return new LinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, numbered, TimeSpan.Zero, redrawn);
        }

        var started = Stopwatch.StartNew();
        var englishVerses = await VerseWords(english.Id, cancellationToken);
        var hebrewVerses = await VerseWords(hebrew.Id, cancellationToken);

        var pairs = new List<LinkDraft>(400_000);
        var statedStrong = new List<(long WordId, string Strong)>(430_000);
        var refused = 0;
        var glossRefused = 0;
        var glossesCompared = 0;
        var glossesDrifted = 0;
        var unreached = 0;
        var statedVerses = 0;
        var numberedVerses = 0;
        var placed = new Dictionary<long, int>(430_000);
        var unjoined = new List<(MappingRecord Record, bool ForGlosses)>();

        foreach (var record in records)
        {
            var bhsaWords = JoinHebrew(record, hebrewVerses, out var glosses);
            glossesCompared += glosses.Compared;
            if (bhsaWords is null)
            {
                unjoined.Add((record, glosses.Share < SameVerse));
                continue;
            }

            // The numbers belong to the Hebrew words, so they need only the Hebrew join. A verse the
            // King James words differently from the file is refused its links, and its Hebrew is
            // still the Hebrew the file numbers.
            StateNumbers(record, bhsaWords, statedStrong, placed);

            if (!Take(record, bhsaWords, glosses))
            {
                refused++;
            }
        }

        // A verse the file and BHSA divide differently is read word by word through the running
        // numbers, which reach across the verse boundary where the two put it in different places.
        var byNumber = WordsByNumber(records, hebrewVerses, placed, out var split);
        foreach (var (record, forGlosses) in unjoined)
        {
            if (ByRunningNumber(record, byNumber, split) is { } bhsaWords && Take(record, bhsaWords, default))
            {
                numberedVerses++;
                continue;
            }

            refused++;
            if (forGlosses)
            {
                glossRefused++;
            }
        }

        statedStrong.AddRange(Unplaced(records, byNumber, placed));
        await Write(english.Id, hebrew.Id, pairs, statedStrong, cancellationToken);

        var outcome = new LinkOutcome(
            false,
            records.Count - refused,
            numberedVerses,
            refused,
            glossRefused,
            glossesCompared,
            glossesDrifted,
            pairs.Count(p => p.Stated),
            statedVerses,
            pairs.Count,
            pairs.Sum(p => p.English.Count),
            pairs.SelectMany(p => p.Hebrew).Distinct().Count(),
            unreached,
            pairs.Count(p => p.Hebrew.Count > 1),
            pairs.Count(p => p.Omitted),
            pairs.Count(p => p.Relation == LinkRelation.Expands),
            statedStrong.DistinctBy(pair => pair.WordId).Count(),
            started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;

        bool Take(MappingRecord record, IReadOnlyList<Word?> bhsaWords, GlossAgreement glosses)
        {
            if (!englishVerses.TryGetValue((record.Book, record.Chapter, record.Verse), out var kjvWords))
            {
                return false;
            }

            var stated = segmentation?.Align(record.Book, record.Chapter, record.Verse, record.Hebrew);
            if (stated is { Count: > 0 })
            {
                statedVerses++;
            }

            var drafts = Build(record, kjvWords, bhsaWords, stated);
            if (drafts is null)
            {
                return false;
            }

            if (glosses.Compared > 0 && glosses.Same < glosses.Compared)
            {
                glossesDrifted++;
            }

            pairs.AddRange(drafts);
            unreached += bhsaWords.OfType<Word>().SelectMany(word => word.Also.Prepend(word.Id)).Distinct().Count()
                         - drafts.SelectMany(d => d.Hebrew).Distinct().Count();
            return true;
        }
    }

    /// <summary>
    /// The links of verses that have none: a verse the loaded text garbled was refused its links, and
    /// once it is corrected it joins; and a verse an earlier reading of the file could not line up
    /// joins once the reading can. Only verses none of whose words is linked to BHSA yet are drawn,
    /// so no verse is linked twice, and the Hebrew numbers are not written again — the Hebrew join
    /// held for them the first time.
    /// </summary>
    /// <param name="verses">
    /// The King James verses, by address, whose words changed; null for every verse the file holds.
    /// </param>
    public async Task<int> Relink(
        IReadOnlyList<MappingRecord> records,
        TahotSegmentation? segmentation,
        IReadOnlyCollection<(int Book, int Chapter, int Verse)>? verses,
        CancellationToken cancellationToken = default)
    {
        var english = await db.Texts.SingleOrDefaultAsync(t => t.Slug == Bible4uTextSource.KingJames, cancellationToken);
        var hebrew = await db.Texts.SingleOrDefaultAsync(t => t.Slug == BhsaTextSource.Slug, cancellationToken);
        if (english is null || hebrew is null || verses is { Count: 0 })
        {
            return 0;
        }

        var englishVerses = await VerseWords(english.Id, cancellationToken);
        var wanted = (verses ?? [.. records.Select(r => (r.Book, r.Chapter, r.Verse))])
            .Where(englishVerses.ContainsKey)
            .ToHashSet();
        var pair = db.LinkWords.Where(lw => lw.Link!.FromTextId == english.Id && lw.Link.ToTextId == hebrew.Id);
        if (verses is not null)
        {
            var words = wanted.SelectMany(address => englishVerses[address].Select(word => word.Id)).ToList();
            pair = pair.Where(lw => words.Contains(lw.WordId));
        }

        var linked = (await pair.Select(lw => lw.WordId).ToListAsync(cancellationToken)).ToHashSet();
        wanted.RemoveWhere(address => englishVerses[address].Any(word => linked.Contains(word.Id)));
        if (wanted.Count == 0)
        {
            return 0;
        }

        var hebrewVerses = await VerseWords(hebrew.Id, cancellationToken);
        var placed = new Dictionary<long, int>(430_000);
        foreach (var record in records)
        {
            if (JoinHebrew(record, hebrewVerses, out _) is { } joinedWords)
            {
                Place(record, joinedWords, placed);
            }
        }

        var byNumber = WordsByNumber(records, hebrewVerses, placed, out var split);
        var pairs = new List<LinkDraft>();
        var joined = 0;
        foreach (var record in records.Where(r => wanted.Contains((r.Book, r.Chapter, r.Verse))))
        {
            if ((JoinHebrew(record, hebrewVerses, out _) ?? ByRunningNumber(record, byNumber, split)) is not { } bhsaWords
                || Build(
                    record,
                    englishVerses[(record.Book, record.Chapter, record.Verse)],
                    bhsaWords,
                    segmentation?.Align(record.Book, record.Chapter, record.Verse, record.Hebrew)) is not { } drafts)
            {
                continue;
            }

            pairs.AddRange(drafts);
            joined++;
        }

        await Write(english.Id, hebrew.Id, pairs, [], cancellationToken);
        logger.LogInformation(
            "Linked {Verses} of {Wanted} King James verses that had no link to BHSA from the mapping file: {Links} links",
            joined, wanted.Count, pairs.Count);
        return pairs.Count;
    }

    /// <summary>
    /// The links of a verse an earlier load drew by position although the file numbers two entries as
    /// one word in it (<see cref="NumbersTwoEntriesAsOneWord"/>), drawn again through the running
    /// numbers: Psalm 60's title, whose count agrees with BHSA's by accident, gave Joab's English the
    /// conjunction and the valley of salt's the preposition before it.
    ///
    /// <para>
    /// What is taken back is only a link of this loader's that the positional join draws and the
    /// running numbers do not; every other link on the verse stays, row for row, and a link the new
    /// reading draws is written only where no link names those words already, so a second run finds
    /// nothing to take back and writes nothing. Where another source or a review stands on a link to
    /// be taken back, the verse is left as it is and logged.
    /// </para>
    ///
    /// <para>
    /// And the links of a word BHSA splits (<see cref="Split"/>), in every verse read through the
    /// running numbers: an earlier load left its English unlinked, and it is drawn where none of its
    /// words, English or Hebrew, is linked to the other text yet.
    /// </para>
    /// </summary>
    private async Task<int> RedrawWhatTheRunningNumbersRead(
        IReadOnlyList<MappingRecord> records,
        TahotSegmentation? segmentation,
        int englishTextId,
        int hebrewTextId,
        CancellationToken cancellationToken)
    {
        var englishVerses = await VerseWords(englishTextId, cancellationToken);
        var hebrewVerses = await VerseWords(hebrewTextId, cancellationToken);
        var positional = new List<(MappingRecord Record, List<Word> Kjv, List<LinkDraft> Drawn)>();
        foreach (var record in records.Where(NumbersTwoEntriesAsOneWord))
        {
            if (englishVerses.TryGetValue((record.Book, record.Chapter, record.Verse), out var kjv)
                && ByPosition(record, hebrewVerses, out _) is { } bhsa
                && Build(record, kjv, bhsa, segmentation?.Align(record.Book, record.Chapter, record.Verse, record.Hebrew))
                    is { Count: > 0 } drawn)
            {
                positional.Add((record, kjv, drawn));
            }
        }

        var shifted = new List<(MappingRecord Record, List<Word> Kjv, HashSet<string> Drawn, List<Standing> Links)>();
        foreach (var (record, kjv, drawn) in positional)
        {
            var words = kjv.Select(word => word.Id)
                .Concat(drawn.SelectMany(draft => draft.Hebrew))
                .ToList();
            var english = kjv.Select(word => word.Id).ToHashSet();
            var hebrew = drawn.SelectMany(draft => draft.Hebrew).ToHashSet();
            var links = (await db.Links.AsNoTracking()
                    .Where(l => l.FromTextId == englishTextId && l.ToTextId == hebrewTextId
                                && l.Words.Any(w => words.Contains(w.WordId)))
                    .Select(l => new
                    {
                        l.Id,
                        l.Relation,
                        Mine = l.Provenance!.Source == Source,
                        From = l.Words.Where(w => w.Side == LinkSide.From).Select(w => w.WordId).ToList(),
                        To = l.Words.Where(w => w.Side == LinkSide.To).Select(w => w.WordId).ToList(),
                        Claimed = l.Claims.Any(c => c.Provenance!.Source != Source && c.Provenance.Source != StatedSource)
                                  || db.EvidentiaReviews.Any(r => r.LinkId == l.Id)
                                  || db.EvidentiaWithdrawals.Any(r => r.LinkId == l.Id),
                    })
                    .ToListAsync(cancellationToken))
                .Where(l => l.From.Count > 0 ? l.From.Any(english.Contains) : l.To.All(hebrew.Contains))
                .Select(l => new Standing(l.Id, Key(l.Relation, l.From, l.To), l.Mine, l.Claimed))
                .ToList();
            var byPosition = drawn.Select(KeyOf).ToHashSet();
            if (links.Any(l => l.Mine && byPosition.Contains(l.Shape)))
            {
                shifted.Add((record, kjv, byPosition, links));
            }
        }

        var placed = new Dictionary<long, int>(430_000);
        foreach (var record in records)
        {
            if (JoinHebrew(record, hebrewVerses, out _) is { } joinedWords)
            {
                Place(record, joinedWords, placed);
            }
        }

        var byNumber = WordsByNumber(records, hebrewVerses, placed, out var split);
        var gone = new List<long>();
        var drafts = new List<LinkDraft>();
        var redrawn = 0;
        foreach (var (record, kjv, byPosition, links) in shifted)
        {
            var reread = ByRunningNumber(record, byNumber, split) is { } bhsa
                ? Build(record, kjv, bhsa, segmentation?.Align(record.Book, record.Chapter, record.Verse, record.Hebrew)) ?? []
                : [];
            var read = reread.Select(KeyOf).ToHashSet();
            var leaving = links.Where(l => l.Mine && byPosition.Contains(l.Shape) && !read.Contains(l.Shape)).ToList();
            if (leaving.Count == 0)
            {
                continue;
            }

            if (leaving.Any(l => l.Claimed))
            {
                logger.LogWarning(
                    "{Book} {Chapter}:{Verse} was linked by position where the file numbers two entries as one " +
                    "word, and another source or a review stands on one of its links; left as it is",
                    record.Book, record.Chapter, record.Verse);
                continue;
            }

            var staying = links.Where(l => !leaving.Contains(l)).Select(l => l.Shape).ToHashSet();
            gone.AddRange(leaving.Select(l => l.Id));
            drafts.AddRange(reread.Where(draft => !staying.Contains(KeyOf(draft))));
            redrawn++;
        }

        var splitWords = split.Values.SelectMany(word => word.Also.Prepend(word.Id)).ToHashSet();
        var redrawnVerses = shifted.Select(s => s.Record).ToHashSet();
        var splitDrafts = new List<(MappingRecord Record, LinkDraft Draft)>();
        foreach (var record in records)
        {
            if (redrawnVerses.Contains(record)
                || !record.Hebrew.Any(entry => split.ContainsKey(entry.Position))
                || JoinHebrew(record, hebrewVerses, out _) is not null
                || !englishVerses.TryGetValue((record.Book, record.Chapter, record.Verse), out var kjv)
                || ByRunningNumber(record, byNumber, split) is not { } bhsa
                || Build(record, kjv, bhsa, segmentation?.Align(record.Book, record.Chapter, record.Verse, record.Hebrew))
                    is not { } built)
            {
                continue;
            }

            splitDrafts.AddRange(built.Where(draft => draft.Hebrew.Any(splitWords.Contains)).Select(draft => (record, draft)));
        }

        if (splitDrafts.Count > 0)
        {
            var named = splitDrafts.SelectMany(d => d.Draft.English.Concat(d.Draft.Hebrew)).Distinct().ToList();
            var linked = (await db.LinkWords
                    .Where(lw => lw.Link!.FromTextId == englishTextId && lw.Link.ToTextId == hebrewTextId
                                 && named.Contains(lw.WordId))
                    .Select(lw => lw.WordId)
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .ToHashSet();
            var unlinked = splitDrafts
                .Where(d => !d.Draft.English.Concat(d.Draft.Hebrew).Any(linked.Contains))
                .ToList();
            drafts.AddRange(unlinked.Select(d => d.Draft));
            redrawn += unlinked.Select(d => d.Record).Distinct().Count();
        }

        if (redrawn == 0)
        {
            return 0;
        }

        await Write(englishTextId, hebrewTextId, drafts, [], cancellationToken, gone);
        return redrawn;

        static string Key(LinkRelation relation, IEnumerable<long> from, IEnumerable<long> to) =>
            $"{relation}|{string.Join(',', from.Order())}|{string.Join(',', to.Order())}";

        static string KeyOf(LinkDraft draft) => Key(RelationOf(draft), draft.English, draft.Hebrew);
    }

    /// <summary>
    /// Which BHSA word each of the Open Hebrew Bible's running word numbers names, read from this
    /// file the way the links are: a verse whose Hebrew lines up word for word gives every word its
    /// number, and the words of a verse divided differently are numbered from the placed words on
    /// either side (<see cref="RunningWordNumbers"/>). The project's other mappings write the same
    /// numbers, so this is how they reach BHSA too. A number no word took is not in the result; a
    /// number whose one word BHSA writes as two or three names them all (<see cref="Split"/>).
    /// </summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<long>>> WordsByRunningNumber(
        string path,
        CancellationToken cancellationToken = default) =>
        (await RunningNumbers(path, cancellationToken)).Now;

    /// <summary>
    /// The running numbers as <see cref="WordsByRunningNumber"/> reads them, and as loads before it
    /// read them: then a verse whose file numbers two entries as one word was placed by position
    /// wherever its count agreed (Psalm 60's title, every word between its two divisions a word
    /// early), and a word BHSA splits had no word at all. A mapping that borrowed the old numbers
    /// compares the two to find what it drew from a wrong one.
    /// </summary>
    public async Task<(IReadOnlyDictionary<int, IReadOnlyList<long>> Now, IReadOnlyDictionary<int, IReadOnlyList<long>> Before)>
        RunningNumbers(string path, CancellationToken cancellationToken = default) =>
        await RunningNumbers(KjvBhsMapping.Read(path), cancellationToken);

    internal async Task<(IReadOnlyDictionary<int, IReadOnlyList<long>> Now, IReadOnlyDictionary<int, IReadOnlyList<long>> Before)>
        RunningNumbers(IReadOnlyList<MappingRecord> records, CancellationToken cancellationToken = default)
    {
        var hebrew = await db.Texts.SingleOrDefaultAsync(t => t.Slug == BhsaTextSource.Slug, cancellationToken)
                     ?? throw new InvalidOperationException(
                         "BHSA is not loaded, and the running word numbers name its words. Load the corpus first.");
        var hebrewVerses = await VerseWords(hebrew.Id, cancellationToken);
        var placed = new Dictionary<long, int>(430_000);
        foreach (var record in records)
        {
            if (JoinHebrew(record, hebrewVerses, out _) is { } bhsa)
            {
                Place(record, bhsa, placed);
            }
        }

        var byNumber = WordsByNumber(records, hebrewVerses, placed, out var split);
        var now = byNumber.Concat(split).ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<long>)[.. pair.Value.Also.Prepend(pair.Value.Id)]);

        var before = byNumber.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<long>)[pair.Value.Id]);
        var byPosition = new HashSet<int>();
        foreach (var record in records.Where(NumbersTwoEntriesAsOneWord))
        {
            if (ByPosition(record, hebrewVerses, out _) is not { } bhsa)
            {
                continue;
            }

            for (var i = 0; i < record.Hebrew.Count; i++)
            {
                if (byPosition.Add(record.Hebrew[i].Position))
                {
                    before[record.Hebrew[i].Position] = [bhsa[i].Id];
                }
            }
        }

        return (now, before);
    }

    /// <summary>
    /// The BHSA word at each running number: the words a joined verse placed, and the stretches
    /// between them numbered by the same reading as the Strong numbers are, with each entry's own
    /// running number standing where its Strong number would.
    /// </summary>
    private static Dictionary<int, Word> WordsByNumber(
        IReadOnlyList<MappingRecord> records,
        Dictionary<(int, int, int), List<Word>> hebrewVerses,
        Dictionary<long, int> placed) =>
        WordsByNumber(records, hebrewVerses, placed, out _);

    /// <param name="split">
    /// The numbers no word took that stand for a word BHSA writes as two or three (<see cref="Split"/>):
    /// the first of them, carrying the others in <see cref="Word.Also"/>. Kept apart because a number
    /// is a Strong number's place, and neither half is the whole word the number names.
    /// </param>
    private static Dictionary<int, Word> WordsByNumber(
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
    private static List<Word?>? ByRunningNumber(
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
    private static List<Word>? JoinHebrew(
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

    private static void Place(MappingRecord record, List<Word> bhsa, Dictionary<long, int> placed)
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

    private static LinkRelation RelationOf(LinkDraft draft) => draft.Omitted ? LinkRelation.Omits : draft.Relation;

    private static void StateNumbers(
        MappingRecord record,
        List<Word> bhsa,
        List<(long WordId, string Strong)> statedStrong,
        Dictionary<long, int> placed)
    {
        Place(record, bhsa, placed);
        for (var i = 0; i < record.Hebrew.Count; i++)
        {
            var strong = StrongNumbers.Normalize(record.Hebrew[i].Strong);
            if (strong is not null)
            {
                statedStrong.Add((bhsa[i].Id, strong));
            }
        }
    }

    /// <summary>
    /// The numbers alone, for a corpus whose links are already written: every Hebrew word whose verse
    /// the file joins and which carries no number yet, and every word an earlier load could have
    /// numbered wrongly and whose number the file now states otherwise.
    ///
    /// <para>
    /// Those are the words of an address two of BHSA's verses share — a psalm title the Hebrew
    /// numbers as two verses, whose words an earlier load read interleaved, Psalm 51's David taking
    /// the article's number and Psalm 52's Doeg David's — and of a verse the file numbers two entries
    /// as one word in (<see cref="NumbersTwoEntriesAsOneWord"/>). Any other number already there is
    /// never replaced, so a second run writes nothing.
    /// </para>
    /// </summary>
    private async Task<int> NumberTheUnnumbered(
        IReadOnlyList<MappingRecord> records,
        int hebrewTextId,
        CancellationToken cancellationToken)
    {
        var hebrewVerses = await VerseWords(hebrewTextId, cancellationToken);
        var shared = await SharedAddresses(hebrewTextId, cancellationToken);
        var doubtful = hebrewVerses
            .Where(verse => shared.Contains(verse.Key))
            .SelectMany(verse => verse.Value.Select(word => word.Id))
            .ToHashSet();
        var statedStrong = new List<(long WordId, string Strong)>(430_000);
        var placed = new Dictionary<long, int>(430_000);
        foreach (var record in records)
        {
            if (NumbersTwoEntriesAsOneWord(record))
            {
                doubtful.UnionWith(WordsAt(record, hebrewVerses).Select(word => word.Id));
                continue;
            }

            if (JoinHebrew(record, hebrewVerses, out _) is { } bhsaWords)
            {
                StateNumbers(record, bhsaWords, statedStrong, placed);
            }
        }

        statedStrong.AddRange(Unplaced(records, WordsByNumber(records, hebrewVerses, placed), placed));
        return await WriteStrongNumbers(statedStrong, doubtful, cancellationToken);
    }

    /// <summary>Every BHSA word the record's verse holds, its superscription first where it is a first verse.</summary>
    private static IEnumerable<Word> WordsAt(MappingRecord record, Dictionary<(int, int, int), List<Word>> hebrewVerses)
    {
        if (record.Verse == FirstVerse
            && hebrewVerses.TryGetValue((record.Book, record.Chapter, Superscription), out var superscription))
        {
            foreach (var word in superscription)
            {
                yield return word;
            }
        }

        if (hebrewVerses.TryGetValue((record.Book, record.Chapter, record.Verse), out var verse))
        {
            foreach (var word in verse)
            {
                yield return word;
            }
        }
    }

    /// <summary>The canonical addresses two or more of the text's verses stand at.</summary>
    private async Task<HashSet<(int, int, int)>> SharedAddresses(int textId, CancellationToken cancellationToken) =>
        (await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.TextId == textId)
            .GroupBy(r => new { r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse })
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToListAsync(cancellationToken))
        .Select(address => (address.CanonicalBook, address.CanonicalChapter, address.CanonicalVerse))
        .ToHashSet();

    /// <summary>
    /// The numbers of the words no verse join placed — verses the file divides differently from BHSA —
    /// read off the file's running word numbers between the placed words on either side.
    /// </summary>
    private static IEnumerable<(long WordId, string Strong)> Unplaced(
        IReadOnlyList<MappingRecord> records,
        Dictionary<int, Word> byNumber,
        Dictionary<long, int> placed)
    {
        var stated = new Dictionary<int, string>(430_000);
        foreach (var entry in records.SelectMany(record => record.Hebrew))
        {
            stated.TryAdd(entry.Position, entry.Strong);
        }

        foreach (var (number, word) in byNumber)
        {
            if (!placed.ContainsKey(word.Id) && StrongNumbers.Normalize(stated[number]) is { } strong)
            {
                yield return (word.Id, strong);
            }
        }
    }

    /// <summary>The numbers without links, in a transaction of their own, which the temporary table needs.</summary>
    /// <param name="doubtful">Words whose number is replaced where the file states another.</param>
    private async Task<int> WriteStrongNumbers(
        List<(long WordId, string Strong)> stated,
        IReadOnlySet<long>? doubtful,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var numbered = await WriteStrongNumbers(
            (NpgsqlConnection)db.Database.GetDbConnection(), stated, cancellationToken, doubtful);
        await transaction.CommitAsync(cancellationToken);
        return numbered;
    }

    /// <summary>
    /// Lines the file's English up with the King James as loaded, over a verse whose Hebrew has
    /// already been read, and refuses rather than guessing when it cannot (<see cref="EnglishWordJoin"/>).
    /// A Hebrew entry with no word is one BHSA divides otherwise, and the English rendering it is
    /// left unlinked rather than given a neighbour's word.
    /// </summary>
    private static List<LinkDraft>? Build(
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
                r.Verse.Sequence,
                r.Verse.Number,
                w.Position,
                w.Id,
                w.Surface,
                w.Gloss,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                // Two verses can stand at one address — the Hebrew numbers Psalm 51's title as two —
                // and their words are one run only in the order the verses are written.
                group => group
                    .OrderBy(r => r.Sequence).ThenBy(r => r.Number).ThenBy(r => r.Position)
                    .Select(r => new Word(r.Id, r.Surface, r.Gloss))
                    .ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        List<LinkDraft> drafts,
        List<(long WordId, string Strong)> stated,
        CancellationToken cancellationToken,
        IReadOnlyCollection<long>? replaced = null)
    {
        if (drafts.Count == 0 && replaced is not { Count: > 0 })
        {
            await WriteStrongNumbers(stated, null, cancellationToken);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (replaced is { Count: > 0 })
        {
            await db.Links.Where(l => replaced.Contains(l.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        var written = await LinkWriter.Write(
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction(),
            [
                .. drafts.Select(draft => new NewLink(
                    fromTextId,
                    toTextId,
                    RelationOf(draft),
                    draft.Confidence is null ? LinkMethod.StatedBySource : LinkMethod.Lexical,
                    draft.Confidence,
                    Source,
                    null,
                    draft.English,
                    draft.Hebrew)),
            ],
            cancellationToken);

        await WriteStrongNumbers(connection, stated, cancellationToken);

        // And the second claim, where a second source reached the same place. TAHOT says nothing
        // about the King James, so it cannot make one of these links stated -- what it says is what
        // the Hebrew morpheme means, and a function word matched to the morpheme whose printed gloss
        // is that very word has two sources behind it rather than one.
        var corroborated = new List<long>();
        for (var i = 0; i < drafts.Count; i++)
        {
            if (drafts[i].Stated)
            {
                corroborated.Add(written.Ids[i]);
            }
        }

        await LinkClaims.Corroborate(
            connection,
            transaction,
            corroborated,
            LinkMethod.Lexical,
            HebrewPrefixes.Stated,
            StatedSource,
            StatedNote,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Writes the Strong numbers the file states for the Hebrew. Half a million single-row updates
    /// would be minutes; a temporary table filled by COPY and one join is seconds.
    ///
    /// The codes above H9000 are the dataset's own for the prefixes Strong's concordance never
    /// numbered — the conjunction, the article, the prefixed prepositions — so a reader looking one
    /// up in a printed concordance will not find it. They are kept because they are what the source
    /// says and because a prefix with no number is a prefix nothing can join on.
    /// </summary>
    private static async Task<int> WriteStrongNumbers(
        NpgsqlConnection connection,
        List<(long WordId, string Strong)> stated,
        CancellationToken cancellationToken,
        IReadOnlySet<long>? doubtful = null)
    {
        if (stated.Count == 0 && doubtful is not { Count: > 0 })
        {
            return 0;
        }

        await using (var create = new NpgsqlCommand(StrongNumberUpdate, connection))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY stated_strong (word_id, strong_number) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var (wordId, strong) in stated.DistinctBy(pair => pair.WordId))
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(strong, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        var renumbered = 0;
        if (doubtful is { Count: > 0 })
        {
            await using (var writer = await connection.BeginBinaryImportAsync(
                             "COPY doubtful_word (word_id) FROM STDIN (FORMAT BINARY)", cancellationToken))
            {
                foreach (var wordId in doubtful)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
                }

                await writer.CompleteAsync(cancellationToken);
            }

            await using (var withdraw = new NpgsqlCommand(Renumber, connection))
            {
                withdraw.Parameters.AddWithValue("written", EntityAnnotationLoader.Written);
                await withdraw.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var renumber = new NpgsqlCommand(
                "UPDATE word SET strong_number = r.strong_number FROM renumbered r WHERE word.id = r.word_id",
                connection);
            renumbered = await renumber.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var update = new NpgsqlCommand(
            "UPDATE word SET strong_number = s.strong_number FROM stated_strong s " +
            "WHERE word.id = s.word_id AND word.strong_number IS NULL",
            connection);
        return renumbered + await update.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The doubtful words whose number is not the one the file states — none, where the file states
    /// none for the word — given the file's, after what the resolution by number wrote from the old
    /// number, and carried across the links from it, is taken back: the number named the record, and
    /// the word is named again from the one it now carries when the annotations are read.
    /// </summary>
    private const string Renumber =
        """
        CREATE TEMP TABLE renumbered ON COMMIT DROP AS
        SELECT d.word_id, s.strong_number
        FROM doubtful_word d
        JOIN word w ON w.id = d.word_id
        LEFT JOIN stated_strong s ON s.word_id = d.word_id
        WHERE w.strong_number IS NOT NULL AND w.strong_number IS DISTINCT FROM s.strong_number;
        DELETE FROM word_entity a
        USING renumbered r
        WHERE a.word_id = r.word_id AND a.source = ANY(@written);
        DELETE FROM word_entity a
        USING renumbered r, link_word mine, link_word other
        WHERE mine.word_id = r.word_id
          AND other.link_id = mine.link_id AND other.side <> mine.side
          AND a.word_id = other.word_id
          AND a.source = ANY(@written)
          AND a.note LIKE 'through % word ' || r.word_id || ',%'
        """;

    /// <param name="Also">
    /// The rest of a word BHSA writes as several where the file writes it as one (<see cref="Split"/>),
    /// which every link of this word names too. Empty for every other word.
    /// </param>
    private sealed record Word(long Id, string Text, string? Gloss)
    {
        public IReadOnlyList<long> Also { get; init; } = [];
    }

    /// <summary>
    /// A link standing on a verse being drawn again: its shape, whether it is this loader's, and
    /// whether another source or a review stands on it too.
    /// </summary>
    private sealed record Standing(long Id, string Shape, bool Mine, bool Claimed);

    /// <param name="Omitted">
    /// The King James renders nothing here, so the link says so with an empty side rather than
    /// attaching the Hebrew word to whatever phrase happened to precede it.
    /// </param>
    /// <param name="Confidence">
    /// Null for everything the file states, and set for the function words matched to their
    /// prefixes, so that the two can never be read as the same kind of claim.
    /// </param>
    private sealed record LinkDraft(List<long> English, List<long> Hebrew, double? Confidence = null)
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
