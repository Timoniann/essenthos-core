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
/// <param name="Listed">
/// The rows of a file no table holds, which the Forge carries as a list of its own until the file
/// replaces it: the export writes them, whatever the database.
/// </param>
/// <param name="Check">
/// For a <paramref name="Listed"/> file, which the load writes to no table: a SELECT over the staged
/// rows <c>s</c> counting the rows that are sound. The load fails unless it counts every row.
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
    string? Requires = null,
    Func<IEnumerable<string?[]>>? Listed = null,
    string? Check = null)
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

    /// <summary>
    /// The columns a file is sorted by, as the last ORDER BY of its export names them (by position,
    /// or by a column's name with or without its table's alias).
    /// </summary>
    public int[] Order(string export)
    {
        var at = LastOrderBy(export)
                 ?? throw new InvalidOperationException($"{Product}: the export has no ORDER BY of its own.");
        return
        [
            .. export[(at + "ORDER BY".Length)..].Split(',').Select(term =>
            {
                var bare = term.Trim();
                if (int.TryParse(bare, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var position)
                    && position >= 1 && position <= Parsed.Length)
                {
                    return position - 1;
                }

                var name = bare[(bare.LastIndexOf('.') + 1)..];
                var index = Array.FindIndex(Parsed, c => c.Name == name);
                return index >= 0
                    ? index
                    : throw new InvalidOperationException($"{Product}: the export sorts by '{bare}', which is no column of the file.");
            }),
        ];
    }

    /// <summary>
    /// The rows in the file's order, compared by value and by ordinal: a file reads the same whatever
    /// the collation of the server it was exported from. Equal keys keep the order they came in.
    /// </summary>
    public List<string?[]> Sorted(IEnumerable<string?[]> rows, int[] order) =>
        [.. rows.Order(Comparer<string?[]>.Create((a, b) =>
        {
            foreach (var i in order)
            {
                var compared = Compare(Parsed[i].Type, a[i], b[i]);
                if (compared != 0)
                {
                    return compared;
                }
            }

            return 0;
        }))];

    private static int Compare(RecordColumnType type, string? a, string? b)
    {
        if (a is null || b is null)
        {
            // Postgres puts the empty last when it sorts upwards.
            return a is null ? (b is null ? 0 : 1) : -1;
        }

        return type switch
        {
            RecordColumnType.Integer => long.Parse(a, System.Globalization.CultureInfo.InvariantCulture)
                .CompareTo(long.Parse(b, System.Globalization.CultureInfo.InvariantCulture)),
            RecordColumnType.Ref or RecordColumnType.Refs => Keys(a).Zip(Keys(b)).Select(p => p.First.CompareTo(p.Second))
                .FirstOrDefault(c => c != 0) is var first and not 0 ? first : Keys(a).Length.CompareTo(Keys(b).Length),
            _ => string.CompareOrdinal(a, b),
        };
    }

    private static VerseKey[] Keys(string text) =>
        [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(key => VerseKey.Parse(key))];

    private static int? LastOrderBy(string sql)
    {
        var depth = 0;
        int? found = null;
        for (var i = 0; i < sql.Length; i++)
        {
            switch (sql[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case '\'':
                    i = sql.IndexOf('\'', i + 1);
                    break;
                default:
                    if (depth == 0 && string.CompareOrdinal(sql, i, "ORDER BY", 0, 8) == 0)
                    {
                        found = i;
                    }

                    break;
            }
        }

        return found;
    }
}
