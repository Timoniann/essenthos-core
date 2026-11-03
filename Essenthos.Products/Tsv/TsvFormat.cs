namespace Essenthos.Core.Products.Tsv;

/// <summary>
/// The TSV products: UTF-8, LF, tab-separated, <c>COPY</c> escaping, comment header. The format of
/// every file people edit by hand, and of machine products until the binary format lands.
/// </summary>
public sealed class TsvFormat : IProductFormat
{
    public static readonly TsvFormat Instance = new();

    public string Extension => ".tsv";

    public IProductWriter<T> Create<T>(string path, ProductHeader header, ProductSchema<T> schema) =>
        new TsvProductWriter<T>(path, header, schema);

    public IProductReader<T> Open<T>(string path, ProductSchema<T> schema) =>
        TsvProductReader<T>.Open(path, schema);

    public IProductReader<string?[]> Open(string path) =>
        TsvProductReader<string?[]>.OpenTexts(path);

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
