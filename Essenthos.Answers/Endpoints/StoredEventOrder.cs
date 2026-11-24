using Npgsql;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The order the events lists are drawn in, written beside the events so a list is ordered and windowed in SQL: each
/// reckoning's year on the astronomical axis (<c>event_date.axis_year</c>) and each event's rank in the text's order
/// over all the events (<c>event.in_year_rank</c>, <see cref="EventOrder.Rank"/>). The build writes it as the events
/// are loaded; a row already right is not written again.
/// </summary>
internal static class StoredEventOrder
{
    private const string Axis = """
        UPDATE event_date d
        SET axis_year = CASE
            WHEN d.stated_year IS NOT NULL THEN CASE WHEN d.stated_year < 0 THEN d.stated_year + 1 ELSE d.stated_year END
            WHEN d.year IS NOT NULL THEN d.year - c.last_year_before_the_common_era
        END
        FROM chronology c
        WHERE c.id = d.chronology_id
          AND d.axis_year IS DISTINCT FROM CASE
            WHEN d.stated_year IS NOT NULL THEN CASE WHEN d.stated_year < 0 THEN d.stated_year + 1 ELSE d.stated_year END
            WHEN d.year IS NOT NULL THEN d.year - c.last_year_before_the_common_era
        END
        """;

    private const string Facts = """
        SELECT slug, realm, source, kind, canonical_book, canonical_chapter, canonical_verse,
               CASE WHEN canonical_book IS NULL AND realm = 'scripture' THEN description END,
               sequence_in_year, year_from_creation, entity_id
        FROM event
        """;

    private const string Ranks = """
        UPDATE event e SET in_year_rank = r.rank
        FROM unnest(@slugs, @ranks) AS r(slug, rank)
        WHERE e.slug = r.slug AND e.in_year_rank IS DISTINCT FROM r.rank
        """;

    /// <summary>Writes both inside the caller's transaction, if any; the rows written.</summary>
    public static async Task<int> Write(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        int written;
        await using (var axis = new NpgsqlCommand(Axis, connection, transaction))
        {
            written = await axis.ExecuteNonQueryAsync(cancellationToken);
        }

        var facts = new List<EventOrder.Facts>();
        await using (var read = new NpgsqlCommand(Facts, connection, transaction))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                facts.Add(new EventOrder.Facts(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10)));
            }
        }

        var ranks = EventOrder.Rank(facts);
        await using var write = new NpgsqlCommand(Ranks, connection, transaction);
        write.Parameters.AddWithValue("slugs", ranks.Keys.ToArray());
        write.Parameters.AddWithValue("ranks", ranks.Values.ToArray());
        return written + await write.ExecuteNonQueryAsync(cancellationToken);
    }
}
