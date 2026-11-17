using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// A window of a narrowed events list in a reader's order, cut in one statement.
///
/// <para>
/// Each event is shown, and placed, by the first reckoning in the reader's preference that gives it a year on the
/// axis (<c>event_date.axis_year</c>); an event none of them dates comes after the rest. With no preference the list is
/// in the base order, by the year from creation. The events of one year are in the order the text tells them, the rank
/// the build stored over every event (<see cref="StoredEventOrder"/>), so a narrowed list keeps the whole list's order.
/// </para>
/// </summary>
internal static class EventWindow
{
    /// <summary>One event of the window: where it stands in the whole list, from zero, and the reckoning it is shown by.</summary>
    internal sealed record Placed(int Id, string Slug, string? ShownBy, int At);

    /// <param name="Total">How many events the narrowed list holds.</param>
    /// <param name="Start">Where the window starts in it.</param>
    /// <param name="Target">Where the event asked to be centred stands, or null where the list does not hold it.</param>
    /// <param name="Known">Whether the event asked to be centred exists at all.</param>
    internal sealed record Window(int Total, int Start, int? Target, bool Known, IReadOnlyList<Placed> Rows, IList<EventCenturyResponse> Centuries);

    /// <summary>A narrowed event's words a search reads.</summary>
    internal sealed record Title(int Id, string Name, string? Location, int? EntityId, string? Person);

    private const string Ordered = """
        WITH f AS (
            SELECT e.id, e.slug, e.year_from_creation AS yfc, e.in_year_rank AS rank, ch.slug AS shown_by, ch.axis_year AS axis
            FROM event e
            LEFT JOIN LATERAL (
                SELECT c.slug, d.axis_year
                FROM event_date d JOIN chronology c ON c.id = d.chronology_id
                WHERE d.event_id = e.id AND d.axis_year IS NOT NULL AND c.slug = ANY(@preference)
                ORDER BY array_position(@preference, c.slug)
                LIMIT 1) ch ON true
            WHERE {0}
        ), o AS (
            SELECT f.*, (row_number() OVER (
                ORDER BY (cardinality(@preference) > 0 AND f.axis IS NULL), COALESCE(f.axis, f.yfc) NULLS LAST, f.rank NULLS LAST, f.slug) - 1)::int AS at
            FROM f
        ), n AS (
            SELECT count(*)::int AS total FROM o
        ), t AS (
            SELECT at FROM o WHERE slug = @around
        ), s AS (
            SELECT CASE WHEN EXISTS (SELECT 1 FROM t)
                THEN GREATEST(0, LEAST((SELECT at FROM t) - @take / 2, (SELECT total FROM n) - @take))
                ELSE @skip END AS start
        ), c AS (
            SELECT array_agg(century ORDER BY first) AS centuries, array_agg(count ORDER BY first) AS counts, array_agg(first ORDER BY first) AS firsts
            FROM (
                SELECT CASE WHEN axis <= 0 THEN -((1 - axis + 99) / 100) ELSE (axis + 99) / 100 END AS century, count(*)::int AS count, min(at) AS first
                FROM o WHERE axis IS NOT NULL GROUP BY 1) g
        )
        SELECT n.total, s.start, (SELECT at FROM t) AS target, EXISTS (SELECT 1 FROM event WHERE slug = @around) AS known,
               c.centuries, c.counts, c.firsts, o.id, o.slug, o.shown_by, o.at
        FROM n CROSS JOIN s CROSS JOIN c
        LEFT JOIN o ON o.at >= s.start AND o.at < s.start + @take
        ORDER BY o.at
        """;

    /// <summary>
    /// The window of <paramref name="take"/> events from <paramref name="skip"/>, or the one centred on
    /// <paramref name="around"/> where the list holds that event; <paramref name="ids"/> narrows to a search's events.
    /// </summary>
    public static async Task<Window> Read(
        AppDbContext db,
        EncyclopediaEndpoints.EventFilter filter,
        IReadOnlyList<int>? ids,
        IReadOnlyList<string> preference,
        int skip,
        int take,
        string? around,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand { Connection = connection };
        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, Ordered, Where(filter, ids, command));
        command.Parameters.AddWithValue("preference", preference.ToArray());
        command.Parameters.AddWithValue("around", (object?)around ?? DBNull.Value).NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text;
        command.Parameters.AddWithValue("skip", skip);
        command.Parameters.AddWithValue("take", take);

        int total = 0, start = skip;
        int? target = null;
        var known = false;
        IList<EventCenturyResponse> centuries = [];
        var rows = new List<Placed>(take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count == 0)
            {
                total = reader.GetInt32(0);
                start = reader.GetInt32(1);
                target = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                known = reader.GetBoolean(3);
                if (!reader.IsDBNull(4))
                {
                    var (named, counts, firsts) = (reader.GetFieldValue<int[]>(4), reader.GetFieldValue<int[]>(5), reader.GetFieldValue<int[]>(6));
                    centuries = [.. named.Select((century, i) => new EventCenturyResponse(century, counts[i], firsts[i]))];
                }
            }

            if (!reader.IsDBNull(7))
            {
                rows.Add(new Placed(reader.GetInt32(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetInt32(10)));
            }
        }

        return new Window(total, start, target, known, rows, centuries);
    }

    /// <summary>The narrowed events' titles, places and people, for a search to read.</summary>
    public static async Task<List<Title>> Titles(AppDbContext db, EncyclopediaEndpoints.EventFilter filter, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand { Connection = connection };
        command.CommandText =
            $"SELECT e.id, e.name, e.location, e.entity_id, p.name FROM event e LEFT JOIN entity p ON p.id = e.entity_id WHERE {Where(filter, null, command)}";
        var titles = new List<Title>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            titles.Add(new Title(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return titles;
    }

    /// <summary>
    /// The narrowing as a condition on <c>event e</c>, its values added to <paramref name="command"/>. Kinds are comma
    /// separated, spelled as the source spells them; a world event is kept to the countries asked for, and left out where
    /// it has none, while Scripture's events are not narrowed by them.
    /// </summary>
    private static string Where(EncyclopediaEndpoints.EventFilter filter, IReadOnlyList<int>? ids, NpgsqlCommand command)
    {
        var conditions = new List<string> { "true" };
        if (filter.Realm is { Length: > 0 } realm)
        {
            conditions.Add("e.realm = @realm");
            command.Parameters.AddWithValue("realm", realm);
        }

        if (filter.Entity is { Length: > 0 } entity)
        {
            conditions.Add("e.entity_id IN (SELECT id FROM entity WHERE slug = @entity)");
            command.Parameters.AddWithValue("entity", entity);
        }

        if (filter.FromYear is { } from)
        {
            conditions.Add("e.year_from_creation >= @fromYear");
            command.Parameters.AddWithValue("fromYear", from);
        }

        if (filter.ToYear is { } to)
        {
            conditions.Add("e.year_from_creation <= @toYear");
            command.Parameters.AddWithValue("toYear", to);
        }

        if (EncyclopediaEndpoints.Listed(filter.Kinds) is { Count: > 0 } kinds)
        {
            conditions.Add("e.kind = ANY(@kinds)");
            command.Parameters.AddWithValue("kinds", kinds.ToArray());
        }

        if (EncyclopediaEndpoints.Listed(filter.NotKinds) is { Count: > 0 } notKinds)
        {
            conditions.Add("(e.kind IS NULL OR NOT e.kind = ANY(@notKinds))");
            command.Parameters.AddWithValue("notKinds", notKinds.ToArray());
        }

        if (EncyclopediaEndpoints.Listed(filter.WorldRegions) is { Count: > 0 } regions)
        {
            conditions.Add($"(e.realm <> '{Realms.World}' OR e.region = ANY(@regions))");
            command.Parameters.AddWithValue("regions", regions.ToArray());
        }

        if (ids is not null)
        {
            conditions.Add("e.id = ANY(@ids)");
            command.Parameters.AddWithValue("ids", ids.ToArray());
        }

        return string.Join(" AND ", conditions);
    }
}
