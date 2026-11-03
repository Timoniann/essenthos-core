using System.Text;
using System.Text.Json;
using Essenthos.Core.Products;

namespace Essenthos.Core.Conversion;

/// <summary>
/// A text's converted words in a shape other tools read: TSV (the product's columns, no header comments),
/// CSV (RFC 4180) or JSON Lines (one object a word, nulls as null, morphology as an object).
/// </summary>
internal static class TextExport
{
    public static long Write(ProductStore store, string slug, string shape, string target)
    {
        using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
        var columns = reader.Columns;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        using var output = new StreamWriter(target, false, new UTF8Encoding(false)) { NewLine = "\n" };
        long rows = 0;
        var sink = new FieldSink();
        var schema = TextWord.Schema(slug);

        switch (shape)
        {
            case "tsv":
                output.WriteLine(string.Join('\t', columns.Select(c => c.Name)));
                break;
            case "csv":
                output.WriteLine(string.Join(',', columns.Select(c => Csv(c.Name))));
                break;
            case "jsonl":
                break;
            default:
                throw new ArgumentException($"\"{shape}\" is not a shape the export writes: tsv, csv or jsonl.");
        }

        foreach (var word in reader.Rows())
        {
            sink.Clear();
            schema.Write(word, sink);
            switch (shape)
            {
                case "tsv":
                    output.WriteLine(string.Join('\t', sink.Fields.Select(f => f is null ? "\\N" : Products.Tsv.TsvEscaping.Escape(f))));
                    break;
                case "csv":
                    output.WriteLine(string.Join(',', sink.Fields.Select(f => f is null ? string.Empty : Csv(f))));
                    break;
                default:
                    output.WriteLine(Json(columns, sink.Fields));
                    break;
            }

            rows++;
        }

        return rows;
    }

    private static string Csv(string field) =>
        field.AsSpan().IndexOfAny("\",\r\n") >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;

    private static string Json(IReadOnlyList<Column> columns, IReadOnlyList<string?> fields)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            for (var i = 0; i < columns.Count; i++)
            {
                var name = columns[i].Name;
                var value = fields[i];
                if (value is null)
                {
                    json.WriteNull(name);
                    continue;
                }

                switch (columns[i].Type)
                {
                    case ColumnType.Integer:
                        json.WriteNumber(name, long.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case ColumnType.Boolean:
                        json.WriteBoolean(name, value == "t");
                        break;
                    case ColumnType.Json:
                        json.WritePropertyName(name);
                        using (var document = JsonDocument.Parse(value))
                        {
                            document.RootElement.WriteTo(json);
                        }

                        break;
                    default:
                        json.WriteString(name, value);
                        break;
                }
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The fields of one row as the schema writes them, booleans as t and f.</summary>
    private sealed class FieldSink : IRowSink
    {
        public List<string?> Fields { get; } = [];

        public void Clear() => Fields.Clear();

        public void Text(string? value) => Fields.Add(value);

        public void Integer(long? value) => Fields.Add(value?.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void Boolean(bool? value) => Fields.Add(value is null ? null : value.Value ? "t" : "f");

        public void Json(string? value) => Fields.Add(value);

        public void VerseKey(VerseKey? value) => Fields.Add(value?.ToString());

        public void WordKey(WordKey? value) => Fields.Add(value?.Own);
    }
}
