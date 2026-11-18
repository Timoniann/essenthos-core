using System.Diagnostics;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Postgres;
using Npgsql;

namespace Essenthos.Core.Build;

/// <summary>
/// The served tables loaded whole from a product: made from files (<see cref="TableProducts"/>, <c>tables/&lt;table&gt;</c>), or,
/// for the ones no producer makes yet, carried from a database as <c>carried/&lt;table&gt;</c> so that a build
/// still reads files only: every column as Postgres prints it, a text by its slug, a word by its id (computed from its key,
/// so the same in every database built from the same words). Loaded through a staging table, each row put back on the text,
/// verse and word the new database holds there; a row whose word or verse the products no longer hold is left out and
/// counted. Each table leaves this list when the package that owns it writes it as a product.
/// </summary>
internal static class CarriedTables
{
    /// <summary>A table loaded whole; <paramref name="After"/> names the table whose rows its load reads, which is loaded first.</summary>
    public sealed record Carried(string Table, string Export, string Columns, string Load, string? After = null);

    public sealed record Outcome(string Table, long Exported, long Loaded, long LeftOut, TimeSpan Elapsed);

    private const string Kept = "('supplied', 'restored', 'doubtful', 'subscription', 'corrupt', 'not-in-ethiopic', 'not-in-greek')";

    public static readonly IReadOnlyList<Carried> All =
    [
        new("word_group",
            $"SELECT x.id::text, t.slug AS text, x.kind, x.parent_id::text, x.position::text, x.features::text, x.mother_group_id::text, x.mother_word_id::text " +
            $"FROM word_group x JOIN text t ON t.id = x.text_id WHERE x.kind NOT IN {Kept} ORDER BY x.id",
            "id, text_id, kind, parent_id, position, features, mother_group_id, mother_word_id",
            "SELECT s.id::bigint, t.id, s.kind, s.parent_id::bigint, s.position::int, s.features::jsonb, s.mother_group_id::bigint, s.mother_word_id::bigint " +
            "FROM s JOIN text t ON t.slug = s.text WHERE s.mother_word_id IS NULL OR EXISTS (SELECT 1 FROM word w WHERE w.id = s.mother_word_id::bigint)"),
        new("word_group_word",
            $"SELECT x.word_group_id::text, x.word_id::text FROM word_group_word x JOIN word_group g ON g.id = x.word_group_id " +
            $"WHERE g.kind NOT IN {Kept} ORDER BY x.word_group_id, x.word_id",
            "word_group_id, word_id",
            "SELECT s.word_group_id::bigint, s.word_id::bigint FROM s WHERE EXISTS (SELECT 1 FROM word w WHERE w.id = s.word_id::bigint) " +
            "AND EXISTS (SELECT 1 FROM word_group g WHERE g.id = s.word_group_id::bigint)",
            After: "word_group"),
        new("word_parsing",
            "SELECT x.id::text, x.word_id::text, x.lemma, x.morphology::text, x.method, x.confidence::text, x.source, x.note FROM word_parsing x ORDER BY x.id",
            "id, word_id, lemma, morphology, method, confidence, source, note",
            "SELECT s.id::bigint, s.word_id::bigint, s.lemma, s.morphology::jsonb, s.method, s.confidence::float8, s.source, s.note FROM s " +
            "WHERE EXISTS (SELECT 1 FROM word w WHERE w.id = s.word_id::bigint)"),
        new("word_strong",
            "SELECT x.id::text, x.word_id::text, x.number, x.method, x.confidence::text, x.source, x.note FROM word_strong x ORDER BY x.id",
            "id, word_id, number, method, confidence, source, note",
            "SELECT s.id::bigint, s.word_id::bigint, s.number, s.method, s.confidence::float8, s.source, s.note FROM s " +
            "WHERE EXISTS (SELECT 1 FROM word w WHERE w.id = s.word_id::bigint)"),
        new("word_referent",
            "SELECT x.word_id::text, x.referent_word_id::text, x.ordinal::text, x.method, x.confidence::text, x.source FROM word_referent x " +
            "ORDER BY x.word_id, x.source, x.ordinal",
            "word_id, referent_word_id, ordinal, method, confidence, source",
            "SELECT s.word_id::bigint, s.referent_word_id::bigint, s.ordinal::int, s.method, s.confidence::float8, s.source FROM s " +
            "WHERE EXISTS (SELECT 1 FROM word w WHERE w.id = s.word_id::bigint) AND EXISTS (SELECT 1 FROM word w WHERE w.id = s.referent_word_id::bigint)"),
        new("text_relation",
            "SELECT x.id::text, f.slug AS from_text, t.slug AS to_text, x.relation, x.scope, x.note, x.source " +
            "FROM text_relation x JOIN text f ON f.id = x.from_text_id JOIN text t ON t.id = x.to_text_id ORDER BY x.id",
            "id, from_text_id, to_text_id, relation, scope, note, source",
            "SELECT s.id::int, f.id, t.id, s.relation, s.scope, s.note, s.source FROM s JOIN text f ON f.slug = s.from_text JOIN text t ON t.slug = s.to_text"),
        Plain("strong_entry", "id:int, strong_number, lemma, transliteration, pronunciation, definition, derivation, kjv_definition, morphology, " +
                              "detailed_definition, see_also, source_language, twot_reference"),
        Plain("strong_entry_translation", "id:int, strong_number, language, definition, derivation, kjv_definition, detailed_definition, method, " +
                                          "confidence:float8, source, note"),
        Plain("strong_relation", "id:int, from_number, to_number, kind, hedged:boolean, position:int, statement, source"),
        Plain("strong_profile", "strong_number, language, part_of_speech, gender, occurrences:int, first_book:int, first_chapter:int, first_verse:int, source",
            order: "strong_number, language"),
        Plain("lexicon_gloss", "id:int, entry, strong_number, lemma, gloss, source"),
        Plain("geez_lexicon_entry", "id:int, entry, headword, forms:text[], consonants:text[], latin:text[], greek:text[], source"),
        Plain("geez_lexicon_rendering", "entry, ordinal:int, sense, en, uk, de, es, model, run, made:date, source", order: "entry, ordinal"),
        Plain("geez_word_gloss", "word_id:bigint, lemma, lemma_source, sense_en, sense_uk, line_en, line_uk, greek_word_ids:bigint[], morphology, " +
                                 "confidence, model, run, made:date, source", order: "word_id"),
        Plain("commentary_entry", "id:int, commentary, source, book:int, first_chapter:int, first_verse:int, last_chapter:int, last_verse:int, sequence:int, body"),
    ];

    public static IReadOnlyList<string> Tables => [.. All.Select(c => c.Table).Distinct()];

    public static string Product(string table) => $"carried/{table}";

    /// <summary>A table with no reference to a renumbered row: every column as printed and cast back.</summary>
    private static Carried Plain(string table, string columns, string order = "id")
    {
        var parsed = columns.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Split(':') is [var name, var type] ? (Name: name, Type: type) : (Name: c, Type: "text")).ToList();
        return new(table,
            $"SELECT {string.Join(", ", parsed.Select(c => $"x.{c.Name}::text"))} FROM {table} x ORDER BY {order}",
            string.Join(", ", parsed.Select(c => c.Name)),
            $"SELECT {string.Join(", ", parsed.Select(c => c.Type == "text" ? $"s.{c.Name}" : $"s.{c.Name}::{c.Type}"))} FROM s");
    }

    /// <summary>
    /// Writes every carried table of the database the connection names as its product: the ones no producer makes from
    /// files (<see cref="TableProducts"/>), or every one with <paramref name="all"/>, for a comparison with what the files make.
    /// </summary>
    public static async Task<List<Outcome>> Export(string connectionString, ProductStore store, CancellationToken cancellationToken, bool all = false)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        var outcomes = new List<Outcome>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var carried in All.Where(c => all || !TableProducts.Tables.Contains(c.Table)))
        {
            var clock = Stopwatch.StartNew();
            await using var command = new NpgsqlCommand(carried.Export, connection) { CommandTimeout = 0 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var schema = new ProductSchema<string?[]>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var at = i;
                schema = schema.OptionalText(reader.GetName(i), row => row[at]);
            }

            schema = schema.Reads(row => row.ToTexts());
            using var writer = store.Create(Product(carried.Table), ProductKind.Machine, schema, ProductHeader.DescribeProducer("forge carried-export"), [],
                header => header.Notes.Add(new("from", $"the {carried.Table} table of {database}")));
            long rows = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new string?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetString(i);
                }

                writer.Write(row);
                rows++;
            }

            writer.Complete();
            outcomes.Add(new Outcome(carried.Table, rows, 0, 0, clock.Elapsed));
        }

        store.Save();
        return outcomes;
    }

    /// <summary>
    /// Loads every table there is a product of through a staging table each: the one made from files
    /// (<see cref="TableProducts"/>) where there is one, else the carried copy. <see cref="TablesAtOnce"/> load at once, the
    /// largest file first, each after the table it reads (<see cref="Carried.After"/>); the outcomes come in list order.
    /// </summary>
    public static async Task<List<Outcome>> Load(string connectionString, ProductStore store, IReadOnlySet<string>? only, CancellationToken cancellationToken)
    {
        var found = All.Where(c => only is null || only.Contains(c.Table))
            .Select(c => (Carried: c, Relative: store.Find(TableProducts.Product(c.Table)) ?? store.Find(Product(c.Table))))
            .Where(c => c.Relative is not null)
            .Select(c => (c.Carried, Path: store.FullPath(c.Relative!)))
            .ToList();
        var loaded = found.ToDictionary(c => c.Carried.Table, _ => new TaskCompletionSource<Outcome>(TaskCreationOptions.RunContinuationsAsynchronously),
            StringComparer.Ordinal);
        using var slots = new SemaphoreSlim(TablesAtOnce);

        async Task LoadAfter(Carried carried, string path)
        {
            try
            {
                if (carried.After is { } after && loaded.TryGetValue(after, out var first))
                {
                    await first.Task;
                }

                await slots.WaitAsync(cancellationToken);
                try
                {
                    loaded[carried.Table].SetResult(await LoadOne(connectionString, carried, path, cancellationToken));
                }
                finally
                {
                    slots.Release();
                }
            }
            catch (Exception exception)
            {
                loaded[carried.Table].TrySetException(exception);
            }
        }

        await Task.WhenAll(found.OrderByDescending(c => new FileInfo(c.Path).Length).Select(c => LoadAfter(c.Carried, c.Path)).ToList());
        var outcomes = new List<Outcome>(found.Count);
        foreach (var (carried, _) in found)
        {
            outcomes.Add(await loaded[carried.Table].Task);
        }

        await Execute(connectionString,
            string.Join("; ", All.Where(c => c.Columns.StartsWith("id,", StringComparison.Ordinal)).Select(c =>
                $"SELECT setval(pg_get_serial_sequence('{c.Table}', 'id'), greatest(coalesce((SELECT max(id) FROM {c.Table}), 0), 1))")),
            cancellationToken);
        return outcomes;
    }

    /// <summary>Tables loaded at once. Each copies into its staging table over up to <see cref="Streams"/> connections.</summary>
    private const int TablesAtOnce = 4;

    private const int Streams = 8;

    private static async Task<Outcome> LoadOne(string connectionString, Carried carried, string path, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var (stage, copied) = await Stage(connectionString, carried, path, cancellationToken);
        var loaded = await Execute(connectionString, Insert(carried, stage), cancellationToken);
        await Execute(connectionString, $"DROP TABLE {stage}", cancellationToken);
        return new Outcome(carried.Table, copied, loaded, copied - loaded, clock.Elapsed);
    }

    /// <summary>
    /// One table replaced from its product inside the caller's transaction (<c>build --apply</c>): the product staged over
    /// connections of its own first, then the table emptied and filled as a fresh build fills it, its identity set past its rows.
    /// </summary>
    public static async Task<Outcome> Replace(NpgsqlConnection connection, NpgsqlTransaction transaction, string connectionString, ProductStore store, string table,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var carried = All.Single(c => c.Table == table);
        var relative = store.Find(TableProducts.Product(table)) ?? store.Find(Product(table))
                       ?? throw new InvalidOperationException($"No tables/{table} or carried/{table} in this store to load {table} from.");
        var (stage, copied) = await Stage(connectionString, carried, store.FullPath(relative), cancellationToken);
        await ApplyReloads.Empty(connection, transaction, [table], cancellationToken);
        var loaded = await Execute(connection, transaction, Insert(carried, stage), cancellationToken);
        if (carried.Columns.StartsWith("id,", StringComparison.Ordinal))
        {
            await Execute(connection, transaction,
                $"SELECT setval(pg_get_serial_sequence('{table}', 'id'), greatest(coalesce((SELECT max(id) FROM {table}), 0), 1))", cancellationToken);
        }

        // The stage is read in the transaction, so it is dropped there too: a drop from another connection would wait for the commit.
        await Execute(connection, transaction, $"DROP TABLE {stage}", cancellationToken);
        return new Outcome(table, copied, loaded, copied - loaded, clock.Elapsed);
    }

    private static async Task<long> Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(string Stage, long Rows)> Stage(string connectionString, Carried carried, string path, CancellationToken cancellationToken)
    {
        string[] names;
        using (var file = Products.Essx.EssxReader.Open(path))
        {
            names = [.. file.Tables[0].Columns.Select(c => c.Name)];
        }

        var stage = $"carried_stage_{carried.Table}";
        await Execute(connectionString, $"DROP TABLE IF EXISTS {stage}; CREATE UNLOGGED TABLE {stage} ({string.Join(", ", names.Select(n => $"\"{n}\" text"))})",
            cancellationToken);
        var copied = await EssxCopy.Load(connectionString, path, stage, null, Streams, CopyMode.Text, cancellation: cancellationToken);
        return (stage, copied.Rows);
    }

    private static string Insert(Carried carried, string stage) =>
        $"INSERT INTO {carried.Table} ({carried.Columns}) {carried.Load.Replace(" FROM s", $" FROM {stage} s", StringComparison.Ordinal)}";

    private static async Task<long> Execute(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
