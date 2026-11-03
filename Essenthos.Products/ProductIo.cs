namespace Essenthos.Core.Products;

/// <summary>
/// Writes one product's rows. <see cref="Complete"/> finishes the file and moves it into place;
/// disposing without it leaves the previous product where it was.
/// </summary>
public interface IProductWriter<in T> : IDisposable
{
    string Path { get; }

    void Write(T row);

    /// <summary>The rows written so far.</summary>
    long Rows { get; }

    void Complete();
}

/// <summary>Reads one product: its header, its columns, and its rows once, in order.</summary>
public interface IProductReader<out T> : IDisposable
{
    string Path { get; }

    /// <summary>Null when the file has no header lines, which a file written by hand may lack.</summary>
    ProductHeader? Header { get; }

    IReadOnlyList<Column> Columns { get; }

    IEnumerable<T> Rows();
}

/// <summary>
/// A file format products are written in. The TSV is the first; the binary one plugs in behind the
/// same three calls, and a producer written against <see cref="ProductStore"/> never names either.
/// </summary>
public interface IProductFormat
{
    /// <summary>With the dot: <c>.tsv</c>.</summary>
    string Extension { get; }

    IProductWriter<T> Create<T>(string path, ProductHeader header, ProductSchema<T> schema);

    IProductReader<T> Open<T>(string path, ProductSchema<T> schema);

    /// <summary>Every row as text, for a tool that knows no row type: a viewer, a converter, a checker.</summary>
    IProductReader<string?[]> Open(string path);
}

public static class ProductReaderExtensions
{
    public static List<T> ReadAll<T>(this IProductReader<T> reader) => [.. reader.Rows()];

    public static void WriteAll<T>(this IProductWriter<T> writer, IEnumerable<T> rows)
    {
        foreach (var row in rows)
        {
            writer.Write(row);
        }

        writer.Complete();
    }
}
