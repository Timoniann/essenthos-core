using System.Globalization;
using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
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
internal sealed record LinkOutcome(
    bool AlreadyLoaded,
    int Verses,
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
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Old Testament links are already loaded"
            : $"{Links} links over {Verses} verses in {Elapsed}: {EnglishWordsLinked} English and " +
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
            logger.LogInformation(
                "The Old Testament links are already loaded; {Numbered} Hebrew words that had no Strong number were " +
                "given the one the file states", numbered);
            return new LinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, numbered, TimeSpan.Zero);
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
        var placed = new Dictionary<long, int>(430_000);

        foreach (var record in records)
        {
            var bhsaWords = JoinHebrew(record, hebrewVerses, out var glosses);
            glossesCompared += glosses.Compared;
            if (bhsaWords is null)
            {
                refused++;
                if (glosses.Share < SameVerse)
                {
                    glossRefused++;
                }

                continue;
            }

            // The numbers belong to the Hebrew words, so they need only the Hebrew join. A verse the
            // King James words differently from the file is refused its links, and its Hebrew is
            // still the Hebrew the file numbers.
            StateNumbers(record, bhsaWords, statedStrong, placed);

            if (!englishVerses.TryGetValue((record.Book, record.Chapter, record.Verse), out var kjvWords))
            {
                refused++;
                continue;
            }

            var stated = segmentation?.Align(record.Book, record.Chapter, record.Verse, record.Hebrew);
            if (stated is { Count: > 0 })
            {
                statedVerses++;
            }

            var drafts = Build(record, kjvWords, bhsaWords, stated);
            if (drafts is null)
            {
                refused++;
                continue;
            }

            if (glosses.Compared > 0 && glosses.Same < glosses.Compared)
            {
                glossesDrifted++;
            }

            pairs.AddRange(drafts);
            unreached += bhsaWords.Count - drafts.SelectMany(d => d.Hebrew).Distinct().Count();
        }

        statedStrong.AddRange(Unplaced(records, hebrewVerses, placed));
        await Write(english.Id, hebrew.Id, pairs, statedStrong, cancellationToken);

        var outcome = new LinkOutcome(
            false,
            records.Count - refused,
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
    }

    /// <summary>
    /// The links of verses the King James came to read as the file does after its words were loaded:
    /// a verse the loaded text garbled was refused its links, and once it is corrected it joins. Only
    /// verses none of whose words is linked to BHSA yet are drawn, so no verse is linked twice, and
    /// the Hebrew numbers are not written again — the Hebrew join held for them the first time.
    /// </summary>
    /// <param name="verses">The King James verses, by address, whose words changed.</param>
    public async Task<int> Relink(
        IReadOnlyList<MappingRecord> records,
        TahotSegmentation? segmentation,
        IReadOnlyCollection<(int Book, int Chapter, int Verse)> verses,
        CancellationToken cancellationToken = default)
    {
        var english = await db.Texts.SingleOrDefaultAsync(t => t.Slug == Bible4uTextSource.KingJames, cancellationToken);
        var hebrew = await db.Texts.SingleOrDefaultAsync(t => t.Slug == BhsaTextSource.Slug, cancellationToken);
        if (english is null || hebrew is null || verses.Count == 0)
        {
            return 0;
        }

        var englishVerses = await VerseWords(english.Id, cancellationToken);
        var wanted = verses.Where(englishVerses.ContainsKey).ToHashSet();
        var words = wanted.SelectMany(address => englishVerses[address].Select(word => word.Id)).ToList();
        var linked = (await db.LinkWords
                .Where(lw => words.Contains(lw.WordId)
                             && lw.Link!.FromTextId == english.Id && lw.Link.ToTextId == hebrew.Id)
                .Select(lw => lw.WordId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        wanted.RemoveWhere(address => englishVerses[address].Any(word => linked.Contains(word.Id)));
        if (wanted.Count == 0)
        {
            return 0;
        }

        var hebrewVerses = await VerseWords(hebrew.Id, cancellationToken);
        var pairs = new List<LinkDraft>();
        var joined = 0;
        foreach (var record in records.Where(r => wanted.Contains((r.Book, r.Chapter, r.Verse))))
        {
            if (JoinHebrew(record, hebrewVerses, out _) is not { } bhsaWords
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
            "Linked {Verses} of {Wanted} corrected King James verses to BHSA from the mapping file: {Links} links",
            joined, wanted.Count, pairs.Count);
        return pairs.Count;
    }

    /// <summary>
    /// Which BHSA word each of the Open Hebrew Bible's running word numbers names, read from this
    /// file the way the links are: a verse whose Hebrew lines up word for word gives every word its
    /// number, and the words of a verse divided differently are numbered from the placed words on
    /// either side (<see cref="RunningWordNumbers"/>). The project's other mappings write the same
    /// numbers, so this is how they reach BHSA too. A number no word took is not in the result.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, long>> WordsByRunningNumber(
        string path,
        CancellationToken cancellationToken = default)
    {
        var hebrew = await db.Texts.SingleOrDefaultAsync(t => t.Slug == BhsaTextSource.Slug, cancellationToken)
                     ?? throw new InvalidOperationException(
                         "BHSA is not loaded, and the running word numbers name its words. Load the corpus first.");
        var records = KjvBhsMapping.Read(path);
        var hebrewVerses = await VerseWords(hebrew.Id, cancellationToken);
        var placed = new Dictionary<long, int>(430_000);
        foreach (var record in records)
        {
            if (JoinHebrew(record, hebrewVerses, out _) is { } bhsa)
            {
                for (var i = 0; i < record.Hebrew.Count; i++)
                {
                    placed[bhsa[i].Id] = record.Hebrew[i].Position;
                }
            }
        }

        // The stretches between placed words are numbered by the same reading as the Strong numbers
        // are, with each entry's own running number standing where its Strong number would.
        var numbers = new Dictionary<int, (string Strong, string Gloss)>(430_000);
        foreach (var entry in records.SelectMany(record => record.Hebrew))
        {
            numbers.TryAdd(entry.Position, (entry.Position.ToString(CultureInfo.InvariantCulture), entry.Gloss));
        }

        var inTextOrder = hebrewVerses.Values
            .SelectMany(words => words)
            .OrderBy(word => word.Id)
            .Select(word => (word.Id, word.Gloss))
            .ToList();
        var byNumber = new Dictionary<int, long>(placed.Count);
        foreach (var (wordId, number) in placed)
        {
            byNumber.TryAdd(number, wordId);
        }

        foreach (var (wordId, number) in RunningWordNumbers.Between(inTextOrder, placed, numbers, SameVerse))
        {
            byNumber.TryAdd(int.Parse(number, CultureInfo.InvariantCulture), wordId);
        }

        return byNumber;
    }

    /// <summary>
    /// The BHSA words the file's Hebrew for this verse lines up with, or null where it does not.
    ///
    /// The join is positional within the verse and checked against the glosses BHSA carries, so a
    /// verse the two sides divide differently is refused even when the counts agree. It runs before
    /// the English join, so the count of refusals for glosses means what it says.
    /// </summary>
    private static List<Word>? JoinHebrew(
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

    private static void StateNumbers(
        MappingRecord record,
        List<Word> bhsa,
        List<(long WordId, string Strong)> statedStrong,
        Dictionary<long, int> placed)
    {
        for (var i = 0; i < record.Hebrew.Count; i++)
        {
            placed[bhsa[i].Id] = record.Hebrew[i].Position;
            var strong = StrongNumbers.Normalize(record.Hebrew[i].Strong);
            if (strong is not null)
            {
                statedStrong.Add((bhsa[i].Id, strong));
            }
        }
    }

    /// <summary>
    /// The numbers alone, for a corpus whose links are already written: every Hebrew word whose verse
    /// the file joins and which carries no number yet. A number already there is never replaced, so
    /// a second run writes nothing.
    /// </summary>
    private async Task<int> NumberTheUnnumbered(
        IReadOnlyList<MappingRecord> records,
        int hebrewTextId,
        CancellationToken cancellationToken)
    {
        if (!await db.Words.AnyAsync(w => w.TextId == hebrewTextId && w.StrongNumber == null, cancellationToken))
        {
            return 0;
        }

        var hebrewVerses = await VerseWords(hebrewTextId, cancellationToken);
        var statedStrong = new List<(long WordId, string Strong)>(430_000);
        var placed = new Dictionary<long, int>(430_000);
        foreach (var record in records)
        {
            if (JoinHebrew(record, hebrewVerses, out _) is { } bhsaWords)
            {
                StateNumbers(record, bhsaWords, statedStrong, placed);
            }
        }

        statedStrong.AddRange(Unplaced(records, hebrewVerses, placed));
        return await WriteStrongNumbers(statedStrong, cancellationToken);
    }

    /// <summary>
    /// The numbers of the words no verse join placed — verses the file divides differently from BHSA —
    /// read off the file's running word numbers between the placed words on either side.
    /// </summary>
    private static IEnumerable<(long WordId, string Strong)> Unplaced(
        IReadOnlyList<MappingRecord> records,
        Dictionary<(int, int, int), List<Word>> hebrewVerses,
        Dictionary<long, int> placed)
    {
        var stated = new Dictionary<int, (string Strong, string Gloss)>(430_000);
        foreach (var entry in records.SelectMany(record => record.Hebrew))
        {
            stated.TryAdd(entry.Position, (entry.Strong, entry.Gloss));
        }

        var inTextOrder = hebrewVerses.Values
            .SelectMany(words => words)
            .OrderBy(word => word.Id)
            .Select(word => (word.Id, word.Gloss))
            .ToList();
        foreach (var (wordId, strong) in RunningWordNumbers.Between(inTextOrder, placed, stated, SameVerse))
        {
            if (StrongNumbers.Normalize(strong) is { } number)
            {
                yield return (wordId, number);
            }
        }
    }

    /// <summary>The numbers without links, in a transaction of their own, which the temporary table needs.</summary>
    private async Task<int> WriteStrongNumbers(
        List<(long WordId, string Strong)> stated,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var numbered = await WriteStrongNumbers(
            (NpgsqlConnection)db.Database.GetDbConnection(), stated, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return numbered;
    }

    /// <summary>
    /// Whether the file and the loaded text spell one word. The file writes the psalm titles' names
    /// whole where the King James hyphenates them — <em>Bathsheba</em>, <em>Altaschith</em> — and
    /// its apostrophe is straight where the text's is curly.
    /// </summary>
    private static bool SameWord(string file, string loaded) =>
        string.Equals(Spelled(file), Spelled(loaded), StringComparison.OrdinalIgnoreCase);

    private static string Spelled(string word) => word.Replace("-", "").Replace('’', '\'');

    /// <summary>
    /// Lines the file's English up with the King James as loaded, over a verse whose Hebrew has
    /// already joined, and refuses rather than guessing when it cannot. The join is positional and
    /// checked against the words themselves, folded for case because the file writes the divine name
    /// in capitals and bible4u does not.
    /// </summary>
    private static List<LinkDraft>? Build(
        MappingRecord record,
        List<Word> kjv,
        List<Word> bhsa,
        IReadOnlyDictionary<int, TahotMorpheme>? stated)
    {
        var fileWords = record.English.SelectMany(segment => segment.Words).ToList();
        if (fileWords.Count != kjv.Count)
        {
            return null;
        }

        for (var i = 0; i < fileWords.Count; i++)
        {
            if (!SameWord(fileWords[i].Text, kjv[i].Text))
            {
                return null;
            }
        }

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
            drafts.Add(new LinkDraft(
                [kjv[english].Id], [bhsa[byPosition[match.HebrewPosition]].Id], match.Confidence)
            {
                Stated = match.Stated,
            });
        }

        foreach (var english in supplied)
        {
            drafts.Add(new LinkDraft([kjv[english].Id], []) { Relation = LinkRelation.Expands });
        }

        var read = 0;

        foreach (var segment in record.English)
        {
            var at = read;
            var words = kjv.GetRange(read, segment.Words.Count)
                .Where((_, offset) => !prefixes.ContainsKey(at + offset) && !supplied.Contains(at + offset))
                .ToList();
            read += segment.Words.Count;

            if (segment.RendersHebrew is null || !byPosition.TryGetValue(segment.RendersHebrew.Position, out var index))
            {
                continue;
            }

            var hebrewWord = bhsa[index];
            var position = record.Hebrew[index].Position;

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

            drafts.Add(new LinkDraft([.. words.Select(w => w.Id)], [hebrewWord.Id]) { LastHebrewPosition = position });
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
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            await WriteStrongNumbers(stated, cancellationToken);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var written = await LinkWriter.Write(
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction(),
            [
                .. drafts.Select(draft => new NewLink(
                    fromTextId,
                    toTextId,
                    draft.Omitted ? LinkRelation.Omits : draft.Relation,
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
        CancellationToken cancellationToken)
    {
        if (stated.Count == 0)
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

        await using var update = new NpgsqlCommand(
            "UPDATE word SET strong_number = s.strong_number FROM stated_strong s " +
            "WHERE word.id = s.word_id AND word.strong_number IS NULL",
            connection);
        return await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record Word(long Id, string Text, string? Gloss);

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
