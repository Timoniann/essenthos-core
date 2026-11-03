using Essenthos.Core.Products.Esw;
using Essenthos.Core.Products.Tsv;

namespace Essenthos.Core.Products.Tools;

public sealed record ConversionOptions
{
    /// <summary>Types a caller states, which win over the inferred ones: <c>own:wordkey,count:integer</c>.</summary>
    public IReadOnlyDictionary<string, ColumnType>? Types { get; init; }

    /// <summary>Which table of a file of several; the first when null.</summary>
    public string? Table { get; init; }

    public EswOptions Esw { get; init; } = EswOptions.Default;

    /// <summary>The header to write when the source has none, as a hand-written CSV has none.</summary>
    public ProductHeader? Header { get; init; }
}

/// <summary>
/// Converts a product between its shapes. Types travel with a product file; a text source without
/// them is read once to infer them and once more to write, so a file of any size converts in
/// bounded memory. The TSV and the binary file of the same rows read back the same.
/// </summary>
public static class ProductConversion
{
    public static ConversionResult Convert(string from, string to, ConversionOptions? options = null)
    {
        options ??= new ConversionOptions();
        var toKind = TextTables.Kind(to);
        IReadOnlyList<Column> columns;
        ProductHeader? header;
        using (var source = TextTables.Open(from, options.Table))
        {
            header = source.Header;
            columns = source.Columns is { } typed && options.Types is null
                ? typed
                : TextTables.Infer(source.Names, source.Rows(), options.Types ?? source.Columns?.ToDictionary(column => column.Name, column => column.Type));
        }

        header = options.Header ?? header ?? new ProductHeader(Path.GetFileName(to).Replace(' ', '_'), [], $"esw convert {Path.GetFileName(from)}");
        header.Rows = null;
        using var rows = TextTables.Open(from, options.Table);
        long count;
        switch (toKind)
        {
            case var esw when esw == EswLayout.Extension:
            case ".tsv":
            {
                IProductFormat format = toKind == EswLayout.Extension ? new EswFormat(options.Esw) : TsvFormat.Instance;
                using var writer = format.Create(to, header, TextTables.Schema(columns));
                writer.WriteAll(rows.Rows());
                count = writer.Rows;
                break;
            }
            default:
            {
                using var file = AtomicFile.Create(to);
                var counted = 0L;
                var counting = rows.Rows().Select(row => { counted++; return row; });
                using (var text = new StreamWriter(file.Stream, Utf8.NoBom, 1 << 16, leaveOpen: true) { NewLine = "\n" })
                {
                    if (toKind == ".csv") TextTables.WriteCsv(text, [.. columns.Select(column => column.Name)], counting);
                    else if (toKind == ".jsonl") TextTables.WriteJsonl(text, columns, counting);
                    else TextTables.WriteXml(text, header, columns, counting);
                }

                file.Commit();
                count = counted;
                break;
            }
        }

        return new ConversionResult(to, columns, count);
    }
}

public sealed record ConversionResult(string Path, IReadOnlyList<Column> Columns, long Rows);
