using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Products;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Indexes;

/// <summary>
/// The texts' indexes into the serving tables (never live): <c>strong_rendering</c>, <c>strong_rendering_place</c>,
/// <c>strong_reach</c>, <c>strong_reach_method</c>, <c>strong_book_reach</c> and <c>strong_book</c> from the strong and
/// occurrences indexes,
/// <c>text_form</c> from the forms, <c>entity_rendering</c> and <c>entity_search_spelling</c> from the spellings. Each
/// table's rows of the texts loaded are replaced in one transaction. What is counted is what the database steps count
/// (<see cref="StrongRenderingCounts"/>, <see cref="EntityRenderingLoader"/>): the
/// commonest phrases are ranked by the database, as before, so ties fall in its collation; the variety and the lexical
/// class are the same C#.
/// </summary>
internal static class IndexLoad
{
    /// <summary>
    /// Whether each number is a word of content: most of the words carrying it, in every edition
    /// that states a part of speech, are of an open class. A number no edition classes is unknown.
    /// </summary>
    internal static IReadOnlyDictionary<string, bool> Lexical(IEnumerable<(string Number, string Label, int Words)> classes) =>
        classes
            .GroupBy(row => row.Number, StringComparer.Ordinal)
            .Select(number =>
            {
                var language = number.Key.StartsWith('G') ? "grc" : "hbo";
                var classed = number
                    .Where(row => EvidentiaMorphologyLabels.PartOfSpeech(row.Label, language) is not null)
                    .ToList();
                var open = classed.Where(row => EvidentiaMorphologyLabels.IsOpenClass(row.Label, language)).Sum(row => row.Words);
                return (number.Key, Known: classed.Count > 0, Open: open * 2 > classed.Sum(row => row.Words));
            })
            .Where(number => number.Known)
            .ToDictionary(number => number.Key, number => number.Open, StringComparer.Ordinal);

    public sealed record Outcome(string Table, long Rows, int Texts, TimeSpan Elapsed, string? Note = null)
    {
        public override string ToString() => $"{Table}: {Rows} rows of {Texts} texts in {Elapsed.TotalSeconds:0.0} s{(Note is null ? "" : $"; {Note}")}";
    }

    private sealed record Counted(
        int TextId,
        List<(string Number, string Phrase, int Uses)> Phrases,
        List<(string Number, int Witness, int Occurrences, int Reached, Variety Variety, bool? Lexical)> Reach,
        List<(string Number, string Method, int Links)> Methods,
        List<(string Number, int Book, int Reached)> Books)
    {
        public List<Placed> Places { get; init; } = [];

        /// <summary>Each number of a row of the index with the text's words that render it, as the index keys them.</summary>
        public List<(string Number, string Keys)> Rendered { get; init; } = [];
    }

    /// <summary>A row of <c>strong_rendering_place</c>: the words of one edition carrying a number that a text renders, in list order.</summary>
    private sealed record Placed(string Number, int Witness, string[] Phrases, long[] Words, int[] PhraseOf, short[] BookOf);

    /// <summary>A word of the database by its own address: text, book, chapter, verse, verse letter and position.</summary>
    private readonly record struct Addressed(int Text, int Book, int Chapter, int Verse, string Label, int Position);

    /// <summary>The id of every word of the editions, by its address: the ids the serving database gave the words it loaded.</summary>
    private static async Task<Dictionary<Addressed, long>> WordIdsOf(NpgsqlConnection connection, int[] editions, CancellationToken cancellationToken)
    {
        var words = new Dictionary<Addressed, long>();
        await using var command = new NpgsqlCommand(
            """
            SELECT w.id, w.text_id, b.canonical_ordinal, v.chapter_number, v.number, v.label, w.position
            FROM word w
            JOIN verse v ON v.id = w.verse_id
            JOIN book b ON b.id = v.book_id
            WHERE w.text_id = ANY(@editions)
            """, connection);
        command.Parameters.AddWithValue("editions", editions);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            words[new Addressed(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), reader.GetInt32(6))] =
                reader.GetInt64(0);
        }

        return words;
    }

    private static async Task<Dictionary<string, (int Id, string Language)>> Texts(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var texts = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT slug, id, language FROM text", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            texts[reader.GetString(0)] = (reader.GetInt32(1), reader.GetString(2));
        }

        return texts;
    }

    /// <summary>
    /// The Strong tables from the strong indexes of <paramref name="texts"/> and the occurrences of the editions they are
    /// counted over. The lexical class of a number reads the occurrences of every text of an original language.
    /// </summary>
    public static async Task<Outcome> Strong(string connectionString, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await Strong(connection, store, texts, cancellationToken, own: true);
    }

    /// <summary>The same on a connection whose transaction the caller holds and commits.</summary>
    public static Task<Outcome> Strong(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken) =>
        Strong(connection, store, texts, cancellationToken, own: false);

    private static async Task<Outcome> Strong(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken, bool own)
    {
        var clock = Stopwatch.StartNew();
        var ids = await Texts(connection, cancellationToken);

        var occurrences = new Dictionary<string, List<OccurrenceRow>>(StringComparer.Ordinal);
        foreach (var slug in ids.Keys.Where(slug => store.Exists(IndexProducts.Of(slug, IndexProducts.Occurrences))))
        {
            using var reader = store.Open(IndexProducts.Of(slug, IndexProducts.Occurrences), OccurrenceRow.Schema);
            occurrences[slug] = [.. reader.Rows()];
        }

        var lexical = Lexical(occurrences
            .Where(o => Renderings.Original.Contains(ids[o.Key].Language))
            .SelectMany(o => o.Value)
            .Where(row => row.Pos is not null)
            .GroupBy(row => (row.Number, row.Pos!))
            .Select(g => (g.Key.Number, g.Key.Item2, g.Sum(row => row.Words))));

        // Each text is counted from its own index alone, so the texts are counted at once.
        var indexed = texts.Where(slug => ids.ContainsKey(slug) && store.Exists(IndexProducts.Of(slug, IndexProducts.Strong))).ToList();
        var primaries = indexed.Select(text =>
        {
            using var reader = store.Open(IndexProducts.Of(text, IndexProducts.Strong));
            return Note(reader.Header!.Notes, "primary") is { } listed ? listed.Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
        }).ToArray();
        var editions = primaries.SelectMany(p => p).ToHashSet(StringComparer.Ordinal);
        var wordIds = await WordIdsOf(connection, [.. editions.Where(ids.ContainsKey).Select(e => ids[e].Id)], cancellationToken);

        var countedBy = new Counted[indexed.Count];
        var unknown = new string?[indexed.Count];
        Parallel.For(0, indexed.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
        {
            var text = indexed[i];
            using var reader = store.Open(IndexProducts.Of(text, IndexProducts.Strong), StrongIndexRow.Schema);
            var notes = reader.Header!.Notes;
            List<StrongIndexRow> rows = [.. reader.Rows()];
            countedBy[i] = Count(ids[text].Id, ids[text].Language, rows, Note(notes, "hebrew"), Note(notes, "greek"), ids, occurrences, lexical) with
            {
                Places = Places(rows, ids, wordIds, out unknown[i]),
                Rendered = [.. rows.Where(r => r.Keys.Length > 0).Select(r => (r.Number, r.Keys))],
            };
        });

        if (unknown.FirstOrDefault(u => u is not null) is { } missing)
        {
            throw new InvalidDataException(
                $"A strong index names {missing}, which the database does not hold: load the editions' words before the indexes.");
        }

        var counted = countedBy.ToList();
        var countedIn = clock.Elapsed;

        await using var transaction = own ? await connection.BeginTransactionAsync(cancellationToken) : null;
        var textIds = counted.Select(c => c.TextId).ToArray();
        var editionIds = editions.Where(ids.ContainsKey).Select(e => ids[e].Id).ToArray();
        await using (var delete = new NpgsqlCommand(
                         """
                         DELETE FROM strong_rendering WHERE text_id = ANY(@texts);
                         DELETE FROM strong_rendering_place WHERE text_id = ANY(@texts);
                         DELETE FROM strong_reach_method WHERE text_id = ANY(@texts);
                         DELETE FROM strong_reach WHERE text_id = ANY(@texts);
                         DELETE FROM strong_book_reach WHERE text_id = ANY(@texts);
                         DELETE FROM strong_book WHERE witness_id = ANY(@editions);
                         DELETE FROM text_book_linked WHERE text_id = ANY(@texts);
                         DELETE FROM strong_book_words WHERE witness_id = ANY(@editions);
                         CREATE TEMP TABLE phrase_stage (text_id int, strong_number text, phrase text, uses int) ON COMMIT DROP;
                         """, connection, transaction))
        {
            delete.Parameters.AddWithValue("texts", textIds);
            delete.Parameters.AddWithValue("editions", editionIds);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync("COPY phrase_stage (text_id, strong_number, phrase, uses) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var text in counted)
            {
                foreach (var (number, phrase, uses) in text.Phrases)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(text.TextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(phrase, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(uses, NpgsqlDbType.Integer, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        long renderings;
        await using (var rank = new NpgsqlCommand(
                         $"""
                          INSERT INTO strong_rendering (strong_number, text_id, rank, phrase, uses)
                          SELECT strong_number, text_id, rank, phrase, uses
                          FROM (
                              SELECT strong_number, text_id, phrase, uses,
                                     row_number() OVER (PARTITION BY text_id, strong_number ORDER BY uses DESC, phrase) AS rank
                              FROM phrase_stage
                          ) ranked
                          WHERE rank <= {StrongRenderingCounts.Kept}
                          """, connection, transaction))
        {
            renderings = await rank.ExecuteNonQueryAsync(cancellationToken);
        }

        long places = 0;
        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_rendering_place (text_id, strong_number, witness_id, phrases, words, phrase_of, book_of) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var text in counted)
            {
                foreach (var row in text.Places)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(text.TextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(row.Witness, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Phrases, NpgsqlDbType.Array | NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(row.Words, NpgsqlDbType.Array | NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(row.PhraseOf, NpgsqlDbType.Array | NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.BookOf, NpgsqlDbType.Array | NpgsqlDbType.Smallint, cancellationToken);
                    places += row.Words.Length;
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_reach (text_id, strong_number, witness_id, occurrences, reached, phrases, renderings, rendering_links, lexical) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var text in counted)
            {
                foreach (var row in text.Reach)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(text.TextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(row.Witness, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Occurrences, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Reached, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Variety.Phrases, NpgsqlDbType.Integer, cancellationToken);
                    if (row.Variety.Renderings is { } distinct)
                    {
                        await writer.WriteAsync(distinct, NpgsqlDbType.Integer, cancellationToken);
                    }
                    else
                    {
                        await writer.WriteNullAsync(cancellationToken);
                    }

                    await writer.WriteAsync(row.Variety.RenderingLinks, NpgsqlDbType.Integer, cancellationToken);
                    if (row.Lexical is { } open)
                    {
                        await writer.WriteAsync(open, NpgsqlDbType.Boolean, cancellationToken);
                    }
                    else
                    {
                        await writer.WriteNullAsync(cancellationToken);
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_reach_method (text_id, strong_number, method, links) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var text in counted)
            {
                foreach (var (number, method, links) in text.Methods)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(text.TextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(links, NpgsqlDbType.Integer, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_book_reach (text_id, strong_number, book, reached) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var text in counted)
            {
                foreach (var (number, book, reached) in text.Books)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(text.TextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(book, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(reached, NpgsqlDbType.Integer, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_book (witness_id, strong_number, book, occurrences) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var edition in editions.Where(e => ids.ContainsKey(e) && occurrences.ContainsKey(e)).Order(StringComparer.Ordinal))
            {
                foreach (var row in occurrences[edition].GroupBy(r => (r.Number, r.Book)))
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(ids[edition].Id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Key.Number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(row.Key.Book, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Sum(r => r.Words), NpgsqlDbType.Integer, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        // What the Strong map reads per book whatever the number: the books each text is linked in, and each edition's
        // numbered words per book with the ETCBC prefix morphemes left out, the denominator of its rate.
        await using (var sums = new NpgsqlCommand(
                         """
                         INSERT INTO text_book_linked (text_id, book)
                         SELECT DISTINCT text_id, book FROM strong_book_reach WHERE text_id = ANY(@texts);
                         INSERT INTO strong_book_words (witness_id, book, words)
                         SELECT witness_id, book, sum(occurrences) FROM strong_book
                         WHERE witness_id = ANY(@editions) AND strong_number NOT LIKE 'H9%'
                         GROUP BY witness_id, book;
                         """, connection, transaction))
        {
            sums.Parameters.AddWithValue("texts", textIds);
            sums.Parameters.AddWithValue("editions", editionIds);
            await sums.ExecuteNonQueryAsync(cancellationToken);
        }

        var marked = texts.Where(ids.ContainsKey).Distinct(StringComparer.Ordinal).Select(slug => (slug, ids[slug].Id)).ToList();
        var renderedBy = indexed.Select((text, i) => (text, countedBy[i].Rendered)).ToDictionary(t => t.text, t => t.Rendered, StringComparer.Ordinal);
        var rendered = await RenderedStrong(connection, transaction, marked, renderedBy, cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new Outcome("strong_*", renderings + rendered + counted.Sum(c => (long)c.Reach.Count + c.Methods.Count + c.Books.Count + c.Places.Count),
            counted.Count, clock.Elapsed,
            $"{renderings} phrases kept, {places} words behind every phrase, {counted.Sum(c => c.Reach.Count)} reach, {counted.Sum(c => c.Methods.Count)} methods, {counted.Sum(c => c.Books.Count)} by book, " +
            $"{editions.Count} editions by book, {rendered} numbers on the words of {marked.Count} texts; counted from the files in {countedIn.TotalSeconds:0.0} s");
    }

    /// <summary>
    /// <c>word_rendered_strong</c> of the texts, replaced: each word's own number, and each number of a row of the text's strong
    /// index on the text's words that row keys, resolved to the words the database holds. A text's rows go in verse order, so a
    /// verse's rows lie together. A key the database has no word at fails the load.
    /// </summary>
    private static async Task<long> RenderedStrong(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        IReadOnlyList<(string Slug, int Id)> texts,
        IReadOnlyDictionary<string, List<(string Number, string Keys)>> rendered,
        CancellationToken cancellationToken)
    {
        await using (var delete = new NpgsqlCommand(
                         "DELETE FROM word_rendered_strong r USING verse v WHERE v.id = r.verse_id AND v.text_id = ANY(@texts)", connection, transaction)
                     { CommandTimeout = 0 })
        {
            delete.Parameters.AddWithValue("texts", texts.Select(t => t.Id).ToArray());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        long written = 0;
        foreach (var (slug, textId) in texts)
        {
            var renders = rendered.GetValueOrDefault(slug) ?? [];
            var words = new Dictionary<Addressed, (long Id, int Verse)>();
            var marks = new HashSet<(int Verse, long Word, string Number)>();
            await using (var command = new NpgsqlCommand(
                             """
                             SELECT w.id, w.verse_id, b.canonical_ordinal, v.chapter_number, v.number, v.label, w.position, w.strong_number
                             FROM word w
                             JOIN verse v ON v.id = w.verse_id
                             JOIN book b ON b.id = v.book_id
                             WHERE w.text_id = @text
                             """, connection, transaction) { CommandTimeout = 0 })
            {
                command.Parameters.AddWithValue("text", textId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var (id, verse) = (reader.GetInt64(0), reader.GetInt32(1));
                    if (!reader.IsDBNull(7))
                    {
                        marks.Add((verse, id, reader.GetString(7)));
                    }

                    if (renders.Count > 0)
                    {
                        words[new Addressed(textId, reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), reader.GetInt32(6))] =
                            (id, verse);
                    }
                }
            }

            foreach (var (number, keys) in renders)
            {
                foreach (var own in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var key = WordKey.Parse(own, slug);
                    if (!words.TryGetValue(new Addressed(textId, key.Verse.Book, key.Verse.Chapter, key.Verse.Verse, key.Verse.VerseLabel, key.Position),
                            out var word))
                    {
                        throw new InvalidDataException(
                            $"texts/{slug}/index/strong names {key}, which the database does not hold: load the text's words before the indexes.");
                    }

                    marks.Add((word.Verse, word.Id, number));
                }
            }

            await using var writer = await connection.BeginBinaryImportAsync(
                "COPY word_rendered_strong (verse_id, word_id, strong_number) FROM STDIN (FORMAT BINARY)", cancellationToken);
            foreach (var (verse, word, number) in marks.OrderBy(m => m.Verse).ThenBy(m => m.Word).ThenBy(m => m.Number, StringComparer.Ordinal))
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(verse, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(word, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(number, NpgsqlDbType.Text, cancellationToken);
            }

            written += (long)await writer.CompleteAsync(cancellationToken);
        }

        return written;
    }

    private static string? Note(IEnumerable<KeyValuePair<string, string>> notes, string key) =>
        notes.LastOrDefault(n => n.Key == key).Value is { } value && value != "-" ? value : null;

    /// <summary>
    /// The words behind every phrase of a text, per number and edition, in the order the list of a number's places shows them:
    /// by the primary place of their verse in the shared frame, then position, then id. <paramref name="unknown"/> names the
    /// first word the database does not hold, if any.
    /// </summary>
    private static List<Placed> Places(
        List<StrongIndexRow> rows,
        Dictionary<string, (int Id, string Language)> ids,
        Dictionary<Addressed, long> words,
        out string? unknown)
    {
        unknown = null;
        var places = new List<Placed>();
        foreach (var group in rows.Where(r => r.Phrase is not null).GroupBy(r => (r.Number, r.Witness)))
        {
            if (!ids.TryGetValue(group.Key.Witness, out var witness))
            {
                continue;
            }

            var phrases = new Dictionary<string, int>(StringComparer.Ordinal);
            var standing = new List<(VerseKey At, int Position, long Id, int Phrase, short Book)>();
            foreach (var row in group)
            {
                var key = WordKey.Parse(row.Original, row.Witness);
                var verse = key.Verse;
                if (!words.TryGetValue(new Addressed(witness.Id, verse.Book, verse.Chapter, verse.Verse, verse.VerseLabel, key.Position), out var id))
                {
                    unknown ??= key.ToString();
                    continue;
                }

                if (!phrases.TryGetValue(row.Phrase!, out var phrase))
                {
                    phrases[row.Phrase!] = phrase = phrases.Count;
                }

                standing.Add((row.At, key.Position, id, phrase, (short)verse.Book));
            }

            if (standing.Count == 0)
            {
                continue;
            }

            standing.Sort((a, b) =>
                a.At.Book != b.At.Book ? a.At.Book.CompareTo(b.At.Book)
                : a.At.Chapter != b.At.Chapter ? a.At.Chapter.CompareTo(b.At.Chapter)
                : a.At.Verse != b.At.Verse ? a.At.Verse.CompareTo(b.At.Verse)
                : a.Position != b.Position ? a.Position.CompareTo(b.Position)
                : a.Id.CompareTo(b.Id));
            places.Add(new Placed(
                group.Key.Number,
                witness.Id,
                [.. phrases.OrderBy(p => p.Value).Select(p => p.Key)],
                [.. standing.Select(s => s.Id)],
                [.. standing.Select(s => s.Phrase)],
                [.. standing.Select(s => s.Book)]));
        }

        return places;
    }

    /// <summary>
    /// What <see cref="StrongRenderingCounts.CountText"/> counts for a text, from its index: every phrase over every edition
    /// it is counted over; the reach, the methods and the books only over the edition counted for the number's language.
    /// </summary>
    private static Counted Count(
        int textId,
        string language,
        List<StrongIndexRow> rows,
        string? hebrew,
        string? greek,
        Dictionary<string, (int Id, string Language)> ids,
        Dictionary<string, List<OccurrenceRow>> occurrences,
        IReadOnlyDictionary<string, bool> lexical)
    {
        var phrases = rows.Where(r => r.Phrase is not null)
            .GroupBy(r => (r.Number, r.Phrase!))
            .Select(g => (g.Key.Number, Phrase: g.Key.Item2, Uses: g.Count()))
            .ToList();
        var variety = RenderingVariety.Count(language, phrases.Select(p => new StrongRenderingCount(p.Number, 0, p.Phrase, p.Uses)));

        string? CountedIn(string number) => number.StartsWith('G') ? greek : hebrew;
        var counted = rows.Where(r => CountedIn(r.Number) == r.Witness).ToList();
        var reached = counted.GroupBy(r => r.Number).ToDictionary(g => g.Key, g => g.Select(r => r.Original).Distinct().Count(), StringComparer.Ordinal);
        var methods = counted
            .SelectMany(r => r.Links.Split(' ').Zip(r.Methods.Split(' ')).Select(l => (r.Number, Link: l.First, Method: l.Second)))
            .Distinct()
            .GroupBy(l => (l.Number, l.Method))
            .Select(g => (g.Key.Number, g.Key.Method, Links: g.Count()))
            .ToList();
        var books = counted
            .GroupBy(r => (r.Number, Book: WordKey.Parse(r.Original, r.Witness).Verse.Book))
            .Select(g => (g.Key.Number, g.Key.Book, Reached: g.Select(r => r.Original).Distinct().Count()))
            .ToList();

        var reach = new List<(string, int, int, int, Variety, bool?)>();
        foreach (var (edition, greekNumbers) in (ReadOnlySpan<(string? Edition, bool Greek)>)[(hebrew, false), (greek, true)])
        {
            if (edition is null || !occurrences.TryGetValue(edition, out var standing) || !ids.TryGetValue(edition, out var witness))
            {
                continue;
            }

            foreach (var number in standing.Where(r => r.Number.StartsWith('G') == greekNumbers).GroupBy(r => r.Number))
            {
                reach.Add((number.Key, witness.Id, number.Sum(r => r.Words), reached.GetValueOrDefault(number.Key), variety.Of(number.Key),
                    lexical.TryGetValue(number.Key, out var open) ? open : null));
            }
        }

        return new Counted(textId, phrases, reach, methods, books);
    }

    /// <summary>
    /// <c>text_form</c> from the forms indexes: each text's rows replaced, a form the index leaves unstemmed stemmed by the
    /// database's dictionary for the text's language.
    /// </summary>
    public static async Task<Outcome> Forms(string connectionString, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await Forms(connection, store, texts, cancellationToken, own: true);
    }

    /// <summary>The same on a connection whose transaction the caller holds and commits.</summary>
    public static Task<Outcome> Forms(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken) =>
        Forms(connection, store, texts, cancellationToken, own: false);

    private static async Task<Outcome> Forms(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken, bool own)
    {
        var clock = Stopwatch.StartNew();
        var ids = await Texts(connection, cancellationToken);
        var loaded = texts.Where(slug => ids.ContainsKey(slug) && store.Exists(IndexProducts.Of(slug, IndexProducts.Forms))).ToList();

        await using var transaction = own ? await connection.BeginTransactionAsync(cancellationToken) : null;
        await using (var stage = new NpgsqlCommand(
                         """
                         DELETE FROM text_form WHERE text_id = ANY(@texts);
                         CREATE TEMP TABLE form_stage (text_id int, form text, occurrences int, stem text) ON COMMIT DROP;
                         """, connection, transaction))
        {
            stage.Parameters.AddWithValue("texts", loaded.Select(slug => ids[slug].Id).ToArray());
            await stage.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync("COPY form_stage (text_id, form, occurrences, stem) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var slug in loaded)
            {
                using var reader = store.Open(IndexProducts.Of(slug, IndexProducts.Forms), FormRow.Schema);
                foreach (var row in reader.Rows())
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(ids[slug].Id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Form, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(row.Occurrences, NpgsqlDbType.Integer, cancellationToken);
                    if (row.Stem is { } stem)
                    {
                        await writer.WriteAsync(stem, NpgsqlDbType.Text, cancellationToken);
                    }
                    else
                    {
                        await writer.WriteNullAsync(cancellationToken);
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        foreach (var language in loaded.Select(slug => ids[slug].Language).Distinct())
        {
            if (SearchWords.Dictionary(language) is not { } dictionary)
            {
                continue;
            }

            await using var stem = new NpgsqlCommand(
                """
                UPDATE form_stage
                SET stem = coalesce(nullif((ts_lexize(@dictionary::regdictionary, form))[1], ''), form)
                WHERE stem IS NULL AND text_id = ANY(@texts)
                """, connection, transaction);
            stem.Parameters.AddWithValue("dictionary", dictionary);
            stem.Parameters.AddWithValue("texts", loaded.Where(slug => ids[slug].Language == language).Select(slug => ids[slug].Id).ToArray());
            await stem.ExecuteNonQueryAsync(cancellationToken);
        }

        long rows;
        await using (var insert = new NpgsqlCommand(
                         "INSERT INTO text_form (text_id, form, stem, occurrences) SELECT text_id, form, coalesce(stem, form), occurrences FROM form_stage",
                         connection, transaction))
        {
            rows = await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new Outcome("text_form", rows, loaded.Count, clock.Elapsed);
    }

    /// <summary>The bits of a rendering's id under its text's id: 7 + 45 keep every id below 2^52, an exact JSON number.</summary>
    internal const int RenderingHashBits = 45;

    /// <summary>
    /// A rendering's id: its text's id in the bits above <see cref="RenderingHashBits"/>, and below them a hash of its spelling,
    /// edition, number and lemma, unique within the text (<see cref="RowIds.Hashed"/>).
    /// </summary>
    internal static long RenderingId(int text, RowIds.Hashed numbering, RenderingRow row) =>
        text is >= 1 and <= RowIds.Texts
            ? ((long)text << RenderingHashBits) | numbering.Of($"{row.Form}|{row.Witness}|{row.Number}|{row.Lemma}")
            : throw new InvalidOperationException($"A text id of {text} does not fit a rendering's id.");

    /// <summary>
    /// <c>word_rendering</c>, <c>word_rendering_place</c>, <c>word_rendering_witness</c> and <c>word_rendering_source</c> from
    /// the renderings indexes (<see cref="RenderingIndex"/>): each text's rows replaced. A rendering's first places are its
    /// samples; the whole list goes beside it only where there are more. A rendering's id is its text's id over a hash of what it
    /// is (<see cref="RenderingId"/>), so a text's rows keep their ids while its index does not change them.
    /// </summary>
    public static async Task<Outcome> WordRenderings(string connectionString, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await WordRenderings(connection, store, texts, cancellationToken, own: true);
    }

    /// <summary>The same on a connection whose transaction the caller holds and commits.</summary>
    public static Task<Outcome> WordRenderings(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken) =>
        WordRenderings(connection, store, texts, cancellationToken, own: false);

    private static async Task<Outcome> WordRenderings(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken, bool own)
    {
        var clock = Stopwatch.StartNew();
        var ids = await Texts(connection, cancellationToken);
        var named = texts.Where(ids.ContainsKey).ToList();
        var loaded = named
            .Where(slug => store.Exists(IndexProducts.Of(slug, IndexProducts.Renderings)) && store.Exists(IndexProducts.Of(slug, IndexProducts.Rendered)))
            .ToList();

        await using var transaction = own ? await connection.BeginTransactionAsync(cancellationToken) : null;
        await using (var delete = new NpgsqlCommand(
                         """
                         DELETE FROM word_rendering_place p USING word_rendering r WHERE p.rendering_id = r.id AND r.text_id = ANY(@texts);
                         DELETE FROM word_rendering WHERE text_id = ANY(@texts);
                         DELETE FROM word_rendering_witness WHERE text_id = ANY(@texts);
                         DELETE FROM word_rendering_source WHERE text_id = ANY(@texts);
                         """, connection, transaction))
        {
            delete.Parameters.AddWithValue("texts", named.Select(slug => ids[slug].Id).ToArray());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        long renderings = 0, places = 0, witnesses = 0, sources = 0;
        var lists = new List<(long Id, string Places)>();
        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY word_rendering (id, text_id, form, witness_id, strong_number, lemma, occurrences, links, methods, shared, places, samples) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var slug in loaded)
            {
                using var reader = store.Open(IndexProducts.Of(slug, IndexProducts.Renderings), RenderingRow.Schema);
                var numbering = new RowIds.Hashed(RenderingHashBits);
                foreach (var row in reader.Rows())
                {
                    var id = RenderingId(ids[slug].Id, numbering, row);
                    var count = RenderingPlaces.Count(row.Places);
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(id, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(ids[slug].Id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Form, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(ids[row.Witness].Id, NpgsqlDbType.Integer, cancellationToken);
                    await Optional(writer, row.Number, cancellationToken);
                    await Optional(writer, row.Lemma, cancellationToken);
                    await writer.WriteAsync(row.Occurrences, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Links, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Methods, NpgsqlDbType.Text, cancellationToken);
                    await Optional(writer, row.Shared, cancellationToken);
                    await writer.WriteAsync(count, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(RenderingPlaces.First(row.Places, RenderingPlaces.Samples), NpgsqlDbType.Text, cancellationToken);
                    renderings++;
                    if (count > RenderingPlaces.Samples)
                    {
                        lists.Add((id, row.Places));
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY word_rendering_place (rendering_id, places) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var (id, list) in lists)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(id, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(list, NpgsqlDbType.Text, cancellationToken);
                places++;
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY word_rendering_witness (text_id, form, witness_id, rank, occurrences, reached, links, methods, shared, sources) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var slug in loaded)
            {
                using var reader = store.Open(IndexProducts.Of(slug, IndexProducts.Rendered), RenderedRow.Schema);
                foreach (var row in reader.Rows())
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(ids[slug].Id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Form, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(ids[row.Witness].Id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Rank, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Occurrences, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Reached, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Links, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Methods, NpgsqlDbType.Text, cancellationToken);
                    await Optional(writer, row.Shared, cancellationToken);
                    await writer.WriteAsync(
                        row.Sources.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(n => int.Parse(n, System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
                        NpgsqlDbType.Array | NpgsqlDbType.Integer, cancellationToken);
                    witnesses++;
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY word_rendering_source (text_id, number, source) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var slug in loaded)
            {
                using var reader = store.Open(IndexProducts.Of(slug, IndexProducts.Rendered), RenderedRow.Schema);
                foreach (var (number, source) in RenderingIndex.Sources(reader.Header).OrderBy(s => s.Key))
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(ids[slug].Id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(number, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(source, NpgsqlDbType.Text, cancellationToken);
                    sources++;
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new Outcome("word_rendering*", renderings + places + witnesses + sources, loaded.Count, clock.Elapsed,
            $"{renderings} renderings, {places} with more places than their samples, {witnesses} by edition, {sources} sources");
    }

    private static async Task Optional(NpgsqlBinaryImporter writer, string? value, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await writer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await writer.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);
        }
    }

    /// <summary>
    /// <c>entity_rendering</c> and <c>entity_search_spelling</c> from the spellings indexes of every text, counted by
    /// <see cref="EntityRenderingLoader.Count"/> as the load counts them: both tables are replaced whole, since a spelling
    /// is chosen across texts. The names the texts print put back over a pass's are counted and not written.
    /// </summary>
    public static async Task<Outcome> Spellings(string connectionString, ProductStore store, EntityRenderingLoader counter, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await Spellings(connection, store, counter, cancellationToken, own: true);
    }

    /// <summary>The same on a connection whose transaction the caller holds and commits.</summary>
    public static Task<Outcome> Spellings(NpgsqlConnection connection, ProductStore store, EntityRenderingLoader counter, CancellationToken cancellationToken) =>
        Spellings(connection, store, counter, cancellationToken, own: false);

    private static async Task<Outcome> Spellings(NpgsqlConnection connection, ProductStore store, EntityRenderingLoader counter, CancellationToken cancellationToken, bool own)
    {
        var clock = Stopwatch.StartNew();
        var ids = await Texts(connection, cancellationToken);
        var records = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("SELECT slug, id FROM entity", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                records[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        var words = new List<NamedWord>();
        var printed = new Dictionary<(int Text, string Normalised), int>();
        var verses = new Dictionary<(int Text, VerseKey Verse), int>();
        var unknown = 0;
        var texts = 0;
        foreach (var (slug, (textId, language)) in ids.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (!store.Exists(IndexProducts.Of(slug, IndexProducts.Spellings)))
            {
                continue;
            }

            texts++;
            using var reader = store.Open(IndexProducts.Of(slug, IndexProducts.Spellings), SpellingRow.Schema(slug));
            foreach (var row in reader.Rows())
            {
                if (!records.TryGetValue(row.Record, out var entity))
                {
                    unknown++;
                    continue;
                }

                if (!verses.TryGetValue((textId, row.Own.Verse), out var verse))
                {
                    verses[(textId, row.Own.Verse)] = verse = verses.Count + 1;
                }

                words.Add(new NamedWord(entity, textId, verse, row.Own.Position, row.Surface, row.Trailer, row.Lemma, language,
                    row.Renders, row.Guess, row.Thing, row.Normalised, row.Firm));
                if (row.Normalised is { } normalised)
                {
                    printed[(textId, normalised)] = row.Printed;
                }
            }
        }

        var (renderings, spellings, search, renamed) = await counter.Count(
            words,
            asked => Task.FromResult(asked.Distinct().ToDictionary(a => a, a => printed.GetValueOrDefault(a))),
            cancellationToken);

        await using var transaction = own ? await connection.BeginTransactionAsync(cancellationToken) : null;
        await using (var delete = new NpgsqlCommand("DELETE FROM entity_rendering; DELETE FROM entity_search_spelling;", connection, transaction))
        {
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY entity_rendering (entity_id, text_id, form, folded, occurrences, heading) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var rendering in renderings)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(rendering.EntityId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(rendering.TextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(rendering.Form, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(rendering.Folded, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(rendering.Occurrences, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(rendering.Heading, NpgsqlDbType.Boolean, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY entity_search_spelling (entity_id, form, folded) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var spelling in spellings)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(spelling.EntityId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(spelling.Form, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(spelling.Folded, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new Outcome("entity_rendering, entity_search_spelling", renderings.Count + spellings.Count, texts, clock.Elapsed,
            $"{words.Count} named words, {renderings.Count} renderings, {spellings.Count} search spellings ({search}); " +
            $"{renamed.Count} names the texts print put back, not written; {unknown} words naming a record the database lacks");
    }
}
