using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Build;

/// <summary>One timed piece of a build, as the report lists it.</summary>
internal sealed record BuildStage(string Stage, string What, TimeSpan Elapsed, long Rows, string Note = "")
{
    public static readonly ProductSchema<BuildStage> Schema = new ProductSchema<BuildStage>()
        .Text("stage", s => s.Stage)
        .Text("what", s => s.What)
        .Text("seconds", s => s.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture))
        .Integer("rows", s => s.Rows)
        .Text("note", s => s.Note)
        .Reads(row => new BuildStage(row.Text(0), row.Text(1), TimeSpan.FromSeconds(double.Parse(row.Text(2), CultureInfo.InvariantCulture)),
            row.Integer(3), row.Text(4)));
}

/// <summary>
/// A serving database made from the products alone (DOC-0224 §13): created and migrated, every secondary index and foreign
/// key dropped, the tables filled by COPY (texts first, then the mapping, the records, the frame and the carried tables at
/// once, then the names, the word groups and the indexes), the indexes and keys made again in parallel, and ANALYZE. It
/// never touches the live corpus or the shared snapshot.
/// </summary>
internal sealed class ServingBuild(string connectionString, ProductStore store, IServiceProvider services, ILogger logger)
{
    public static readonly string[] Untouchable = ["essenthos_core", "essenthos_core_rework_src", "postgres", "template0", "template1"];

    public string Database { get; } = new NpgsqlConnectionStringBuilder(connectionString).Database
                                      ?? throw new ArgumentException("The build's connection names no database.");

    public ConcurrentQueue<BuildStage> Stages { get; } = new();

    /// <summary>Rows each served table holds once the build is done, which <c>verify --level essentials</c> holds the database to.</summary>
    public Dictionary<string, long> Rows { get; } = new(StringComparer.Ordinal);

    public static void Refuse(string database)
    {
        if (Untouchable.Contains(database, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"forge build never builds into {database}; name a database of its own.");
        }
    }

    public async Task Create(bool replace, CancellationToken cancellationToken)
    {
        Refuse(Database);
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false }.ConnectionString;
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync(cancellationToken);
            await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
            exists.Parameters.AddWithValue("name", Database);
            if (await exists.ExecuteScalarAsync(cancellationToken) is not null)
            {
                if (!replace)
                {
                    throw new InvalidOperationException($"{Database} already exists; build into a new name, or pass --replace to drop it first.");
                }

                await Execute(admin, $"DROP DATABASE \"{Database}\" WITH (FORCE)", cancellationToken);
            }

            await Execute(admin, $"CREATE DATABASE \"{Database}\"", cancellationToken);
        }

        await Timed("database", "migrate", async () =>
        {
            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken);
            return 0;
        });
    }

    public async Task Fill(int parallel, CancellationToken cancellationToken)
    {
        var (indexes, keys) = await Unindex(cancellationToken);
        var groups = await Timed("load", "texts", async () =>
        {
            var (outcome, collected) = await TextLoad.Run(connectionString, store, parallel, cancellationToken);
            logger.LogInformation("Texts: {Texts} texts, {Verses} verses, {Words} words, {Notes} notes in {Elapsed}",
                outcome.Texts, outcome.Verses, outcome.Words, outcome.Notes, outcome.Elapsed);
            return (outcome.Words, collected);
        });

        await Task.WhenAll(
            Timed("load", "mapping", async () =>
            {
                var outcome = await MappingLoad.Run(connectionString, store, logger, cancellationToken);
                if (outcome.Unresolved > 0)
                {
                    throw new InvalidOperationException($"{outcome.Unresolved} links name a word key the texts do not hold.");
                }

                return outcome.Links;
            }),
            Timed("load", "records", async () =>
            {
                long rows = 0;
                foreach (var (product, count) in await RecordFiles.Load(connectionString, store, cancellationToken))
                {
                    if (count < 0)
                    {
                        logger.LogWarning("{Product}: not loaded, what it stands on is not in this database", product);
                    }

                    rows += Math.Max(count, 0);
                }

                return rows;
            }),
            Timed("load", "carried", async () =>
            {
                long rows = 0;
                foreach (var outcome in await CarriedTables.Load(connectionString, store, null, cancellationToken))
                {
                    logger.LogInformation("Carried {Table}: {Loaded} rows, {LeftOut} left out, in {Elapsed}", outcome.Table, outcome.Loaded, outcome.LeftOut, outcome.Elapsed);
                    Stages.Enqueue(new BuildStage("load", $"carried/{outcome.Table}", outcome.Elapsed, outcome.Loaded,
                        outcome.LeftOut > 0 ? $"{outcome.LeftOut} rows on a word, verse or group this build does not hold" : ""));
                    rows += outcome.Loaded;
                }

                return rows;
            }),
            Timed("load", "frame", () => Frame(null, cancellationToken)),
            Timed("load", "verse links", async () =>
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                var outcome = await VerseLinkFiles.Load(connection, store, null, cancellationToken);
                logger.LogInformation("Verse links: {Links} over {Pairs} pairs, {Verses} memberships, {LeftOut} left out (a verse this build does not hold), in {Elapsed}",
                    outcome.Links, outcome.Pairs, outcome.Verses, outcome.LeftOut, outcome.Elapsed);
                return outcome.Links;
            }));

        await Timed("load", "names", async () =>
        {
            var projection = new NamesProjection(store);
            var texts = projection.Texts.Where(t => store.Exists(NameProducts.Projected(t))).ToList();
            var outcome = await NamesLoad.Run(connectionString, projection.Store, texts, cancellationToken);
            logger.LogInformation("Names: {Rows} rows of {Texts} texts; {Records} on a record the database lacks, {Words} on a word without an id",
                outcome.Rows, outcome.Texts, outcome.UnknownRecords, outcome.UnknownWords);
            return outcome.Rows;
        });

        await Timed("load", "word groups", () => TextLoad.WriteGroups(connectionString, groups.collected, cancellationToken));
        await Indexes(null, true, true, cancellationToken);
        await Reindex(indexes, keys, parallel, cancellationToken);
        await Timed("database", "analyze", async () =>
        {
            await Execute(connectionString, "ANALYZE", cancellationToken);
            return 0;
        });
        await Panes(null, true, parallel, cancellationToken);
        await Count(cancellationToken);
    }

    /// <summary>
    /// The panes of the texts named (every text when null) whose inputs moved, or all of them when <paramref name="force"/>
    /// is set (a fresh database holds none), read from the tables as they now stand.
    /// </summary>
    public Task<long> Panes(IReadOnlyCollection<string>? texts, bool force, int parallel, CancellationToken cancellationToken) =>
        Timed("load", "panes", async () =>
        {
            var outcomes = await new PaneBuild(connectionString, store, services, logger).Run(texts, force, parallel, cancellationToken);
            var made = outcomes.Where(o => o.Made).ToList();
            logger.LogInformation("Panes: {Made} texts made ({Panes} panes, {Compressed} bytes compressed), {Current} current",
                made.Count, made.Sum(o => o.Panes), made.Sum(o => o.Compressed), outcomes.Count - made.Count);
            if (made.Count > 0)
            {
                await Execute(connectionString, "ANALYZE pane", cancellationToken);
            }

            return (long)made.Sum(o => o.Panes);
        });

    /// <summary>The frame of the texts named, or of every text with a frame file, each text replaced in a transaction of its own.</summary>
    public async Task<long> Frame(IReadOnlyCollection<string>? only, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loader = scope.ServiceProvider.GetRequiredService<CanonicalFrameLoader>();
        long rows = 0;
        foreach (var text in await db.Texts.AsNoTracking().OrderBy(t => t.Slug).ToListAsync(cancellationToken))
        {
            if ((only is null || only.Contains(text.Slug)) && store.Exists(Loading.Frame.FrameFile.Product(text.Slug)))
            {
                var outcome = await loader.Fill(text, store, cancellationToken);
                rows += outcome.References;
            }
        }

        return rows;
    }

    /// <summary>The per-text indexes: Strong and forms of the texts named (every text when null), spellings across all texts when asked.</summary>
    public async Task Indexes(IReadOnlyCollection<string>? texts, bool forms, bool spellings, CancellationToken cancellationToken)
    {
        var context = new IndexContext(store);
        var named = texts ?? [.. context.Texts.Keys.Order(StringComparer.Ordinal)];
        named = [.. named.Where(context.Texts.ContainsKey)];
        await Timed("load", "indexes: strong", async () => (await IndexLoad.Strong(connectionString, store, named, cancellationToken)).Rows);
        if (forms)
        {
            await Timed("load", "indexes: forms", async () => (await IndexLoad.Forms(connectionString, store, named, cancellationToken)).Rows);
        }

        if (spellings)
        {
            await Timed("load", "indexes: spellings", async () =>
            {
                using var scope = services.CreateScope();
                return (await IndexLoad.Spellings(connectionString, store, scope.ServiceProvider.GetRequiredService<EntityRenderingLoader>(),
                    cancellationToken)).Rows;
            });
        }
    }

    private sealed record Definition(string Table, string Name, string Sql);

    /// <summary>Every secondary index and every foreign key of the empty schema, written down and dropped.</summary>
    private async Task<(List<Definition> Indexes, List<Definition> Keys)> Unindex(CancellationToken cancellationToken)
    {
        var indexes = new List<Definition>();
        var keys = new List<Definition>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT i.tablename, i.indexname, i.indexdef FROM pg_indexes i
                         WHERE i.schemaname = 'public' AND NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conname = i.indexname AND c.connamespace = 'public'::regnamespace)
                         """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                indexes.Add(new Definition(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        await using (var command = new NpgsqlCommand(
                         "SELECT conrelid::regclass::text, conname, pg_get_constraintdef(oid) FROM pg_constraint WHERE contype = 'f' AND connamespace = 'public'::regnamespace",
                         connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add(new Definition(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        foreach (var key in keys)
        {
            await Execute(connection, $"ALTER TABLE {key.Table} DROP CONSTRAINT \"{key.Name}\"", cancellationToken);
        }

        foreach (var index in indexes)
        {
            await Execute(connection, $"DROP INDEX \"{index.Name}\"", cancellationToken);
        }

        logger.LogInformation("{Indexes} indexes and {Keys} foreign keys dropped before the load", indexes.Count, keys.Count);
        return (indexes, keys);
    }

    /// <summary>The dropped indexes made again, the largest tables first and <paramref name="parallel"/> at once, then the keys, validated.</summary>
    private async Task Reindex(List<Definition> indexes, List<Definition> keys, int parallel, CancellationToken cancellationToken)
    {
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT relname, pg_total_relation_size(oid) FROM pg_class WHERE relkind = 'r' AND relnamespace = 'public'::regnamespace", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sizes[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        var gone = new ConcurrentBag<string>();
        await Timed("database", "indexes", async () =>
        {
            await Parallel.ForEachAsync(indexes.OrderByDescending(i => sizes.GetValueOrDefault(i.Table)),
                new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken }, async (index, token) =>
                {
                    var sql = index.Sql.Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ", StringComparison.Ordinal)
                        .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.Ordinal);
                    await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync(token);
                    await Execute(connection, "SET maintenance_work_mem = '1GB'", token);
                    try
                    {
                        await Execute(connection, sql, token);
                    }
                    catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.UndefinedTable)
                    {
                        // A loader's own schema step (the records files take entity.source out) removed what the index was on.
                        gone.Add($"{index.Name}: {exception.MessageText}");
                    }
                });
            if (!gone.IsEmpty)
            {
                logger.LogWarning("{Count} indexes stood on columns the loaders' schema steps removed: {Gone}", gone.Count, string.Join("; ", gone.Order()));
            }

            return indexes.Count - gone.Count;
        });

        await Timed("database", "foreign keys", async () =>
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand("SELECT conname FROM pg_constraint WHERE contype = 'f' AND connamespace = 'public'::regnamespace", connection);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    present.Add(reader.GetString(0));
                }
            }

            var failed = new ConcurrentBag<string>();
            await Parallel.ForEachAsync(keys.Where(k => !present.Contains(k.Name)).OrderByDescending(k => sizes.GetValueOrDefault(k.Table)),
                new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken }, async (key, token) =>
                {
                    try
                    {
                        await Execute(connectionString, $"ALTER TABLE {key.Table} ADD CONSTRAINT \"{key.Name}\" {key.Sql}", token);
                    }
                    catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.UndefinedTable)
                    {
                        gone.Add($"{key.Name}: {exception.MessageText}");
                    }
                    catch (PostgresException exception)
                    {
                        failed.Add($"{key.Table}.{key.Name}: {exception.MessageText}");
                    }
                });

            if (!failed.IsEmpty)
            {
                throw new InvalidOperationException($"{failed.Count} foreign keys do not hold on the loaded rows:\n  " + string.Join("\n  ", failed.Order()));
            }

            return keys.Count;
        });
    }

    public async Task Count(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var tables = new List<string>();
        await using (var command = new NpgsqlCommand("SELECT relname FROM pg_class WHERE relkind = 'r' AND relnamespace = 'public'::regnamespace ORDER BY relname", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables)
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", connection);
            Rows[table] = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }
    }

    public async Task<T> Timed<T>(string stage, string what, Func<Task<T>> run)
    {
        var clock = Stopwatch.StartNew();
        logger.LogInformation("{Stage}: {What}", stage, what);
        var result = await run();
        var rows = result switch
        {
            long n => n,
            int n => n,
            ValueTuple<long, TextLoad.Groups> t => t.Item1,
            _ => 0,
        };
        Stages.Enqueue(new BuildStage(stage, what, clock.Elapsed, rows));
        logger.LogInformation("{Stage}: {What} done in {Seconds:0.0} s, {Rows} rows", stage, what, clock.Elapsed.TotalSeconds, rows);
        return result;
    }

    private static async Task Execute(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await Execute(connection, sql, cancellationToken);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
