namespace Essenthos.Core.Products.Tsv;

/// <summary>
/// The TSV products: UTF-8, LF, tab-separated, <c>COPY</c> escaping, comment header. The format of
/// every file people edit by hand, and of machine products until the binary format lands.
/// </summary>
public sealed class TsvFormat : IProductFormat
{
    /// <summary>A machine's TSV: the header counts the rows and the reader holds the file to that count.</summary>
    public static readonly TsvFormat Instance = new(countsRows: true);

    /// <summary>
    /// A file people edit: no rows count is written and none is checked, so a line added or taken out by hand never makes
    /// the file unreadable (a count an exporter wrote before is ignored).
    /// </summary>
    public static readonly TsvFormat HandEdited = new(countsRows: false);

    private TsvFormat(bool countsRows)
    {
        CountsRows = countsRows;
    }

    public bool CountsRows { get; }

    public string Extension => ".tsv";

    public IProductWriter<T> Create<T>(string path, ProductHeader header, ProductSchema<T> schema) =>
        new TsvProductWriter<T>(path, header, schema, CountsRows);

    public IProductReader<T> Open<T>(string path, ProductSchema<T> schema) =>
        TsvProductReader<T>.Open(path, schema, CountsRows);

    public IProductReader<string?[]> Open(string path) =>
        TsvProductReader<string?[]>.OpenTexts(path, CountsRows);

    /// <summary>
    /// The rows of a product as <c>COPY ... FROM STDIN</c> takes them: a reader positioned after the
    /// header and the column line, whose remaining text is the body, escaped as <c>COPY</c> expects.
    /// <paramref name="columns"/> says in which order the body's values come.
    /// </summary>
    public static TextReader OpenBody(string path, out ProductHeader? header, out IReadOnlyList<string> columns)
    {
        var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan), Utf8.NoBom, false, 1 << 16);
        var comments = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null && line.StartsWith('#'))
        {
            comments.Add(line);
        }

        if (line is null)
        {
            reader.Dispose();
            throw new ProductFormatException($"{path}: no column line after the header.");
        }

        header = comments.Count == 0 ? null : ProductHeader.Parse(comments);
        columns = line.Split('\t');
        return reader;
    }
}
