using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Essenthos.Core.Zefania;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Unmatched">
/// English words carrying a Strong number that no Greek word in their verse carries. Counted and
/// **not** written as anything: the King James may be rendering a longer Greek text than this
/// corpus holds, or the match may simply have failed, and nothing here can tell those apart. Saying
/// <c>expands</c> would assert the first. Loading the Textus Receptus is what settles it.
/// </param>
internal sealed record GreekLinkOutcome(
    bool AlreadyLoaded,
    int Verses,
    int Refused,
    int Links,
    int Unambiguous,
    int Contended,
    int Unmatched,
    int SpellingDiffers,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the New Testament links are already loaded"
            : $"{Links} links over {Verses} verses in {Elapsed}: {Unambiguous} where one English word and one " +
              $"Greek word carried the number alone, {Contended} where more than one did, {Unmatched} English " +
              $"words whose number no Greek word in the verse carries, {SpellingDiffers} verses where the two " +
              $"editions spell a word differently, {Refused} verses refused";
}

/// <summary>
/// The New Testament correspondences, which **no source states**. They are this loader matching
/// Strong numbers within a verse, so every one carries <c>strong-number</c> and a confidence, and
/// none of them may be mistaken for the Old Testament's, which a file states.
///
/// The old loader silenced fifty-five English words and four passages of Matthew to make its
/// coverage look better. That list is deliberately not carried: its passage checks named no book,
/// so they silenced fifteen unintended verses in fourteen other books, and the word list hid 1,144
/// of the 4,057 words nothing matched. An unmatched word is a fact about the corpus and is counted.
/// </summary>
internal sealed class NewTestamentLinkLoader(AppDbContext db, ILogger<NewTestamentLinkLoader> logger)
{
    private const string Source = "Zefania KJV+ Strong numbers, matched within the verse";

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

    public async Task<GreekLinkOutcome> Load(string zefaniaPath, CancellationToken cancellationToken = default)
    {
        var english = await db.Texts.SingleOrDefaultAsync(t => t.Slug == "kjv", cancellationToken);
        var greek = await db.Texts.SingleOrDefaultAsync(t => t.Slug == NestleTextSource.Slug, cancellationToken);
        if (english is null || greek is null)
        {
            throw new InvalidOperationException(
                "The King James and Nestle 1904 must both be loaded before the correspondences between them can " +
                "be. Load the texts first; this reads them, it does not create them.");
        }

        if (await db.Links.AnyAsync(l => l.FromTextId == english.Id && l.ToTextId == greek.Id, cancellationToken))
        {
            logger.LogInformation("The New Testament links are already loaded; nothing to do");
            return new GreekLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var tagged = Tagged(zefaniaPath);
        var englishVerses = await VerseWords(english.Id, cancellationToken);
        var greekVerses = await VerseWords(greek.Id, cancellationToken);

        var drafts = new List<GreekLinkDraft>(200_000);
        var refused = 0;
        var unmatched = 0;
        var verses = 0;
        var spelled = 0;

        foreach (var (address, tags) in tagged)
        {
            if (!englishVerses.TryGetValue(address, out var kjv) ||
                !greekVerses.TryGetValue(address, out var nestle))
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

            verses++;
            if (agreement < 1)
            {
                spelled++;
            }
            drafts.AddRange(Build(tags, kjv, nestle, ref unmatched));
        }

        await Write(english.Id, greek.Id, drafts, cancellationToken);

        var outcome = new GreekLinkOutcome(
            false,
            verses,
            refused,
            drafts.Count,
            drafts.Count(d => d.Confidence == Unambiguous),
            drafts.Count(d => d.Confidence < Unambiguous),
            unmatched,
            spelled,
            started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// One link per Strong number per verse, naming every English word that carries it and every
    /// Greek word that does. Where several English words render one Greek word — 995 times in the
    /// New Testament against 30 in the whole Old — that is one claim about a set, not several
    /// claims each pretending to be about a pair.
    /// </summary>
    private static List<GreekLinkDraft> Build(
        List<TaggedWord> tags,
        List<Word> kjv,
        List<Word> nestle,
        ref int unmatched)
    {
        var greekByNumber = nestle
            .Where(word => word.Strong is not null)
            .GroupBy(word => word.Strong!)
            .ToDictionary(group => group.Key, group => group.Select(word => word.Id).ToList());

        var drafts = new List<GreekLinkDraft>(16);
        foreach (var group in tags
                     .Select((tag, index) => (tag.Strong, Word: kjv[index]))
                     .Where(pair => pair.Strong is not null)
                     .GroupBy(pair => pair.Strong!))
        {
            var englishWords = group.Select(pair => pair.Word.Id).ToList();
            if (!greekByNumber.TryGetValue(group.Key, out var greekWords))
            {
                unmatched += englishWords.Count;
                continue;
            }

            drafts.Add(new GreekLinkDraft(englishWords, greekWords, Confidence(englishWords.Count, greekWords.Count)));
        }

        return drafts;
    }

    private static double Confidence(int englishWords, int greekWords) => (englishWords, greekWords) switch
    {
        (1, 1) => Unambiguous,
        (1, _) or (_, 1) => OneSideContended,
        _ => BothSidesContended,
    };

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
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position)
                    .Select(r => new Word(r.Id, r.Surface, r.StrongNumber))
                    .ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        List<GreekLinkDraft> drafts,
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
        var method = EnumSpelling.Of(LinkMethod.StrongNumber);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(drafts[i].Confidence, NpgsqlDbType.Double, cancellationToken);
                await writer.WriteAsync(Source, NpgsqlDbType.Text, cancellationToken);
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

        await transaction.CommitAsync(cancellationToken);
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

    private sealed record Word(long Id, string Text, string? Strong);

    private sealed record GreekLinkDraft(List<long> English, List<long> Greek, double Confidence);
}
