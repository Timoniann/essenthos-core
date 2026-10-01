using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Spans">Spans of the file naming at least one BHS word, over the verses it holds.</param>
/// <param name="Placed">Of those, spans whose Chinese and whose Hebrew were both found in the corpus.</param>
/// <param name="Corroborated">
/// Placed spans naming exactly the words a link of the pair already names — FHL's numbers matched
/// within the verse, most of them. They write no link; they add the mapping's claim to that one.
/// </param>
/// <param name="Added">Placed spans naming Hebrew words no link of the pair reaches yet.</param>
/// <param name="Contradicted">
/// Placed spans whose Hebrew a link of the pair already gives to other Chinese words, or gives other
/// Hebrew words to the same Chinese. Written beside it: two answers to the same question are a fact
/// about the translation, and which one is right is not this loader's to decide.
/// </param>
/// <param name="WithoutHebrew">Spans whose running numbers name no BHSA word.</param>
/// <param name="WithoutChinese">Spans whose characters no word of the corpus's text holds.</param>
internal sealed record OpenHebrewCuvOutcome(
    string Slug,
    bool AlreadyLoaded,
    int Spans,
    int Placed,
    int Corroborated,
    int Added,
    int Contradicted,
    int WithoutHebrew,
    int WithoutChinese,
    int Verses,
    int VersesRealigned,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"{Slug}: the Open Hebrew Bible's mapping is already linked"
            : $"{Slug}: {Spans} mapped spans over {Verses} verses in {Elapsed}, {Placed} placed on both sides " +
              $"({(Spans == 0 ? 0 : (double)Placed / Spans):P1}): {Corroborated} corroborate a link already here, " +
              $"{Added} name Hebrew no link reaches, {Contradicted} disagree with a link already here; " +
              $"{WithoutHebrew} name no BHSA word, {WithoutChinese} no word of ours; {VersesRealigned} verses " +
              "whose characters differ from the module's were lined up letter by letter";
}

/// <summary>
/// The Chinese Union Version's Old Testament linked to BHSA by the Open Hebrew Bible's mapping
/// (<see cref="OpenHebrewCuvMapping"/>): Eliran Wong's statement of which BHS word each of FHL's
/// spans renders. Where FHL's numbers only say which lexeme a span renders, and the match within the
/// verse has to guess which occurrence, the mapping names the word.
///
/// <para>
/// **Its links are <c>stated-by-source</c>**, as the King James's from the same project are, and
/// carry no confidence. Where a link of the pair already names exactly the same words, the mapping
/// is a claim on it instead of a second link, which is what makes the agreement countable.
/// </para>
///
/// <para>
/// **The file's Chinese is laid onto ours by its characters.** The spans are FHL's and so are the
/// words the corpus loaded from CrossWire's module, so in all but about a hundred verses the two
/// print the same letters in the same order and a span is the words its letters fall in. Where they
/// differ — the file prints FHL's notes inside the verse, the module beside it — the letters are
/// lined up first and only the ones both print carry a span over. The simplified text is FHL's
/// conversion of the same spans, so it takes the traditional text's words by their place in the
/// verse, wherever the two hold as many words.
/// </para>
/// </summary>
internal sealed class OpenHebrewCuvLinkLoader(
    AppDbContext db,
    OldTestamentLinkLoader hebrew,
    ILogger<OpenHebrewCuvLinkLoader> logger)
{
    public const string Credit = Sources.OpenHebrewCuvCredit;

    /// <summary>Where the King James mapping is, whose Hebrew lines say which BHSA word each running number is.</summary>
    public static readonly string[] KingJamesMapping = ["mapping", "KJV-OT-mapped-to-BHS-full-mapping.csv"];

    public static readonly string[] File = ["OpenHebrewBible", "CUV-OT-mapped-to-BHS.csv"];

    private const string Note = "the Open Hebrew Bible's mapping names the same words";

    private const string LinkImport =
        "COPY link (id, from_text_id, to_text_id, relation, method, confidence, source) FROM STDIN (FORMAT BINARY)";

    private const string LinkWordImport = "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    /// <param name="Words">Indexes into the verse's words, in order.</param>
    /// <param name="Positions">The running numbers the spans name.</param>
    internal sealed record Placement(IReadOnlyList<int> Words, IReadOnlyList<int> Positions);

    /// <param name="replace">
    /// Remove the links FHL's numbers drew for the pair first. Matched again afterwards
    /// (<see cref="UnionStrongLinkLoader"/>), the numbers leave the words this mapping states to it and
    /// become a claim on every link they agree with, where matched before it they stand beside it as
    /// a second link wherever they could not tell one occurrence of a lexeme from another.
    /// </param>
    public async Task<IReadOnlyList<OpenHebrewCuvOutcome>> Load(
        string resources,
        bool replace = false,
        CancellationToken cancellationToken = default)
    {
        var numbers = await hebrew.WordsByRunningNumber(
            ResourcePaths.File(resources, KingJamesMapping), cancellationToken);
        var verses = OpenHebrewCuvMapping.Read(ResourcePaths.File(resources, File));
        var bhsa = await db.Texts.SingleAsync(t => t.Slug == BhsaTextSource.Slug, cancellationToken);

        var traditional = await Words(SwordTextSource.ChineseUnion, cancellationToken);
        var placements = new Dictionary<(int, int, int), List<Placement>>(verses.Count);
        var realigned = 0;
        foreach (var verse in verses)
        {
            if (traditional.Words.TryGetValue((verse.Book, verse.Chapter, verse.Verse), out var words))
            {
                placements[(verse.Book, verse.Chapter, verse.Verse)] =
                    Place(verse.Runs, [.. words.Select(word => word.Surface)], out var lined);
                realigned += lined ? 1 : 0;
            }
        }

        var outcomes = new List<OpenHebrewCuvOutcome>();
        foreach (var slug in new[] { SwordTextSource.ChineseUnion, SwordTextSource.ChineseUnionSimplified })
        {
            var text = slug == SwordTextSource.ChineseUnion ? traditional : await Words(slug, cancellationToken);
            if (replace)
            {
                var removed = await db.Links
                    .Where(l => l.FromTextId == text.Id && l.ToTextId == bhsa.Id
                                && l.Source != null && l.Source.StartsWith(UnionStrongLinkLoader.Credit))
                    .ExecuteDeleteAsync(cancellationToken);
                logger.LogInformation("{Slug}: {Removed} links of FHL's numbers removed, to be matched again after", slug, removed);
            }

            outcomes.Add(await Write(text, bhsa.Id, verses, placements, traditional, numbers, realigned, cancellationToken));
            logger.LogInformation("{Outcome}", outcomes[^1]);
        }

        return outcomes;
    }

    /// <summary>
    /// The spans of one verse laid onto its words: each span becomes the words its letters fall in,
    /// and spans sharing a word are one placement, since a word can be one rendering only once.
    /// </summary>
    /// <param name="realigned">Whether the two printed different letters and had to be lined up.</param>
    internal static List<Placement> Place(IReadOnlyList<OhbRun> runs, IReadOnlyList<string> words, out bool realigned)
    {
        var (fileLetters, fileOwners) = Letters(runs.Select(run => run.Text));
        var (ourLetters, ourOwners) = Letters(words);
        var spanWords = runs.Select(_ => new SortedSet<int>()).ToArray();

        realigned = fileLetters != ourLetters;
        foreach (var (file, ours) in realigned ? Lined(fileLetters, ourLetters) : Same(fileLetters.Length))
        {
            spanWords[fileOwners[file]].Add(ourOwners[ours]);
        }

        var parent = Enumerable.Range(0, runs.Count).ToArray();
        int Find(int run) => parent[run] == run ? run : parent[run] = Find(parent[run]);
        var claimedBy = new Dictionary<int, int>();
        for (var run = 0; run < runs.Count; run++)
        {
            if (runs[run].Positions.Count == 0)
            {
                continue;
            }

            foreach (var word in spanWords[run])
            {
                if (claimedBy.TryGetValue(word, out var other))
                {
                    parent[Find(run)] = Find(other);
                }
                else
                {
                    claimedBy[word] = run;
                }
            }
        }

        return
        [
            .. Enumerable.Range(0, runs.Count)
                .Where(run => runs[run].Positions.Count > 0)
                .GroupBy(Find)
                .Select(group => new Placement(
                    [.. group.SelectMany(run => spanWords[run]).Distinct().Order()],
                    [.. group.SelectMany(run => runs[run].Positions).Distinct()])),
        ];
    }

    private static (string Letters, List<int> Owners) Letters(IEnumerable<string> pieces)
    {
        var letters = new StringBuilder();
        var owners = new List<int>();
        var index = 0;
        foreach (var piece in pieces)
        {
            foreach (var rune in piece.EnumerateRunes())
            {
                if (Rune.IsLetterOrDigit(rune))
                {
                    letters.Append(rune.ToString());
                    for (var unit = 0; unit < rune.Utf16SequenceLength; unit++)
                    {
                        owners.Add(index);
                    }
                }
            }

            index++;
        }

        return (letters.ToString(), owners);
    }

    private static IEnumerable<(int, int)> Same(int length) => Enumerable.Range(0, length).Select(i => (i, i));

    /// <summary>The longest common run of letters, as pairs of positions in each string.</summary>
    private static List<(int, int)> Lined(string file, string ours)
    {
        var longest = new int[file.Length + 1, ours.Length + 1];
        for (var f = file.Length - 1; f >= 0; f--)
        {
            for (var o = ours.Length - 1; o >= 0; o--)
            {
                longest[f, o] = file[f] == ours[o]
                    ? longest[f + 1, o + 1] + 1
                    : Math.Max(longest[f + 1, o], longest[f, o + 1]);
            }
        }

        var pairs = new List<(int, int)>(Math.Min(file.Length, ours.Length));
        for (int f = 0, o = 0; f < file.Length && o < ours.Length;)
        {
            if (file[f] == ours[o])
            {
                pairs.Add((f, o));
                f++;
                o++;
            }
            else if (longest[f + 1, o] >= longest[f, o + 1])
            {
                f++;
            }
            else
            {
                o++;
            }
        }

        return pairs;
    }

    private sealed record CorpusText(
        string Slug,
        int Id,
        Dictionary<(int Book, int Chapter, int Verse), List<(long Id, string Surface)>> Words);

    private async Task<CorpusText> Words(string slug, CancellationToken cancellationToken)
    {
        var text = await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"{slug} is not loaded, and the mapping is laid onto the words the corpus holds. Load the corpus first.");
        var rows = await db.Words
            .Where(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal <= BookReferences.OldTestamentBookCount)
            .Select(w => new { w.Id, w.Surface, w.Position, w.Verse!.Book!.CanonicalOrdinal, w.Verse.ChapterNumber, w.Verse.Number })
            .ToListAsync(cancellationToken);

        return new CorpusText(slug, text.Id, rows
            .GroupBy(row => (row.CanonicalOrdinal, row.ChapterNumber, row.Number))
            .ToDictionary(
                verse => verse.Key,
                verse => verse.OrderBy(row => row.Position).Select(row => (row.Id, row.Surface)).ToList()));
    }

    private async Task<OpenHebrewCuvOutcome> Write(
        CorpusText text,
        int bhsaId,
        IReadOnlyList<OhbVerse> verses,
        Dictionary<(int, int, int), List<Placement>> placements,
        CorpusText traditional,
        IReadOnlyDictionary<int, long> numbers,
        int realigned,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        if (await db.Links.AnyAsync(
                l => l.FromTextId == text.Id && l.ToTextId == bhsaId && l.Source != null && l.Source.StartsWith(Credit),
                cancellationToken)
            || await db.LinkClaims.AnyAsync(
                c => c.Source.StartsWith(Credit) && c.Link!.FromTextId == text.Id && c.Link.ToTextId == bhsaId,
                cancellationToken))
        {
            return new OpenHebrewCuvOutcome(text.Slug, true, 0, 0, 0, 0, 0, 0, 0, 0, 0, clock.Elapsed);
        }

        var (shapes, chineseOf, hebrewOf) = await Existing(text.Id, bhsaId, cancellationToken);
        int spans = 0, withoutHebrew = 0, withoutChinese = 0, contradicted = 0, added = 0;
        var corroborated = new List<long>();
        var drafts = new List<(long[] Chinese, long[] Hebrew)>();

        foreach (var verse in verses)
        {
            var key = (verse.Book, verse.Chapter, verse.Verse);
            if (!placements.TryGetValue(key, out var placed))
            {
                continue;
            }

            text.Words.TryGetValue(key, out var words);
            var sameWords = words is not null && words.Count == traditional.Words[key].Count;
            foreach (var placement in placed)
            {
                spans++;
                long[] hebrewWords = [.. placement.Positions.Where(numbers.ContainsKey).Select(p => numbers[p]).Distinct().Order()];
                if (hebrewWords.Length == 0)
                {
                    withoutHebrew++;
                    continue;
                }

                if (!sameWords || placement.Words.Count == 0)
                {
                    withoutChinese++;
                    continue;
                }

                long[] chineseWords = [.. placement.Words.Select(i => words![i].Id)];
                if (shapes.TryGetValue(Shape(chineseWords, hebrewWords), out var link))
                {
                    corroborated.Add(link);
                    continue;
                }

                var disagrees = hebrewWords.Any(word => chineseOf.TryGetValue(word, out var others) && !others.SetEquals(chineseWords))
                                || chineseWords.Any(word => hebrewOf.TryGetValue(word, out var others) && !others.SetEquals(hebrewWords));
                contradicted += disagrees ? 1 : 0;
                added += disagrees ? 0 : 1;
                drafts.Add((chineseWords, hebrewWords));
            }
        }

        await Store(text.Id, bhsaId, drafts, corroborated, cancellationToken);
        return new OpenHebrewCuvOutcome(
            text.Slug, false, spans, corroborated.Count + drafts.Count, corroborated.Count, added, contradicted,
            withoutHebrew, withoutChinese, placements.Count, realigned, clock.Elapsed);
    }

    private static string Shape(IEnumerable<long> from, IEnumerable<long> to) =>
        string.Join(',', from.Order()) + "|" + string.Join(',', to.Order());

    /// <summary>
    /// The pair's links by the words they name, and which words each side's words are already given
    /// to. The aligner's are left out: a guess is not an answer a stated mapping agrees or disagrees with.
    /// </summary>
    private async Task<(Dictionary<string, long> Shapes, Dictionary<long, HashSet<long>> ChineseOf, Dictionary<long, HashSet<long>> HebrewOf)>
        Existing(int fromTextId, int toTextId, CancellationToken cancellationToken)
    {
        var rows = await db.LinkWords
            .Where(word => word.Link!.FromTextId == fromTextId
                           && word.Link!.ToTextId == toTextId
                           && word.Link!.Method != LinkMethod.Aligner)
            .Select(word => new { word.LinkId, word.WordId, word.Side })
            .ToListAsync(cancellationToken);

        var shapes = new Dictionary<string, long>(StringComparer.Ordinal);
        var chineseOf = new Dictionary<long, HashSet<long>>();
        var hebrewOf = new Dictionary<long, HashSet<long>>();
        foreach (var link in rows.GroupBy(row => row.LinkId))
        {
            var from = link.Where(w => w.Side == LinkSide.From).Select(w => w.WordId).ToList();
            var to = link.Where(w => w.Side == LinkSide.To).Select(w => w.WordId).ToList();
            shapes[Shape(from, to)] = link.Key;
            foreach (var word in to)
            {
                (chineseOf.TryGetValue(word, out var set) ? set : chineseOf[word] = []).UnionWith(from);
            }

            foreach (var word in from)
            {
                (hebrewOf.TryGetValue(word, out var set) ? set : hebrewOf[word] = []).UnionWith(to);
            }
        }

        return (shapes, chineseOf, hebrewOf);
    }

    private async Task Store(
        int fromTextId,
        int toTextId,
        List<(long[] Chinese, long[] Hebrew)> drafts,
        List<long> corroborated,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        if (drafts.Count > 0)
        {
            long firstId;
            await using (var reserve = new NpgsqlCommand(
                             "SELECT setval(pg_get_serial_sequence('link', 'id'), " +
                             "coalesce((SELECT max(id) FROM link), 0) + @count) - @count + 1", connection))
            {
                reserve.Parameters.AddWithValue("count", drafts.Count);
                firstId = (long)(await reserve.ExecuteScalarAsync(cancellationToken))!;
            }

            var renders = EnumSpelling.Of(LinkRelation.Renders);
            var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
            await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
            {
                for (var i = 0; i < drafts.Count; i++)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(stated, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteNullAsync(cancellationToken);
                    await writer.WriteAsync(Credit, NpgsqlDbType.Text, cancellationToken);
                }

                await writer.CompleteAsync(cancellationToken);
            }

            var from = EnumSpelling.Of(LinkSide.From);
            var to = EnumSpelling.Of(LinkSide.To);
            await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
            {
                for (var i = 0; i < drafts.Count; i++)
                {
                    foreach (var (words, side) in new[] { (drafts[i].Chinese, from), (drafts[i].Hebrew, to) })
                    {
                        foreach (var word in words)
                        {
                            await writer.StartRowAsync(cancellationToken);
                            await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                            await writer.WriteAsync(word, NpgsqlDbType.Bigint, cancellationToken);
                            await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
                        }
                    }
                }

                await writer.CompleteAsync(cancellationToken);
            }

            await LinkClaims.Record(connection, transaction, firstId, drafts.Count, cancellationToken);
        }

        await LinkClaims.Corroborate(
            connection, transaction, corroborated.Distinct().ToList(), LinkMethod.StatedBySource, null, Credit, Note,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
