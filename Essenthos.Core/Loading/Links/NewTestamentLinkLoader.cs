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
/// verb's person. These are the 35.6% of the New Testament that carries no Strong number at all,
/// and they are <c>lexical</c>, never <c>strong-number</c>.
/// </param>
/// <param name="StrongNumbers">
/// English words given the Strong number the tagged edition puts on them. It is the evidence these
/// links were built from, and a corpus that keeps the conclusion and throws the evidence away
/// cannot afterwards be asked whether the conclusion follows.
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
    int SpellingDiffers,
    int StrongNumbers,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the New Testament links are already loaded"
            : $"{Links} links over {Verses} verses in {Elapsed}: {Unambiguous} where one English word and one " +
              $"Greek word carried the number alone, {Contended} where more than one did, {Resolved} English " +
              $"words matched through the lemma the dictionary names for their form over {Redirects} numbers it " +
              $"resolved, {Recovered} untagged English words given a Greek word by its morphology, {Unmatched} " +
              $"English words whose number no Greek word in the verse carries, {SpellingDiffers} verses where " +
              $"the two editions spell a word differently, {StrongNumbers} English words given the number the " +
              $"tagged edition states, {Refused} verses refused";
}

/// <summary>
/// The New Testament correspondences, which **no source states**. They are this loader matching
/// Strong numbers within a verse, so every one carries <c>strong-number</c> and a confidence, and
/// none of them may be mistaken for the Old Testament's, which a file states.
///
/// Two things reach past a bare number match, and both are labelled apart from it. The concordance
/// numbers Greek by the form and the editions tag it by the lemma, so the dictionary's own
/// derivations are read to join the two — and every redirect is measured against the corpus before
/// it is used. And the tagged edition numbers content words only, so the article, the case endings
/// and the verb's person are recovered from the morphology both editions state, as <c>lexical</c>.
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
    /// How much of a verse has to match word for word before the tagged text and the loaded King
    /// James are taken to be the same verse.
    ///
    /// They are two editions of one translation and they spell names differently — Boaz against
    /// Booz, Judea against Judaea, worshiped against worshipped — so demanding every word match
    /// refused 883 verses that are plainly the same verse. Demanding none would accept a verse whose
    /// words merely happen to number the same. Where the counts agree and most of the words do, the
    /// nth word of one is the nth word of the other whatever it is spelled like.
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
        CREATE TEMP TABLE tagged_strong (word_id bigint PRIMARY KEY, strong_number text) ON COMMIT DROP;
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
            return new GreekLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var tagged = Tagged(zefaniaPath);
        var englishVerses = await VerseWords(english.Id, cancellationToken);
        var greekVerses = await VerseWords(greek.Id, cancellationToken);

        var refused = 0;
        var spelled = 0;
        var pairs = new List<VersePair>(8_000);

        foreach (var (address, tags) in tagged)
        {
            if (!englishVerses.TryGetValue(address, out var kjv) ||
                !greekVerses.TryGetValue(address, out var witness))
            {
                continue;
            }

            if (tags.Count != kjv.Count)
            {
                refused++;
                continue;
            }

            var agreement = Agreement(tags, kjv);
            if (agreement < SameVerse)
            {
                refused++;
                continue;
            }

            if (agreement < 1)
            {
                spelled++;
            }

            pairs.Add(new VersePair(tags, kjv, witness));
        }

        var resolution = await Resolution(pairs, cancellationToken);

        var drafts = new List<GreekLinkDraft>(300_000);
        var stated = new List<(long WordId, string Strong)>(120_000);
        var unmatched = 0;
        var resolved = 0;

        foreach (var pair in pairs)
        {
            drafts.AddRange(Build(pair, resolution, ref unmatched, ref resolved));

            for (var i = 0; i < pair.Tags.Count; i++)
            {
                if (pair.Tags[i].Strong is { } strong)
                {
                    stated.Add((pair.English[i].Id, strong));
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
            spelled,
            stated.DistinctBy(pair => pair.WordId).Count(),
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

            foreach (var tag in pair.Tags.Where(tag => tag.Strong is not null))
            {
                yield return new NumberOccurrence(tag.Strong!, numbers);
            }
        }
    }

    /// <summary>
    /// One link per effective Strong number per verse, naming every English word that carries it
    /// and every Greek word that does. Where several English words render one Greek word — 995
    /// times in the New Testament against 30 in the whole Old — that is one claim about a set, not
    /// several claims each pretending to be about a pair.
    ///
    /// The effective number is the tagged edition's own where the Greek writes it, and the one the
    /// dictionary resolves it to otherwise. Grouping on it rather than on the tag is what lets a
    /// verse's ἐστί and its εἰμί arrive at the same link instead of at two claiming the same word.
    /// </summary>
    private static List<GreekLinkDraft> Build(
        VersePair pair,
        IReadOnlyDictionary<string, NumberRedirect> resolution,
        ref int unmatched,
        ref int resolved)
    {
        var byNumber = pair.Greek
            .Where(word => word.Strong is not null)
            .GroupBy(word => word.Strong!)
            .ToDictionary(group => group.Key, group => group.Select(word => word.Id).ToList(), StringComparer.Ordinal);

        var order = new List<string>(16);
        var groups = new Dictionary<string, Group>(16, StringComparer.Ordinal);

        for (var i = 0; i < pair.Tags.Count; i++)
        {
            if (pair.Tags[i].Strong is not { } strong)
            {
                continue;
            }

            if (byNumber.TryGetValue(strong, out var direct))
            {
                Collect(order, groups, strong, pair.English[i].Id, direct, false);
                continue;
            }

            if (resolution.TryGetValue(strong, out var redirect)
                && Together(byNumber, redirect.Numbers) is { } words)
            {
                Collect(order, groups, string.Join('+', redirect.Numbers), pair.English[i].Id, words, true);
                resolved++;
                continue;
            }

            unmatched++;
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
            // do not, nothing here can choose, and the set stands.
            if (group.English.Count == group.Greek.Count && group.English.Count > 1)
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

            drafts.Add(new GreekLinkDraft(
                group.English,
                group.Greek,
                Confidence(group.English.Count, group.Greek.Count, group.Resolved),
                group.English.Count == 1 && group.Greek.Count == 1 ? GreekMatch.Unambiguous : GreekMatch.Contended));
        }

        drafts.AddRange(Recover(pair, drafts));
        return drafts;
    }

    /// <summary>
    /// The untagged English words, given the Greek their phrase's own word states. They hang off the
    /// links already built: a function word is only attached through a content word whose Greek is
    /// a single settled word, so nothing here can widen a set that was already a guess.
    /// </summary>
    private static List<GreekLinkDraft> Recover(VersePair pair, List<GreekLinkDraft> drafts)
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

            if (draft.Greek.Count != 1)
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
            [.. pair.Tags.Select(tag => tag.Strong)],
            anchors,
            [.. pair.Greek.Select(word => word.Morphology)],
            claimed);

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

    private static void Collect(
        List<string> order,
        Dictionary<string, Group> groups,
        string key,
        long english,
        List<long> greek,
        bool resolved)
    {
        if (!groups.TryGetValue(key, out var group))
        {
            group = new Group(greek);
            groups[key] = group;
            order.Add(key);
        }

        group.English.Add(english);
        group.Resolved |= resolved;
    }

    private static double Confidence(int englishWords, int greekWords, bool resolved) => Lower(
        (englishWords, greekWords) switch
        {
            (1, 1) => Unambiguous,
            (1, _) or (_, 1) => OneSideContended,
            _ => BothSidesContended,
        },
        resolved);

    // Rounded because the column is read by people and 0.3 less 0.1 is 0.19999999999999998.
    private static double Lower(double confidence, bool resolved) =>
        resolved ? Math.Round(confidence - ResolvedNumber, 2) : confidence;

    /// <summary>How much of the verse the two editions write the same way.</summary>
    private static double Agreement(List<TaggedWord> tags, List<Word> kjv)
    {
        if (tags.Count == 0)
        {
            return 1;
        }

        var same = 0;
        for (var i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i].Text, kjv[i].Text, StringComparison.OrdinalIgnoreCase))
            {
                same++;
            }
        }

        return (double)same / tags.Count;
    }

    private static Dictionary<(int, int, int), List<TaggedWord>> Tagged(string path)
    {
        var bible = new ZefaniaParser().Parse(File.ReadAllText(path));
        var verses = new Dictionary<(int, int, int), List<TaggedWord>>(8_000);

        foreach (var book in bible.Books)
        {
            var canonical = BibleBookAbbreviation.GetAbbreviation(book.ShortName)
                            ?? BibleBookAbbreviation.GetByOrdinal(book.Number);
            if (canonical is null)
            {
                continue;
            }

            foreach (var chapter in book.Chapters)
            {
                foreach (var verse in chapter.Verses)
                {
                    verses[(canonical.Ordinal, chapter.Number, verse.Number)] = verse.Words
                        .Select(word => new TaggedWord(word.Text, StrongTags.Read(word.StrongNo)))
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
        var byNumber = EnumSpelling.Of(LinkMethod.StrongNumber);
        var lexical = EnumSpelling.Of(LinkMethod.Lexical);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);
        var source = Source(greekSlug);
        var recovered = RecoveredSource(greekSlug);

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                var fromMorphology = drafts[i].Kind == GreekMatch.FunctionWord;
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(fromMorphology ? lexical : byNumber, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(drafts[i].Confidence, NpgsqlDbType.Double, cancellationToken);
                await writer.WriteAsync(fromMorphology ? recovered : source, NpgsqlDbType.Text, cancellationToken);
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
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Puts the tagged edition's Strong numbers on the English words themselves, so that the links
    /// built from them can afterwards be checked against something. It runs once per Greek witness
    /// over the same tags, which is why it writes only where the number is not already there.
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
                         "COPY tagged_strong (word_id, strong_number) FROM STDIN (FORMAT BINARY)", cancellationToken))
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
            "UPDATE word SET strong_number = s.strong_number FROM tagged_strong s " +
            "WHERE word.id = s.word_id AND word.strong_number IS DISTINCT FROM s.strong_number",
            connection);
        await update.ExecuteNonQueryAsync(cancellationToken);
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

    private sealed record TaggedWord(string Text, string? Strong);

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
    }

    private sealed record Group(List<long> Greek)
    {
        public List<long> English { get; } = [];

        public bool Resolved { get; set; }
    }

    private sealed record GreekLinkDraft(List<long> English, List<long> Greek, double Confidence, GreekMatch Kind);
}
