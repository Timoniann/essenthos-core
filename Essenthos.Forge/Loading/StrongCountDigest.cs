using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading;

/// <summary>
/// Everything a text's Strong counts are counted from, summed per text in one pass over each table,
/// so <see cref="StrongRenderingLoader"/> can tell in seconds which texts a count would come out
/// differently for.
///
/// <para>
/// A text's count reads its own words and verses, its links to every original it is linked to (how
/// many decide which edition it is counted over), those originals' words, verses and books, and the
/// parts of speech every edition states. Each table is summed as a count and a sum of a 64-bit hash of
/// every row's columns that the count reads, so a row added, removed or changed in place moves it; a
/// link's words move it through the link's fingerprint, which the database keeps in step with them.
/// </para>
///
/// <para>
/// What the code counts is in it too: the statements themselves, and <see cref="Version"/> for what
/// is counted in C#. Change <see cref="Version"/> with any change to how the phrases, their variety or
/// the lexical class are counted, or the load keeps the counts of the old code.
/// </para>
/// </summary>
internal sealed class StrongCountDigest
{
    public const int Version = 2;

    private const string PairSums =
        """
        SELECT from_text_id, to_text_id, count(*),
               sum(hashtextextended(concat_ws('|', id, relation, method, coalesce(fingerprint::text, '-')), 0))::text
        FROM link
        GROUP BY from_text_id, to_text_id
        """;

    private const string WordSums =
        """
        SELECT text_id, count(*) || ':' || sum(hashtextextended(concat_ws('|', id, verse_id, position, text, coalesce(strong_number, '-')), 0))
        FROM word
        GROUP BY text_id
        """;

    private const string VerseSums =
        """
        SELECT text_id, count(*) || ':' || sum(hashtextextended(concat_ws('|', id, book_id, number), 0))
        FROM verse
        GROUP BY text_id
        """;

    private const string BookSums =
        """
        SELECT text_id, count(*) || ':' || sum(hashtextextended(concat_ws('|', id, canonical_ordinal), 0))
        FROM book
        GROUP BY text_id
        """;

    private static readonly string[] PhraseTables = ["strong_rendering", "strong_reach", "strong_reach_method", "strong_book_reach"];

    private readonly IReadOnlyDictionary<int, (string Slug, string Name, TextKind Kind, string Language)> _texts;
    private readonly IReadOnlyDictionary<(int From, int To), (long Links, string Sum)> _pairs;
    private readonly IReadOnlyDictionary<int, string> _words;
    private readonly IReadOnlyDictionary<int, string> _verses;
    private readonly IReadOnlyDictionary<int, string> _books;
    private readonly string _classes;
    private readonly IReadOnlySet<int> _numbered;

    private StrongCountDigest(
        IReadOnlyDictionary<int, (string Slug, string Name, TextKind Kind, string Language)> texts,
        IReadOnlyDictionary<(int From, int To), (long Links, string Sum)> pairs,
        IReadOnlyDictionary<int, string> words,
        IReadOnlyDictionary<int, string> verses,
        IReadOnlyDictionary<int, string> books,
        string classes,
        IReadOnlySet<int> numbered)
    {
        _numbered = numbered;
        _texts = texts;
        _pairs = pairs;
        _words = words;
        _verses = verses;
        _books = books;
        _classes = classes;
    }

    /// <summary>The sums of every table a count reads, and <paramref name="classes"/> as the count reads them.</summary>
    public static async Task<StrongCountDigest> Read(
        AppDbContext db,
        IEnumerable<(string Number, string Label, int Words)> classes,
        CancellationToken cancellationToken)
    {
        var texts = (await db.Texts.AsNoTracking()
                .Select(t => new { t.Id, t.Slug, t.Name, t.Kind, t.Language })
                .ToListAsync(cancellationToken))
            .ToDictionary(t => t.Id, t => (t.Slug, t.Name, t.Kind, t.Language));

        var connection = await Open(db, cancellationToken);
        var pairs = new Dictionary<(int From, int To), (long Links, string Sum)>();
        await using (var command = new NpgsqlCommand(PairSums, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                pairs[(reader.GetInt32(0), reader.GetInt32(1))] = (reader.GetInt64(2), reader.GetString(3));
            }
        }

        var lines = classes
            .Select(row => $"{row.Number}\t{row.Label}\t{row.Words}")
            .Order(StringComparer.Ordinal);

        return new StrongCountDigest(
            texts,
            pairs,
            await Sums(connection, WordSums, cancellationToken),
            await Sums(connection, VerseSums, cancellationToken),
            await Sums(connection, BookSums, cancellationToken),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))),
            (await db.Texts.Where(t => db.Words.Any(w => w.TextId == t.Id && w.StrongNumber != null))
                .Select(t => t.Id)
                .ToListAsync(cancellationToken)).ToHashSet());
    }

    /// <summary>
    /// The originals <paramref name="textId"/> is linked to, in the order <see cref="LinkedOriginals.Of"/>
    /// gives them, counted from the same links.
    /// </summary>
    public List<LinkedOriginal> Originals(int textId) =>
        LinkedOriginals.Ordered(
            _texts
                .Where(other => other.Key != textId
                                && (_pairs.ContainsKey((textId, other.Key)) || _pairs.ContainsKey((other.Key, textId))))
                .Select(other => new LinkedOriginal(
                    other.Key,
                    other.Value.Slug,
                    other.Value.Name,
                    other.Value.Language,
                    other.Value.Kind,
                    (int)(Links(textId, other.Key) + Links(other.Key, textId)))
                {
                    Numbered = _numbered.Contains(other.Key),
                }));

    /// <summary>Everything the text's phrases, reach, methods and reach by book are counted from.</summary>
    public string Phrases(int textId)
    {
        var inputs = new StringBuilder()
            .Append("v").Append(Version).Append('\n')
            .Append(StrongRenderingCounts.Statements).Append('\n')
            .Append("classes ").Append(_classes).Append('\n')
            .Append(Own(textId));
        foreach (var original in Originals(textId).OrderBy(original => original.Id))
        {
            inputs.Append("original ").Append(Own(original.Id))
                .Append(" to ").Append(Pair(textId, original.Id))
                .Append(" from ").Append(Pair(original.Id, textId)).Append('\n');
        }

        return inputs.ToString();
    }

    /// <summary>Everything an edition's numbers by book are counted from: its own words, verses and books.</summary>
    public string Books(int editionId) =>
        $"v{Version}\n{StrongRenderingCounts.Statements}\n{Own(editionId)}";

    /// <summary>The rows the text's phrases, reach, methods and reach by book stand as, each table summed; every text, or the one given.</summary>
    public static Task<IReadOnlyDictionary<int, string>> Counted(AppDbContext db, int? textId, CancellationToken cancellationToken) =>
        Rows(db, PhraseTables, "text_id", textId, cancellationToken);

    /// <summary>The rows the edition's numbers by book stand as; every edition, or the one given.</summary>
    public static Task<IReadOnlyDictionary<int, string>> CountedBooks(AppDbContext db, int? editionId, CancellationToken cancellationToken) =>
        Rows(db, ["strong_book"], "witness_id", editionId, cancellationToken);

    /// <summary>A digest of what a count was made from and the rows it stands as.</summary>
    public static Guid Of(string inputs, string counted) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(inputs + "\ncounted " + counted)));

    private string Own(int textId)
    {
        var (slug, _, kind, language) = _texts[textId];
        return string.Join(
            ' ',
            textId.ToString(CultureInfo.InvariantCulture),
            slug,
            kind.ToString(),
            language,
            "words", _words.GetValueOrDefault(textId, "-"),
            "verses", _verses.GetValueOrDefault(textId, "-"),
            "books", _books.GetValueOrDefault(textId, "-"));
    }

    private long Links(int from, int to) => _pairs.TryGetValue((from, to), out var pair) ? pair.Links : 0;

    private string Pair(int from, int to) =>
        _pairs.TryGetValue((from, to), out var pair) ? $"{pair.Links}:{pair.Sum}" : "-";

    private static async Task<IReadOnlyDictionary<int, string>> Rows(
        AppDbContext db,
        IReadOnlyList<string> tables,
        string key,
        int? only,
        CancellationToken cancellationToken)
    {
        var filter = only is null ? "" : $"WHERE {key} = @only";
        var sql = string.Join(
            "\nUNION ALL\n",
            tables.Select((table, index) =>
                $"SELECT {index}, {key}, count(*) || ':' || sum(hashtextextended(x::text, 0)) FROM {table} x {filter} GROUP BY {key}"));

        await using var command = new NpgsqlCommand(sql, await Open(db, cancellationToken));
        if (only is { } id)
        {
            command.Parameters.AddWithValue("only", id);
        }

        var parts = new Dictionary<int, string[]>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var text = reader.GetInt32(1);
            if (!parts.TryGetValue(text, out var sums))
            {
                parts[text] = sums = [.. tables.Select(_ => "-")];
            }

            sums[reader.GetInt32(0)] = reader.GetString(2);
        }

        return parts.ToDictionary(part => part.Key, part => string.Join(' ', part.Value));
    }

    private static async Task<IReadOnlyDictionary<int, string>> Sums(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var sums = new Dictionary<int, string>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sums[reader.GetInt32(0)] = reader.GetString(1);
        }

        return sums;
    }

    private static async Task<NpgsqlConnection> Open(AppDbContext db, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return connection;
    }
}
