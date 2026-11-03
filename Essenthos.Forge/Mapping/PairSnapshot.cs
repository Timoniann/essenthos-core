using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Mapping;

/// <summary>A claim on a link as the corpus stores it.</summary>
internal readonly record struct StoredClaim(string Method, double? Confidence, int Provenance);

/// <summary>A word of a link's first side that writes another word's form or belongs to its phrase.</summary>
internal readonly record struct StoredRole(long Word, long Head, string Role);

/// <summary>One link of the corpus with its words and claims, by row ids.</summary>
internal sealed class StoredLink(long id, string relation, string method, double? confidence, int provenance)
{
    public long Id { get; } = id;
    public string Relation { get; } = relation;
    public string Method { get; } = method;
    public double? Confidence { get; } = confidence;
    public int Provenance { get; } = provenance;
    public List<long> From { get; } = new(2);
    public List<long> To { get; } = new(2);
    public List<StoredRole> Roles { get; } = [];
    public List<StoredClaim> Claims { get; } = new(2);
}

/// <summary>
/// Every link of one pair of texts in a database, with its words and claims: what the export writes into
/// the method files and what the comparison reads from both databases. Three ordered binary copies per pair,
/// so the cost is the pair's own rows and never a scan of the corpus.
/// </summary>
internal static class PairSnapshot
{
    public static async Task<List<StoredLink>> Read(NpgsqlConnection connection, int fromTextId, int toTextId, CancellationToken cancellationToken)
    {
        var pair = $"l.from_text_id = {fromTextId.ToString(CultureInfo.InvariantCulture)} AND l.to_text_id = {toTextId.ToString(CultureInfo.InvariantCulture)}";
        var links = new List<StoredLink>(1 << 16);
        var byId = new Dictionary<long, StoredLink>(1 << 16);
        using (var export = connection.BeginBinaryExport(
                         $"COPY (SELECT l.id, l.relation, l.method, l.confidence, l.provenance_id FROM link l WHERE {pair} ORDER BY l.id) TO STDOUT (FORMAT BINARY)"))
        {
            while (export.StartRow() >= 0)
            {
                var id = export.Read<long>(NpgsqlDbType.Bigint);
                var relation = string.Intern(export.Read<string>(NpgsqlDbType.Text));
                var method = string.Intern(export.Read<string>(NpgsqlDbType.Text));
                var confidence = Nullable<double>(export, NpgsqlDbType.Double);
                var provenance = export.Read<int>(NpgsqlDbType.Integer);
                var link = new StoredLink(id, relation, method, confidence, provenance);
                links.Add(link);
                byId[id] = link;
            }
        }

        using (var export = connection.BeginBinaryExport(
                         $"""
                          COPY (SELECT w.link_id, w.word_id, w.side = 'from', w.head_word_id, w.role
                                FROM link_word w JOIN link l ON l.id = w.link_id WHERE {pair}) TO STDOUT (FORMAT BINARY)
                          """))
        {
            while (export.StartRow() >= 0)
            {
                var link = byId[export.Read<long>(NpgsqlDbType.Bigint)];
                var word = export.Read<long>(NpgsqlDbType.Bigint);
                var from = export.Read<bool>(NpgsqlDbType.Boolean);
                var head = Nullable<long>(export, NpgsqlDbType.Bigint);
                var role = Text(export);
                (from ? link.From : link.To).Add(word);
                if (head is { } h && role is not null)
                {
                    link.Roles.Add(new StoredRole(word, h, string.Intern(role)));
                }
            }
        }

        using (var export = connection.BeginBinaryExport(
                         $"""
                          COPY (SELECT c.link_id, c.method, c.confidence, c.provenance_id
                                FROM link_claim c JOIN link l ON l.id = c.link_id WHERE {pair}) TO STDOUT (FORMAT BINARY)
                          """))
        {
            while (export.StartRow() >= 0)
            {
                var link = byId[export.Read<long>(NpgsqlDbType.Bigint)];
                var method = string.Intern(export.Read<string>(NpgsqlDbType.Text));
                var confidence = Nullable<double>(export, NpgsqlDbType.Double);
                link.Claims.Add(new StoredClaim(method, confidence, export.Read<int>(NpgsqlDbType.Integer)));
            }
        }

        return links;
    }

    /// <summary>The pairs of texts the database holds links between, by text id.</summary>
    public static async Task<List<(int From, int To)>> Pairs(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var pairs = new List<(int, int)>();
        await using var command = new NpgsqlCommand(
            """
            SELECT f.id, t.id FROM text f CROSS JOIN text t
            WHERE EXISTS (SELECT 1 FROM link l WHERE l.from_text_id = f.id AND l.to_text_id = t.id)
            """, connection) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pairs.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }

        return pairs;
    }

    internal static T? Nullable<T>(NpgsqlBinaryExporter export, NpgsqlDbType type)
        where T : struct
    {
        if (export.IsNull)
        {
            export.Skip();
            return null;
        }

        return export.Read<T>(type);
    }

    internal static string? Text(NpgsqlBinaryExporter export)
    {
        if (export.IsNull)
        {
            export.Skip();
            return null;
        }

        return export.Read<string>(NpgsqlDbType.Text);
    }
}

/// <summary>
/// Who says each link and claim is true: the database's <c>provenance</c> rows by id, the source strings
/// shared rather than repeated. 1.8 M rows on today's corpus, nearly all of them EVIDENTIA's per-decision notes.
/// </summary>
internal sealed class Provenances
{
    private readonly Dictionary<int, (string Source, string? Note)> _rows;

    private Provenances(Dictionary<int, (string, string?)> rows)
    {
        _rows = rows;
    }

    public (string Source, string? Note) this[int id] => _rows[id];

    public static async Task<Provenances> Load(NpgsqlConnection connection, bool notes, CancellationToken cancellationToken)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new Dictionary<int, (string, string?)>(1 << 20);
        using var export = connection.BeginBinaryExport(
            notes
                ? "COPY (SELECT id, source, note FROM provenance) TO STDOUT (FORMAT BINARY)"
                : "COPY (SELECT id, source, NULL::text FROM provenance) TO STDOUT (FORMAT BINARY)");
        while (export.StartRow() >= 0)
        {
            var id = export.Read<int>(NpgsqlDbType.Integer);
            var source = export.Read<string>(NpgsqlDbType.Text);
            if (!sources.TryGetValue(source, out var shared))
            {
                sources[source] = shared = source;
            }

            rows[id] = (shared, PairSnapshot.Text(export));
        }

        return new Provenances(rows);
    }
}
