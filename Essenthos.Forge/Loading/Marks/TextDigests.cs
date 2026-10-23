using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// One digest per text of every row that belongs to it: the links of every pair it is in (the link's
/// fingerprint carries its words) with their claims, its verse links, its words, and the names, Strong
/// numbers and groups on its words. Each table is summed in one grouped pass, ids included, the tables
/// over several sessions at once; a text's digest is the sums of its own rows and of every pair it is in.
///
/// <para>
/// A pair counts for both its texts, so a text's digest also moves when another text's links to it move:
/// that is what a composition measured on the other texts that state its target reads.
/// </para>
/// </summary>
internal static class TextDigests
{
    /// <summary>The tables whose rows a text's digest covers. A recipe line's other reads count whole.</summary>
    public static readonly IReadOnlySet<string> Tables = new HashSet<string>(StringComparer.Ordinal)
    {
        // A link's provenance is a lookup row its id points at, so the link's own row carries it.
        "link", "link_word", "link_claim", "provenance", "verse_link", "verse_link_verse",
        "word", "word_entity", "word_entity_claim", "word_strong", "word_group_word",
    };

    private const string Hash = "count(*) || ':' || coalesce(sum(hashtextextended(x::text, 0)), 0)";

    // Each statement yields (from text, to text or null, table, sum); a null second text is the text's own rows.
    private static readonly (string Table, string Sql)[] Sums =
    [
        ("link", $"SELECT x.from_text_id, x.to_text_id, {Hash} FROM link x {{0}} GROUP BY 1, 2"),
        ("link_claim", $"SELECT l.from_text_id, l.to_text_id, {Hash} FROM link_claim x JOIN link l ON l.id = x.link_id {{1}} GROUP BY 1, 2"),
        ("verse_link", $"SELECT x.from_text_id, x.to_text_id, {Hash} FROM verse_link x {{0}} GROUP BY 1, 2"),
        ("verse_link_verse", $"SELECT v.text_id, NULL::int, {Hash} FROM verse_link_verse x JOIN verse v ON v.id = x.verse_id {{2}} GROUP BY 1"),
        ("word", $"SELECT x.text_id, NULL::int, {Hash} FROM word x {{3}} GROUP BY 1"),
        ("word_entity", $"SELECT w.text_id, NULL::int, {Hash} FROM word_entity x JOIN word w ON w.id = x.word_id {{4}} GROUP BY 1"),
        ("word_entity_claim", $"SELECT w.text_id, NULL::int, {Hash} FROM word_entity_claim x JOIN word_entity e ON e.id = x.word_entity_id JOIN word w ON w.id = e.word_id {{4}} GROUP BY 1"),
        ("word_strong", $"SELECT w.text_id, NULL::int, {Hash} FROM word_strong x JOIN word w ON w.id = x.word_id {{4}} GROUP BY 1"),
        ("word_group_word", $"SELECT w.text_id, NULL::int, {Hash} FROM word_group_word x JOIN word w ON w.id = x.word_id {{4}} GROUP BY 1"),
    ];

    /// <summary>
    /// The digest of every text, by its slug in capitals, or of the texts named only (their digests are the same either
    /// way: a pair is read whole whichever of its texts is named).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> Of(
        string connectionString, IReadOnlyCollection<string>? only, CancellationToken cancellationToken)
    {
        var slugs = new Dictionary<int, string>();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT id, slug FROM text", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                slugs[reader.GetInt32(0)] = reader.GetString(1).ToUpperInvariant();
            }
        }

        var wanted = only is null
            ? null
            : slugs.Where(text => only.Contains(text.Value, StringComparer.OrdinalIgnoreCase)).Select(text => text.Key).ToArray();
        string[] filters = wanted is null
            ? ["", "", "", "", ""]
            :
            [
                "WHERE x.from_text_id = ANY(@texts) OR x.to_text_id = ANY(@texts)",
                "WHERE l.from_text_id = ANY(@texts) OR l.to_text_id = ANY(@texts)",
                "WHERE v.text_id = ANY(@texts)",
                "WHERE x.text_id = ANY(@texts)",
                "WHERE w.text_id = ANY(@texts)",
            ];

        var rows = await Task.WhenAll(Sums.Select(async sum =>
        {
            var found = new List<(int From, int? To, string Table, string Sum)>();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(string.Format(sum.Sql, filters), connection) { CommandTimeout = 1800 };
            if (wanted is not null)
            {
                command.Parameters.AddWithValue("texts", wanted);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                found.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), sum.Table, reader.GetString(2)));
            }

            return found;
        }));

        var lines = new Dictionary<int, List<string>>();
        foreach (var (from, to, table, sum) in rows.SelectMany(found => found))
        {
            var line = to is { } other ? $"{table} {Slug(from)} {Slug(other)} {sum}" : $"{table} {sum}";
            Add(from, line);
            if (to is { } second && second != from)
            {
                Add(second, line);
            }
        }

        return slugs
            .Where(text => wanted is null || wanted.Contains(text.Key))
            .ToDictionary(
                text => text.Value,
                text => Digest(lines.TryGetValue(text.Key, out var own) ? own : []),
                StringComparer.Ordinal);

        void Add(int text, string line)
        {
            if (!lines.TryGetValue(text, out var list))
            {
                lines[text] = list = [];
            }

            list.Add(line);
        }

        string Slug(int id) => slugs.TryGetValue(id, out var slug) ? slug : id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Digest(List<string> lines) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines.Order(StringComparer.Ordinal)))));
}
