using System.Diagnostics;
using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Named">Name pairs the spelling and the order settled.</param>
/// <param name="Checked">Of those, the ones whose source name another method links to the target.</param>
/// <param name="Agreed">Of the checked, the ones that method links to the same word.</param>
/// <param name="NameLinks">The aligner's links with a name at either end.</param>
/// <param name="Refused">Aligner links that contradict what the names settled.</param>
/// <param name="Held">Of the refused, the ones a review names, which are left for the review to settle.</param>
/// <param name="Overruled">Of the refused on a word another method links, how many it links elsewhere.</param>
/// <param name="OverruledChecked">Of the refused, the ones on a word another method links.</param>
/// <param name="Added">Settled pairs no link joins yet, on a source name no other method answers for.</param>
/// <param name="CrossedBefore">The aligner's links between names that cross another such link in their verse.</param>
/// <param name="CrossedAfter">The same once the contradicting links are gone and the settled pairs added.</param>
internal sealed record NameListOutcome(
    string From,
    string To,
    string Scope,
    int Verses,
    int Named,
    int Checked,
    int Agreed,
    int NameLinks,
    int Refused,
    int Held,
    int Overruled,
    int OverruledChecked,
    int Added,
    int CrossedBefore,
    int CrossedAfter)
{
    public override string ToString() =>
        $"{From} to {To}, {Scope}: {Named} name pairs settled by spelling and order over {Verses} verses; " +
        (Checked > 0
            ? $"{Agreed} of the {Checked} another method also links agree with it ({(double)Agreed / Checked:P1}); "
            : "no other method links these names; ") +
        $"of {NameLinks} aligner links touching a name, {Refused} contradict it ({Held} of them held by a review" +
        (OverruledChecked > 0
            ? $"; where another method links the same word, it links it elsewhere for {Overruled} of {OverruledChecked}"
            : string.Empty) +
        $"), and {Added} settled pairs are linked by nothing. Name-to-name links out of order with another: " +
        $"{CrossedBefore} before, {CrossedAfter} after";
}

/// <summary>
/// <see cref="NameLists"/> over links already written: which of the aligner's links in a pair of
/// texts put a name against the wrong name of its verse, and which pairs of names no link joins.
///
/// <para>
/// Aligning a pair again settles the names as it reads the model's answer. This is for the links
/// already in the corpus, which would otherwise wait for every pair to be aligned and composed again
/// — hours each — for a correction that needs only the words and the links. Without
/// <c>--apply</c> it reports and writes nothing; with it, the contradicting links go and the
/// settled pairs are written, as the aligner's, at <see cref="NameLists.Settled"/>.
/// </para>
/// </summary>
internal sealed class NameListPass(AppDbContext db, AlignmentPipeline aligner, ILogger<NameListPass> logger)
{
    internal const string Source = "the names of the verse, paired by spelling and order";

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    /// <param name="chapters">
    /// Chapters to report on apart from the whole, as canonical book and chapter. Applying always
    /// covers every verse read.
    /// </param>
    /// <param name="books">Canonical books to read, or all of them.</param>
    public async Task<string> Run(
        string fromSlug,
        string toSlug,
        IReadOnlySet<(int Book, int Chapter)> chapters,
        IReadOnlySet<int>? books,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var from = await db.Texts.SingleAsync(t => t.Slug == fromSlug, cancellationToken);
        var to = await db.Texts.SingleAsync(t => t.Slug == toSlug, cancellationToken);
        var source = await aligner.Named(fromSlug, books, cancellationToken);
        var target = await aligner.Named(toSlug, books, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var links = await Links(connection, from.Id, to.Id, cancellationToken);
            var reviewed = await Reviewed(connection, from.Id, to.Id, cancellationToken);

            var whole = new Tally();
            var within = new Tally();
            var refused = new HashSet<long>();
            var added = new List<(long From, long To)>();

            foreach (var address in source.Keys.Intersect(target.Keys).Order())
            {
                var verse = Settle(source[address], target[address], links, reviewed);
                whole.Add(verse);
                if (chapters.Contains((address.Item1, address.Item2)))
                {
                    within.Add(verse);
                }

                refused.UnionWith(verse.Refused);
                added.AddRange(verse.Added);
            }

            var adding = added.Distinct().ToList();
            var report = new StringBuilder()
                .AppendLine(whole.Outcome(fromSlug, toSlug, "every verse read").ToString());
            if (chapters.Count > 0)
            {
                report.AppendLine(within.Outcome(fromSlug, toSlug, "the chapters asked for").ToString())
                    .AppendLine(await Examples(connection, within, cancellationToken));
            }

            if (apply)
            {
                await Write(connection, from.Id, to.Id, refused, adding, cancellationToken);
                report.AppendLine($"Withdrew {refused.Count} links and wrote {adding.Count}.");
            }

            logger.LogInformation("Settled the names of {From} against {To} in {Elapsed}", fromSlug, toSlug,
                started.Elapsed);
            return report.ToString();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>One verse: what the names settle, measured against the links the pair already has.</summary>
    internal static VerseNames Settle(
        List<AlignmentPipeline.Word> source,
        List<AlignmentPipeline.Word> target,
        StoredLinks links,
        IReadOnlySet<long> reviewed)
    {
        var targetIndex = target.Select((word, at) => (word.Id, at)).ToDictionary(pair => pair.Id, pair => pair.at);
        var proposed = source
            .SelectMany((word, s) => links.Guessed[word.Id]
                .Where(guess => targetIndex.ContainsKey(guess.To))
                .Select(guess => (s, targetIndex[guess.To])))
            .ToHashSet();
        var sourceNames = Names([.. source.Select(word => word.Name)]);
        var targetNames = Names(NameLists.Recognised(
            [.. source.Select(word => word.Name)],
            [.. target.Select(word => word.Name)],
            [.. target.Select(word => word.Letters)],
            proposed));
        var matched = NameLists.Match(sourceNames, targetNames, proposed);
        var matchedBack = matched.ToDictionary(pair => pair.Value, pair => pair.Key);
        var sourceAt = sourceNames.Select(name => name.At).ToHashSet();
        var targetAt = targetNames.Select(name => name.At).ToHashSet();
        var verse = new VerseNames { Verses = 1 };

        for (var s = 0; s < source.Count; s++)
        {
            foreach (var (link, toWord) in links.Guessed[source[s].Id])
            {
                if (!targetIndex.TryGetValue(toWord, out var t) || (!sourceAt.Contains(s) && !targetAt.Contains(t)))
                {
                    continue;
                }

                verse.NameLinks.Add(link);
                if (NameLists.Contradicts(s, t, matched, matchedBack, sourceAt, targetAt))
                {
                    (reviewed.Contains(link) ? verse.Held : verse.Refused).Add(link);
                    var stated = links.Stated[source[s].Id].ToList();
                    if (stated.Count > 0)
                    {
                        verse.OverruledChecked++;
                        verse.Overruled += stated.Contains(toWord) ? 0 : 1;
                    }
                }
            }
        }

        var before = new List<(int, int, long)>();
        var after = new List<(int, int, long)>();
        for (var s = 0; s < source.Count; s++)
        {
            foreach (var (link, toWord) in links.Guessed[source[s].Id])
            {
                if (targetIndex.TryGetValue(toWord, out var t) && sourceAt.Contains(s) && targetAt.Contains(t))
                {
                    before.Add((s, t, link));
                    if (!verse.Refused.Contains(link) && !verse.Held.Contains(link))
                    {
                        after.Add((s, t, link));
                    }
                }
            }
        }

        foreach (var (s, t) in matched)
        {
            verse.Named++;
            var sourceWord = source[s].Id;
            var targetWord = target[t].Id;
            var stated = links.Stated[sourceWord].ToList();
            if (stated.Count > 0)
            {
                verse.Checked++;
                verse.Agreed += stated.Contains(targetWord) ? 1 : 0;
                continue;
            }

            if (!links.Guessed[sourceWord].Any(guess => guess.To == targetWord))
            {
                verse.Added.Add((sourceWord, targetWord));
                after.Add((s, t, -verse.Added.Count));
            }
        }

        verse.CrossedBefore = Crossing(before);
        verse.CrossedAfter = Crossing(after);

        return verse;
    }

    private static int Crossing(List<(int Source, int Target, long Link)> pairs) =>
        pairs.Count(one => pairs.Any(other =>
            (one.Source - other.Source) * (one.Target - other.Target) < 0));

    private static List<(int At, string Skeleton)> Names(IReadOnlyList<string?> names) =>
        [.. names.Select((name, at) => (At: at, Name: name))
            .Where(name => name.Name is not null)
            .Select(name => (name.At, name.Name!))];

    /// <summary>
    /// The pair's links, one word to one word: the aligner's by source word with the link each came
    /// from, and every other method's by source word — a phrase a source states answers for each of
    /// its words.
    /// </summary>
    private static async Task<StoredLinks> Links(
        NpgsqlConnection connection,
        int fromTextId,
        int toTextId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT l.id, l.method = 'aligner', f.word_id, t.word_id
            FROM link l
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            JOIN link_word t ON t.link_id = l.id AND t.side = 'to'
            WHERE l.from_text_id = @from AND l.to_text_id = @to
            """, connection);
        command.Parameters.AddWithValue("from", fromTextId);
        command.Parameters.AddWithValue("to", toTextId);
        command.CommandTimeout = 600;

        var guessed = new List<(long From, long Link, long To)>(600_000);
        var stated = new List<(long From, long To)>(600_000);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetBoolean(1))
            {
                guessed.Add((reader.GetInt64(2), reader.GetInt64(0), reader.GetInt64(3)));
            }
            else
            {
                stated.Add((reader.GetInt64(2), reader.GetInt64(3)));
            }
        }

        return new StoredLinks(
            guessed.ToLookup(row => row.From, row => (row.Link, row.To)),
            stated.ToLookup(row => row.From, row => row.To));
    }

    /// <summary>Links a review of the aligner names. Deleting one would lose the verdict, so they stay.</summary>
    private static async Task<HashSet<long>> Reviewed(
        NpgsqlConnection connection,
        int fromTextId,
        int toTextId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT DISTINCT r.link_id
            FROM evidentia_review r
            JOIN link l ON l.id = r.link_id
            WHERE l.from_text_id = @from AND l.to_text_id = @to
            """, connection);
        command.Parameters.AddWithValue("from", fromTextId);
        command.Parameters.AddWithValue("to", toTextId);

        var reviewed = new HashSet<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                reviewed.Add(reader.GetInt64(0));
            }
        }

        return reviewed;
    }

    /// <summary>A few of what would change in the chapters asked for, as the texts spell them.</summary>
    private static async Task<string> Examples(
        NpgsqlConnection connection,
        Tally within,
        CancellationToken cancellationToken)
    {
        const int shown = 40;
        var refused = within.Refused.Take(shown).ToArray();
        var added = within.Added.Take(shown).ToArray();

        await using var command = new NpgsqlCommand(
            """
            SELECT 'refused', r.canonical_book, r.canonical_chapter, r.canonical_verse,
                   fw.text || ' ' || fw.position, tw.text || ' ' || tw.position
            FROM link l
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from' JOIN word fw ON fw.id = f.word_id
            JOIN link_word t ON t.link_id = l.id AND t.side = 'to' JOIN word tw ON tw.id = t.word_id
            JOIN verse_reference r ON r.verse_id = fw.verse_id AND r.is_primary
            WHERE l.id = ANY(@refused)
            UNION ALL
            SELECT 'added', r.canonical_book, r.canonical_chapter, r.canonical_verse,
                   fw.text || ' ' || fw.position, tw.text || ' ' || tw.position
            FROM unnest(@froms, @tos) AS pair(f, t)
            JOIN word fw ON fw.id = pair.f JOIN word tw ON tw.id = pair.t
            JOIN verse_reference r ON r.verse_id = fw.verse_id AND r.is_primary
            ORDER BY 1 DESC, 2, 3, 4
            """, connection);
        command.Parameters.AddWithValue("refused", refused);
        command.Parameters.AddWithValue("froms", added.Select(pair => pair.From).ToArray());
        command.Parameters.AddWithValue("tos", added.Select(pair => pair.To).ToArray());

        var lines = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.AppendLine(
                $"  {reader.GetString(0),-8} {reader.GetInt32(1)} {reader.GetInt32(2)}:{reader.GetInt32(3)}  " +
                $"{reader.GetString(4)} -> {reader.GetString(5)}");
        }

        return lines.ToString();
    }

    private async Task Write(
        NpgsqlConnection connection,
        int fromTextId,
        int toTextId,
        IReadOnlyCollection<long> refused,
        IReadOnlyList<(long From, long To)> added,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await using (var delete = new NpgsqlCommand(
                         "DELETE FROM link WHERE id = ANY(@ids) AND method = 'aligner'", connection))
        {
            delete.Parameters.AddWithValue("ids", refused.ToArray());
            delete.CommandTimeout = 600;
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (added.Count > 0)
        {
            var firstId = await ReserveLinkIds(connection, added.Count, cancellationToken);
            var renders = EnumSpelling.Of(LinkRelation.Renders);
            var method = EnumSpelling.Of(LinkMethod.Aligner);

            await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
            {
                for (var i = 0; i < added.Count; i++)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(NameLists.Settled, NpgsqlDbType.Double, cancellationToken);
                    await writer.WriteAsync(Source, NpgsqlDbType.Text, cancellationToken);
                }

                await writer.CompleteAsync(cancellationToken);
            }

            await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
            {
                var fromSide = EnumSpelling.Of(LinkSide.From);
                var toSide = EnumSpelling.Of(LinkSide.To);
                for (var i = 0; i < added.Count; i++)
                {
                    await Row(writer, firstId + i, added[i].From, fromSide, cancellationToken);
                    await Row(writer, firstId + i, added[i].To, toSide, cancellationToken);
                }

                await writer.CompleteAsync(cancellationToken);
            }

            await LinkClaims.Record(connection, transaction, firstId, added.Count, cancellationToken);
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

    /// <param name="Guessed">The aligner's links by source word: the link, and the word it reaches.</param>
    /// <param name="Stated">Every other method's target words by source word.</param>
    internal sealed record StoredLinks(
        ILookup<long, (long Link, long To)> Guessed,
        ILookup<long, long> Stated);

    internal sealed class VerseNames
    {
        public int Verses { get; init; }
        public int Named { get; set; }
        public int Checked { get; set; }
        public int Agreed { get; set; }
        public int Overruled { get; set; }
        public int OverruledChecked { get; set; }
        public int CrossedBefore { get; set; }
        public int CrossedAfter { get; set; }
        public HashSet<long> NameLinks { get; } = [];
        public HashSet<long> Refused { get; } = [];
        public HashSet<long> Held { get; } = [];
        public List<(long From, long To)> Added { get; } = [];
    }

    private sealed class Tally
    {
        private int _verses;
        private int _named;
        private int _checked;
        private int _agreed;
        private int _overruled;
        private int _overruledChecked;
        private int _crossedBefore;
        private int _crossedAfter;
        private readonly HashSet<long> _nameLinks = [];
        private readonly HashSet<long> _held = [];

        public HashSet<long> Refused { get; } = [];

        public HashSet<(long From, long To)> Added { get; } = [];

        public void Add(VerseNames verse)
        {
            _verses += verse.Verses;
            _named += verse.Named;
            _checked += verse.Checked;
            _agreed += verse.Agreed;
            _overruled += verse.Overruled;
            _overruledChecked += verse.OverruledChecked;
            _crossedBefore += verse.CrossedBefore;
            _crossedAfter += verse.CrossedAfter;
            _nameLinks.UnionWith(verse.NameLinks);
            _held.UnionWith(verse.Held);
            Refused.UnionWith(verse.Refused);
            Added.UnionWith(verse.Added);
        }

        public NameListOutcome Outcome(string from, string to, string scope) =>
            new(from, to, scope, _verses, _named, _checked, _agreed, _nameLinks.Count,
                Refused.Count + _held.Count, _held.Count, _overruled, _overruledChecked, Added.Count,
                _crossedBefore, _crossedAfter);
    }
}
