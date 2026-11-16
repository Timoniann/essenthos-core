using System.Diagnostics;
using System.Threading.Channels;
using Essenthos.Core.Comparison;
using Essenthos.Core.Conversion;
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
/// leaves the tables as they were. With the triggers off it writes what they would: each link's fingerprint and
/// set of methods, and each source's count of claims.
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
        long Unresolved,
        List<string> Sources);

    /// <summary>
    /// Every pair's final into the database. <paramref name="resolved"/> resolves the keys; without it the texts' ids are read from
    /// the database, which must then hold the texts already.
    /// </summary>
    public static async Task<Outcome> Run(string connection, ProductStore store, ILogger logger, CancellationToken cancellationToken, ILinkWords? resolved = null)
    {
        var clock = Stopwatch.StartNew();
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(cancellationToken);
        if (Untouchable.Contains(db.Database, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The mapping load never writes {db.Database}; point it at a database of its own.");
        }

        var pairs = MappingProducts.Pairs(store).Where(p => store.Exists(MappingProducts.Of(p, MappingProducts.Final))).ToList();
        var keys = resolved ?? await Words(store, db, Texts(pairs), cancellationToken);
        logger.LogInformation("The texts' word ids found in {Elapsed}", clock.Elapsed);
        var provenance = new Dictionary<string, int>(StringComparer.Ordinal);
        var numbering = new RowIds.Hashed(ProvenanceBits);

        // The pairs are read several at once and handed on in their order, so every row's place in the COPY is the same as
        // one reader's.
        var channel = Channel.CreateBounded<PairRows>(2);
        var reading = Task.Run(async () =>
        {
            try
            {
                var window = new Queue<Task<PairRows>>();
                foreach (var pair in pairs)
                {
                    window.Enqueue(Task.Run(() => Read(store, pair, keys), cancellationToken));
                    if (window.Count >= Readers)
                    {
                        await channel.Writer.WriteAsync(await window.Dequeue(), cancellationToken);
                    }
                }

                while (window.Count > 0)
                {
                    await channel.Writer.WriteAsync(await window.Dequeue(), cancellationToken);
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
        var cited = new Dictionary<int, int>();
        await foreach (var rows in channel.Reader.ReadAllAsync(cancellationToken))
        {
            var sources = Globalize(rows.Sources, provenance, numbering);
            foreach (var claim in rows.Claims)
            {
                cited[sources[claim.Provenance]] = cited.GetValueOrDefault(sources[claim.Provenance]) + 1;
            }

            await Copy(db, rows, sources, cancellationToken, freeze: true);
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

        using (var import = db.BeginBinaryImport("COPY provenance (id, source, claims) FROM STDIN (FORMAT BINARY, FREEZE)"))
        {
            foreach (var (source, id) in provenance)
            {
                import.StartRow();
                import.Write(id, NpgsqlDbType.Integer);
                import.Write(source, NpgsqlDbType.Text);
                import.Write(cited.GetValueOrDefault(id), NpgsqlDbType.Integer);
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
            SELECT setval(pg_get_serial_sequence('provenance', 'id'), greatest(coalesce((SELECT max(id) FROM provenance), 0), 1 << 30));
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Committed at {Elapsed}", clock.Elapsed);
        await Execute(db, "ANALYZE link; ANALYZE link_word; ANALYZE link_claim; ANALYZE provenance", cancellationToken);
        return new Outcome(pairs.Count, links, words, claims, provenance.Count, unresolved, clock.Elapsed);
    }

    /// <summary>
    /// The links of the pairs named made what their finals say inside the caller's transaction, every other pair's left as it
    /// is. A link's id is derived from its words, so a pair's links are matched by id: one the final no longer holds, or holds
    /// otherwise (relation, settled claim, words, roles or claims), is deleted with its words and claims, and one the database
    /// lacks or held otherwise is copied in; the rest stand. A source the database has not seen takes its own provenance id
    /// (<see cref="ProvenanceBits"/>), as a whole load would give it.
    /// Indexes and keys stay; this is for a few pairs.
    /// </summary>
    public static async Task<Outcome> ReplacePairs(NpgsqlConnection db, ProductStore store, IReadOnlyCollection<string> pairs, ILogger logger,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        if (Untouchable.Contains(db.Database, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The mapping load never writes {db.Database}; point it at a database of its own.");
        }

        var keys = await Words(store, db, Texts(pairs), cancellationToken);
        logger.LogInformation("The pairs' word ids found in {Elapsed}", clock.Elapsed);
        var provenance = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("SELECT source, id FROM provenance", db))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                provenance[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        var holding = provenance.Values.Select(id => (long)id).ToHashSet();
        var numbering = new RowIds.Hashed(ProvenanceBits, holding.Contains);
        var written = provenance.Keys.ToHashSet(StringComparer.Ordinal);
        long links = 0, words = 0, claims = 0, unresolved = 0;
        foreach (var pair in pairs)
        {
            var (fromText, toText) = MappingProducts.Split(pair);
            var fromId = keys.TextId(fromText) ?? throw new InvalidDataException($"The database has no text {fromText}.");
            var toId = keys.TextId(toText) ?? throw new InvalidDataException($"The database has no text {toText}.");
            if (!store.Exists(MappingProducts.Of(pair, MappingProducts.Final)))
            {
                await using var delete = new NpgsqlCommand("DELETE FROM link WHERE from_text_id = @from AND to_text_id = @to", db) { CommandTimeout = 0 };
                delete.Parameters.AddWithValue("from", fromId);
                delete.Parameters.AddWithValue("to", toId);
                logger.LogInformation("{Pair}: no final, {Deleted} links deleted", pair, await delete.ExecuteNonQueryAsync(cancellationToken));
                continue;
            }

            var all = Read(store, pair, keys);
            var wanted = Signatures(
                all.Links.Select(l => (l.Id, l.Relation, l.Method, l.Confidence, all.Sources[l.Provenance - 1], l.Shape)),
                all.Words,
                all.Claims.Select(c => (c.Link, c.Method, c.Confidence, all.Sources[c.Provenance - 1])));
            var held = await Held(db, fromId, toId, cancellationToken);
            var gone = held.Where(h => !wanted.TryGetValue(h.Key, out var signature) || signature != h.Value).Select(h => h.Key).ToArray();
            var fresh = wanted.Where(w => !held.TryGetValue(w.Key, out var signature) || signature != w.Value).Select(w => w.Key).ToHashSet();
            if (gone.Length > 0)
            {
                await using var delete = new NpgsqlCommand("DELETE FROM link WHERE id = ANY(@ids)", db) { CommandTimeout = 0 };
                delete.Parameters.AddWithValue("ids", gone);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            logger.LogInformation("{Pair}: {Gone} links deleted, {Fresh} copied, {Same} as they were", pair, gone.Length, fresh.Count, wanted.Count - fresh.Count);
            var rows = all with
            {
                Links = [.. all.Links.Where(l => fresh.Contains(l.Id))],
                Words = [.. all.Words.Where(w => fresh.Contains(w.Link))],
                Claims = [.. all.Claims.Where(c => fresh.Contains(c.Link))],
            };
            var sources = Globalize(rows.Sources, provenance, numbering);
            if (provenance.Count > written.Count)
            {
                using var import = db.BeginBinaryImport("COPY provenance (id, source) FROM STDIN (FORMAT BINARY)");
                foreach (var (source, id) in provenance.Where(p => written.Add(p.Key)).OrderBy(p => p.Value))
                {
                    import.StartRow();
                    import.Write(id, NpgsqlDbType.Integer);
                    import.Write(source, NpgsqlDbType.Text);
                }

                import.Complete();
            }

            await Copy(db, rows, sources, cancellationToken);
            links += rows.Links.Count;
            words += rows.Words.Count;
            claims += rows.Claims.Count;
            unresolved += rows.Unresolved;
        }

        await Execute(db,
            """
            SELECT setval(pg_get_serial_sequence('link', 'id'), greatest(coalesce((SELECT max(id) FROM link), 0), 1));
            SELECT setval(pg_get_serial_sequence('link_claim', 'id'), greatest(coalesce((SELECT max(id) FROM link_claim), 0), 1));
            SELECT setval(pg_get_serial_sequence('provenance', 'id'), greatest(coalesce((SELECT max(id) FROM provenance), 0), 1 << 30));
            """, cancellationToken);
        return new Outcome(pairs.Count, links, words, claims, provenance.Count, unresolved, clock.Elapsed);
    }

    /// <summary>How many pairs are read at once ahead of the COPY.</summary>
    private const int Readers = 6;

    private static List<string> Texts(IEnumerable<string> pairs) =>
        [.. pairs.Select(MappingProducts.Split).SelectMany(p => new[] { p.From, p.To }).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The words the keys of these texts name: worked out from the words products where the store holds every text's, else read
    /// from the database.
    /// </summary>
    private static async Task<ILinkWords> Words(ProductStore store, NpgsqlConnection db, IReadOnlyCollection<string> texts, CancellationToken cancellationToken) =>
        texts.All(t => store.Exists(TextWord.ProductOf(t)))
            ? await ProductWords.Read(store, db, texts, cancellationToken)
            : await WordKeys.Load(db, reverse: true, cancellationToken);

    /// <summary>
    /// What each link of a pair is, by id, in one string: its relation, settled method, confidence and source, its shape, its
    /// words with their sides, heads and roles, and its claims; the same for rows read from a final and from a database.
    /// </summary>
    private static Dictionary<long, string> Signatures(
        IEnumerable<(long Id, string Relation, string Method, double? Confidence, string Source, Guid Shape)> links,
        IEnumerable<(long Link, long Word, bool From, long? Head, string? Role)> words,
        IEnumerable<(long Link, string Method, double? Confidence, string Source)> claims)
    {
        static string Number(double? value) => value?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "-";
        var byLink = words.GroupBy(w => w.Link).ToDictionary(g => g.Key,
            g => string.Join(' ', g.Select(w => $"{w.Word}:{(w.From ? 'f' : 't')}:{w.Head}:{w.Role}").Order(StringComparer.Ordinal)));
        var claimed = claims.GroupBy(c => c.Link).ToDictionary(g => g.Key,
            g => string.Join('\u001f', g.Select(c => $"{c.Method}\u001e{Number(c.Confidence)}\u001e{c.Source}").Order(StringComparer.Ordinal)));
        return links.ToDictionary(l => l.Id, l => string.Join('\u001d', l.Relation, l.Method, Number(l.Confidence), l.Source, l.Shape.ToString(),
            byLink.GetValueOrDefault(l.Id) ?? "", claimed.GetValueOrDefault(l.Id) ?? ""));
    }

    /// <summary>The links of one pair as the database holds them, by <see cref="Signatures"/>.</summary>
    private static async Task<Dictionary<long, string>> Held(NpgsqlConnection db, int fromId, int toId, CancellationToken cancellationToken)
    {
        var links = new List<(long, string, string, double?, string, Guid)>();
        var words = new List<(long, long, bool, long?, string?)>();
        var claims = new List<(long, string, double?, string)>();
        var from = EnumSpelling.Of(LinkSide.From);
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT l.id, l.relation, l.method, l.confidence, coalesce(p.source, ''), l.fingerprint FROM link l
                         LEFT JOIN provenance p ON p.id = l.provenance_id WHERE l.from_text_id = @from AND l.to_text_id = @to;
                         SELECT w.link_id, w.word_id, w.side, w.head_word_id, w.role FROM link_word w JOIN link l ON l.id = w.link_id
                         WHERE l.from_text_id = @from AND l.to_text_id = @to;
                         SELECT c.link_id, c.method, c.confidence, coalesce(p.source, '') FROM link_claim c JOIN link l ON l.id = c.link_id
                         LEFT JOIN provenance p ON p.id = c.provenance_id WHERE l.from_text_id = @from AND l.to_text_id = @to;
                         """, db) { CommandTimeout = 0 })
        {
            command.Parameters.AddWithValue("from", fromId);
            command.Parameters.AddWithValue("to", toId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                links.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetDouble(3), reader.GetString(4),
                    reader.GetGuid(5)));
            }

            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                words.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2) == from, reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claims.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetDouble(2), reader.GetString(3)));
            }
        }

        return Signatures(links, words, claims);
    }

    /// <summary>
    /// One pair's final as rows to copy; a claim's provenance is the pair's own number of its source, counted from 1 in the
    /// order the file first names each (<see cref="PairRows.Sources"/>), until <see cref="Globalize"/> numbers it for the database.
    /// </summary>
    private static PairRows Read(ProductStore store, string pair, ILinkWords keys)
    {
        var provenance = new Dictionary<string, int>(StringComparer.Ordinal);
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

        return new PairRows(pair, links, words, claims, unresolved, [.. provenance.OrderBy(p => p.Value).Select(p => p.Key)]);
    }

    private static int Provenance(Dictionary<string, int> provenance, string source)
    {
        if (!provenance.TryGetValue(source, out var id))
        {
            provenance[source] = id = provenance.Count + 1;
        }

        return id;
    }

    /// <summary>
    /// A pair's own source numbers turned into the database's: a source already numbered keeps its id, a new one takes the
    /// next, in the order the pair first names them. Index 0 is unused.
    /// </summary>
    /// <summary>
    /// A source's provenance id is a hash of the source (30 bits, positive), so a source keeps its id whatever other sources
    /// come and go, and a link's row moves only when its own source does. The ids above 2^30 are left to the sequence, which a
    /// load sets past them, for a source written by <see cref="ProvenanceIds"/>.
    /// </summary>
    internal const int ProvenanceBits = 30;

    private static int[] Globalize(List<string> sources, Dictionary<string, int> provenance, RowIds.Hashed numbering)
    {
        var ids = new int[sources.Count + 1];
        for (var i = 0; i < sources.Count; i++)
        {
            if (!provenance.TryGetValue(sources[i], out var id))
            {
                provenance[sources[i]] = id = checked((int)numbering.Of(sources[i]));
            }

            ids[i + 1] = id;
        }

        return ids;
    }

    private static long[]? Resolve(string own, string text, int textId, ILinkWords keys)
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

    private static Dictionary<long, (long? Head, string? Role)> Roles(string? roles, string text, int textId, ILinkWords keys)
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

    private static async Task Copy(NpgsqlConnection db, PairRows rows, int[] sources, CancellationToken cancellationToken, bool freeze = false)
    {
        // Into tables this transaction emptied, the rows go in frozen: nothing reads them again only to mark them committed.
        var options = freeze ? "FORMAT BINARY, FREEZE" : "FORMAT BINARY";
        var methods = new Dictionary<long, short>();
        foreach (var claim in rows.Claims)
        {
            methods[claim.Link] = (short)(methods.GetValueOrDefault(claim.Link) | LinkMethods.Of([claim.Method]));
        }

        using (var import = db.BeginBinaryImport(
                   $"COPY link (id, from_text_id, to_text_id, relation, method, confidence, provenance_id, fingerprint, methods) FROM STDIN ({options})"))
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
                import.Write(sources[provenance], NpgsqlDbType.Integer);
                import.Write(shape, NpgsqlDbType.Uuid);
                import.Write(methods.GetValueOrDefault(id), NpgsqlDbType.Smallint);
            }

            import.Complete();
        }

        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);
        using (var import = db.BeginBinaryImport($"COPY link_word (link_id, word_id, side, head_word_id, role) FROM STDIN ({options})"))
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

        using (var import = db.BeginBinaryImport($"COPY link_claim (link_id, method, confidence, provenance_id) FROM STDIN ({options})"))
        {
            foreach (var (link, method, confidence, provenance) in rows.Claims)
            {
                import.StartRow();
                import.Write(link, NpgsqlDbType.Bigint);
                import.Write(method, NpgsqlDbType.Text);
                Nullable(import, confidence);
                import.Write(sources[provenance], NpgsqlDbType.Integer);
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
