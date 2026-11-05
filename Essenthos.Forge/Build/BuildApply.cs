using System.Diagnostics;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using Npgsql;

namespace Essenthos.Core.Build;

/// <summary>What an apply replaced: the products that moved and the pairs among them.</summary>
internal sealed record Applied(IReadOnlyList<string> Changed, IReadOnlyCollection<string> Pairs);

/// <summary>
/// <c>forge build --apply</c>, the daily path (DOC-0224 §13, NOT-0216 D13): the configured database takes only what moved
/// since <c>build/served/&lt;db&gt;</c> says it was built. The pairs whose final moved, the records, and the Strong and
/// forms indexes of the texts whose index moved are replaced in one transaction; the names on words and the spellings,
/// which a records load empties, are kept across it by record slug. The spellings are then counted again and the names
/// loaded again each in a transaction of its own, so every commit leaves a whole database. A text whose words moved, or
/// a carried table or one made whole from files (<c>tables/</c>), needs <c>build --into</c>.
/// </summary>
internal sealed class BuildApply(string connectionString, ProductStore store, ProductGraph graph, IServiceProvider services, ILogger logger)
{
    public List<BuildStage> Stages { get; } = [];

    private static readonly string[] TextLayer = ["texts/{text}/words", "texts/{text}/verses", "texts/{text}/notes", "texts/{text}/books", "texts/{text}/text"];

    public async Task<Applied?> Run(CancellationToken cancellationToken)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        ServingBuild.Refuse(database);
        if (ServedState.Read(store, database) is not { } served)
        {
            logger.LogError("No build/served/{Database} under {Resources}: build the database with --into first", database, store.ResourcesPath);
            return null;
        }

        var changed = Changed(served.Header);
        if (changed.Count == 0)
        {
            logger.LogInformation("Nothing {Database} was built from has moved", database);
            Stages.Add(new BuildStage("apply", "nothing moved", TimeSpan.Zero, 0));
            // The panes and what a record page reads of the words still answer for themselves: a database built before
            // them, or a product made since, is caught up.
            var catchUp = new ServingBuild(connectionString, store, services, logger);
            var panes = await catchUp.Panes(null, false, PaneParallelism, cancellationToken);
            if (panes + await catchUp.RecordWords(null, false, cancellationToken) > 0)
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
        var frames = new SortedSet<string>(StringComparer.Ordinal);
        var verses = new SortedSet<string>(StringComparer.Ordinal);
        bool records = false, names = false, spellings = false;
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
                    names = true;
                    break;
                case "texts/{text}/index/strong" or "texts/{text}/index/occurrences":
                    strong.Add(bound["text"]);
                    break;
                case "texts/{text}/index/forms":
                    forms.Add(bound["text"]);
                    break;
                case "texts/{text}/index/spellings":
                    spellings = true;
                    break;
                case var p when p.StartsWith("records/", StringComparison.Ordinal) || p.StartsWith("external/", StringComparison.Ordinal):
                    records = true;
                    break;
            }
        }

        var serving = new ServingBuild(connectionString, store, services, logger);
        await serving.Timed("apply", $"transaction: {pairs.Count} pairs, records {(records ? "yes" : "no")}, strong {strong.Count}, forms {forms.Count}", async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            long rows = 0;
            if (pairs.Count > 0)
            {
                var outcome = await MappingLoad.ReplacePairs(connection, store, pairs, logger, cancellationToken);
                if (outcome.Unresolved > 0)
                {
                    throw new InvalidOperationException($"{outcome.Unresolved} links name a word key the database does not hold.");
                }

                rows += outcome.Links;
            }

            if (verses.Count > 0)
            {
                var outcome = await VerseLinkFiles.Load(connection, store, verses, cancellationToken);
                if (outcome.LeftOut > 0)
                {
                    throw new InvalidOperationException($"{outcome.LeftOut} verse links name a verse the database does not hold.");
                }

                rows += outcome.Links;
            }

            if (records)
            {
                await Keep(connection, cancellationToken);
                foreach (var (_, count) in await RecordFiles.Load(connection, transaction, store, cancellationToken))
                {
                    rows += Math.Max(count, 0);
                }

                var lost = await Restore(connection, cancellationToken);
                if (lost > 0)
                {
                    logger.LogWarning("{Lost} names and spellings stood on records the files no longer hold", lost);
                }

                spellings = true;
            }

            if (strong.Count > 0)
            {
                rows += (await IndexLoad.Strong(connection, store, strong, cancellationToken)).Rows;
            }

            if (forms.Count > 0)
            {
                rows += (await IndexLoad.Forms(connection, store, forms, cancellationToken)).Rows;
            }

            await transaction.CommitAsync(cancellationToken);
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

        if (names)
        {
            await serving.Timed("apply", "names", async () =>
            {
                var projection = new NamesProjection(store);
                var texts = projection.Texts.Where(t => store.Exists(NameProducts.Projected(t))).ToList();
                return (await NamesLoad.Run(connectionString, projection.Store, texts, cancellationToken)).Rows;
            });
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

        // The panes and what a record page reads of the words read what the steps above left, so they come last; only the
        // texts whose inputs moved are made again.
        await serving.Panes(null, false, PaneParallelism, cancellationToken);
        await serving.RecordWords(null, false, cancellationToken);
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

    /// <summary>The names on words and the spellings, by record slug, before a records load empties them.</summary>
    private static async Task Keep(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            CREATE TEMP TABLE kept_names ON COMMIT DROP AS
                SELECT w.word_id, e.slug, w.method, w.confidence, w.source, w.note, w.role, w.origin FROM word_entity w JOIN entity e ON e.id = w.entity_id;
            CREATE TEMP TABLE kept_renderings ON COMMIT DROP AS
                SELECT e.slug, r.text_id, r.form, r.folded, r.occurrences, r.heading FROM entity_rendering r JOIN entity e ON e.id = r.entity_id;
            CREATE TEMP TABLE kept_spellings ON COMMIT DROP AS
                SELECT e.slug, s.form, s.folded FROM entity_search_spelling s JOIN entity e ON e.id = s.entity_id;
            """, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>What <see cref="Keep"/> held, put back on the records of the same slug; the rows whose record is gone are counted.</summary>
    private static async Task<long> Restore(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
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
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}
