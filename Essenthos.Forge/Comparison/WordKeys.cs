using Essenthos.Core.Products;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Comparison;

/// <summary>
/// Every word of one database by its key and back: <c>KJV 1.1:1.3</c> for a word id, and the id for a key.
/// What turns rows that point at words into rows two databases can be compared on, and what a loader
/// resolves the keys of a product file through. A word id is a hash of its key (<see cref="WordIds"/>), so the words
/// are held in arrays sorted by id and found by binary search: all 29 M words take about 0.4 GB, a key 25 compares.
/// </summary>
internal sealed class WordKeys : Mapping.ILinkWords
{
    private readonly Dictionary<int, string> _texts;
    private readonly Dictionary<string, int> _textIds;
    private readonly int[] _verseText;
    private readonly VerseKey[] _verseKey;
    private readonly string?[] _verseOwn;
    private readonly long[] _wordIds;
    private readonly int[] _wordVerse;
    private readonly short[] _wordPosition;
    private Dictionary<(int Text, VerseKey Verse), int>? _verses;
    private int[]? _verseStart;
    private long[]? _ids;
    private short[]? _positions;

    private WordKeys(Dictionary<int, string> texts, int[] verseText, VerseKey[] verseKey, long[] wordIds, int[] wordVerse, short[] wordPosition)
    {
        _texts = texts;
        _textIds = texts.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        _verseText = verseText;
        _verseKey = verseKey;
        _verseOwn = new string?[verseKey.Length];
        _wordIds = wordIds;
        _wordVerse = wordVerse;
        _wordPosition = wordPosition;
    }

    public int Words { get; private set; }

    public IReadOnlyDictionary<string, int> TextIds => _textIds;

    public string Slug(int textId) => _texts[textId];

    public int? TextId(string slug) => _textIds.TryGetValue(slug, out var id) ? id : null;

    /// <summary>Reads every text, verse and word of the database; with <paramref name="reverse"/>, the lookup from key to id too.</summary>
    public static async Task<WordKeys> Load(NpgsqlConnection connection, bool reverse, CancellationToken cancellationToken)
    {
        var texts = new Dictionary<int, string>();
        await using (var command = new NpgsqlCommand("SELECT id, slug FROM text", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                texts[reader.GetInt32(0)] = reader.GetString(1);
            }
        }

        var maxVerse = await Scalar(connection, "SELECT coalesce(max(id), 0) FROM verse", cancellationToken);
        var wordCount = await Scalar(connection, "SELECT count(*) FROM word", cancellationToken);
        var verseText = new int[maxVerse + 1];
        var verseKey = new VerseKey[maxVerse + 1];
        using (var export = connection.BeginBinaryExport(
                         """
                         COPY (SELECT v.id, v.text_id, b.canonical_ordinal, v.chapter_number, v.number, v.label
                               FROM verse v JOIN book b ON b.id = v.book_id) TO STDOUT (FORMAT BINARY)
                         """))
        {
            while (export.StartRow() >= 0)
            {
                var id = export.Read<int>(NpgsqlDbType.Integer);
                verseText[id] = export.Read<int>(NpgsqlDbType.Integer);
                var book = export.Read<int>(NpgsqlDbType.Integer);
                var chapter = export.Read<int>(NpgsqlDbType.Integer);
                var number = export.Read<int>(NpgsqlDbType.Integer);
                var label = export.Read<string>(NpgsqlDbType.Text);
                verseKey[id] = new VerseKey(book, chapter, number, label.Length == 0 ? string.Empty : string.Intern(label));
            }
        }

        // Each word's verse and position packed beside its id, so that one sort orders all three.
        var wordIds = new long[wordCount];
        var packed = new long[wordCount];
        var count = 0;
        using (var export = connection.BeginBinaryExport(
                         "COPY (SELECT id, verse_id, position FROM word) TO STDOUT (FORMAT BINARY)"))
        {
            while (export.StartRow() >= 0)
            {
                if (count == wordCount)
                {
                    throw new InvalidOperationException("The word table grew while its keys were read.");
                }

                wordIds[count] = export.Read<long>(NpgsqlDbType.Bigint);
                var verse = export.Read<int>(NpgsqlDbType.Integer);
                var position = checked((short)export.Read<int>(NpgsqlDbType.Integer));
                packed[count++] = ((long)verse << 16) | (ushort)position;
            }
        }

        if (count < wordCount)
        {
            Array.Resize(ref wordIds, count);
            Array.Resize(ref packed, count);
        }

        Array.Sort(wordIds, packed);
        var wordVerse = new int[count];
        var wordPosition = new short[count];
        for (var i = 0; i < count; i++)
        {
            wordVerse[i] = (int)(packed[i] >> 16);
            wordPosition[i] = (short)(ushort)packed[i];
        }

        var keys = new WordKeys(texts, verseText, verseKey, wordIds, wordVerse, wordPosition) { Words = count };
        if (reverse)
        {
            keys.BuildReverse();
        }

        return keys;
    }

    public bool Has(long word) => Array.BinarySearch(_wordIds, word) >= 0;

    public int VerseOf(long word) => Verse(word);

    public int TextOf(long word) => _verseText[Verse(word)];

    public VerseKey VerseKeyOf(int verse) => _verseKey[verse];

    public int TextOfVerse(int verse) => _verseText[verse];

    public WordKey Key(long word)
    {
        var at = At(word);
        var verse = _wordVerse[at];
        return new WordKey(_texts[_verseText[verse]], _verseKey[verse], _wordPosition[at]);
    }

    /// <summary>The key without its text, <c>1.1:1.3</c>, as a file of one text or one pair writes it.</summary>
    public string Own(long word)
    {
        var at = At(word);
        var verse = _wordVerse[at];
        var own = _verseOwn[verse] ??= _verseKey[verse].ToString();
        return string.Concat(own, ".", _wordPosition[at].ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The word a key names, or null where this database has no such word.</summary>
    public long? Id(int textId, VerseKey verse, int position)
    {
        if (_verses is null)
        {
            throw new InvalidOperationException("The keys were loaded without the lookup from key to id.");
        }

        if (!_verses.TryGetValue((textId, verse), out var v))
        {
            return null;
        }

        var start = _verseStart![v];
        var length = _verseStart[v + 1] - start;
        var at = Array.BinarySearch(_positions!, start, length, checked((short)position));
        return at >= 0 ? _ids![at] : null;
    }

    public long? Id(int textId, WordKey key) => Id(textId, key.Verse, key.Position);

    private int At(long word) =>
        Array.BinarySearch(_wordIds, word) is var at and >= 0 ? at : throw new KeyNotFoundException($"The database has no word {word}.");

    private int Verse(long word) => _wordVerse[At(word)];

    private void BuildReverse()
    {
        var verses = _verseKey.Length;
        _verses = new Dictionary<(int, VerseKey), int>(verses);
        for (var v = 1; v < verses; v++)
        {
            if (_verseText[v] != 0)
            {
                _verses[(_verseText[v], _verseKey[v])] = v;
            }
        }

        var start = new int[verses + 1];
        for (var w = 0; w < _wordVerse.Length; w++)
        {
            start[_wordVerse[w] + 1]++;
        }

        for (var v = 1; v <= verses; v++)
        {
            start[v] += start[v - 1];
        }

        var fill = (int[])start.Clone();
        var ids = new long[Words];
        var positions = new short[Words];
        for (var w = 0; w < _wordVerse.Length; w++)
        {
            var at = fill[_wordVerse[w]]++;
            ids[at] = _wordIds[w];
            positions[at] = _wordPosition[w];
        }

        for (var v = 1; v < verses; v++)
        {
            var length = start[v + 1] - start[v];
            if (length > 1)
            {
                Array.Sort(positions, ids, start[v], length);
            }
        }

        _verseStart = start;
        _ids = ids;
        _positions = positions;
    }

    private static async Task<int> Scalar(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return checked((int)Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)));
    }
}
