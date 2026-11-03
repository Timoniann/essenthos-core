using Essenthos.Core.Products;
using Npgsql;

namespace Essenthos.Core.Conversion;

/// <summary>
/// A text as a corpus database holds it, read into the shapes of the converted products: what
/// <c>forge texts ids</c> takes the ids from and what <c>forge texts compare</c> holds a conversion against.
/// Reads only.
/// </summary>
internal sealed class TextSnapshot
{
    public required string Slug { get; init; }
    public required List<TextWord> Words { get; init; }
    public required List<VerseManifestRow> Verses { get; init; }
    public required List<TextNote> Notes { get; init; }
    public required List<TextBook> Books { get; init; }

    private const string WordQuery =
        """
        SELECT w.id, b.canonical_ordinal, v.chapter_number, v.number, v.label, w."position", w."text", w.trailer,
               w.lemma, w.strong_number, w.gloss, w.morphology::text, w.normalised_text, w.graphical_text, w.elided, w."break"
        FROM word w JOIN verse v ON v.id = w.verse_id JOIN book b ON b.id = v.book_id
        WHERE w.text_id = @text
        ORDER BY b.canonical_ordinal, v.chapter_number, v.number, v.label COLLATE "C", w."position"
        """;

    private const string GroupQuery =
        """
        SELECT gw.word_id, g.id, g.kind, g."position", g.features::text
        FROM word_group g JOIN word_group_word gw ON gw.word_group_id = g.id
        WHERE g.text_id = @text AND g.kind IN ('supplied', 'restored', 'doubtful', 'subscription')
        """;

    private const string VerseQuery =
        """
        SELECT b.canonical_ordinal, v.chapter_number, v.number, v.label, v.sequence,
               (SELECT count(*) FROM word w WHERE w.verse_id = v.id)
        FROM verse v JOIN book b ON b.id = v.book_id
        WHERE v.text_id = @text
        """;

    private const string NoteQuery =
        """
        SELECT b.canonical_ordinal || '.' || v.chapter_number || ':' || v.number || v.label, 'verse', n."position", n.kind, n.content,
               (SELECT w."position" FROM word w WHERE w.id = n.anchor_word_id), NULL
        FROM verse_note n JOIN verse v ON v.id = n.verse_id JOIN book b ON b.id = v.book_id WHERE v.text_id = @text
        UNION ALL
        SELECT b.canonical_ordinal || '.' || v.chapter_number || ':' || v.number || v.label, 'stated', s."position", '',
               s.chapter_number || s.chapter_label || ':' || s.number, NULL, NULL
        FROM stated_verse_number s JOIN verse v ON v.id = s.verse_id JOIN book b ON b.id = v.book_id WHERE v.text_id = @text
        UNION ALL
        SELECT b.canonical_ordinal || '.' || c.number, 'chapter', n."position", n.kind, n.content, NULL, n.heading
        FROM chapter_note n JOIN chapter c ON c.id = n.chapter_id JOIN book b ON b.id = c.book_id WHERE c.text_id = @text
        UNION ALL
        SELECT b.canonical_ordinal::text, 'book', n."position", n.kind, n.content, NULL, NULL
        FROM book_note n JOIN book b ON b.id = n.book_id WHERE b.text_id = @text
        """;

    private const string BookQuery =
        "SELECT canonical_ordinal, \"position\", name, name_native, abbreviation, slug FROM book WHERE text_id = @text";

    public static async Task<int?> TextId(NpgsqlConnection connection, string slug)
    {
        await using var command = new NpgsqlCommand("SELECT id FROM text WHERE slug = @slug", connection);
        command.Parameters.AddWithValue("slug", slug);
        return await command.ExecuteScalarAsync() is int id ? id : null;
    }

    /// <summary>Only the ids, by key, in key order.</summary>
    public static async Task<List<TextWordId>> ReadIds(NpgsqlConnection connection, string slug)
    {
        var text = await TextId(connection, slug) ?? throw new InvalidOperationException($"The database holds no text {slug}.");
        var ids = new List<TextWordId>(1 << 20);
        await using var command = new NpgsqlCommand(
            """
            SELECT w.id, b.canonical_ordinal, v.chapter_number, v.number, v.label, w."position"
            FROM word w JOIN verse v ON v.id = w.verse_id JOIN book b ON b.id = v.book_id
            WHERE w.text_id = @text
            ORDER BY b.canonical_ordinal, v.chapter_number, v.number, v.label COLLATE "C", w."position"
            """, connection) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("text", text);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(new TextWordId(
                new WordKey(slug, new VerseKey(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4)), reader.GetInt32(5)),
                reader.GetInt64(0)));
        }

        return ids;
    }

    public static async Task<TextSnapshot> Read(NpgsqlConnection connection, string slug)
    {
        var text = await TextId(connection, slug) ?? throw new InvalidOperationException($"The database holds no text {slug}.");

        var groups = new Dictionary<long, List<(long Group, string Kind, int Position, string? Features)>>();
        await using (var command = Command(connection, GroupQuery, text))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var word = reader.GetInt64(0);
                if (!groups.TryGetValue(word, out var list))
                {
                    groups[word] = list = [];
                }

                list.Add((reader.GetInt64(1), reader.GetString(2), reader.GetInt32(3), Text(reader, 4)));
            }
        }

        var words = new List<TextWord>(1 << 20);
        var wordGroups = new List<(int Index, long Group, string Kind, int Position, string? Features)>();
        await using (var command = Command(connection, WordQuery, text))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt64(0);
                var key = new WordKey(slug, new VerseKey(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4)), reader.GetInt32(5));
                if (groups.TryGetValue(id, out var memberships))
                {
                    wordGroups.AddRange(memberships.Select(g => (words.Count, g.Group, g.Kind, g.Position, g.Features)));
                }

                words.Add(new TextWord(key, id, reader.GetString(6), reader.GetString(7), Text(reader, 8), Text(reader, 9), Text(reader, 10),
                    Text(reader, 11), Text(reader, 12), Text(reader, 13), reader.GetBoolean(14), Text(reader, 15), null, null));
            }
        }

        // A supplied or restored group's number is its order among the verse's groups of its kind, by its first word, as the
        // converter numbers spans; a doubtful or subscription group's is its position, which runs through the text.
        foreach (var byVerse in wordGroups.Where(g => g.Kind is "supplied" or "restored").GroupBy(g => (words[g.Index].Key.Verse, g.Kind)))
        {
            var order = byVerse.GroupBy(g => g.Group).OrderBy(g => g.Min(m => m.Index)).Select((g, at) => (g.Key, at + 1)).ToDictionary();
            foreach (var member in byVerse)
            {
                words[member.Index] = member.Kind == "restored"
                    ? words[member.Index] with { Restored = order[member.Group] }
                    : words[member.Index] with { Supplied = order[member.Group], SuppliedMark = Feature(member.Features, "mark") };
            }
        }

        foreach (var member in wordGroups.Where(g => g.Kind is "doubtful" or "subscription"))
        {
            words[member.Index] = member.Kind == "doubtful"
                ? words[member.Index] with { Doubtful = member.Position, Brackets = Feature(member.Features, "brackets") is { } degree ? int.Parse(degree) : null }
                : words[member.Index] with { Subscription = member.Position };
        }

        var verses = new List<VerseManifestRow>(32_000);
        await using (var command = Command(connection, VerseQuery, text))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                verses.Add(new VerseManifestRow(
                    new VerseKey(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3)),
                    reader.GetInt32(4), (int)reader.GetInt64(5), default));
            }
        }

        var notes = new List<TextNote>();
        await using (var command = Command(connection, NoteQuery, text))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                notes.Add(new TextNote(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5), Text(reader, 6)));
            }
        }

        var books = new List<TextBook>();
        await using (var command = Command(connection, BookQuery, text))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                books.Add(new TextBook(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), Text(reader, 3), Text(reader, 4), reader.GetString(5)));
            }
        }

        return new TextSnapshot { Slug = slug, Words = words, Verses = verses, Notes = notes, Books = books };
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, int text)
    {
        var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("text", text);
        return command;
    }

    private static string? Feature(string? features, string name) =>
        features is null || System.Text.Json.JsonDocument.Parse(features).RootElement is not { ValueKind: System.Text.Json.JsonValueKind.Object } root
        || !root.TryGetProperty(name, out var value)
            ? null
            : value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => value.GetString(),
                System.Text.Json.JsonValueKind.Null => null,
                _ => value.GetRawText(),
            };

    private static string? Text(NpgsqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);
}
