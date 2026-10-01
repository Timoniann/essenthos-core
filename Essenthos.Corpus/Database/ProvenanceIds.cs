using Essenthos.Core.Database.Entities;
using Npgsql;

namespace Essenthos.Core.Database;

/// <summary>
/// The id of a <see cref="Provenance"/>, written if it is new. Every writer of links and claims
/// asks here rather than inserting the row itself, so two writers naming the same source in the
/// same words get the same row whichever came first.
/// </summary>
public static class ProvenanceIds
{
    /// <summary>
    /// One statement: what is new comes back from the insert, what was there from the read. A pair
    /// another transaction commits while this one runs is in neither, because the read sees the
    /// table as it was when the statement began, so the caller asks again for what is missing.
    /// </summary>
    private const string Upsert =
        """
        WITH wanted AS (
            SELECT DISTINCT w.source, w.note FROM unnest(@sources, @notes) AS w(source, note)),
        written AS (
            INSERT INTO provenance (source, note) SELECT source, note FROM wanted
            ON CONFLICT (source, note) DO NOTHING
            RETURNING id, source, note)
        SELECT id, source, note FROM written
        UNION ALL
        SELECT p.id, p.source, p.note
        FROM wanted w JOIN provenance p ON p.source = w.source AND p.note IS NOT DISTINCT FROM w.note
        """;

    private const int Attempts = 3;

    public static async Task<int> Of(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string source,
        string? note,
        CancellationToken cancellationToken)
    {
        var ids = await Of(connection, transaction, [(source, note)], cancellationToken);
        return ids[(source, note)];
    }

    /// <summary>One round trip for any number of pairs; the result is keyed by the pair.</summary>
    public static async Task<Dictionary<(string Source, string? Note), int>> Of(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        IEnumerable<(string Source, string? Note)> wanted,
        CancellationToken cancellationToken)
    {
        var distinct = wanted.Distinct().ToList();
        var ids = new Dictionary<(string Source, string? Note), int>(distinct.Count);
        if (distinct.Count == 0)
        {
            return ids;
        }

        for (var attempt = 0; attempt < Attempts && ids.Count < distinct.Count; attempt++)
        {
            var missing = attempt == 0 ? distinct : distinct.Where(pair => !ids.ContainsKey(pair)).ToList();
            await using var command = new NpgsqlCommand(Upsert, connection, transaction);
            command.Parameters.AddWithValue("sources", missing.Select(pair => pair.Source).ToArray());
            command.Parameters.Add(new NpgsqlParameter<string?[]>("notes", missing.Select(pair => pair.Note).ToArray()));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids[(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))] = reader.GetInt32(0);
            }
        }

        if (ids.Count < distinct.Count)
        {
            throw new InvalidOperationException(
                $"{distinct.Count - ids.Count} sources were neither written nor found in provenance after " +
                $"{Attempts} attempts; another writer is holding them. Run the step again once it has finished.");
        }

        return ids;
    }
}
