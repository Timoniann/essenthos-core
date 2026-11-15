using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Conversion;
using Essenthos.Core.Database;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Build;

/// <summary>What an apply replaced: the products that moved and the pairs among them.</summary>
internal sealed record Applied(IReadOnlyList<string> Changed, IReadOnlyCollection<string> Pairs);

/// <summary>
/// <c>forge build --apply</c>, the daily path (DOC-0224 §13, NOT-0216 D13): the configured database takes only what moved
/// since <c>build/served/&lt;db&gt;</c> says it was built. The pairs whose final moved, the records, and the Strong and
/// forms indexes of the texts whose index moved are replaced in one transaction; the names on words and the spellings,
/// which a records load empties, are kept across it by record slug. The spellings are then counted again and the names of
/// the texts whose projection moved replaced each in a transaction of its own, so every commit leaves a whole database. A text whose words moved, or
/// a carried table or one made whole from files (<c>tables/</c>), needs <c>build --into</c>.
/// </summary>
internal sealed class BuildApply(string connectionString, ProductStore store, ProductGraph graph, IServiceProvider services, ILogger logger)
{
    public List<BuildStage> Stages { get; } = [];

    private static readonly string[] TextLayer =
        ["texts/{text}/words", "texts/{text}/verses", "texts/{text}/notes", "texts/{text}/books", "texts/{text}/text", WordIdCollision.Product];

    public async Task<Applied?> Run(CancellationToken cancellationToken)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        ServingBuild.Refuse(database);
        if (ServedState.Read(store, database) is not { } served)
        {
            logger.LogError("No build/served/{Database} under {Resources}: build the database with --into first", database, store.ResourcesPath);
            return null;
        }

        // A migration may change what the loads write into tables nothing here replaces, so a database behind this Forge's
        // migrations (a seed cloned before they were merged) is built again, not applied.
        using (var scope = services.CreateScope())
        {
            var pending = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                logger.LogError("{Database} lacks {Count} migration(s) of this Forge ({Migrations}): build --into a new database", database, pending.Count,
                    string.Join(", ", pending));
                return null;
            }
        }

        var changed = Changed(served.Header);
        if (changed.Count == 0)
        {
            logger.LogInformation("Nothing {Database} was built from has moved", database);
            Stages.Add(new BuildStage("apply", "nothing moved", TimeSpan.Zero, 0));
            // The panes and what a record page reads of the words still answer for themselves: a database built before
            // them, or a product made since, is caught up.
            var catchUp = new ServingBuild(connectionString, store, services, logger);
            var caught = await catchUp.Panes(null, false, PaneParallelism, cancellationToken);
            caught += await catchUp.RecordWords(null, false, cancellationToken);
            caught += await catchUp.Families(false, cancellationToken);
            caught += await catchUp.Context(false, cancellationToken);
            if (caught + await catchUp.Strength(null, false, cancellationToken) > 0)
            {
                await catchUp.Count(cancellationToken);
                await WriteServed(database, catchUp.Rows, cancellationToken);
                Stages.AddRange(catchUp.Stages);
            }

            return new Applied([], []);
        }

        var pairs = new SortedSet<string>(StringComparer.Ordinal);
        var strong = new SortedSet<string>(StringComparer.Ordinal);
        var forms = new SortedSet<string>(StringComparer.Ordinal);
        var renderings = new SortedSet<string>(StringComparer.Ordinal);
        var frames = new SortedSet<string>(StringComparer.Ordinal);
        var verses = new SortedSet<string>(StringComparer.Ordinal);
        var recordWords = new SortedSet<string>(StringComparer.Ordinal);
        var strength = new SortedSet<string>(StringComparer.Ordinal);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        bool records = false, spellings = false;
        foreach (var product in changed)
        {
            if (graph.Find(product) is not { } line)
            {
                continue;
            }

            var (declaration, bound) = line;
            logger.LogInformation("moved: {Product}", product);
            switch (declaration.Product)
            {
                case var p when TextLayer.Contains(p) || p.StartsWith("carried/", StringComparison.Ordinal) || p.StartsWith("tables/", StringComparison.Ordinal):
                    logger.LogError("{Product} moved: a text's words or a table loaded whole is not applied in place; build --into a new database", product);
                    return null;
                case "mapping/{from}-{to}/final":
                    pairs.Add($"{bound["from"]}-{bound["to"]}");
                    break;
                case "mapping/{from}-{to}/verses":
                    verses.Add($"{bound["from"]}-{bound["to"]}");
                    break;
                case "texts/{text}/frame":
                    frames.Add(bound["text"]);
                    break;
                case "names/projected/{text}":
                    names.Add(bound["text"]);
                    break;
                case "texts/{text}/index/strong" or "texts/{text}/index/occurrences":
                    strong.Add(bound["text"]);
                    break;
                case "texts/{text}/index/forms":
                    forms.Add(bound["text"]);
                    break;
                case "texts/{text}/index/renderings" or "texts/{text}/index/rendered":
                    renderings.Add(bound["text"]);
                    break;
                case "texts/{text}/index/spellings":
                    spellings = true;
                    break;
                // The store lists one table of record words; a list written since this database was served came from another
                // database (a fresh build from the same store, an apply on another copy) and does not describe this one's rows,
                // so those texts are counted again even where the list's own inputs are current. The panes keep their bodies
                // and are matched to the table by hash whatever wrote them.
                case "record-spellings/{text}" or "record-positions/{text}":
                    recordWords.Add(bound["text"]);
                    break;
                // A text's strength names its reference texts by slug and is a function of its inputs alone, so a list another
                // database counted since this one was served is copied as it stands.
                case "strength/{text}":
                    strength.Add(bound["text"]);
                    break;
                case var p when p.StartsWith("records/", StringComparison.Ordinal) || p.StartsWith("external/", StringComparison.Ordinal):
                    records = true;
                    break;
            }
        }

        var serving = new ServingBuild(connectionString, store, services, logger);
        await serving.Timed("apply", $"transaction: {pairs.Count} pairs, records {(records ? "yes" : "no")}, strong {strong.Count}, forms {forms.Count}, renderings {renderings.Count}", async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            long rows = 0;
            var step = Stopwatch.StartNew();
            void Done(string what)
            {
                logger.LogInformation("apply: {What} in {Seconds:0.0} s", what, step.Elapsed.TotalSeconds);
                step.Restart();
            }

            if (pairs.Count > 0)
            {
                var outcome = await MappingLoad.ReplacePairs(connection, store, pairs, logger, cancellationToken);
                if (outcome.Unresolved > 0)
                {
                    throw new InvalidOperationException($"{outcome.Unresolved} links name a word key the database does not hold.");
                }

                rows += outcome.Links;
                Done($"{pairs.Count} pairs");
            }

            if (verses.Count > 0)
            {
                var outcome = await VerseLinkFiles.Load(connection, store, verses, cancellationToken);
                if (outcome.LeftOut > 0)
                {
                    throw new InvalidOperationException($"{outcome.LeftOut} verse links name a verse the database does not hold.");
                }

                rows += outcome.Links;
                Done($"verse links of {verses.Count} pairs");
            }

            if (records)
            {
                var kept = await Detach(connection, cancellationToken);
                var took = new List<(string File, TimeSpan Elapsed)>();
                foreach (var (_, count) in await RecordFiles.Load(connection, transaction, store, cancellationToken, (file, elapsed) => took.Add((file, elapsed))))
                {
                    rows += Math.Max(count, 0);
                }

                logger.LogInformation("apply: the slowest record files: {Files}",
                    string.Join(", ", took.OrderByDescending(t => t.Elapsed).Take(5).Select(t => $"{t.File} {t.Elapsed.TotalSeconds:0.0} s")));
                Done("records");
                var (moved, lost) = await Attach(connection, kept, cancellationToken);
                if (lost > 0)
                {
                    logger.LogWarning("{Lost} names and spellings stood on records the files no longer hold", lost);
                }

                Done(moved ? "names, spellings and record words moved to the records' new ids" : "names, spellings and record words kept: no record's id moved");
                spellings = true;
            }

            if (strong.Count > 0)
            {
                rows += (await IndexLoad.Strong(connection, store, strong, cancellationToken)).Rows;
                Done($"Strong indexes of {strong.Count} texts");
            }

            if (forms.Count > 0)
            {
                rows += (await IndexLoad.Forms(connection, store, forms, cancellationToken)).Rows;
                Done($"forms of {forms.Count} texts");
            }

            if (renderings.Count > 0)
            {
                rows += (await IndexLoad.WordRenderings(connection, store, renderings, cancellationToken)).Rows;
                Done($"renderings of {renderings.Count} texts");
            }

            await transaction.CommitAsync(cancellationToken);
            Done("commit");
            return rows;
        });

        if (spellings)
        {
            await serving.Timed("apply", "spellings", async () =>
            {
                using var scope = services.CreateScope();
                return (await IndexLoad.Spellings(connectionString, store, scope.ServiceProvider.GetRequiredService<EntityRenderingLoader>(), cancellationToken)).Rows;
            });
        }

        // Only the texts whose projection moved are replaced, by their words; when most moved, the whole table is loaded again
        // without its indexes, which is then the faster way (PRB-1484).
        if (names.Count > 0)
        {
            var projection = new NamesProjection(store);
            var texts = projection.Texts.Where(t => store.Exists(NameProducts.Projected(t))).ToList();
            var whole = names.Count * 2 > texts.Count;
            await serving.Timed("apply", whole ? "names of every text" : $"names of {string.Join(",", names)}", async () => whole
                ? (await NamesLoad.Run(connectionString, projection.Store, texts, cancellationToken, readBack: false)).Rows
                : (await NamesLoad.Replace(connectionString, projection.Store, [.. names], cancellationToken)).Rows);
        }

        if (frames.Count > 0)
        {
            await serving.Timed("apply", $"frame of {string.Join(",", frames)}", () => serving.Frame(frames, cancellationToken));
        }

        await serving.Timed("apply", "analyze", async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("ANALYZE", connection) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync(cancellationToken);
            return 0;
        });

        // The panes and what a record page reads of the words read what the steps above left, so they come last: a pane is read
        // again only where its chapter's inputs moved, and the table takes every pane of the products it lacks.
        await serving.Panes(null, false, PaneParallelism, cancellationToken);
        if (recordWords.Count > 0)
        {
            await serving.RecordWords(recordWords, true, cancellationToken);
        }

        await serving.RecordWords(null, false, cancellationToken);
        await serving.Families(false, cancellationToken);
        await serving.Context(false, cancellationToken);
        await serving.Strength(null, false, cancellationToken, strength);
        await serving.Count(cancellationToken);
        await WriteServed(database, serving.Rows, cancellationToken);
        Stages.AddRange(serving.Stages);
        return new Applied(changed, pairs);
    }

    /// <summary>How many panes are read at once.</summary>
    private const int PaneParallelism = 16;

    private async Task WriteServed(string database, IReadOnlyDictionary<string, long> rows, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_constraint WHERE contype = 'f' AND connamespace = 'public'::regnamespace", connection);
        ServedState.Write(store, graph, database, rows, Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)), "forge build --apply");
    }

    /// <summary>
    /// The products with tables whose file, hashed as it is now (a hand edit records nothing in the manifest), is not the one the
    /// database was built from, and those gone or new since. A manifest entry whose file is gone is gone.
    /// </summary>
    private List<string> Changed(ProductHeader served)
    {
        var then = served.Inputs.ToDictionary(i => i.Path, i => i.Sha256, StringComparer.Ordinal);
        var now = store.Manifest.Entries
            .Where(e => graph.Find(e.Product) is { Declaration.Tables.Count: > 0 } && File.Exists(store.FullPath(e.Product)))
            .ToDictionary(e => e.Product, e => store.Hasher.Hash(e.Product), StringComparer.Ordinal);
        return [.. now.Where(n => !then.TryGetValue(n.Key, out var sha) || sha != n.Value).Select(n => n.Key)
            .Concat(then.Keys.Where(t => !now.ContainsKey(t))).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The tables outside the record files that point at a record, which a records load would empty through their keys' CASCADE:
    /// the names on words and the spellings, kept across the load, and the record words, counted again when emptied.
    /// </summary>
    private static readonly string[] OnRecords = ["word_entity", "entity_rendering", "entity_search_spelling", "record_spelling", "record_position"];

    /// <summary>
    /// Before a records load: every record's id by slug written down, and the keys from <see cref="OnRecords"/> to the records
    /// taken off, so the load's CASCADE leaves those tables as they are. Returns the keys, to put back.
    /// </summary>
    private static async Task<List<(string Table, string Name, string Definition)>> Detach(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var keys = new List<(string, string, string)>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT conrelid::regclass::text, quote_ident(conname), pg_get_constraintdef(oid) FROM pg_constraint
                         WHERE contype = 'f' AND confrelid = 'entity'::regclass AND conrelid::regclass::text = ANY(@tables) ORDER BY 1, 2
                         """, connection))
        {
            command.Parameters.AddWithValue("tables", OnRecords);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var sql = new System.Text.StringBuilder("CREATE TEMP TABLE kept_ids ON COMMIT DROP AS SELECT id, slug FROM entity;\n");
        foreach (var (table, name, _) in keys)
        {
            sql.Append(CultureInfo.InvariantCulture, $"ALTER TABLE {table} DROP CONSTRAINT {name};\n");
        }

        await using var detach = new NpgsqlCommand(sql.ToString(), connection) { CommandTimeout = 0 };
        await detach.ExecuteNonQueryAsync(cancellationToken);
        return keys;
    }

    /// <summary>
    /// After a records load: where every record kept its id (the records are numbered in slug order, so an edit that adds or
    /// removes none moves none), the rows of <see cref="OnRecords"/> stand as they were. Otherwise the names and spellings are
    /// moved to the new id of their record's slug, those whose record is gone are dropped and counted, and the record words are
    /// emptied, to be counted again. The keys go back on, validated, either way.
    /// </summary>
    private static async Task<(bool Moved, long Lost)> Attach(NpgsqlConnection connection, List<(string Table, string Name, string Definition)> keys,
        CancellationToken cancellationToken)
    {
        await using (var count = new NpgsqlCommand(
                         "SELECT count(*) FROM kept_ids k LEFT JOIN entity e ON e.id = k.id WHERE e.slug IS DISTINCT FROM k.slug", connection) { CommandTimeout = 0 })
        {
            if (Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0)
            {
                await Reattach(connection, keys, cancellationToken);
                return (false, 0);
            }
        }

        await using var command = new NpgsqlCommand(
            """
            CREATE TEMP TABLE kept_names ON COMMIT DROP AS
                SELECT w.word_id, k.slug, w.method, w.confidence, w.source, w.note, w.role, w.origin FROM word_entity w JOIN kept_ids k ON k.id = w.entity_id;
            CREATE TEMP TABLE kept_renderings ON COMMIT DROP AS
                SELECT k.slug, r.text_id, r.form, r.folded, r.occurrences, r.heading FROM entity_rendering r JOIN kept_ids k ON k.id = r.entity_id;
            CREATE TEMP TABLE kept_spellings ON COMMIT DROP AS
                SELECT k.slug, s.form, s.folded FROM entity_search_spelling s JOIN kept_ids k ON k.id = s.entity_id;
            TRUNCATE word_entity, entity_rendering, entity_search_spelling, record_spelling, record_position CASCADE;
            INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note, role, origin)
                SELECT k.word_id, e.id, k.method, k.confidence, k.source, k.note, k.role, k.origin FROM kept_names k JOIN entity e ON e.slug = k.slug;
            INSERT INTO entity_rendering (entity_id, text_id, form, folded, occurrences, heading)
                SELECT e.id, k.text_id, k.form, k.folded, k.occurrences, k.heading FROM kept_renderings k JOIN entity e ON e.slug = k.slug;
            INSERT INTO entity_search_spelling (entity_id, form, folded)
                SELECT e.id, k.form, k.folded FROM kept_spellings k JOIN entity e ON e.slug = k.slug;
            SELECT (SELECT count(*) FROM kept_names k WHERE NOT EXISTS (SELECT 1 FROM entity e WHERE e.slug = k.slug))
                 + (SELECT count(*) FROM kept_renderings k WHERE NOT EXISTS (SELECT 1 FROM entity e WHERE e.slug = k.slug))
                 + (SELECT count(*) FROM kept_spellings k WHERE NOT EXISTS (SELECT 1 FROM entity e WHERE e.slug = k.slug));
            """, connection) { CommandTimeout = 0 };
        var lost = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        await Reattach(connection, keys, cancellationToken);
        return (true, lost);
    }

    private static async Task Reattach(NpgsqlConnection connection, List<(string Table, string Name, string Definition)> keys, CancellationToken cancellationToken)
    {
        foreach (var (table, name, definition) in keys)
        {
            await using var command = new NpgsqlCommand($"ALTER TABLE {table} ADD CONSTRAINT {name} {definition}", connection) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
