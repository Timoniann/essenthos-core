using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Tsv;

namespace Essenthos.Core.Products.Tools;

public sealed record ValidationReport(string Path, IReadOnlyList<string> Problems, long Rows)
{
    public bool Ok => Problems.Count == 0;
}

/// <summary>
/// Says whether a product file is what its writer meant: a binary file's magic, checksum, footer,
/// every chunk inside the data, row counts that add up, dictionary ids within their dictionaries,
/// keys that parse; a TSV's header, field counts and declared row count. Reads everything once.
/// </summary>
public static class ProductValidation
{
    public static ValidationReport Validate(string path)
    {
        var problems = new List<string>();
        long rows = 0;
        try
        {
            rows = TextTables.Kind(path) == EssxLayout.Extension ? ValidateEssx(path, problems) : ValidateText(path, problems);
        }
        catch (Exception exception) when (exception is ProductFormatException or FormatException or IOException or ArgumentException)
        {
            problems.Add(exception.Message);
        }

        return new ValidationReport(path, problems, rows);
    }

    private static long ValidateEssx(string path, List<string> problems)
    {
        using var reader = EssxReader.Open(path);
        if (!reader.Verify())
        {
            problems.Add("the SHA-256 in the trailer is not the file's: bytes changed after it was written");
        }

        if (reader.Footer.Format != "essx")
        {
            problems.Add($"footer format is '{reader.Footer.Format}'");
        }

        var dataEnd = reader.Length - EssxLayout.TrailerLength;
        long total = 0;
        foreach (var table in reader.Tables)
        {
            var info = table.Info;
            var sum = info.RowGroups.Sum(group => (long)group.Rows);
            if (sum != info.Rows)
            {
                problems.Add($"table {info.Name}: the row groups hold {sum} rows and the footer says {info.Rows}");
            }

            foreach (var column in info.Columns)
            {
                foreach (var physical in column.Physical)
                {
                    if (info.Physical.All(p => p.Name != physical))
                    {
                        problems.Add($"table {info.Name}: column {column.Name} refers to a stored column '{physical}' the footer does not list");
                    }
                }
            }

            foreach (var physical in info.Physical)
            {
                if (physical.Dictionary is { } dictionary && (dictionary.Offset < EssxLayout.PreambleLength || dictionary.Offset + dictionary.Length > dataEnd))
                {
                    problems.Add($"table {info.Name}: the dictionary of {physical.Name} lies outside the data");
                }
            }

            for (var g = 0; g < info.RowGroups.Count; g++)
            {
                var group = info.RowGroups[g];
                if (group.Chunks.Count != info.Physical.Count)
                {
                    problems.Add($"table {info.Name} group {g}: {group.Chunks.Count} chunks for {info.Physical.Count} stored columns");
                    continue;
                }

                for (var c = 0; c < group.Chunks.Count; c++)
                {
                    var chunk = group.Chunks[c];
                    if (chunk.Offset < EssxLayout.PreambleLength || chunk.Offset + chunk.Length > dataEnd)
                    {
                        problems.Add($"table {info.Name} group {g} column {info.Physical[c].Name}: chunk at {chunk.Offset}+{chunk.Length} lies outside the data");
                    }

                    if (chunk.Nulls > group.Rows)
                    {
                        problems.Add($"table {info.Name} group {g} column {info.Physical[c].Name}: {chunk.Nulls} nulls in {group.Rows} rows");
                    }

                    if (info.Physical[c].Encoding == "dict" && chunk.Max is { } max && max > (info.Physical[c].Dictionary?.Count ?? 0))
                    {
                        problems.Add($"table {info.Name} group {g} column {info.Physical[c].Name}: id {max} beyond the dictionary's {info.Physical[c].Dictionary?.Count}");
                    }
                }
            }

            if (problems.Count > 0)
            {
                continue;
            }

            try
            {
                for (var g = 0; g < info.RowGroups.Count; g++)
                {
                    var decoded = table.ReadGroup(g);
                    for (var c = 0; c < table.Columns.Count; c++)
                    {
                        var column = table.Columns[c];
                        var data = decoded.Column(c);
                        var nulls = 0;
                        for (var i = 0; i < decoded.Rows; i++)
                        {
                            if (data.IsNull(i))
                            {
                                nulls++;
                            }
                            else if (column.Type == ColumnType.VerseKey)
                            {
                                data.VerseKey(i);
                            }
                            else if (column.Type == ColumnType.WordKey)
                            {
                                data.WordKey(i, "X");
                            }
                        }

                        if (nulls > 0 && !column.Nullable)
                        {
                            problems.Add($"table {info.Name} group {g}: column {column.Name} is not nullable and holds {nulls} nulls");
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is ProductFormatException or IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidDataException)
            {
                problems.Add($"table {info.Name}: decoding failed: {exception.Message}");
            }

            total += info.Rows;
        }

        if (reader.Tables.Count > 0 && reader.Footer.Product.Rows != reader.Tables[0].Info.Rows)
        {
            problems.Add($"the product says {reader.Footer.Product.Rows} rows and its first table holds {reader.Tables[0].Info.Rows}");
        }

        return total;
    }

    private static long ValidateText(string path, List<string> problems)
    {
        using var table = TextTables.Open(path);
        long rows = 0;
        foreach (var _ in table.Rows())
        {
            rows++;
        }

        if (table.Header?.Rows is { } declared && declared != rows)
        {
            problems.Add($"the header says {declared} rows and the file holds {rows}");
        }

        if (TextTables.Kind(path) == ".tsv" && table.Header is null)
        {
            problems.Add("no product header (a hand-written file; fine if that is what it is)");
        }

        return rows;
    }
}
