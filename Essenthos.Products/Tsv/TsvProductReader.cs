namespace Essenthos.Core.Products.Tsv;

/// <summary>
/// Reads a TSV product: the comment lines at the top into a <see cref="ProductHeader"/>, the next
/// line as the column names, every line after as a row. A line whose field count is not the
/// column count is an error naming the line, since <c>COPY</c> would refuse it too.
/// </summary>
public sealed class TsvProductReader<T> : IProductReader<T>
{
    private readonly StreamReader _reader;
    private readonly ProductSchema<T>? _schema;
    private readonly Func<TsvRow, T> _read;
    private readonly string[] _names;
    private readonly bool _checkRows;
    private int _line;
    private bool _rowsTaken;

    private TsvProductReader(string path, StreamReader reader, ProductSchema<T>? schema, Func<TsvRow, T> read, bool checkRows)
    {
        Path = path;
        _checkRows = checkRows;
        _reader = reader;
        _schema = schema;
        _read = read;

        var comments = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            _line++;
            if (line.StartsWith('#'))
            {
                comments.Add(line);
                continue;
            }

            break;
        }

        Header = comments.Count == 0 ? null : ProductHeader.Parse(comments);
        if (line is null)
        {
            throw new ProductFormatException($"{path}: no column line after the header.");
        }

        _names = line.Split('\t');
        if (schema is not null)
        {
            schema.Check(_names, path);
            Columns = schema.Columns;
        }
        else
        {
            Columns = [.. _names.Select(name => new Column(name, ColumnType.Text, Nullable: true))];
        }
    }

    /// <param name="checkRows">Hold the file to the rows count its header states; false for a file people edit.</param>
    public static TsvProductReader<T> Open(string path, ProductSchema<T> schema, bool checkRows = true)
    {
        var reader = OpenText(path);
        try
        {
            return new TsvProductReader<T>(path, reader, schema, row => schema.Read(row), checkRows);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>The untyped reader, which copies each row's fields out since the row is reused.</summary>
    public static TsvProductReader<string?[]> OpenTexts(string path, bool checkRows = true)
    {
        var reader = OpenText(path);
        try
        {
            return new TsvProductReader<string?[]>(path, reader, null, row => row.CopyFields(), checkRows);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private static StreamReader OpenText(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan), Utf8.NoBom, detectEncodingFromByteOrderMarks: false, 1 << 16);

    public string Path { get; }

    public ProductHeader? Header { get; }

    public IReadOnlyList<Column> Columns { get; }

    /// <summary>The names as the file writes them, whether or not a schema typed them.</summary>
    public IReadOnlyList<string> Names => _names;

    public IEnumerable<T> Rows()
    {
        if (_rowsTaken)
        {
            throw new InvalidOperationException("The rows of a reader are read once.");
        }

        _rowsTaken = true;
        var row = new TsvRow(Columns);
        var rows = 0L;
        string? line;
        while ((line = _reader.ReadLine()) is not null)
        {
            _line++;
            if (!TsvEscaping.SplitLine(line, row.Fields))
            {
                throw new ProductFormatException($"{Path} line {_line}: {CountTabs(line) + 1} fields where {Columns.Count} columns are declared.");
            }

            rows++;
            yield return _read(row);
        }

        if (_checkRows && Header?.Rows is { } declared && declared != rows)
        {
            throw new ProductFormatException($"{Path}: the header says {declared} rows and the file holds {rows}.");
        }
    }

    private static int CountTabs(string line)
    {
        var tabs = 0;
        foreach (var c in line)
        {
            if (c == '\t')
            {
                tabs++;
            }
        }

        return tabs;
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>The one row object a TSV reader fills line after line.</summary>
public sealed class TsvRow : ProductRow
{
    public TsvRow(IReadOnlyList<Column> columns) : base(columns)
    {
        Fields = new string?[columns.Count];
    }

    public string?[] Fields { get; }

    public override bool IsNull(int column) => Fields[column] is null;

    public override string? TextOrNull(int column) => Fields[column];

    public string?[] CopyFields() => (string?[])Fields.Clone();
}
