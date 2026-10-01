using System.Data.Common;
using System.Diagnostics;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Desk;

/// <summary>
/// What the corpus holds of every text, counted in one pass over it: its size, what its words carry,
/// and which other texts its words are linked to, how, and how far — book by book. Every text is
/// named by its identifier, never by a row id, because the count is kept on disk across reloads.
/// </summary>
/// <param name="At">When it was counted, in UTC.</param>
/// <param name="Seconds">How long the count took.</param>
/// <param name="LoadedAt">When the corpus was last loaded before it was counted: the last check a load wrote.</param>
/// <param name="BookNames">What each book is called, in English and in Ukrainian, as the corpus's own texts print them.</param>
internal sealed record TextCensusSnapshot(
    string At,
    double Seconds,
    string? LoadedAt,
    IReadOnlyList<TextCount> Texts,
    IReadOnlyList<BookName> BookNames);

/// <param name="Local">The name a Ukrainian text of the corpus prints for it, where one does.</param>
internal sealed record BookName(int Ordinal, string Name, string? Local);

/// <param name="Words">The words the edition prints; a word it records and prints no letters for is counted in <paramref name="Elided"/> and nowhere else.</param>
/// <param name="Linked">Words any link names, to any other text.</param>
/// <param name="LinkedVerses">Verses any verse link names.</param>
/// <param name="Features">What the words, verses and books carry, each counted where it is present.</param>
/// <param name="Morphology">Each feature the words' own analysis records, and how many words record it.</param>
/// <param name="PerBook">The size of each book this text holds.</param>
/// <param name="Links">Every text this one is linked to, busiest first.</param>
internal sealed record TextCount(
    string Text,
    int Books,
    int Chapters,
    int Verses,
    int Words,
    int Elided,
    int Linked,
    int LinkedVerses,
    IReadOnlyList<FeatureCount> Features,
    IReadOnlyList<KeyCount> Morphology,
    IReadOnlyList<BookCount> PerBook,
    IReadOnlyList<TextLinkCount> Links);

/// <param name="Key">What is carried: <c>lemma</c>, <c>strong</c>, <c>strong-proposed</c>, <c>gloss</c>, <c>morphology</c>, <c>parsing</c>, <c>entities</c>, <c>joined</c>, <c>paragraphs</c>, <c>lines</c>, <c>syntax</c>, <c>supplied</c>, <c>notes</c>, <c>stated-numbers</c>, <c>book-names</c>.</param>
/// <param name="Unit">What <paramref name="Count"/> counts: <c>words</c>, <c>verses</c> and <c>books</c> are shares of the text's own; <c>items</c> are spans or notes, which are not.</param>
/// <param name="Parts">The same count broken down — by method, by kind, by source — where it has parts.</param>
/// <param name="Credits">The datasets it came from, where it came from somewhere other than the edition itself.</param>
internal sealed record FeatureCount(
    string Key,
    string Unit,
    int Count,
    IReadOnlyList<KeyCount> Parts,
    IReadOnlyList<string> Credits);

internal sealed record KeyCount(string Key, int Count);

internal sealed record BookCount(int Ordinal, int Chapters, int Verses, int Words, int Linked);

/// <param name="Other">The other text.</param>
/// <param name="Words">Words of this text a link to the other names.</param>
/// <param name="Promised">Words of this text in the books the other also holds: what could have been linked.</param>
/// <param name="Links">Links between the two, whichever way they were written.</param>
/// <param name="Corroborated">Of those, the links more than one independent claim stands on.</param>
/// <param name="VerseLinks">Verse correspondences between the two.</param>
/// <param name="Methods">What established the links, most first.</param>
/// <param name="Relations">What the links assert — renders, equals, expands, omits, transposes.</param>
/// <param name="PerBook">The same words, book by book, over the books both hold.</param>
internal sealed record TextLinkCount(
    string Other,
    int Words,
    int Promised,
    int Links,
    int Corroborated,
    int VerseLinks,
    IReadOnlyList<LinkMethodCount> Methods,
    IReadOnlyList<KeyCount> Relations,
    IReadOnlyList<BookLinkCount> PerBook);

/// <param name="Confidence">The mean confidence of the links that carry one — an inference's — or null where every one was stated.</param>
/// <param name="Credits">Whose statement or reasoning the links rest on, by dataset.</param>
internal sealed record LinkMethodCount(string Method, int Links, double? Confidence, IReadOnlyList<string> Credits);

/// <param name="Words">Words of this text in the book.</param>
/// <param name="Linked">Of those, the ones a link to the other text names.</param>
internal sealed record BookLinkCount(int Ordinal, int Words, int Linked);

/// <summary>
/// The census itself: a few grouped sweeps of the word and link tables in one read-only snapshot,
/// so every number is taken from the same state of the corpus. It runs for about a minute on the
/// full corpus — the words each link names, book by book, are most of that — which is why the
/// console keeps what it counted rather than counting on every request.
/// </summary>
internal static class TextCensus
{
    /// <summary>How long one statement may run: the link sweep is well under this on the full corpus.</summary>
    private const int StatementSeconds = 900;

    /// <summary>Enough memory for the link sweep's sort to stay off the disk, for this transaction only.</summary>
    private const string SweepMemory = "256MB";

    /// <summary>The language whose texts name the books in the owner's language.</summary>
    private const string LocalLanguage = "ukr";

    /// <summary>What a link's side is called in the database, on the side it was written from.</summary>
    private const string FromSide = "from";

    /// <summary>
    /// Counts every text on <paramref name="connection"/>, inside the transaction already open on it
    /// or inside a read-only one of its own, so nothing the census runs can change the corpus.
    /// </summary>
    public static async Task<TextCensusSnapshot> Count(DbConnection connection, bool inTransaction, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        DbTransaction? own = null;
        if (!inTransaction)
        {
            own = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
            await Execute(connection, "set transaction read only", cancellationToken);
        }

        try
        {
            await Execute(connection, $"set local work_mem = '{SweepMemory}'", cancellationToken);
            return await Sweep(connection, clock, cancellationToken);
        }
        finally
        {
            if (own is not null)
            {
                await own.RollbackAsync(CancellationToken.None);
                await own.DisposeAsync();
            }
        }
    }

    private static async Task<TextCensusSnapshot> Sweep(DbConnection connection, Stopwatch clock, CancellationToken cancellationToken)
    {
        var slugs = (await Rows(connection, "select id, slug from text", r => (Id: r.GetInt32(0), Slug: r.GetString(1)), cancellationToken))
            .ToDictionary(row => row.Id, row => row.Slug);

        var bookWords = await Rows(connection,
            """
            select w.text_id, b.canonical_ordinal, count(*) filter (where not w.elided), count(*) filter (where w.elided)
            from word w join verse v on v.id = w.verse_id join book b on b.id = v.book_id
            group by 1, 2
            """,
            r => (Text: r.GetInt32(0), Book: r.GetInt32(1), Words: Int(r, 2), Elided: Int(r, 3)), cancellationToken);

        var bookVerses = await Rows(connection,
            """
            select v.text_id, b.canonical_ordinal, count(*), count(distinct v.chapter_id)
            from verse v join book b on b.id = v.book_id
            group by 1, 2
            """,
            r => (Text: r.GetInt32(0), Book: r.GetInt32(1), Verses: Int(r, 2), Chapters: Int(r, 3)), cancellationToken);

        var books = await Rows(connection,
            """
            select b.text_id, b.canonical_ordinal, b.name, b.name_native, t.language
            from book b join text t on t.id = b.text_id
            order by b.canonical_ordinal, t.id
            """,
            r => (Text: r.GetInt32(0), Book: r.GetInt32(1), Name: r.GetString(2), Native: Nullable(r, 3), Language: r.GetString(4)),
            cancellationToken);

        var words = (await Rows(connection,
            """
            select text_id,
                   count(*) filter (where not elided and lemma is not null),
                   count(*) filter (where not elided and strong_number is not null),
                   count(*) filter (where not elided and gloss is not null),
                   count(*) filter (where not elided and morphology is not null),
                   count(*) filter (where not elided and graphical_text is not null),
                   count(*) filter (where break = 'paragraph'),
                   count(*) filter (where break = 'line')
            from word
            group by text_id
            """,
            r => (Text: r.GetInt32(0), Lemma: Int(r, 1), Strong: Int(r, 2), Gloss: Int(r, 3), Morphology: Int(r, 4),
                Joined: Int(r, 5), Paragraphs: Int(r, 6), Lines: Int(r, 7)),
            cancellationToken)).ToDictionary(row => row.Text);

        var keys = await Rows(connection,
            """
            select w.text_id, k.key, count(*)
            from word w cross join lateral jsonb_object_keys(w.morphology) as k(key)
            where w.morphology is not null and jsonb_typeof(w.morphology) = 'object' and not w.elided
            group by 1, 2
            """,
            r => (Text: r.GetInt32(0), Key: r.GetString(1), Count: Int(r, 2)), cancellationToken);

        // Each of these three is broken down by what produced it, with the text's own total — a word
        // two methods both reached is one word — in the row whose breakdown is null.
        var entities = await Rows(connection,
            """
            select w.text_id, we.method, count(distinct we.word_id)
            from word_entity we join word w on w.id = we.word_id
            where not w.elided
            group by grouping sets ((w.text_id, we.method), (w.text_id))
            """,
            r => (Text: r.GetInt32(0), Part: Nullable(r, 1), Count: Int(r, 2)), cancellationToken);

        var proposed = await Rows(connection,
            """
            select w.text_id, ws.method, count(distinct ws.word_id)
            from word_strong ws join word w on w.id = ws.word_id
            where w.strong_number is null and not w.elided
            group by grouping sets ((w.text_id, ws.method), (w.text_id))
            """,
            r => (Text: r.GetInt32(0), Part: Nullable(r, 1), Count: Int(r, 2)), cancellationToken);

        var parsings = await Rows(connection,
            """
            select w.text_id, wp.source, count(distinct wp.word_id)
            from word_parsing wp join word w on w.id = wp.word_id
            where not w.elided
            group by grouping sets ((w.text_id, wp.source), (w.text_id))
            """,
            r => (Text: r.GetInt32(0), Part: Nullable(r, 1), Count: Int(r, 2)), cancellationToken);

        var groups = await Rows(connection, "select text_id, kind, count(*) from word_group group by 1, 2",
            r => (Text: r.GetInt32(0), Kind: r.GetString(1), Count: Int(r, 2)), cancellationToken);

        var notes = await Rows(connection,
            "select v.text_id, n.kind, count(*) from verse_note n join verse v on v.id = n.verse_id group by 1, 2",
            r => (Text: r.GetInt32(0), Kind: r.GetString(1), Count: Int(r, 2)), cancellationToken);

        var stated = (await Rows(connection,
            "select v.text_id, count(distinct s.verse_id) from stated_verse_number s join verse v on v.id = s.verse_id group by 1",
            r => (Text: r.GetInt32(0), Count: Int(r, 1)), cancellationToken)).ToDictionary(row => row.Text, row => row.Count);

        // An aligner's source names its run and its parameters, thousands of strings that all say the
        // same thing, so only the others are kept apart.
        var links = await Rows(connection,
            """
            select l.from_text_id, l.to_text_id, l.method, l.relation, coalesce(p.source, ''),
                   count(*), count(l.confidence), coalesce(sum(l.confidence), 0)
            from link l
            left join provenance p on p.id = l.provenance_id and l.method <> 'aligner'
            group by 1, 2, 3, 4, 5
            """,
            r => (From: r.GetInt32(0), To: r.GetInt32(1), Method: r.GetString(2), Relation: r.GetString(3), Source: r.GetString(4),
                Count: Int(r, 5), Inferred: Int(r, 6), Confidence: r.GetDouble(7)),
            cancellationToken);

        var corroborated = await Rows(connection,
            """
            select l.from_text_id, l.to_text_id, count(*)
            from link l join (select link_id from link_claim group by link_id having count(*) > 1) c on c.link_id = l.id
            group by 1, 2
            """,
            r => (From: r.GetInt32(0), To: r.GetInt32(1), Count: Int(r, 2)), cancellationToken);

        // The words each link names, by the text they belong to, the text on the link's other side
        // and the book — and, in the rows whose other text is null, by any link at all.
        var linked = await Rows(connection,
            $"""
            with named as (
                select case when lw.side = '{FromSide}' then l.from_text_id else l.to_text_id end as text_id,
                       case when lw.side = '{FromSide}' then l.to_text_id else l.from_text_id end as other_id,
                       lw.word_id
                from link_word lw join link l on l.id = lw.link_id)
            select n.text_id, n.other_id, b.canonical_ordinal, count(distinct n.word_id)
            from named n join word w on w.id = n.word_id join verse v on v.id = w.verse_id join book b on b.id = v.book_id
            where not w.elided
            group by grouping sets ((n.text_id, n.other_id, b.canonical_ordinal), (n.text_id, b.canonical_ordinal))
            """,
            r => (Text: r.GetInt32(0), Other: r.IsDBNull(1) ? (int?)null : r.GetInt32(1), Book: r.GetInt32(2), Count: Int(r, 3)),
            cancellationToken);

        var verseLinks = await Rows(connection, "select from_text_id, to_text_id, count(*) from verse_link group by 1, 2",
            r => (From: r.GetInt32(0), To: r.GetInt32(1), Count: Int(r, 2)), cancellationToken);

        var versesLinked = (await Rows(connection,
            "select v.text_id, count(distinct vlv.verse_id) from verse_link_verse vlv join verse v on v.id = vlv.verse_id group by 1",
            r => (Text: r.GetInt32(0), Count: Int(r, 1)), cancellationToken)).ToDictionary(row => row.Text, row => row.Count);

        var loadedAt = await LastLoad(connection, cancellationToken);

        var texts = new List<TextCount>();
        foreach (var (id, slug) in slugs.OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            var own = bookWords.Where(row => row.Text == id).ToDictionary(row => row.Book);
            var ownVerses = bookVerses.Where(row => row.Text == id).ToDictionary(row => row.Book);
            var ownLinked = linked.Where(row => row.Text == id).ToList();
            var anyLinked = ownLinked.Where(row => row.Other is null).ToDictionary(row => row.Book, row => row.Count);

            var perBook = ownVerses.Keys.Union(own.Keys).Order()
                .Select(book => new BookCount(
                    book,
                    ownVerses.GetValueOrDefault(book).Chapters,
                    ownVerses.GetValueOrDefault(book).Verses,
                    own.GetValueOrDefault(book).Words,
                    anyLinked.GetValueOrDefault(book)))
                .ToList();

            var word = words.GetValueOrDefault(id);
            var ownBooks = books.Where(row => row.Text == id).ToList();
            var features = Features(
                word, ownBooks.Count(row => row.Native is not null), stated.GetValueOrDefault(id),
                Parted(entities.Where(row => row.Text == id).Select(row => (row.Part, row.Count))),
                Parted(proposed.Where(row => row.Text == id).Select(row => (row.Part, row.Count))),
                Parted(parsings.Where(row => row.Text == id).Select(row => (row.Part, row.Count))),
                [.. groups.Where(row => row.Text == id).Select(row => new KeyCount(row.Kind, row.Count))],
                [.. notes.Where(row => row.Text == id).Select(row => new KeyCount(row.Kind, row.Count))],
                slug);

            texts.Add(new TextCount(
                slug,
                perBook.Count(book => book.Verses > 0),
                perBook.Sum(book => book.Chapters),
                perBook.Sum(book => book.Verses),
                perBook.Sum(book => book.Words),
                own.Values.Sum(row => row.Elided),
                perBook.Sum(book => book.Linked),
                versesLinked.GetValueOrDefault(id),
                features,
                [.. keys.Where(row => row.Text == id).OrderByDescending(row => row.Count).ThenBy(row => row.Key, StringComparer.Ordinal)
                    .Select(row => new KeyCount(row.Key, row.Count))],
                perBook,
                Linked(id, slugs, own, bookWords, ownLinked, links, corroborated, verseLinks)));
        }

        var names = books
            .GroupBy(row => row.Book)
            .OrderBy(group => group.Key)
            .Select(group => new BookName(
                group.Key,
                group.First().Name,
                group.FirstOrDefault(row => row.Language == LocalLanguage && row.Native is not null).Native))
            .ToList();

        return new TextCensusSnapshot(JsonFiles.Now(), Math.Round(clock.Elapsed.TotalSeconds, 1), loadedAt, texts, names);
    }

    /// <summary>When the corpus was last loaded: the time of the check every load writes when it finishes.</summary>
    public static async Task<string?> LastLoad(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var at = await Rows(connection, "select max(ran_at) from verification_run",
            r => r.IsDBNull(0) ? null : TextBoard.Stamp(r.GetFieldValue<DateTimeOffset>(0)), cancellationToken);
        return at.FirstOrDefault();
    }

    private static List<FeatureCount> Features(
        (int Text, int Lemma, int Strong, int Gloss, int Morphology, int Joined, int Paragraphs, int Lines) word,
        int nativeBookNames,
        int statedNumbers,
        (int Total, List<KeyCount> Parts) entities,
        (int Total, List<KeyCount> Parts) proposed,
        (int Total, List<KeyCount> Parts) parsings,
        List<KeyCount> groups,
        List<KeyCount> notes,
        string slug)
    {
        const string Words = "words", Verses = "verses", Books = "books", Items = "items";
        var syntax = groups.Where(group => group.Key != "supplied").OrderByDescending(group => group.Count).ToList();
        var supplied = groups.Where(group => group.Key == "supplied").Sum(group => group.Count);

        var features = new List<FeatureCount>
        {
            new("strong", Words, word.Strong, [], []),
            new("strong-proposed", Words, proposed.Total, proposed.Parts, []),
            new("lemma", Words, word.Lemma, [], Credit(dataset => dataset.Lemmas, slug)),
            new("gloss", Words, word.Gloss, [], Credit(dataset => dataset.WordGlosses, slug)),
            new("morphology", Words, word.Morphology, [], []),
            new("parsing", Words, parsings.Total,
                [.. parsings.Parts.Select(part => part with { Key = Datasets.Match(part.Key)?.Name ?? part.Key })], []),
            new("entities", Words, entities.Total, entities.Parts, []),
            new("joined", Words, word.Joined, [], []),
            new("syntax", Items, syntax.Sum(group => group.Count), syntax, []),
            new("supplied", Items, supplied, [], []),
            new("notes", Items, notes.Sum(note => note.Count), notes, []),
            new("paragraphs", Items, word.Paragraphs, [], []),
            new("lines", Items, word.Lines, [], []),
            new("stated-numbers", Verses, statedNumbers, [], []),
            new("book-names", Books, nativeBookNames, [], []),
        };

        return features;
    }

    /// <summary>The datasets whose declaration says they supply this to the text.</summary>
    private static List<string> Credit(Func<Datasets.Dataset, string?> supplies, string slug) =>
        [.. Datasets.All.Where(dataset => string.Equals(supplies(dataset), slug, StringComparison.OrdinalIgnoreCase)).Select(dataset => dataset.Name)];

    private static (int Total, List<KeyCount> Parts) Parted(IEnumerable<(string? Part, int Count)> rows)
    {
        var list = rows.ToList();
        return (
            list.Where(row => row.Part is null).Sum(row => row.Count),
            [.. list.Where(row => row.Part is not null).OrderByDescending(row => row.Count).Select(row => new KeyCount(row.Part!, row.Count))]);
    }

    private static List<TextLinkCount> Linked(
        int id,
        IReadOnlyDictionary<int, string> slugs,
        IReadOnlyDictionary<int, (int Text, int Book, int Words, int Elided)> own,
        IReadOnlyList<(int Text, int Book, int Words, int Elided)> bookWords,
        IReadOnlyList<(int Text, int? Other, int Book, int Count)> ownLinked,
        IReadOnlyList<(int From, int To, string Method, string Relation, string Source, int Count, int Inferred, double Confidence)> links,
        IReadOnlyList<(int From, int To, int Count)> corroborated,
        IReadOnlyList<(int From, int To, int Count)> verseLinks)
    {
        var mine = links.Where(row => row.From == id || row.To == id).ToList();
        var others = mine.Select(row => row.From == id ? row.To : row.From)
            .Concat(verseLinks.Where(row => row.From == id || row.To == id).Select(row => row.From == id ? row.To : row.From))
            .Where(other => other != id && slugs.ContainsKey(other))
            .Distinct();

        var result = new List<TextLinkCount>();
        foreach (var other in others)
        {
            var between = mine.Where(row => row.From == other || row.To == other).ToList();
            var theirBooks = bookWords.Where(row => row.Text == other && row.Words > 0).Select(row => row.Book).ToHashSet();
            var linkedByBook = ownLinked.Where(row => row.Other == other).ToDictionary(row => row.Book, row => row.Count);
            var perBook = own.Values
                .Where(row => theirBooks.Contains(row.Book) && row.Words > 0)
                .OrderBy(row => row.Book)
                .Select(row => new BookLinkCount(row.Book, row.Words, linkedByBook.GetValueOrDefault(row.Book)))
                .ToList();

            var methods = between
                .GroupBy(row => row.Method)
                .Select(method =>
                {
                    var inferred = method.Sum(row => row.Inferred);
                    return new LinkMethodCount(
                        method.Key,
                        method.Sum(row => row.Count),
                        inferred == 0 ? null : Math.Round(method.Sum(row => row.Confidence) / inferred, 3),
                        [.. method.Select(row => Datasets.Match(row.Source)?.Name).OfType<string>().Distinct(StringComparer.Ordinal)]);
                })
                .OrderByDescending(method => method.Links)
                .ToList();

            result.Add(new TextLinkCount(
                slugs[other],
                perBook.Sum(book => book.Linked),
                perBook.Sum(book => book.Words),
                between.Sum(row => row.Count),
                corroborated.Where(row => (row.From == id && row.To == other) || (row.From == other && row.To == id)).Sum(row => row.Count),
                verseLinks.Where(row => (row.From == id && row.To == other) || (row.From == other && row.To == id)).Sum(row => row.Count),
                methods,
                [.. between.GroupBy(row => row.Relation).Select(relation => new KeyCount(relation.Key, relation.Sum(row => row.Count)))
                    .OrderByDescending(relation => relation.Count)],
                perBook));
        }

        return [.. result.OrderByDescending(link => link.Words).ThenByDescending(link => link.Links).ThenBy(link => link.Other, StringComparer.Ordinal)];
    }

    private static async Task Execute(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<T>> Rows<T>(DbConnection connection, string sql, Func<DbDataReader, T> read, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = StatementSeconds;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    private static int Int(DbDataReader reader, int ordinal) => Convert.ToInt32(reader.GetValue(ordinal));

    private static string? Nullable(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
