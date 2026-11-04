using System.Globalization;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Tsv;

namespace Essenthos.Core.Products.Tools;

public sealed record ConversionOptions
{
    /// <summary>Types a caller states, which win over the inferred ones: <c>own:wordkey,count:integer</c>.</summary>
    public IReadOnlyDictionary<string, ColumnType>? Types { get; init; }

    /// <summary>Which table of a file of several; the first when null.</summary>
    public string? Table { get; init; }

    public EssxOptions Essx { get; init; } = EssxOptions.Default;

    /// <summary>The header to write when the source has none, as a hand-written CSV has none.</summary>
    public ProductHeader? Header { get; init; }

    /// <summary>How many rows of a text source its types are inferred from before writing starts.</summary>
    public int InferFrom { get; init; } = 1 << 16;
}

/// <summary>
/// Converts a product between its shapes. A binary source is copied typed: its decoded columns go to the
/// writer as numbers, shared strings and key parts, never through text. A text source is read once: its
/// types are inferred from the first <see cref="ConversionOptions.InferFrom"/> rows and every later value
/// is parsed as it is written; if a later value contradicts the inference (a text in a column of
/// integers), the conversion starts again with the types inferred from the whole file. The TSV and the
/// binary file of the same rows read back the same.
/// </summary>
public static class ProductConversion
{
    public static ConversionResult Convert(string from, string to, ConversionOptions? options = null)
    {
        options ??= new ConversionOptions();
        var fromKind = TextTables.Kind(from);
        var toKind = TextTables.Kind(to);
        var toProduct = toKind == EssxLayout.Extension || toKind == ".tsv";
        if (fromKind == EssxLayout.Extension && options.Types is null && toProduct)
        {
            return Typed(from, to, toKind, options);
        }

        if (fromKind == ".tsv" && toKind == EssxLayout.Extension)
        {
            return TsvToEssx(from, to, options);
        }

        using var source = TextTables.Open(from, options.Table);
        var header = options.Header ?? source.Header ?? new ProductHeader(Path.GetFileName(to).Replace(' ', '_'), [], $"essx convert {Path.GetFileName(from)}");
        header.Rows = null;
        if (source.Columns is { } typed && options.Types is null)
        {
            return Write(to, toKind, header, typed, source.Rows(), options);
        }

        var declared = options.Types ?? source.Columns?.ToDictionary(column => column.Name, column => column.Type);
        using var rows = source.Rows().GetEnumerator();
        var prefix = new List<string?[]>();
        while (prefix.Count < options.InferFrom && rows.MoveNext())
        {
            prefix.Add(rows.Current);
        }

        var columns = TextTables.Infer(source.Names, prefix, declared);
        if (prefix.Count < options.InferFrom || source.Names.All(name => declared?.ContainsKey(name) == true))
        {
            return Write(to, toKind, header, columns, Rest(prefix, rows), options);
        }

        var written = 0L;
        try
        {
            return Write(to, toKind, header, columns, Counted(Rest(prefix, rows), () => written++), options);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException && written > prefix.Count)
        {
            // A value past the prefix contradicts its type: infer from every row and write again.
        }

        return Whole(from, to, toKind, header, declared, options);
    }

    /// <summary>The types from every row of a text source, then the rows again: two passes, for a source whose later rows contradict its first ones.</summary>
    private static ConversionResult Whole(string from, string to, string toKind, ProductHeader header, IReadOnlyDictionary<string, ColumnType>? declared, ConversionOptions options)
    {
        IReadOnlyList<Column> whole;
        using (var again = TextTables.Open(from, options.Table))
        {
            whole = TextTables.Infer(again.Names, again.Rows(), declared);
        }

        using var rows = TextTables.Open(from, options.Table);
        return Write(to, toKind, header, whole, rows.Rows(), options);
    }

    /// <summary>
    /// A TSV into ESSX in one pass: the prefix is split into strings to infer the types, every line after it
    /// is cut into spans and each field goes to its column's encoder as a number, a key or a span, so a
    /// repeated string or a number costs no string of its own.
    /// </summary>
    private static ConversionResult TsvToEssx(string from, string to, ConversionOptions options)
    {
        using var text = new StreamReader(new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan),
            Utf8.NoBom, detectEncodingFromByteOrderMarks: false, 1 << 16);
        var comments = new List<string>();
        var number = 0;
        string? line;
        while ((line = text.ReadLine()) is not null)
        {
            number++;
            if (!line.StartsWith('#'))
            {
                break;
            }

            comments.Add(line);
        }

        if (line is null)
        {
            throw new ProductFormatException($"{from}: no column line after the header.");
        }

        var names = line.Split('\t');
        var header = options.Header ?? (comments.Count == 0 ? null : ProductHeader.Parse(comments))
                     ?? new ProductHeader(Path.GetFileName(to).Replace(' ', '_'), [], $"essx convert {Path.GetFileName(from)}");
        header.Rows = null;
        var prefix = new List<string>();
        var firstNumber = number + 1;
        while (prefix.Count < options.InferFrom && (line = text.ReadLine()) is not null)
        {
            number++;
            prefix.Add(line);
        }

        var columns = TextTables.Infer(names, Split(prefix, names.Length, from, firstNumber), options.Types);
        var types = columns.Select(column => column.Type).ToArray();
        try
        {
            using var file = EssxWriter.Create(to, header, options.Essx);
            var table = file.Table(EssxFormat.TableName, TextTables.Schema(columns));
            var encoders = table.Encoders;
            for (var i = 0; i < prefix.Count; i++)
            {
                WriteLine(prefix[i], types, encoders, from, firstNumber + i);
                table.EndRow();
            }

            while ((line = text.ReadLine()) is not null)
            {
                number++;
                WriteLine(line, types, encoders, from, number);
                table.EndRow();
            }

            file.Complete();
            return new ConversionResult(to, columns, table.Rows);
        }
        catch (Contradiction contradiction)
        {
            if (names.All(name => options.Types?.ContainsKey(name) == true))
            {
                throw new ProductFormatException(contradiction.Message);
            }
        }

        return Whole(from, to, EssxLayout.Extension, header, options.Types, options);
    }

    /// <summary>The prefix's lines as fields, in one array reused from line to line, for the inference to read.</summary>
    private static IEnumerable<string?[]> Split(List<string> lines, int columns, string path, int firstNumber)
    {
        var fields = new string?[columns];
        for (var i = 0; i < lines.Count; i++)
        {
            if (!TsvEscaping.SplitLine(lines[i], fields))
            {
                throw FieldCount(path, firstNumber + i, columns);
            }

            yield return fields;
        }
    }

    private static void WriteLine(ReadOnlySpan<char> line, ColumnType[] types, IReadOnlyList<ColumnEncoder> encoders, string path, int number)
    {
        for (var c = 0; c < types.Length; c++)
        {
            var tab = line.IndexOf('\t');
            ReadOnlySpan<char> field;
            if (c == types.Length - 1)
            {
                if (tab >= 0) throw FieldCount(path, number, types.Length);
                field = line;
            }
            else
            {
                if (tab < 0) throw FieldCount(path, number, types.Length);
                field = line[..tab];
                line = line[(tab + 1)..];
            }

            var encoder = encoders[c];
            var type = types[c];
            if (field is @"\N")
            {
                switch (type)
                {
                    case ColumnType.Integer: encoder.Integer(null); break;
                    case ColumnType.Boolean: encoder.Boolean(null); break;
                    case ColumnType.VerseKey: encoder.VerseKey(null); break;
                    case ColumnType.WordKey: encoder.WordKey(null); break;
                    default: encoder.Text((string?)null); break;
                }

                continue;
            }

            var value = field.Contains('\\') ? TsvEscaping.Unescape(field).AsSpan() : field;
            switch (type)
            {
                case ColumnType.Text or ColumnType.Json:
                    encoder.Text(value);
                    break;
                case ColumnType.Integer:
                    encoder.Integer(long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : throw Contradicted(path, number, c, value, type));
                    break;
                case ColumnType.Boolean:
                    encoder.Boolean(value is "t" ? true : value is "f" ? false : throw Contradicted(path, number, c, value, type));
                    break;
                case ColumnType.VerseKey:
                    encoder.VerseKey(VerseKey.TryParse(value, out var verse) ? verse : throw Contradicted(path, number, c, value, type));
                    break;
                case ColumnType.WordKey:
                    encoder.WordKey(WordKey.TryParseOwn(value, "X", out var word) ? word : throw Contradicted(path, number, c, value, type));
                    break;
            }
        }
    }

    private static ProductFormatException FieldCount(string path, int number, int columns) =>
        new($"{path} line {number}: the field count is not the {columns} columns declared.");

    private static Contradiction Contradicted(string path, int number, int column, ReadOnlySpan<char> value, ColumnType type) =>
        new($"{path} line {number}: column {column + 1} holds '{value}', not {type.ToString().ToLowerInvariant()}; state its type with --types.");

    /// <summary>A value past the prefix that is not the type the prefix gave its column.</summary>
    private sealed class Contradiction(string message) : Exception(message);

    /// <summary>A binary source into a product file, column by column as decoded.</summary>
    private static ConversionResult Typed(string from, string to, string toKind, ConversionOptions options)
    {
        using var reader = EssxReader.Open(from);
        var table = options.Table is null ? reader.Tables[0] : reader.Table(options.Table);
        var header = options.Header ?? reader.Header;
        header.Rows = null;
        IProductFormat format = toKind == EssxLayout.Extension ? new EssxFormat(options.Essx) : TsvFormat.Instance;
        using var writer = format.Create(to, header, EssxTypedRows.Schema(table.Columns));
        foreach (var row in table.Rows())
        {
            writer.Write(row);
        }

        writer.Complete();
        return new ConversionResult(to, table.Columns, writer.Rows);
    }

    private static ConversionResult Write(string to, string toKind, ProductHeader header, IReadOnlyList<Column> columns, IEnumerable<string?[]> rows, ConversionOptions options)
    {
        if (toKind == EssxLayout.Extension || toKind == ".tsv")
        {
            IProductFormat format = toKind == EssxLayout.Extension ? new EssxFormat(options.Essx) : TsvFormat.Instance;
            using var writer = format.Create(to, header, TextTables.Schema(columns));
            writer.WriteAll(rows);
            return new ConversionResult(to, columns, writer.Rows);
        }

        using var file = AtomicFile.Create(to);
        var counted = 0L;
        var counting = Counted(rows, () => counted++);
        using (var text = new StreamWriter(file.Stream, Utf8.NoBom, 1 << 16, leaveOpen: true) { NewLine = "\n" })
        {
            if (toKind == ".csv") TextTables.WriteCsv(text, [.. columns.Select(column => column.Name)], counting);
            else if (toKind == ".jsonl") TextTables.WriteJsonl(text, columns, counting);
            else TextTables.WriteXml(text, header, columns, counting);
        }

        file.Commit();
        return new ConversionResult(to, columns, counted);
    }

    private static IEnumerable<string?[]> Rest(List<string?[]> prefix, IEnumerator<string?[]> rows)
    {
        foreach (var row in prefix)
        {
            yield return row;
        }

        while (rows.MoveNext())
        {
            yield return rows.Current;
        }
    }

    private static IEnumerable<T> Counted<T>(IEnumerable<T> rows, Action count)
    {
        foreach (var row in rows)
        {
            count();
            yield return row;
        }
    }
}

public sealed record ConversionResult(string Path, IReadOnlyList<Column> Columns, long Rows);

/// <summary>
/// A schema over the rows of a decoded ESSX table that hands each value to the writer as it is typed:
/// integers as numbers, strings as the dictionary's shared instances, keys as their parts when the
/// writer takes parts (another ESSX file) and as keys otherwise (a TSV writes them once, as text).
/// </summary>
internal static class EssxTypedRows
{
    public static ProductSchema<EssxRow> Schema(IReadOnlyList<Column> columns)
    {
        var schema = new ProductSchema<EssxRow>();
        for (var i = 0; i < columns.Count; i++)
        {
            var index = i;
            var column = columns[i];
            Action<EssxRow, IRowSink> write = column.Type switch
            {
                ColumnType.Text or ColumnType.Json => (row, sink) => Text(row, index, sink, column.Type),
                ColumnType.Integer => (row, sink) => sink.Integer(row.IntegerOrNull(index)),
                ColumnType.Boolean => (row, sink) => sink.Boolean(row.BooleanOrNull(index)),
                ColumnType.VerseKey => (row, sink) => Key(row, index, sink, withPosition: false),
                ColumnType.WordKey => (row, sink) => Key(row, index, sink, withPosition: true),
                _ => throw new ArgumentOutOfRangeException(nameof(columns)),
            };
            schema.Add(column, write);
        }

        return schema.Reads(row => (EssxRow)row);
    }

    private static void Text(EssxRow row, int index, IRowSink sink, ColumnType type)
    {
        if (sink is IDictionarySink dictionary && row.Group.Column(index) is DictData ids)
        {
            dictionary.Text(ids.Dictionary, ids.Ids[row.Index]);
        }
        else if (type == ColumnType.Json)
        {
            sink.Json(row.TextOrNull(index));
        }
        else
        {
            sink.Text(row.TextOrNull(index));
        }
    }

    private static void Key(EssxRow row, int index, IRowSink sink, bool withPosition)
    {
        var key = (KeyData)row.Group.Column(index);
        var r = row.Index;
        if (sink is IKeyPartsSink parts)
        {
            if (key.IsNull(r))
            {
                parts.KeyParts(null, 0, string.Empty, 0, string.Empty, 0);
            }
            else
            {
                parts.KeyParts(key.Book.Values[r], key.Chapter.Values[r], key.ChapterLabel.Text(r) ?? string.Empty, key.Verse.Values[r],
                    key.VerseLabel.Text(r) ?? string.Empty, key.Position?.Values[r] ?? 0);
            }
        }
        else if (withPosition)
        {
            sink.WordKey(key.WordKey(r, "X"));
        }
        else
        {
            sink.VerseKey(key.VerseKey(r));
        }
    }
}
