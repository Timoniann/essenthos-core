using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

internal enum RecordColumnType
{
    Text,
    Integer,
    Boolean,
    Json,

    /// <summary>A canonical verse: <c>int[3]</c> (book, chapter, verse) in SQL, a <see cref="VerseKey"/> in the file.</summary>
    Ref,

    /// <summary>Canonical verses in order: a flat <c>int[]</c> of triples in SQL, keys joined by a space in the file.</summary>
    Refs,
}

internal sealed record RecordColumn(string Name, RecordColumnType Type, bool Nullable)
{
    /// <summary>
    /// Columns written as <c>name[:type][?]</c>, one per word: <c>slug kind subtype? n:int back:bool ref:ref</c>.
    /// </summary>
    public static RecordColumn[] Parse(string columns) =>
    [
        .. columns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
        {
            var nullable = word.EndsWith('?');
            var bare = nullable ? word[..^1] : word;
            var colon = bare.IndexOf(':');
            var name = colon < 0 ? bare : bare[..colon];
            var type = (colon < 0 ? "text" : bare[(colon + 1)..]) switch
            {
                "text" => RecordColumnType.Text,
                "int" => RecordColumnType.Integer,
                "bool" => RecordColumnType.Boolean,
                "json" => RecordColumnType.Json,
                "ref" => RecordColumnType.Ref,
                "refs" => RecordColumnType.Refs,
                var other => throw new ArgumentException($"Unknown column type '{other}' in '{word}'."),
            };
            return new RecordColumn(name, type, nullable);
        }),
    ];

    public string SqlType => Type switch
    {
        RecordColumnType.Text => "text",
        RecordColumnType.Integer => "bigint",
        RecordColumnType.Boolean => "boolean",
        RecordColumnType.Json => "jsonb",
        _ => "int[]",
    };
}

/// <summary>
/// One file of our records, facts, languages or external datasets, and how it is taken from a database
/// and put back into one.
/// </summary>
/// <param name="Product">Its product name under <c>Resources/Essenthos/</c>, without an extension.</param>
/// <param name="Kind">Hand for what people and agents edit; Machine for what a producer derives.</param>
/// <param name="Columns">The file's columns in order, as <see cref="RecordColumn.Parse"/> reads them.</param>
/// <param name="Export">
/// The SELECT giving the file's rows from a database of today's schema, one column per file column in
/// order, sorted by the file's natural key.
/// </param>
/// <param name="Load">
/// The INSERT putting the rows back, reading them from the temporary table <c>s</c> (one column per
/// file column, same names). It must write as many rows as the file has, so a slug that names no
/// record fails the load instead of dropping the row.
/// </param>
/// <param name="Tables">The tables the load writes, emptied before it, in the order they are emptied.</param>
/// <param name="After">Statements run after <paramref name="Load"/>, with <c>s</c> still there.</param>
/// <param name="Requires">A condition on the database without which the file is not loaded (and the load says so).</param>
/// <param name="ExportLoaded">
/// The SELECT for a database this loader filled, where it differs from <paramref name="Export"/>
/// (the dataset columns are gone there), so that a loaded database exports to the same file.
/// </param>
internal sealed record RecordFile(
    string Product,
    ProductKind Kind,
    string Columns,
    string Export,
    string Load,
    string[] Tables,
    string? After = null,
    string? ExportLoaded = null,
    string? About = null,
    string? Requires = null)
{
    public RecordColumn[] Parsed { get; } = RecordColumn.Parse(Columns);

    public ProductSchema<string?[]> Schema()
    {
        var schema = new ProductSchema<string?[]>();
        for (var i = 0; i < Parsed.Length; i++)
        {
            var index = i;
            var column = Parsed[i];
            schema = (column.Type, column.Nullable) switch
            {
                (RecordColumnType.Integer, false) => schema.Integer(column.Name, row => long.Parse(row[index]!, System.Globalization.CultureInfo.InvariantCulture)),
                (RecordColumnType.Integer, true) => schema.OptionalInteger(column.Name, row => row[index] is { } text ? long.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : null),
                (RecordColumnType.Boolean, false) => schema.Boolean(column.Name, row => row[index] == "t"),
                (RecordColumnType.Boolean, true) => schema.OptionalBoolean(column.Name, row => row[index] is { } text ? text == "t" : null),
                (RecordColumnType.Json, false) => schema.Json(column.Name, row => row[index]!),
                (RecordColumnType.Json, true) => schema.OptionalJson(column.Name, row => row[index]),
                (_, false) => schema.Text(column.Name, row => row[index]!),
                (_, true) => schema.OptionalText(column.Name, row => row[index]),
            };
        }

        return schema.Reads(row => row.ToTexts());
    }
}
