using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Essenthos.Core.Products.Tsv;

namespace Essenthos.Core.Products.Tools;

/// <summary>A table as text knows it: names, maybe a header, rows of strings with nulls.</summary>
public interface ITextTable : IDisposable
{
    ProductHeader? Header { get; }

    IReadOnlyList<string> Names { get; }

    /// <summary>The declared types where the source has them (a product file), else null.</summary>
    IReadOnlyList<Column>? Columns { get; }

    IEnumerable<string?[]> Rows();
}

/// <summary>
/// The shapes a product converts to and from, by extension: <c>.esw</c>, <c>.tsv</c>, <c>.csv</c>,
/// <c>.jsonl</c>, <c>.xml</c>. Null is <c>\N</c> in TSV, an empty unquoted field in CSV (as
/// <c>COPY ... CSV</c> has it, a quoted empty string being the empty string), JSON <c>null</c>,
/// and an absent element in XML.
/// </summary>
public static class TextTables
{
    /// <summary>The index of a name, or -1.</summary>
    public static int IndexOf(this IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] == name) return i;
        }

        return -1;
    }

    public static readonly string[] Extensions = [Esw.EswLayout.Extension, ".tsv", ".csv", ".jsonl", ".xml"];

    public static string Kind(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return Extensions.Contains(extension)
            ? extension
            : throw new ArgumentException($"'{path}': a product is .esw, .tsv, .csv, .jsonl or .xml.", nameof(path));
    }

    public static ITextTable Open(string path, string? table = null) => Kind(path) switch
    {
        var esw when esw == Esw.EswLayout.Extension => new EswText(path, table),
        ".tsv" => new TsvText(path),
        ".csv" => new CsvText(path),
        ".jsonl" => new JsonlText(path),
        _ => new XmlText(path),
    };

    /// <summary>A schema over string rows, for writing rows read as text into any format.</summary>
    public static ProductSchema<string?[]> Schema(IReadOnlyList<Column> columns)
    {
        var schema = new ProductSchema<string?[]>();
        for (var i = 0; i < columns.Count; i++)
        {
            var index = i;
            var column = columns[i];
            switch (column.Type)
            {
                case ColumnType.Text:
                    schema.OptionalText(column.Name, row => row[index]);
                    break;
                case ColumnType.Json:
                    schema.OptionalJson(column.Name, row => row[index]);
                    break;
                case ColumnType.Integer:
                    schema.OptionalInteger(column.Name, row => row[index] is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : null);
                    break;
                case ColumnType.Boolean:
                    schema.OptionalBoolean(column.Name, row => row[index] switch { null => null, "t" => true, "f" => false, var other => throw new ProductFormatException($"Column '{column.Name}' holds '{other}', not t or f.") });
                    break;
                case ColumnType.VerseKey:
                    schema.OptionalVerseKey(column.Name, row => row[index] is { } text ? VerseKey.Parse(text) : null);
                    break;
                case ColumnType.WordKey:
                    schema.OptionalOwnWordKey(column.Name, row => row[index] is { } text ? WordKey.Parse(text, "X") : null);
                    break;
            }
        }

        return schema.Reads(row => row.ToTexts());
    }

    /// <summary>
    /// What type each column of a text table has, from its values: a word key when every value is
    /// one, else a verse key, an integer, a boolean (<c>t</c>/<c>f</c>), JSON (every value an object
    /// or an array), else text. Nullable when any value is null. A caller's own types win.
    /// </summary>
    public static IReadOnlyList<Column> Infer(IReadOnlyList<string> names, IEnumerable<string?[]> rows, IReadOnlyDictionary<string, ColumnType>? declared = null)
    {
        var candidates = new bool[names.Count, 5];
        var nullable = new bool[names.Count];
        var seen = new bool[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            for (var t = 0; t < 5; t++) candidates[i, t] = true;
        }

        foreach (var row in rows)
        {
            for (var i = 0; i < names.Count; i++)
            {
                var value = row[i];
                if (value is null)
                {
                    nullable[i] = true;
                    continue;
                }

                seen[i] = true;
                if (candidates[i, 0]) candidates[i, 0] = WordKey.TryParseOwn(value, "X", out _);
                if (candidates[i, 1]) candidates[i, 1] = VerseKey.TryParse(value, out _);
                if (candidates[i, 2]) candidates[i, 2] = IsInteger(value);
                if (candidates[i, 3]) candidates[i, 3] = value is "t" or "f";
                if (candidates[i, 4]) candidates[i, 4] = value.Length >= 2 && (value[0] == '{' && value[^1] == '}' || value[0] == '[' && value[^1] == ']');
            }
        }

        var columns = new List<Column>();
        for (var i = 0; i < names.Count; i++)
        {
            ColumnType type;
            if (declared is not null && declared.TryGetValue(names[i], out var given))
            {
                type = given;
            }
            else if (!seen[i])
            {
                type = ColumnType.Text;
            }
            else if (candidates[i, 0]) type = ColumnType.WordKey;
            else if (candidates[i, 1]) type = ColumnType.VerseKey;
            else if (candidates[i, 2]) type = ColumnType.Integer;
            else if (candidates[i, 3]) type = ColumnType.Boolean;
            else if (candidates[i, 4]) type = ColumnType.Json;
            else type = ColumnType.Text;
            columns.Add(new Column(names[i], type, nullable[i]));
        }

        return columns;
    }

    private static bool IsInteger(string value)
    {
        if (value.Length == 0 || value.Length > 1 && value[0] == '0' || value.StartsWith("-0") && value.Length > 2)
        {
            return false;
        }

        return long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    }

    /// <summary><c>own:wordkey,count:integer</c> as a caller types it.</summary>
    public static IReadOnlyDictionary<string, ColumnType> ParseTypes(string? spec)
    {
        var types = new Dictionary<string, ColumnType>(StringComparer.Ordinal);
        foreach (var part in (spec ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0 || !Enum.TryParse<ColumnType>(part[(colon + 1)..], ignoreCase: true, out var type))
            {
                throw new ArgumentException($"'{part}': a type is name:text|integer|boolean|json|versekey|wordkey.", nameof(spec));
            }

            types[part[..colon]] = type;
        }

        return types;
    }

    private sealed class EswText : ITextTable
    {
        private readonly Esw.EswReader _reader;
        private readonly Esw.EswTable _table;

        public EswText(string path, string? table)
        {
            _reader = Esw.EswReader.Open(path);
            _table = table is null ? _reader.Tables[0] : _reader.Table(table);
        }

        public ProductHeader? Header => _reader.Header;
        public IReadOnlyList<string> Names => [.. _table.Columns.Select(column => column.Name)];
        public IReadOnlyList<Column>? Columns => _table.Columns;
        public IEnumerable<string?[]> Rows() => _table.Rows().Select(row => row.ToTexts());
        public void Dispose() => _reader.Dispose();
    }

    private sealed class TsvText(string path) : ITextTable
    {
        private readonly TsvProductReader<string?[]> _reader = TsvProductReader<string?[]>.OpenTexts(path);

        public ProductHeader? Header => _reader.Header;
        public IReadOnlyList<string> Names => _reader.Names;
        public IReadOnlyList<Column>? Columns => null;
        public IEnumerable<string?[]> Rows() => _reader.Rows();
        public void Dispose() => _reader.Dispose();
    }

    private sealed class CsvText : ITextTable
    {
        private readonly StreamReader _reader;

        public CsvText(string path)
        {
            _reader = new StreamReader(path, Utf8.NoBom, detectEncodingFromByteOrderMarks: true, 1 << 16);
            var first = ReadRecord() ?? throw new ProductFormatException($"{path}: no header line.");
            Names = [.. first.Select(name => name ?? string.Empty)];
        }

        public ProductHeader? Header => null;
        public IReadOnlyList<string> Names { get; }
        public IReadOnlyList<Column>? Columns => null;

        public IEnumerable<string?[]> Rows()
        {
            while (ReadRecord() is { } record)
            {
                if (record.Length != Names.Count)
                {
                    throw new ProductFormatException($"A CSV record has {record.Length} fields where the header has {Names.Count}.");
                }

                yield return record;
            }
        }

        /// <summary>RFC 4180: quoted fields may hold commas, quotes doubled, and line breaks.</summary>
        private string?[]? ReadRecord()
        {
            if (_reader.Peek() < 0)
            {
                return null;
            }

            var fields = new List<string?>();
            var field = new StringBuilder();
            var quoted = false;
            var wasQuoted = false;
            while (true)
            {
                var c = _reader.Read();
                if (c < 0)
                {
                    fields.Add(wasQuoted ? field.ToString() : field.Length == 0 ? null : field.ToString());
                    return [.. fields];
                }

                var ch = (char)c;
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (_reader.Peek() == '"')
                        {
                            _reader.Read();
                            field.Append('"');
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(ch);
                    }

                    continue;
                }

                switch (ch)
                {
                    case '"' when field.Length == 0 && !wasQuoted:
                        quoted = true;
                        wasQuoted = true;
                        break;
                    case ',':
                        fields.Add(wasQuoted ? field.ToString() : field.Length == 0 ? null : field.ToString());
                        field.Clear();
                        wasQuoted = false;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        fields.Add(wasQuoted ? field.ToString() : field.Length == 0 ? null : field.ToString());
                        return [.. fields];
                    default:
                        field.Append(ch);
                        break;
                }
            }
        }

        public void Dispose() => _reader.Dispose();
    }

    private sealed class JsonlText : ITextTable
    {
        private readonly string _path;

        public JsonlText(string path)
        {
            _path = path;
            using var reader = new StreamReader(path, Utf8.NoBom);
            var first = reader.ReadLine() ?? throw new ProductFormatException($"{path}: empty.");
            using var document = JsonDocument.Parse(first);
            Names = [.. document.RootElement.EnumerateObject().Select(property => property.Name)];
        }

        public ProductHeader? Header => null;
        public IReadOnlyList<string> Names { get; }
        public IReadOnlyList<Column>? Columns => null;

        public IEnumerable<string?[]> Rows()
        {
            using var reader = new StreamReader(_path, Utf8.NoBom);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                using var document = JsonDocument.Parse(line);
                var row = new string?[Names.Count];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = document.RootElement.TryGetProperty(Names[i], out var value) ? Text(value) : null;
                }

                yield return row;
            }
        }

        private static string? Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "t",
            JsonValueKind.False => "f",
            JsonValueKind.Number => value.GetRawText(),
            _ => value.GetRawText(),
        };

        public void Dispose()
        {
        }
    }

    private sealed class XmlText : ITextTable
    {
        private readonly string _path;

        public XmlText(string path)
        {
            _path = path;
            using var reader = XmlReader.Create(path);
            var names = new List<string>();
            if (reader.ReadToFollowing("row"))
            {
                var depth = reader.Depth;
                while (reader.Read() && reader.Depth > depth)
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == depth + 1)
                    {
                        names.Add(reader.LocalName);
                    }
                }
            }

            Names = names;
        }

        public ProductHeader? Header => null;
        public IReadOnlyList<string> Names { get; }
        public IReadOnlyList<Column>? Columns => null;

        public IEnumerable<string?[]> Rows()
        {
            using var reader = XmlReader.Create(_path);
            while (reader.ReadToFollowing("row"))
            {
                var row = new string?[Names.Count];
                using var subtree = reader.ReadSubtree();
                var moved = subtree.Read() && subtree.Read();
                while (moved && subtree.NodeType != XmlNodeType.None)
                {
                    if (subtree.NodeType != XmlNodeType.Element)
                    {
                        moved = subtree.Read();
                        continue;
                    }

                    var index = Names.IndexOf(subtree.LocalName);
                    if (subtree.IsEmptyElement)
                    {
                        if (index >= 0) row[index] = string.Empty;
                        moved = subtree.Read();
                    }
                    else
                    {
                        // Leaves the reader on the node after the end tag, so no Read follows.
                        var text = subtree.ReadElementContentAsString();
                        if (index >= 0) row[index] = text;
                    }
                }

                yield return row;
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Writes rows as CSV, <c>COPY ... CSV</c> style: null empty, empty string quoted.</summary>
    public static void WriteCsv(TextWriter writer, IReadOnlyList<string> names, IEnumerable<string?[]> rows)
    {
        writer.Write(string.Join(',', names.Select(CsvField)));
        writer.Write('\n');
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                if (i > 0) writer.Write(',');
                if (row[i] is { } value) writer.Write(value.Length == 0 ? "\"\"" : CsvField(value));
            }

            writer.Write('\n');
        }
    }

    private static string CsvField(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";

    /// <summary>One JSON object per line; integers and booleans as JSON numbers and booleans where the types are known.</summary>
    public static void WriteJsonl(TextWriter writer, IReadOnlyList<Column> columns, IEnumerable<string?[]> rows)
    {
        var options = new JsonWriterOptions { Indented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using var buffer = new MemoryStream();
        foreach (var row in rows)
        {
            buffer.SetLength(0);
            using (var json = new Utf8JsonWriter(buffer, options))
            {
                json.WriteStartObject();
                for (var i = 0; i < columns.Count; i++)
                {
                    json.WritePropertyName(columns[i].Name);
                    var value = row[i];
                    if (value is null) json.WriteNullValue();
                    else if (columns[i].Type == ColumnType.Integer) json.WriteNumberValue(long.Parse(value, CultureInfo.InvariantCulture));
                    else if (columns[i].Type == ColumnType.Boolean) json.WriteBooleanValue(value == "t");
                    else if (columns[i].Type == ColumnType.Json) json.WriteRawValue(value, skipInputValidation: false);
                    else json.WriteStringValue(value);
                }

                json.WriteEndObject();
            }

            writer.Write(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
            writer.Write('\n');
        }
    }

    /// <summary><c>&lt;product name="…"&gt;&lt;row&gt;&lt;own&gt;…&lt;/own&gt;…&lt;/row&gt;…&lt;/product&gt;</c>; a null value has no element.</summary>
    public static void WriteXml(TextWriter writer, ProductHeader? header, IReadOnlyList<Column> columns, IEnumerable<string?[]> rows)
    {
        using var xml = XmlWriter.Create(writer, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false, NewLineChars = "\n", Encoding = Utf8.NoBom });
        xml.WriteStartElement("product");
        if (header is not null)
        {
            xml.WriteAttributeString("name", header.Product);
            xml.WriteAttributeString("producer", header.Producer);
            if (header.Rows is { } count) xml.WriteAttributeString("rows", count.ToString(CultureInfo.InvariantCulture));
            foreach (var input in header.Inputs)
            {
                xml.WriteStartElement("input");
                xml.WriteAttributeString("path", input.Path);
                xml.WriteAttributeString("sha256", input.Sha256);
                xml.WriteEndElement();
            }
        }

        xml.WriteStartElement("columns");
        foreach (var column in columns)
        {
            xml.WriteStartElement("column");
            xml.WriteAttributeString("name", column.Name);
            xml.WriteAttributeString("type", column.Type.ToString().ToLowerInvariant());
            if (column.Nullable) xml.WriteAttributeString("nullable", "true");
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        foreach (var row in rows)
        {
            xml.WriteStartElement("row");
            for (var i = 0; i < columns.Count; i++)
            {
                if (row[i] is { } value)
                {
                    xml.WriteElementString(columns[i].Name, value);
                }
            }

            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }
}
