using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Refused">
/// Verses where the file and the corpus do not line up, so no link was written. A refusal is the
/// point: a link written from a verse whose words do not correspond is a claim about the wrong
/// words, and it would look exactly like the 402,232 correct ones.
/// </param>
internal sealed record LinkOutcome(
    bool AlreadyLoaded,
    int Verses,
    int Refused,
    int Links,
    int EnglishWordsLinked,
    int HebrewWordsLinked,
    int HebrewWordsUnreached,
    int MultiWordLinks,
    int Omissions,
    int StrongNumbers,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Old Testament links are already loaded"
            : $"{Links} links over {Verses} verses in {Elapsed}: {EnglishWordsLinked} English and " +
              $"{HebrewWordsLinked} Hebrew words, {MultiWordLinks} naming more than one Hebrew word, " +
              $"{Omissions} naming a Hebrew word the English does not render, " +
              $"{HebrewWordsUnreached} Hebrew words reached by nothing, {StrongNumbers} Hebrew words given the " +
              $"Strong number the file states, {Refused} verses refused";
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
    private const string Source = "mapping/KJV-OT-mapped-to-BHS-full-mapping.csv";

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        """
        COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)
        """;

    /// <summary>
    /// The mapping file gives every Hebrew word a Strong number, which BHSA itself does not carry.
    /// It is a claim the same source makes about the same words, so it arrives with the links and
    /// not before them: it is only trustworthy for a verse whose join held.
    /// </summary>
    private const string StrongNumberUpdate =
        """
        CREATE TEMP TABLE stated_strong (word_id bigint PRIMARY KEY, strong_number text) ON COMMIT DROP;
        """;

    public async Task<LinkOutcome> Load(
        IReadOnlyList<MappingRecord> records,
        CancellationToken cancellationToken = default)
    {
        var english = await db.Texts.SingleOrDefaultAsync(t => t.Slug == "kjv", cancellationToken);
        var hebrew = await db.Texts.SingleOrDefaultAsync(t => t.Slug == BhsaTextSource.Slug, cancellationToken);
        if (english is null || hebrew is null)
        {
            throw new InvalidOperationException(
                "The King James and BHSA must both be loaded before the correspondences between them can be. " +
                "Load the texts first; this reads them, it does not create them.");
        }

        if (await db.Links.AnyAsync(l => l.FromTextId == english.Id && l.ToTextId == hebrew.Id, cancellationToken))
        {
            logger.LogInformation("The Old Testament links are already loaded; nothing to do");
            return new LinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var englishVerses = await VerseWords(english.Id, cancellationToken);
        var hebrewVerses = await VerseWords(hebrew.Id, cancellationToken);

        var pairs = new List<LinkDraft>(400_000);
        var stated = new List<(long WordId, string Strong)>(430_000);
        var refused = 0;
        var unreached = 0;

        foreach (var record in records)
        {
            var address = (record.Book, record.Chapter, record.Verse);
            if (!englishVerses.TryGetValue(address, out var kjvWords) ||
                !hebrewVerses.TryGetValue(address, out var bhsaWords))
            {
                refused++;
                continue;
            }

            var drafts = Build(record, kjvWords, bhsaWords);
            if (drafts is null)
            {
                refused++;
                continue;
            }

            pairs.AddRange(drafts);
            unreached += bhsaWords.Count - drafts.SelectMany(d => d.Hebrew).Distinct().Count();

            for (var i = 0; i < record.Hebrew.Count; i++)
            {
                var strong = StrongNumbers.Normalize(record.Hebrew[i].Strong);
                if (strong is not null)
                {
                    stated.Add((bhsaWords[i].Id, strong));
                }
            }
        }

        await Write(english.Id, hebrew.Id, pairs, stated, cancellationToken);

        var outcome = new LinkOutcome(
            false,
            records.Count - refused,
            refused,
            pairs.Count,
            pairs.Sum(p => p.English.Count),
            pairs.SelectMany(p => p.Hebrew).Distinct().Count(),
            unreached,
            pairs.Count(p => p.Hebrew.Count > 1),
            pairs.Count(p => p.Omitted),
            stated.DistinctBy(pair => pair.WordId).Count(),
            started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Lines the file's verse up with the corpus, and refuses rather than guessing when it cannot.
    ///
    /// The Hebrew join is positional within the verse and checked against the glosses BHSA carries;
    /// the English join is positional and checked against the words themselves, folded for case
    /// because the file writes the divine name in capitals and bible4u does not.
    /// </summary>
    private static List<LinkDraft>? Build(MappingRecord record, List<Word> kjv, List<Word> bhsa)
    {
        if (record.Hebrew.Count != bhsa.Count)
        {
            return null;
        }

        var fileWords = record.English.SelectMany(segment => segment.Words).ToList();
        if (fileWords.Count != kjv.Count)
        {
            return null;
        }

        for (var i = 0; i < fileWords.Count; i++)
        {
            if (!string.Equals(fileWords[i], kjv[i].Text, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var byPosition = new Dictionary<int, int>(record.Hebrew.Count);
        for (var i = 0; i < record.Hebrew.Count; i++)
        {
            byPosition.TryAdd(record.Hebrew[i].Position, i);
        }

        var drafts = new List<LinkDraft>(record.English.Count);
        var read = 0;

        foreach (var segment in record.English)
        {
            var words = kjv.GetRange(read, segment.Words.Count);
            read += segment.Words.Count;

            if (segment.RendersHebrew is null || !byPosition.TryGetValue(segment.RendersHebrew.Position, out var at))
            {
                continue;
            }

            var hebrewWord = bhsa[at];
            var position = record.Hebrew[at].Position;

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
                w.Position,
                w.Id,
                w.Surface,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position).Select(r => new Word(r.Id, r.Surface)).ToList());
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
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveLinkIds(connection, drafts.Count, cancellationToken);
        var renders = EnumSpelling.Of(LinkRelation.Renders);
        var omits = EnumSpelling.Of(LinkRelation.Omits);
        var method = EnumSpelling.Of(LinkMethod.StatedBySource);
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
                await writer.WriteAsync(drafts[i].Omitted ? omits : renders, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
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

                foreach (var wordId in drafts[i].Hebrew.Distinct())
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
    /// Writes the Strong numbers the file states for the Hebrew. Half a million single-row updates
    /// would be minutes; a temporary table filled by COPY and one join is seconds.
    ///
    /// The codes above H9000 are the dataset's own for the prefixes Strong's concordance never
    /// numbered — the conjunction, the article, the prefixed prepositions — so a reader looking one
    /// up in a printed concordance will not find it. They are kept because they are what the source
    /// says and because a prefix with no number is a prefix nothing can join on.
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
            "UPDATE word SET strong_number = s.strong_number FROM stated_strong s WHERE word.id = s.word_id",
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

    /// <summary>
    /// COPY cannot report the keys it generated, so a block of them is taken from the identity
    /// sequence up front and the rows are written with the ids already known.
    /// </summary>
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

    private sealed record Word(long Id, string Text);

    /// <param name="Omitted">
    /// The King James renders nothing here, so the link says so with an empty side rather than
    /// attaching the Hebrew word to whatever phrase happened to precede it.
    /// </param>
    private sealed record LinkDraft(List<long> English, List<long> Hebrew)
    {
        public int LastHebrewPosition { get; set; }

        public bool Omitted { get; init; }
    }
}
