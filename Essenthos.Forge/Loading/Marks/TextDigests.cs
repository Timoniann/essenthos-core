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
///
/// <para>
/// A text's rows are digested in three parts (<see cref="Parts"/>): its links, its words and the names on
/// its words. A recipe line is keyed only on the parts its statements read, so a name written on a word
/// runs the lines that read names and not every line that aligns the text.
/// </para>
/// </summary>
internal static class TextDigests
{
    /// <summary>The links of every pair the text is in, their words, claims and provenance, and its verse links.</summary>
    public const string Links = "links";

    /// <summary>The text's words, their Strong numbers and the groups they stand in.</summary>
    public const string Words = "words";

    /// <summary>The names on the text's words, and their claims.</summary>
    public const string Names = "names";

    /// <summary>Which part of a text each table's rows belong to.</summary>
    public static readonly IReadOnlyDictionary<string, string> PartOf = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // A link's provenance is a lookup row its id points at, so the link's own row carries it.
        ["link"] = Links, ["link_word"] = Links, ["link_claim"] = Links, ["provenance"] = Links,
        ["verse_link"] = Links, ["verse_link_verse"] = Links,
        ["word"] = Words, ["word_strong"] = Words, ["word_group_word"] = Words,
        ["word_entity"] = Names, ["word_entity_claim"] = Names,
    };

    /// <summary>Every part, in the order a whole digest joins them.</summary>
    public static readonly IReadOnlyList<string> Parts = [Links, Names, Words];

    /// <summary>The tables whose rows a text's digest covers. A recipe line's other reads count whole.</summary>
    public static readonly IReadOnlySet<string> Tables = new HashSet<string>(PartOf.Keys, StringComparer.Ordinal);

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
        string connectionString, IReadOnlyCollection<string>? only, CancellationToken cancellationToken) =>
        (await PartsOf(connectionString, only, cancellationToken))
        .ToDictionary(text => text.Key, text => Whole(text.Value), StringComparer.Ordinal);

    /// <summary>The digest of every part of every text (or of the texts named only), by slug in capitals and part.</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> PartsOf(
        string connectionString, IReadOnlyCollection<string>? only, CancellationToken cancellationToken) =>
        PartsOf(await Read(connectionString, only, among: false, cancellationToken), only);

    /// <summary>
    /// The parts of every text a set of pieces (<see cref="Pieces"/>) covers, or of the texts named only.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> PartsOf(
        Pieces pieces, IReadOnlyCollection<string>? only)
    {
        var lines = new Dictionary<(string Text, string Part), List<string>>();
        foreach (var (key, piece) in pieces.Sums)
        {
            var line = $"{key} {piece}";
            var part = PartOf[key.Table];
            Add(key.From, part, line);
            if (key.To is { } second && second != key.From)
            {
                Add(second, part, line);
            }
        }

        return pieces.Texts
            .Where(text => only is null || only.Contains(text, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(
                text => text,
                text => (IReadOnlyDictionary<string, string>)Parts.ToDictionary(
                    part => part,
                    part => Digest(lines.TryGetValue((text, part), out var own) ? own : []),
                    StringComparer.Ordinal),
                StringComparer.Ordinal);

        void Add(string text, string part, string line)
        {
            if (!lines.TryGetValue((text, part), out var list))
            {
                lines[(text, part)] = list = [];
            }

            list.Add(line);
        }
    }

    /// <summary>One table's rows of one text, or of one pair (from, to): the unit a recipe line's writes are measured in.</summary>
    internal readonly record struct Piece(string Table, string From, string? To)
    {
        public override string ToString() => To is null ? $"{Table} {From}" : $"{Table} {From} {To}";
    }

    /// <summary>The sum of every piece read, and every text there is.</summary>
    internal sealed record Pieces(IReadOnlyDictionary<Piece, string> Sums, IReadOnlyList<string> Texts);

    /// <summary>
    /// Every piece of every text (<paramref name="only"/> null), of the texts named and every pair either is in,
    /// or, <paramref name="among"/>, of the texts named and only the pairs between two of them: what a line
    /// over those texts can have written, read through the pair's index rather than the whole table.
    /// </summary>
    public static async Task<Pieces> Read(
        string connectionString, IReadOnlyCollection<string>? only, bool among, CancellationToken cancellationToken)
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
        var pair = among ? "AND" : "OR";
        string[] filters = wanted is null
            ? ["", "", "", "", ""]
            :
            [
                $"WHERE x.from_text_id = ANY(@texts) {pair} x.to_text_id = ANY(@texts)",
                $"WHERE l.from_text_id = ANY(@texts) {pair} l.to_text_id = ANY(@texts)",
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

        return new Pieces(
            rows.SelectMany(found => found).ToDictionary(
                row => new Piece(row.Table, Slug(row.From), row.To is { } to ? Slug(to) : null), row => row.Sum),
            [.. slugs.Values]);

        string Slug(int id) => slugs.TryGetValue(id, out var slug) ? slug : id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The digest of a set of pieces, in a stable order.</summary>
    public static string Digest(IEnumerable<KeyValuePair<Piece, string>> pieces) =>
        Digest([.. pieces.Select(piece => $"{piece.Key} {piece.Value}")]);

    /// <summary>A text's digest whole, from the digests of its parts.</summary>
    public static string Whole(IReadOnlyDictionary<string, string> parts) =>
        Digest([.. Parts.Select(part => $"{part} {parts.GetValueOrDefault(part, "absent")}")]);

    private static string Digest(List<string> lines) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines.Order(StringComparer.Ordinal)))));
}
