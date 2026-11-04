using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Build;

/// <summary>
/// The texts' products into an empty serving database: <c>text</c>, <c>book</c>, <c>chapter</c>, <c>verse</c>, <c>word</c>,
/// the notes and stated verse numbers, and the four kinds of word group the converter keeps on the words (supplied,
/// restored, doubtful, subscription). Book, chapter and verse ids are given here in catalogue and key order; word ids are
/// the ones the words products carry. Words go in over one connection per text, sixteen texts at a time.
/// </summary>
internal static class TextLoad
{
    public static readonly string[] Tables =
        ["text", "book", "chapter", "verse", "word", "verse_note", "chapter_note", "book_note", "stated_verse_number"];

    public sealed record Outcome(int Texts, long Verses, long Words, long Notes, TimeSpan Elapsed);

    /// <summary>What the words say of the groups they stand in, written after the carried groups so their ids follow them.</summary>
    public sealed class Groups
    {
        internal readonly ConcurrentBag<(int Text, string Kind, int Position, string? Features, long[] Words)> Rows = [];

        public int Count => Rows.Count;
    }

    private sealed record LoadedText(string Slug, int Id, Dictionary<VerseKey, int> Verses, Dictionary<(int Book, int Chapter), int> Chapters,
        Dictionary<int, int> Books);

    public static IReadOnlyList<string> Slugs(ProductStore store) =>
        [.. TextCatalogue.All.Select(t => t.Slug).Where(slug => store.Exists(TextWord.ProductOf(slug)))];

    public static async Task<(Outcome Outcome, Groups Groups)> Run(string connectionString, ProductStore store, int parallel,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var slugs = Slugs(store);
        var texts = new List<LoadedText>();
        long verseCount = 0;
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            var nextBook = 0;
            var nextChapter = 0;
            var nextVerse = 0;
            await using (var textCopy = await connection.BeginBinaryImportAsync(
                             "COPY text (id, slug, name, name_native, kind, language, direction, versification, published_year, source_url, rights_holder, " +
                             "licence, licence_url, redistribution, textual_family, citation, about, edition, edition_year, editors, rights_note, translators, " +
                             "script, part_sources) FROM STDIN (FORMAT BINARY)", cancellationToken))
            {
                for (var i = 0; i < slugs.Count; i++)
                {
                    await WriteText(textCopy, i + 1, Fields(store, slugs[i]), cancellationToken);
                }

                await textCopy.CompleteAsync(cancellationToken);
            }

            var books = new List<(int Id, int Text, TextBook Book)>();
            var chapters = new List<(int Id, int Text, int Book, int Number)>();
            var verses = new List<(int Id, int Text, int Book, int Chapter, VerseManifestRow Row)>();
            for (var i = 0; i < slugs.Count; i++)
            {
                var textId = i + 1;
                var loaded = new LoadedText(slugs[i], textId, [], [], []);
                using (var reader = store.Open(TextBook.ProductOf(slugs[i]), TextBook.Schema))
                {
                    foreach (var book in reader.Rows())
                    {
                        loaded.Books[book.Book] = ++nextBook;
                        books.Add((nextBook, textId, book));
                    }
                }

                using (var reader = store.Open(VerseManifestRow.ProductOf(slugs[i]), VerseManifestRow.Schema))
                {
                    foreach (var row in reader.Rows().OrderBy(r => r.Own))
                    {
                        if (row.Own.ChapterLabel.Length > 0)
                        {
                            throw new InvalidOperationException($"{slugs[i]} {row.Own}: the verse table holds no chapter label.");
                        }

                        var book = loaded.Books.TryGetValue(row.Own.Book, out var known)
                            ? known
                            : throw new InvalidOperationException($"{slugs[i]} {row.Own}: its book is not in the text's books product.");
                        if (!loaded.Chapters.TryGetValue((row.Own.Book, row.Own.Chapter), out var chapter))
                        {
                            loaded.Chapters[(row.Own.Book, row.Own.Chapter)] = chapter = ++nextChapter;
                            chapters.Add((chapter, textId, book, row.Own.Chapter));
                        }

                        loaded.Verses[row.Own] = ++nextVerse;
                        verses.Add((nextVerse, textId, book, chapter, row));
                    }
                }

                texts.Add(loaded);
            }

            verseCount = verses.Count;
            await using (var copy = await connection.BeginBinaryImportAsync(
                             "COPY book (id, text_id, canonical_ordinal, position, name, name_native, abbreviation, slug) FROM STDIN (FORMAT BINARY)",
                             cancellationToken))
            {
                foreach (var (id, text, book) in books)
                {
                    await copy.WriteRowAsync(cancellationToken, id, text, book.Book, book.Position, book.Name, Db(book.NameNative), Db(book.Abbreviation), book.Slug);
                }

                await copy.CompleteAsync(cancellationToken);
            }

            await using (var copy = await connection.BeginBinaryImportAsync("COPY chapter (id, text_id, book_id, number) FROM STDIN (FORMAT BINARY)", cancellationToken))
            {
                foreach (var (id, text, book, number) in chapters)
                {
                    await copy.WriteRowAsync(cancellationToken, id, text, book, number);
                }

                await copy.CompleteAsync(cancellationToken);
            }

            await using (var copy = await connection.BeginBinaryImportAsync(
                             "COPY verse (id, text_id, book_id, chapter_id, chapter_number, number, label, sequence) FROM STDIN (FORMAT BINARY)", cancellationToken))
            {
                foreach (var (id, text, book, chapter, row) in verses)
                {
                    await copy.WriteRowAsync(cancellationToken, id, text, book, chapter, row.Own.Chapter, row.Own.Verse, row.Own.VerseLabel, row.Sequence);
                }

                await copy.CompleteAsync(cancellationToken);
            }

            await Restart(connection, "book", nextBook, cancellationToken);
            await Restart(connection, "chapter", nextChapter, cancellationToken);
            await Restart(connection, "verse", nextVerse, cancellationToken);
            await Restart(connection, "text", slugs.Count, cancellationToken);
        }

        var groups = new Groups();
        long words = 0;
        long notes = 0;
        await Parallel.ForEachAsync(texts, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken }, async (text, token) =>
        {
            var (written, noted) = await LoadWords(connectionString, store, text, groups, token);
            Interlocked.Add(ref words, written);
            Interlocked.Add(ref notes, noted);
        });

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            foreach (var table in (string[])["verse_note", "chapter_note", "book_note", "stated_verse_number"])
            {
                await using var command = new NpgsqlCommand(
                    $"SELECT setval(pg_get_serial_sequence('{table}', 'id'), greatest(coalesce((SELECT max(id) FROM {table}), 0), 1))", connection);
                await command.ExecuteScalarAsync(cancellationToken);
            }
        }

        return (new Outcome(texts.Count, verseCount, words, notes, clock.Elapsed), groups);
    }

    private static async Task<(long Words, long Notes)> LoadWords(string connectionString, ProductStore store, LoadedText text, Groups groups,
        CancellationToken cancellationToken)
    {
        var strongServed = TextWordLoad.StrongServed(store, text.Slug);
        var byKey = new Dictionary<WordKey, long>();
        var spans = new Dictionary<(string Kind, VerseKey Verse, int Number), (string? Mark, List<long> Words)>();
        var marks = new SortedDictionary<(string Kind, int Position), (int? Brackets, List<long> Words)>();
        var spanOrder = new List<(string Kind, VerseKey Verse, int Number)>();
        long written = 0;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY word (id, text_id, verse_id, position, text, trailer, lemma, strong_number, gloss, morphology, normalised_text, elided, " +
                         "graphical_text, break) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            using var reader = store.Open(TextWord.ProductOf(text.Slug), TextWord.Schema(text.Slug));
            foreach (var word in reader.Rows())
            {
                var id = word.Id ?? throw new InvalidOperationException($"{word.Key} has no id; convert the text again.");
                var verse = text.Verses.TryGetValue(word.Key.Verse, out var known)
                    ? known
                    : throw new InvalidOperationException($"{word.Key}: its verse is not in the text's verse manifest.");
                byKey[word.Key] = id;
                await copy.StartRowAsync(cancellationToken);
                await copy.WriteAsync(id, NpgsqlDbType.Bigint, cancellationToken);
                await copy.WriteAsync(text.Id, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(verse, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(word.Key.Position, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(word.Surface, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(word.Trailer, NpgsqlDbType.Text, cancellationToken);
                await Write(copy, word.Lemma, NpgsqlDbType.Text, cancellationToken);
                await Write(copy, strongServed ? word.Strong : null, NpgsqlDbType.Text, cancellationToken);
                await Write(copy, word.Gloss, NpgsqlDbType.Text, cancellationToken);
                await Write(copy, word.Morphology, NpgsqlDbType.Jsonb, cancellationToken);
                await Write(copy, word.Normalised, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(word.Elided, NpgsqlDbType.Boolean, cancellationToken);
                await Write(copy, word.Graphical, NpgsqlDbType.Text, cancellationToken);
                await Write(copy, word.Break, NpgsqlDbType.Text, cancellationToken);
                written++;

                if (word.Supplied is { } supplied)
                {
                    Span(("supplied", word.Key.Verse, supplied), word.SuppliedMark, id);
                }

                if (word.Restored is { } restored)
                {
                    Span(("restored", word.Key.Verse, restored), null, id);
                }

                if (word.Doubtful is { } doubtful)
                {
                    Mark(("doubtful", doubtful), word.Brackets, id);
                }

                if (word.Subscription is { } subscription)
                {
                    // The Stephanus prints each subscription in one pair of brackets, which is all the edition-marks step recorded.
                    Mark(("subscription", subscription), 1, id);
                }
            }

            await copy.CompleteAsync(cancellationToken);
        }

        foreach (var kind in spanOrder.GroupBy(s => s.Kind))
        {
            var position = 0;
            foreach (var span in kind)
            {
                var (mark, members) = spans[span];
                groups.Rows.Add((text.Id, span.Kind, ++position, mark is null ? null : JsonSerializer.Serialize(new { mark }), [.. members]));
            }
        }

        foreach (var ((kind, position), (brackets, members)) in marks)
        {
            groups.Rows.Add((text.Id, kind, position, brackets is { } count ? $"{{\"brackets\": {count}}}" : null, [.. members]));
        }

        var notes = await LoadNotes(connection, store, text, byKey, cancellationToken);
        return (written, notes);

        void Span((string Kind, VerseKey Verse, int Number) key, string? mark, long id)
        {
            if (!spans.TryGetValue(key, out var span))
            {
                spans[key] = span = (mark, []);
                spanOrder.Add(key);
            }

            span.Words.Add(id);
        }

        void Mark((string Kind, int Position) key, int? brackets, long id)
        {
            if (!marks.TryGetValue(key, out var mark))
            {
                marks[key] = mark = (brackets, []);
            }

            mark.Words.Add(id);
        }
    }

    private static async Task<long> LoadNotes(NpgsqlConnection connection, ProductStore store, LoadedText text, Dictionary<WordKey, long> words,
        CancellationToken cancellationToken)
    {
        List<TextNote> notes;
        using (var reader = store.Open(TextNote.ProductOf(text.Slug), TextNote.Schema))
        {
            notes = [.. reader.Rows()];
        }

        if (notes.Count == 0)
        {
            return 0;
        }

        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY verse_note (verse_id, position, kind, content, anchor_word_id) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var note in notes.Where(n => n.Scope == "verse"))
            {
                var key = VerseKey.Parse(note.At);
                object anchor = note.Anchor is { } position && words.TryGetValue(new WordKey(text.Slug, key, position), out var word) ? word : DBNull.Value;
                await copy.WriteRowAsync(cancellationToken, Verse(text, key), note.Position, note.Kind, note.Content, anchor);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY stated_verse_number (verse_id, position, chapter_number, chapter_label, number) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var note in notes.Where(n => n.Scope == "stated"))
            {
                var colon = note.Content.IndexOf(':');
                if (!VerseKey.TryParseNumbered(note.Content.AsSpan(0, colon), out var chapter, out var label))
                {
                    throw new InvalidOperationException($"{text.Slug} {note.At}: a stated number '{note.Content}' that does not read.");
                }

                await copy.WriteRowAsync(cancellationToken, Verse(text, VerseKey.Parse(note.At)), note.Position, chapter, label,
                    int.Parse(note.Content[(colon + 1)..], CultureInfo.InvariantCulture));
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY chapter_note (chapter_id, position, heading, kind, content) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var note in notes.Where(n => n.Scope == "chapter"))
            {
                var dot = note.At.IndexOf('.');
                var chapter = text.Chapters[(int.Parse(note.At[..dot], CultureInfo.InvariantCulture), int.Parse(note.At[(dot + 1)..], CultureInfo.InvariantCulture))];
                await copy.WriteRowAsync(cancellationToken, chapter, note.Position, note.Heading ?? string.Empty, note.Kind, note.Content);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY book_note (book_id, position, kind, content) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var note in notes.Where(n => n.Scope == "book"))
            {
                await copy.WriteRowAsync(cancellationToken, text.Books[int.Parse(note.At, CultureInfo.InvariantCulture)], note.Position, note.Kind, note.Content);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        return notes.Count;
    }

    /// <summary>The word groups the words products carry, numbered after whatever <c>word_group</c> already holds.</summary>
    public static async Task<long> WriteGroups(string connectionString, Groups groups, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        long next;
        await using (var command = new NpgsqlCommand("SELECT coalesce(max(id), 0) FROM word_group", connection))
        {
            next = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        var ordered = groups.Rows.OrderBy(g => g.Text).ThenBy(g => g.Kind, StringComparer.Ordinal).ThenBy(g => g.Position).ToList();
        var ids = new long[ordered.Count];
        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY word_group (id, text_id, kind, position, features) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                ids[i] = ++next;
                await copy.StartRowAsync(cancellationToken);
                await copy.WriteAsync(ids[i], NpgsqlDbType.Bigint, cancellationToken);
                await copy.WriteAsync(ordered[i].Text, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(ordered[i].Kind, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(ordered[i].Position, NpgsqlDbType.Integer, cancellationToken);
                await Write(copy, ordered[i].Features, NpgsqlDbType.Jsonb, cancellationToken);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync("COPY word_group_word (word_group_id, word_id) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                foreach (var word in ordered[i].Words.Distinct())
                {
                    await copy.WriteRowAsync(cancellationToken, ids[i], word);
                }
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand($"SELECT setval(pg_get_serial_sequence('word_group', 'id'), {Math.Max(next, 1)})", connection))
        {
            await command.ExecuteScalarAsync(cancellationToken);
        }

        return ordered.Count;
    }

    private static int Verse(LoadedText text, VerseKey key) =>
        text.Verses.TryGetValue(key, out var id) ? id : throw new InvalidOperationException($"{text.Slug} {key}: a note on a verse the text does not hold.");

    private static Dictionary<string, List<string?>> Fields(ProductStore store, string slug)
    {
        var fields = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
        using var reader = store.Open(TextField.ProductOf(slug), TextField.Schema);
        foreach (var field in reader.Rows())
        {
            if (!fields.TryGetValue(field.Field, out var values))
            {
                fields[field.Field] = values = [];
            }

            values.Add(field.Value);
        }

        return fields;
    }

    private static async Task WriteText(NpgsqlBinaryImporter copy, int id, Dictionary<string, List<string?>> fields, CancellationToken cancellationToken)
    {
        string? One(string name) => fields.TryGetValue(name, out var values) ? values[0] : null;
        int? Year(string name) => One(name) is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : null;
        string Required(string name) => One(name) ?? throw new InvalidOperationException($"The text declaration lacks '{name}'.");

        // Each part source was written as the serialised record; the table holds them as one JSON array with camel-case names.
        string? parts = fields.TryGetValue("part-source", out var listed) && listed.Count > 0
            ? "[" + string.Join(", ", listed.Select(part => JsonSerializer.Serialize(
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(part!)!
                    .Where(pair => pair.Value.ValueKind != JsonValueKind.Null)
                    .ToDictionary(pair => JsonNamingPolicy.CamelCase.ConvertName(pair.Key), pair => pair.Value)))) + "]"
            : null;

        await copy.StartRowAsync(cancellationToken);
        await copy.WriteAsync(id, NpgsqlDbType.Integer, cancellationToken);
        await copy.WriteAsync(Required("slug"), NpgsqlDbType.Text, cancellationToken);
        await copy.WriteAsync(Required("name"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("name-native"), NpgsqlDbType.Text, cancellationToken);
        await copy.WriteAsync(Required("kind"), NpgsqlDbType.Text, cancellationToken);
        await copy.WriteAsync(Required("language"), NpgsqlDbType.Text, cancellationToken);
        await copy.WriteAsync(Required("direction"), NpgsqlDbType.Text, cancellationToken);
        await copy.WriteAsync(Required("versification"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, Year("published-year"), NpgsqlDbType.Integer, cancellationToken);
        await Write(copy, One("source-url"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("rights-holder"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("licence"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("licence-url"), NpgsqlDbType.Text, cancellationToken);
        await copy.WriteAsync(Required("redistribution"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("textual-family"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("citation"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("about"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("edition"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, Year("edition-year"), NpgsqlDbType.Integer, cancellationToken);
        await Write(copy, One("editors"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("rights-note"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("translators"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, One("script"), NpgsqlDbType.Text, cancellationToken);
        await Write(copy, parts, NpgsqlDbType.Jsonb, cancellationToken);
    }

    private static async Task Restart(NpgsqlConnection connection, string table, int last, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT setval(pg_get_serial_sequence('{table}', 'id'), {Math.Max(last, 1)})", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static object Db(string? value) => value is null ? DBNull.Value : value;

    private static Task Write<T>(NpgsqlBinaryImporter copy, T? value, NpgsqlDbType type, CancellationToken cancellationToken) where T : class =>
        value is null ? copy.WriteNullAsync(cancellationToken) : copy.WriteAsync(value, type, cancellationToken);

    private static Task Write(NpgsqlBinaryImporter copy, int? value, NpgsqlDbType type, CancellationToken cancellationToken) =>
        value is { } v ? copy.WriteAsync(v, type, cancellationToken) : copy.WriteNullAsync(cancellationToken);
}
