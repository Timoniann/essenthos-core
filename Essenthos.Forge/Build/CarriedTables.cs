using System.Diagnostics;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Postgres;
using Npgsql;

namespace Essenthos.Core.Build;

/// <summary>
/// The served tables loaded whole from a product: made from files (<see cref="TableProducts"/>, <c>tables/&lt;table&gt;</c>), or,
/// for the ones no producer makes yet, carried from a database as <c>carried/&lt;table&gt;</c> so that a build
/// still reads files only: every column as Postgres prints it, a text by its slug, a word by its id (word ids are the products' own). Loaded through a staging table, each row put back on the text,
/// verse and word the new database holds there; a row whose word or verse the products no longer hold is left out and
/// counted. Each table leaves this list when the package that owns it writes it as a product.
/// </summary>
internal static class CarriedTables
{
    public sealed record Carried(string Table, string Export, string Columns, string Load);

    public sealed record Outcome(string Table, long Exported, long Loaded, long LeftOut, TimeSpan Elapsed);

    private const string Kept = "('supplied', 'restored', 'doubtful', 'subscription')";

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
            "AND EXISTS (SELECT 1 FROM word_group g WHERE g.id = s.word_group_id::bigint)"),
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
    /// Loads every table there is a product of, in list order, through a staging table each: the one made from files
    /// (<see cref="TableProducts"/>) where there is one, else the carried copy.
    /// </summary>
    public static async Task<List<Outcome>> Load(string connectionString, ProductStore store, IReadOnlySet<string>? only, CancellationToken cancellationToken)
    {
        var outcomes = new List<Outcome>();
        foreach (var carried in All.Where(c => only is null || only.Contains(c.Table)))
        {
            if ((store.Find(TableProducts.Product(carried.Table)) ?? store.Find(Product(carried.Table))) is not { } relative)
            {
                continue;
            }

            var clock = Stopwatch.StartNew();
            var path = store.FullPath(relative);
            string[] names;
            using (var file = Products.Essx.EssxReader.Open(path))
            {
                names = [.. file.Tables[0].Columns.Select(c => c.Name)];
            }

            var stage = $"carried_stage_{carried.Table}";
            await Execute(connectionString, $"DROP TABLE IF EXISTS {stage}; CREATE UNLOGGED TABLE {stage} ({string.Join(", ", names.Select(n => $"\"{n}\" text"))})",
                cancellationToken);
            var copied = await EssxCopy.Load(connectionString, path, stage, null, 8, CopyMode.Text, cancellation: cancellationToken);
            var loaded = await Execute(connectionString,
                $"INSERT INTO {carried.Table} ({carried.Columns}) {carried.Load.Replace(" FROM s", $" FROM {stage} s", StringComparison.Ordinal)}", cancellationToken);
            await Execute(connectionString, $"DROP TABLE {stage}", cancellationToken);
            outcomes.Add(new Outcome(carried.Table, copied.Rows, loaded, copied.Rows - loaded, clock.Elapsed));
        }

        await Execute(connectionString,
            string.Join("; ", All.Where(c => c.Columns.StartsWith("id,", StringComparison.Ordinal)).Select(c =>
                $"SELECT setval(pg_get_serial_sequence('{c.Table}', 'id'), greatest(coalesce((SELECT max(id) FROM {c.Table}), 0), 1))")),
            cancellationToken);
        return outcomes;
    }

    private static async Task<long> Execute(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
