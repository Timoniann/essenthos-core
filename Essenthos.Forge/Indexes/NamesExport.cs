using Essenthos.Core.Comparison;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Npgsql;

namespace Essenthos.Core.Indexes;

/// <summary>
/// The names a database holds, one product per text (<c>names/exported/&lt;SLUG&gt;</c>, <see cref="ExportedName"/>): what
/// the spellings index reads until the names layer (P6, DOC-0224 §7) writes names from files. Reads the database only;
/// the columns are the ones <c>EntityRenderingLoader</c> reads an annotation by, computed by the same SQL.
/// </summary>
internal static class NamesExport
{
    private static readonly string Named =
        $"""
         SELECT a.word_id, e.slug,
                CASE WHEN a.note LIKE @guessed THEN a.confidence END AS guess,
                e.kind IN ('object', 'observance', 'title') AS thing,
                CASE WHEN a.note LIKE @guessed THEN a.confidence >= @sure
                     ELSE a.method IN ('manual', 'stated-by-source') OR a.confidence IS NULL OR a.confidence >= @sure
                END AS firm,
                NOT {Annotating.NeverAName} AND NOT {Pronouns.IsWord()} AS spellable
         FROM word_entity a
         JOIN entity e ON e.id = a.entity_id
         JOIN word hw ON hw.id = a.word_id
         JOIN text ht ON ht.id = hw.text_id
         """;

    public sealed record Outcome(int Texts, long Names, TimeSpan Elapsed);

    public static async Task<Outcome> Run(string connectionString, ProductStore store, string source, CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var keys = await WordKeys.Load(connection, reverse: false, cancellationToken);

        var byText = new Dictionary<string, List<ExportedName>>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(Named, connection))
        {
            command.Parameters.AddWithValue("guessed", $"{Annotating.CarriedNote}, linked by {EnumSpelling.Of(LinkMethod.Aligner)}");
            command.Parameters.AddWithValue("sure", SearchSpellings.HighConfidence);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var word = reader.GetInt64(0);
                var text = keys.Slug(keys.TextOf(word));
                if (!byText.TryGetValue(text, out var names))
                {
                    byText[text] = names = [];
                }

                var key = keys.Key(word);
                names.Add(new ExportedName(
                    new WordKey(text, key.Verse, key.Position),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetDouble(2),
                    reader.GetBoolean(3),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5)));
            }
        }

        long rows = 0;
        foreach (var (text, names) in byText.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            using var writer = store.Create(IndexProducts.ExportedNames(text), ProductKind.Machine, ExportedName.Schema(text),
                ProductHeader.DescribeProducer("forge names-export"), [],
                header => header.Notes.Add(new("exported", $"from {source}: a stand-in until the names layer writes names from files")));
            foreach (var name in names.OrderBy(n => n.Own).ThenBy(n => n.Record, StringComparer.Ordinal))
            {
                writer.Write(name);
            }

            writer.Complete();
            rows += writer.Rows;
        }

        store.Save();
        return new Outcome(byText.Count, rows, clock.Elapsed);
    }
}
