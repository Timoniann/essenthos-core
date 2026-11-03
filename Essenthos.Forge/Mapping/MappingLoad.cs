using System.Diagnostics;
using System.Threading.Channels;
using Essenthos.Core.Comparison;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Products;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The pairs' finals put into a database (never live, never the shared snapshot): <c>link</c>, <c>link_word</c>,
/// <c>link_claim</c> and <c>provenance</c> replaced in one transaction, ids as the files keep them, every key
/// resolved to the word the database holds at it. The four tables' indexes and keys are dropped first and built
/// again after the rows are in, so the rows go in by COPY alone; a failure anywhere rolls all of it back and
/// leaves the tables as they were.
/// </summary>
/// <remarks>
/// Provenance carries the source alone: what a source's note said is evidence, and evidence stays in the method
/// files (the owner, NOT-0215 point 6). The API reads <c>provenance.source</c> and never its note.
/// </remarks>
internal static class MappingLoad
{
    public static readonly string[] Untouchable = ["essenthos_core", "essenthos_core_rework_src"];

    private static readonly string[] Tables = ["link", "link_word", "link_claim", "provenance"];

    public sealed record Outcome(int Pairs, long Links, long Words, long Claims, int Provenances, long Unresolved, TimeSpan Elapsed);

    private sealed record PairRows(
        string Pair,
        List<(long Id, int From, int To, string Relation, string Method, double? Confidence, int Provenance, Guid Shape)> Links,
        List<(long Link, long Word, bool From, long? Head, string? Role)> Words,
        List<(long Link, string Method, double? Confidence, int Provenance)> Claims,
        long Unresolved);

    public static async Task<Outcome> Run(string connection, ProductStore store, ILogger logger, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(cancellationToken);
        if (Untouchable.Contains(db.Database, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The mapping load never writes {db.Database}; point it at a database of its own.");
        }

        var keys = await WordKeys.Load(db, reverse: true, cancellationToken);
        logger.LogInformation("{Words} words of {Database} read in {Elapsed}", keys.Words, db.Database, clock.Elapsed);
        var pairs = MappingProducts.Pairs(store).Where(p => store.Exists(MappingProducts.Of(p, MappingProducts.Final))).ToList();
        var provenance = new Dictionary<string, int>(StringComparer.Ordinal);

        var channel = Channel.CreateBounded<PairRows>(2);
        var reading = Task.Run(async () =>
        {
            try
            {
                foreach (var pair in pairs)
                {
                    await channel.Writer.WriteAsync(Read(store, pair, keys, provenance), cancellationToken);
                }
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await Execute(db, "SET LOCAL maintenance_work_mem = '2GB'; SET LOCAL max_parallel_maintenance_workers = 4", cancellationToken);
        var ddl = await Definitions(db, cancellationToken);
        foreach (var c in ddl.Constraints.OrderBy(c => c.Kind == 'f' ? 0 : 1))
        {
            await Execute(db, $"ALTER TABLE {c.Table} DROP CONSTRAINT {c.Name}", cancellationToken);
        }

        foreach (var (name, _) in ddl.Indexes)
        {
            await Execute(db, $"DROP INDEX {name}", cancellationToken);
        }

        foreach (var table in Tables)
        {
            await Execute(db, $"ALTER TABLE {table} DISABLE TRIGGER USER", cancellationToken);
        }

        await Execute(db, "TRUNCATE link, link_word, link_claim, provenance", cancellationToken);
        logger.LogInformation("Tables emptied and unindexed at {Elapsed}", clock.Elapsed);

        long links = 0, words = 0, claims = 0, unresolved = 0;
        await foreach (var rows in channel.Reader.ReadAllAsync(cancellationToken))
        {
            await Copy(db, rows, cancellationToken);
            links += rows.Links.Count;
            words += rows.Words.Count;
            claims += rows.Claims.Count;
            unresolved += rows.Unresolved;
            if (rows.Unresolved > 0)
            {
                logger.LogWarning("{Pair}: {Unresolved} links name a key this database has no word at, and were not loaded", rows.Pair, rows.Unresolved);
            }
        }

        await reading;
        logger.LogInformation("{Links} links, {Words} link words, {Claims} claims copied at {Elapsed}", links, words, claims, clock.Elapsed);

        using (var import = db.BeginBinaryImport("COPY provenance (id, source) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var (source, id) in provenance)
            {
                import.StartRow();
                import.Write(id, NpgsqlDbType.Integer);
                import.Write(source, NpgsqlDbType.Text);
            }

            import.Complete();
        }

        foreach (var c in ddl.Constraints.Where(c => c.Kind != 'f'))
        {
            await Execute(db, $"ALTER TABLE {c.Table} ADD CONSTRAINT {c.Name} {c.Definition}", cancellationToken);
        }

        foreach (var (_, definition) in ddl.Indexes)
        {
            await Execute(db, definition, cancellationToken);
        }

        logger.LogInformation("Keys and indexes built at {Elapsed}", clock.Elapsed);
        foreach (var (table, name, definition, _, column) in ddl.Constraints.Where(c => c.Kind == 'f'))
        {
            if (!Tables.Contains(table) && column is not null)
            {
                // A row outside the mapping that pointed at a link the final no longer holds: what its key's
                // ON DELETE says, as if the link had been deleted.
                var target = definition.Contains("ON DELETE CASCADE", StringComparison.Ordinal)
                    ? $"DELETE FROM {table} t WHERE t.{column} IS NOT NULL AND NOT EXISTS (SELECT 1 FROM link l WHERE l.id = t.{column})"
                    : $"UPDATE {table} t SET {column} = NULL WHERE t.{column} IS NOT NULL AND NOT EXISTS (SELECT 1 FROM link l WHERE l.id = t.{column})";
                await Execute(db, target, cancellationToken);
            }

            await Execute(db, $"ALTER TABLE {table} ADD CONSTRAINT {name} {definition}", cancellationToken);
        }

        foreach (var table in Tables)
        {
            await Execute(db, $"ALTER TABLE {table} ENABLE TRIGGER USER", cancellationToken);
        }

        await Execute(db,
            """
            SELECT setval(pg_get_serial_sequence('link', 'id'), greatest(coalesce((SELECT max(id) FROM link), 0), 1));
            SELECT setval(pg_get_serial_sequence('link_claim', 'id'), greatest(coalesce((SELECT max(id) FROM link_claim), 0), 1));
            SELECT setval(pg_get_serial_sequence('provenance', 'id'), greatest(coalesce((SELECT max(id) FROM provenance), 0), 1));
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Committed at {Elapsed}", clock.Elapsed);
        await Execute(db, "ANALYZE link; ANALYZE link_word; ANALYZE link_claim; ANALYZE provenance", cancellationToken);
        return new Outcome(pairs.Count, links, words, claims, provenance.Count, unresolved, clock.Elapsed);
    }

    private static PairRows Read(ProductStore store, string pair, WordKeys keys, Dictionary<string, int> provenance)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var fromId = keys.TextId(fromText) ?? throw new InvalidDataException($"The database has no text {fromText}.");
        var toId = keys.TextId(toText) ?? throw new InvalidDataException($"The database has no text {toText}.");
        var links = new List<(long, int, int, string, string, double?, int, Guid)>();
        var words = new List<(long, long, bool, long?, string?)>();
        var claims = new List<(long, string, double?, int)>();
        long unresolved = 0;
        long current = -1;
        var skip = false;
        using var reader = store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema);
        foreach (var row in reader.Rows())
        {
            if (row.Id != current)
            {
                current = row.Id;
                if (!row.Settled)
                {
                    throw new InvalidDataException($"{pair}: link {row.Id} does not begin with its settled claim.");
                }

                var from = Resolve(row.From, fromText, fromId, keys);
                var to = Resolve(row.To, toText, toId, keys);
                skip = from is null || to is null;
                if (skip)
                {
                    unresolved++;
                    continue;
                }

                links.Add((row.Id, fromId, toId, row.Relation, EnumSpelling.Of(MappingMethods.DatabaseMethod(row.Method)), row.Confidence,
                    Provenance(provenance, row.Source), LinkShape.Of(from!, to!)));
                var roles = Roles(row.Roles, fromText, fromId, keys);
                foreach (var word in from!)
                {
                    var role = roles.GetValueOrDefault(word);
                    words.Add((row.Id, word, true, role.Head, role.Role));
                }

                foreach (var word in to!)
                {
                    words.Add((row.Id, word, false, null, null));
                }
            }
            else if (row.Settled)
            {
                throw new InvalidDataException($"{pair}: link {row.Id} has two settled claims.");
            }

            if (!skip)
            {
                claims.Add((row.Id, EnumSpelling.Of(MappingMethods.DatabaseMethod(row.Method)), row.Confidence, Provenance(provenance, row.Source)));
            }
        }

        return new PairRows(pair, links, words, claims, unresolved);
    }

    private static int Provenance(Dictionary<string, int> provenance, string source)
    {
        lock (provenance)
        {
            if (!provenance.TryGetValue(source, out var id))
            {
                provenance[source] = id = provenance.Count + 1;
            }

            return id;
        }
    }

    private static long[]? Resolve(string own, string text, int textId, WordKeys keys)
    {
        if (own.Length == 0)
        {
            return [];
        }

        var parts = own.Split(' ');
        var ids = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (keys.Id(textId, WordKey.Parse(parts[i], text)) is not { } id)
            {
                return null;
            }

            ids[i] = id;
        }

        return ids;
    }

    private static Dictionary<long, (long? Head, string? Role)> Roles(string? roles, string text, int textId, WordKeys keys)
    {
        var map = new Dictionary<long, (long?, string?)>();
        if (roles is null)
        {
            return map;
        }

        foreach (var part in roles.Split(' '))
        {
            var equals = part.IndexOf('=');
            var at = part.IndexOf('@', equals);
            var word = keys.Id(textId, WordKey.Parse(part[..equals], text));
            var head = keys.Id(textId, WordKey.Parse(part[(at + 1)..], text));
            if (word is { } w)
            {
                map[w] = (head, part[(equals + 1)..at]);
            }
        }

        return map;
    }

    private static async Task Copy(NpgsqlConnection db, PairRows rows, CancellationToken cancellationToken)
    {
        using (var import = db.BeginBinaryImport(
                   "COPY link (id, from_text_id, to_text_id, relation, method, confidence, provenance_id, fingerprint) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var (id, from, to, relation, method, confidence, provenance, shape) in rows.Links)
            {
                import.StartRow();
                import.Write(id, NpgsqlDbType.Bigint);
                import.Write(from, NpgsqlDbType.Integer);
                import.Write(to, NpgsqlDbType.Integer);
                import.Write(relation, NpgsqlDbType.Text);
                import.Write(method, NpgsqlDbType.Text);
                Nullable(import, confidence);
                import.Write(provenance, NpgsqlDbType.Integer);
                import.Write(shape, NpgsqlDbType.Uuid);
            }

            import.Complete();
        }

        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);
        using (var import = db.BeginBinaryImport("COPY link_word (link_id, word_id, side, head_word_id, role) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var (link, word, from, head, role) in rows.Words)
            {
                import.StartRow();
                import.Write(link, NpgsqlDbType.Bigint);
                import.Write(word, NpgsqlDbType.Bigint);
                import.Write(from ? fromSide : toSide, NpgsqlDbType.Text);
                if (head is { } h) import.Write(h, NpgsqlDbType.Bigint); else import.WriteNull();
                if (role is not null) import.Write(role, NpgsqlDbType.Text); else import.WriteNull();
            }

            import.Complete();
        }

        using (var import = db.BeginBinaryImport("COPY link_claim (link_id, method, confidence, provenance_id) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var (link, method, confidence, provenance) in rows.Claims)
            {
                import.StartRow();
                import.Write(link, NpgsqlDbType.Bigint);
                import.Write(method, NpgsqlDbType.Text);
                Nullable(import, confidence);
                import.Write(provenance, NpgsqlDbType.Integer);
            }

            import.Complete();
        }

        await Task.CompletedTask;
    }

    private static void Nullable(NpgsqlBinaryImporter import, double? value)
    {
        if (value is { } v)
        {
            import.Write(v, NpgsqlDbType.Double);
        }
        else
        {
            import.WriteNull();
        }
    }

    /// <summary>The keys, unique constraints, foreign keys (in and out) and plain indexes of the four tables, as SQL that makes them again.</summary>
    private static async Task<Ddl> Definitions(NpgsqlConnection db, CancellationToken cancellationToken)
    {
        var constraints = new List<(string Table, string Name, string Definition, char Kind, string? Column)>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT c.conrelid::regclass::text, quote_ident(c.conname), pg_get_constraintdef(c.oid), c.contype::text,
                                (SELECT quote_ident(a.attname) FROM pg_attribute a WHERE a.attrelid = c.conrelid AND a.attnum = c.conkey[1])
                         FROM pg_constraint c
                         WHERE c.contype IN ('p', 'u', 'f')
                           AND (c.conrelid = ANY(@tables::regclass[]) OR c.confrelid = ANY(@tables::regclass[]))
                         ORDER BY c.contype, c.conrelid::regclass::text, c.conname
                         """, db))
        {
            command.Parameters.AddWithValue("tables", Tables);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                constraints.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)[0], reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        var indexes = new List<(string, string)>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT i.indexrelid::regclass::text, pg_get_indexdef(i.indexrelid)
                         FROM pg_index i
                         WHERE i.indrelid = ANY(@tables::regclass[])
                           AND NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conindid = i.indexrelid AND c.contype IN ('p', 'u', 'x'))
                         ORDER BY 1
                         """, db))
        {
            command.Parameters.AddWithValue("tables", Tables);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                indexes.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        return new Ddl(constraints, indexes);
    }

    private sealed record Ddl(
        List<(string Table, string Name, string Definition, char Kind, string? Column)> Constraints,
        List<(string Name, string Definition)> Indexes);

    private static async Task Execute(NpgsqlConnection db, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, db) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
